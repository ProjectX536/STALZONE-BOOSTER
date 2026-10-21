using System.Globalization;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>
/// Аргументы PresentMon 2.x (проверены на 2.6.0). Каждый аргумент — отдельный элемент списка,
/// поэтому имя процесса с пробелами не разбивается на части.
/// </summary>
public static class PresentMonArguments
{
    public static IReadOnlyList<string> ForCapture(FrameCaptureRequest request, string outputCsv, string sessionName)
    {
        Validate(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputCsv);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        string[] target = request.ProcessName is { } name
            ? ["--process_name", name]
            : ["--process_id", request.ProcessId!.Value.ToString(CultureInfo.InvariantCulture)];
        return
        [
            .. target,
            "--output_file", outputCsv,
            // TimeInQPC в тиках QueryPerformanceCounter — те же часы, что TimeProvider.System.GetTimestamp(): по ним серия режется на прогоны.
            "--qpc_time",
            "--timed", TimedSeconds(request.Duration).ToString(CultureInfo.InvariantCulture),
            "--terminate_after_timed",
            .. (request.StopWhenProcessExits ? (string[])["--terminate_on_proc_exit"] : []),
            "--no_console_stats",
            "--session_name", sessionName,
            // Сессия с тем же именем могла остаться от прерванного захвата: без этого PresentMon не запустится.
            "--stop_existing_session",
        ];
    }

    /// <summary>Имя ETW-сессии замера: у замера игровой сессии своё, чтобы он и серия бенчмарка не останавливали друг друга.</summary>
    public static string SessionName(string baseName, FrameCapturePurpose purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        return purpose == FrameCapturePurpose.GameSession ? baseName + "-GameSession" : baseName;
    }

    /// <summary>Закрывает ETW-сессию, которую оставил убитый PresentMon.</summary>
    public static IReadOnlyList<string> ForTerminate(string sessionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        return ["--terminate_existing_session", "--session_name", sessionName];
    }

    /// <summary>PresentMon принимает целые секунды; округление вверх, чтобы замер не стал короче запрошенного.</summary>
    public static long TimedSeconds(TimeSpan duration) => (long)Math.Ceiling(duration.TotalSeconds);

    /// <summary>Ровно одна цель — непустое имя или положительный PID — и длительность больше нуля.</summary>
    public static void Validate(FrameCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.ProcessName is null) == (request.ProcessId is null))
            throw new ArgumentException("Exactly one of ProcessName and ProcessId must be set.", nameof(request));
        if (request.ProcessName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ProcessName, nameof(request));
        if (request.ProcessId is { } processId)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId, nameof(request));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.Duration, TimeSpan.Zero, nameof(request));
    }
}
