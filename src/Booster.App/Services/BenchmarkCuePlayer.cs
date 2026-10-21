using Microsoft.Extensions.Logging;

namespace Booster.App.Services;

/// <summary>Звуковые сигналы серии: пользователь в игре и не смотрит на Booster, но слышит начало и конец прогона.</summary>
public interface IBenchmarkCuePlayer
{
    void RunStarted();

    void RunEnded();
}

/// <summary>
/// Системный динамик через <see cref="Console.Beep(int, int)"/>: высокий короткий — прогон начался, низкий длинный —
/// закончился. Звук играет в пуле потоков: Beep синхронный, а ход серии ждать не должен.
/// </summary>
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
                // Нет звукового устройства или Beep не поддерживается — серия идёт без звука.
                LogBeepFailed(ex);
            }
        });

    [LoggerMessage(Level = LogLevel.Debug, Message = "Benchmark cue could not be played")]
    private partial void LogBeepFailed(Exception exception);
}
