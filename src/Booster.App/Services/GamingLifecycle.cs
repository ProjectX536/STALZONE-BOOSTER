using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booster.App.Services;

/// <summary>
/// Игровая часть приложения: восстановление после сбоя и координатор сессий. Запускается только из окна — в режиме
/// <c>--export</c> Booster ничего не меняет. Остановка хоста восстанавливает активную сессию.
/// </summary>
public sealed partial class GamingLifecycle(StartupRecovery recovery, GameSessionCoordinator coordinator, ILogger<GamingLifecycle> logger)
    : IHostedService
{
    /// <summary>Отчёт восстановления при запуске; null — окно ещё не запускало игровую часть.</summary>
    public RecoveryReport? LastRecovery { get; private set; }

    /// <summary>Сначала восстановление незавершённых сессий, затем координатор. Ошибки не мешают открыть окно.</summary>
    public async Task<RecoveryReport> StartAsync()
    {
        RecoveryReport report;
        try
        {
            report = await Task.Run(() => recovery.RunAsync(CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Не пустой отчёт: экран журнала должен показать, что восстановление не выполнено.
            LogRecoveryFailed(ex);
            report = new RecoveryReport([], [], [new JournalProblem("", JournalProblemKind.RecoveryFailed, ex.Message)]);
        }
        LastRecovery = report;

        try
        {
            await coordinator.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCoordinatorStartFailed(ex);
        }
        return report;
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => coordinator.StopAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Error, Message = "Startup recovery failed")]
    private partial void LogRecoveryFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Game session coordinator failed to start")]
    private partial void LogCoordinatorStartFailed(Exception exception);
}
