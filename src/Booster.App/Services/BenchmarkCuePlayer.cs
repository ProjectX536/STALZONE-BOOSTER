using Microsoft.Extensions.Logging;

namespace Booster.App.Services;

public interface IBenchmarkCuePlayer
{
    void RunStarted();

    void RunEnded();
}

public sealed partial class BenchmarkCuePlayer(ILogger<BenchmarkCuePlayer> logger) : IBenchmarkCuePlayer
{
    private const int StartFrequencyHz = 880;
    private const int StartDurationMs = 150;
    private const int EndFrequencyHz = 440;
    private const int EndDurationMs = 300;

    public void RunStarted() => Play(StartFrequencyHz, StartDurationMs);

    public void RunEnded() => Play(EndFrequencyHz, EndDurationMs);

    private void Play(int frequency, int duration) =>
        _ = Task.Run(() =>
        {
            try
            {
                Console.Beep(frequency, duration);
            }
            catch (Exception ex)
            {
                LogBeepFailed(ex);
            }
        });

    [LoggerMessage(Level = LogLevel.Debug, Message = "Benchmark cue could not be played")]
    private partial void LogBeepFailed(Exception exception);
}
