using System.ComponentModel;
using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>
/// Замер кадров внешним PresentMon (ETW). В процесс игры Booster не вмешивается: PresentMon только читает события
/// презентации кадров. Параллельно счётчики GPU Engine показывают, на каком адаптере игра рисует.
/// </summary>
public sealed partial class PresentMonProvider(
    PresentMonLocator locator,
    IProcessRunner runner,
    IFileProbe files,
    IAppPaths paths,
    IPrivilegeInfo privileges,
    IGpuAdapterUsageSampler sampler,
    IProcessLookup processes,
    IBoosterConfiguration config,
    ICapabilityRegistry capabilities,
    TimeProvider time,
    ILogger<PresentMonProvider> logger) : IFrameCaptureProvider
{
    public const string ProviderId = "platform.windows.presentmon";

    /// <summary>ERROR_CANCELLED: пользователь отказал в запросе UAC.</summary>
    private const int ErrorCancelled = 1223;

    private const string AdapterSource = GpuAdapterUsageAggregator.Source;

    public Task<FrameCaptureAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var location = locator.Locate();
        var mode = privileges.GetCurrent().CanCaptureFramesWithoutPrompt ? FrameCaptureMode.Direct : FrameCaptureMode.ElevationPrompt;

        var status = location is null ? CapabilityStatus.NotSupported
            : mode == FrameCaptureMode.Direct ? CapabilityStatus.Supported
            : CapabilityStatus.RequiresAdmin;
        capabilities.Report(new CapabilityReport(CapabilityIds.FrameCapture, CapabilityScopes.System, status, ProviderId,
            location is null ? "PresentMon not found; scripts/Install-PresentMon.ps1 installs it" : null));

        return Task.FromResult(new FrameCaptureAvailability(location is not null, location?.ExecutablePath, location?.Version, mode));
    }

    public async Task<FrameCaptureResult> CaptureAsync(FrameCaptureRequest request, Action? started, CancellationToken cancellationToken)
    {
        PresentMonArguments.Validate(request);
        var settings = config.Current.Settings.PresentMon;

        var location = locator.Locate();
        if (location is null)
        {
            return new FrameCaptureResult(FrameCaptureStatus.NotInstalled, null, NotMeasured("PresentMon is not installed"),
                "PresentMon not found; scripts/Install-PresentMon.ps1 installs it");
        }

        var csvPath = Path.Combine(paths.Captures,
            string.Create(CultureInfo.InvariantCulture, $"{time.GetLocalNow():yyyyMMdd-HHmmss}-{FileLabel(request)}.csv"));
        var sessionName = PresentMonArguments.SessionName(settings.SessionName, request.Purpose);
        var launch = new ProcessLaunch(location.ExecutablePath,
            PresentMonArguments.ForCapture(request, csvPath, sessionName),
            Elevate: !privileges.GetCurrent().CanCaptureFramesWithoutPrompt)
        {
            Started = started,
        };

        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Отдельный поток: первое чтение счётчиков синхронное и не должно задерживать вызывающий (UI) поток.
        var adapterTask = Task.Run(() => SampleRenderAdapterAsync(request, sampling.Token), CancellationToken.None);

        int exitCode;
        try
        {
            exitCode = await runner.RunAsync(launch, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            await StopSamplingAsync(sampling, adapterTask).ConfigureAwait(false);
            return new FrameCaptureResult(FrameCaptureStatus.ElevationDeclined, null, NotMeasured("capture did not start"),
                "UAC prompt was declined");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopSamplingAsync(sampling, adapterTask).ConfigureAwait(false);
            return await CancelledAsync(launch, location, sessionName, request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            await StopSamplingAsync(sampling, adapterTask).ConfigureAwait(false);
            LogStartFailed(location.ExecutablePath, ex.GetType().Name, ex.Message);
            return new FrameCaptureResult(FrameCaptureStatus.Failed, null, NotMeasured("capture did not start"),
                $"PresentMon did not start: {ex.GetType().Name}: {ex.Message}");
        }

        var adapter = await StopSamplingAsync(sampling, adapterTask).ConfigureAwait(false);
        if (exitCode != 0)
        {
            return new FrameCaptureResult(FrameCaptureStatus.Failed, null, adapter,
                string.Create(CultureInfo.InvariantCulture, $"PresentMon exited with code {exitCode}"));
        }

        // PresentMon 2.6.0 не создаёт CSV, если цель не показала ни одного кадра: процесс не запущен или ничего не рисует.
        if (!files.FileExists(csvPath))
        {
            return new FrameCaptureResult(FrameCaptureStatus.TargetNotFound, null, adapter,
                "PresentMon created no CSV: the target presented no frames during the capture");
        }

        try
        {
            using var reader = files.OpenText(csvPath);
            var parse = PresentMonCsvParser.Parse(reader, request.ProcessName, request.ProcessId, csvPath);
            return new FrameCaptureResult(parse.Status, parse.Capture, adapter, parse.Detail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FrameCaptureResult(FrameCaptureStatus.Failed, null, adapter,
                $"CSV {csvPath} could not be read: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<FrameCaptureResult> CancelledAsync(ProcessLaunch launch, PresentMonLocation location, string sessionName,
        FrameCaptureRequest request)
    {
        var notMeasured = NotMeasured("capture was cancelled");
        if (launch.Elevate)
        {
            // Повышенный PresentMon не остановить без прав администратора; --terminate_after_timed завершит его сам.
            return new FrameCaptureResult(FrameCaptureStatus.Cancelled, null, notMeasured, string.Create(CultureInfo.InvariantCulture,
                $"PresentMon runs elevated and exits by itself within {PresentMonArguments.TimedSeconds(request.Duration)} s"));
        }

        // Убитый PresentMon оставляет ETW-сессию открытой; следующий захват закрыл бы её сам (--stop_existing_session), но лучше сразу.
        try
        {
            var code = await runner.RunAsync(
                new ProcessLaunch(location.ExecutablePath, PresentMonArguments.ForTerminate(sessionName), Elevate: false),
                CancellationToken.None).ConfigureAwait(false);
            if (code != 0)
                LogTerminateFailed(sessionName, string.Create(CultureInfo.InvariantCulture, $"exit code {code}"));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            LogTerminateFailed(sessionName, $"{ex.GetType().Name}: {ex.Message}");
        }

        return new FrameCaptureResult(FrameCaptureStatus.Cancelled, null, notMeasured, null);
    }

    private async Task<Reading<AdapterLuid>> SampleRenderAdapterAsync(FrameCaptureRequest request, CancellationToken ct)
    {
        int processId;
        if (request.ProcessId is { } id)
        {
            processId = id;
        }
        else
        {
            // PresentMon ждёт процесс по имени, а счётчикам нужен PID уже сейчас. Процесс, запущенный позже, — без адаптера.
            IReadOnlyList<int> ids;
            try
            {
                ids = processes.FindIdsByName(request.ProcessName!);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                return Reading<AdapterLuid>.Failure(AdapterSource, $"process list: {ex.GetType().Name}: {ex.Message}");
            }

            if (ids.Count != 1)
            {
                return Reading<AdapterLuid>.Unknown(AdapterSource, ids.Count == 0
                    ? "the process was not running when the capture started"
                    : string.Create(CultureInfo.InvariantCulture, $"{ids.Count} processes with this name were running"));
            }
            processId = ids[0];
        }

        return await sampler.SampleUntilCancelledAsync(processId, ct).ConfigureAwait(false);
    }

    private static async Task<Reading<AdapterLuid>> StopSamplingAsync(CancellationTokenSource sampling, Task<Reading<AdapterLuid>> adapterTask)
    {
        await sampling.CancelAsync().ConfigureAwait(false);
        return await adapterTask.ConfigureAwait(false);
    }

    private static Reading<AdapterLuid> NotMeasured(string note) => Reading<AdapterLuid>.Unknown(AdapterSource, note);

    /// <summary>Часть имени файла: имя процесса без запрещённых в именах файлов символов или <c>pid1234</c>.</summary>
    private static string FileLabel(FrameCaptureRequest request)
    {
        if (request.ProcessName is not { } name)
            return string.Create(CultureInfo.InvariantCulture, $"pid{request.ProcessId}");
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PresentMon {Path} did not start: {ExceptionType}: {Reason}")]
    private partial void LogStartFailed(string path, string exceptionType, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW session {SessionName} was not closed after cancel: {Reason}")]
    private partial void LogTerminateFailed(string sessionName, string reason);
}
