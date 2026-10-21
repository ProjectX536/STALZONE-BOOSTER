using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Latency;

public enum TraceState
{
    /// <summary>wpr.exe нет в системном каталоге, маркера записи нет.</summary>
    Unavailable,

    Idle,

    /// <summary>Маркер записан после последней загрузки, время загрузки неизвестно или маркер не прочитался.</summary>
    Recording,

    /// <summary>Маркер старше последней загрузки: запись в памяти пропала вместе с перезагрузкой.</summary>
    Interrupted,
}

/// <param name="StartedAt">null — маркер не прочитался.</param>
public sealed record TraceStatus(TraceState State, DateTimeOffset? StartedAt);

/// <summary>
/// Запись трассы WPR по кнопке. Маркер в каталоге state напоминает о незавершённой записи после сбоя Booster;
/// когда он пишется и удаляется — docs/ARCHITECTURE.md, раздел 11, «Задержки DPC и трасса WPR». Операции идут по одной.
/// </summary>
public sealed partial class TraceRecorder(IWprRunner wpr, IStateFileStore state, IBootTimeSource boot, TimeProvider time,
    ILogger<TraceRecorder> logger)
{
    public const string MarkerFileName = "wpr-recording.json";

    private const int MarkerSchemaVersion = 1;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TraceStatus> GetStatusAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadStatusAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Маркер пишется до запуска: если Booster упадёт после согласия в UAC, напоминание останется. Исключение из запуска
    /// (отмена ожидания) маркер тоже оставляет: запись могла начаться, а остановить или отменить её безопасно.
    /// </summary>
    /// <exception cref="InvalidOperationException">Маркер уже есть: запись надо остановить, отменить или убрать как прерванную.</exception>
    public async Task<WprResult> StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await state.ReadAsync(MarkerFileName, ct).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("A trace recording marker already exists");
            if (!wpr.IsAvailable)
                return new WprResult(WprOutcome.Failed, Detail: "wpr.exe was not found in the system directory");

            await state.WriteAsync(MarkerFileName, Marker(time.GetUtcNow()), ct).ConfigureAwait(false);
            var result = await wpr.StartAsync(ct).ConfigureAwait(false);
            if (result.Outcome != WprOutcome.Done)
                state.Delete(MarkerFileName);
            LogResult("start", result.Outcome, result.ExitCode, result.Detail);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Файл trace-yyyyMMdd-HHmmss.etl по местному времени. После сбоя маркер остаётся: запись ещё можно отменить.</summary>
    public Task<WprResult> StopAsync(CancellationToken ct) =>
        RunAsync("stop", token => wpr.StopAsync(TraceFileName(), token), outcome => outcome == WprOutcome.Done, ct);

    /// <summary>После сбоя маркер удаляется: записи уже нет или её не отменить, напоминание бесполезно.</summary>
    public Task<WprResult> CancelAsync(CancellationToken ct) =>
        RunAsync("cancel", wpr.CancelAsync, outcome => outcome != WprOutcome.Declined, ct);

    /// <summary>Только для Interrupted: удаляет маркер, wpr.exe не запускается.</summary>
    /// <exception cref="InvalidOperationException">Запись не прервана.</exception>
    public async Task DismissAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var status = await ReadStatusAsync(ct).ConfigureAwait(false);
            if (status.State != TraceState.Interrupted)
                throw new InvalidOperationException($"Only an interrupted recording can be dismissed, the state is {status.State}");
            state.Delete(MarkerFileName);
            LogDismissed(status.StartedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WprResult> RunAsync(string operation, Func<CancellationToken, Task<WprResult>> run, Func<WprOutcome, bool> removesMarker,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await run(ct).ConfigureAwait(false);
            if (removesMarker(result.Outcome))
                state.Delete(MarkerFileName);
            LogResult(operation, result.Outcome, result.ExitCode, result.Detail);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Запись в режиме памяти не переживает перезагрузку. Время загрузки неизвестно или маркер не прочитался — считается,
    /// что запись идёт: остановить или отменить её безопасно, а потерять напоминание — нет.
    /// </summary>
    private async Task<TraceStatus> ReadStatusAsync(CancellationToken ct)
    {
        var marker = await state.ReadAsync(MarkerFileName, ct).ConfigureAwait(false);
        if (marker is null)
            return new TraceStatus(wpr.IsAvailable ? TraceState.Idle : TraceState.Unavailable, null);

        if (StartedAt(marker) is not { } startedAt)
        {
            LogUnreadableMarker();
            return new TraceStatus(TraceState.Recording, null);
        }

        var lastBoot = boot.LastBootTime();
        return new TraceStatus(lastBoot.IsAvailable && startedAt < lastBoot.Value ? TraceState.Interrupted : TraceState.Recording, startedAt);
    }

    private string TraceFileName() => string.Create(CultureInfo.InvariantCulture, $"trace-{time.GetLocalNow():yyyyMMdd-HHmmss}.etl");

    private static string Marker(DateTimeOffset startedAt) => JsonSerializer.Serialize(new MarkerFile(MarkerSchemaVersion, startedAt));

    private static DateTimeOffset? StartedAt(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var schemaVersion)
                || schemaVersion != MarkerSchemaVersion
                || !root.TryGetProperty("startedAt", out var started) || started.ValueKind != JsonValueKind.String
                || !started.TryGetDateTimeOffset(out var startedAt))
                return null;
            return startedAt;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record MarkerFile(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "WPR {Operation}: {Outcome}, exit code {ExitCode}, {Detail}")]
    private partial void LogResult(string operation, WprOutcome outcome, int? exitCode, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WPR recording marker is unreadable; treating the recording as running")]
    private partial void LogUnreadableMarker();

    [LoggerMessage(Level = LogLevel.Information, Message = "Interrupted WPR recording started at {StartedAt} dismissed")]
    private partial void LogDismissed(DateTimeOffset? startedAt);
}
