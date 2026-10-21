using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Latency;

public enum TraceState
{
    Unavailable,

    Idle,

    Recording,

    Interrupted,
}

public sealed record TraceStatus(TraceState State, DateTimeOffset? StartedAt);

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

    public Task<WprResult> StopAsync(CancellationToken ct) =>
        RunAsync("stop", token => wpr.StopAsync(TraceFileName(), token), outcome => outcome == WprOutcome.Done, ct);

    public Task<WprResult> CancelAsync(CancellationToken ct) =>
        RunAsync("cancel", wpr.CancelAsync, outcome => outcome != WprOutcome.Declined, ct);

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
