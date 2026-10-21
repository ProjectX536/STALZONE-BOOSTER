using System.Diagnostics;
using System.Reflection;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Hags;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Microsoft.Extensions.Logging;

namespace Booster.App.Services;

/// <summary>
/// Последний снимок системы, последняя серия бенчмарка, последний тест сети и состояние A/B-теста HAGS — общие данные всех
/// экранов и экспорта.
/// <see cref="Changed"/> приходит в том потоке, где завершилось изменение, а не обязательно в UI-потоке.
/// </summary>
public sealed partial class DiagnosticsSession(SnapshotCollector collector, HagsExperimentService hags, IProfileRepository profiles,
    IBoosterConfiguration config, TimeProvider time, ILogger<DiagnosticsSession> logger)
{
    private static readonly string AppVersion =
        typeof(DiagnosticsSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(DiagnosticsSession).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile SystemSnapshot? _snapshot;
    private volatile BenchmarkReport? _benchmark;
    private volatile NetworkTestResult? _network;
    private volatile HagsExperimentView? _hags;

    public event EventHandler? Changed;

    public SystemSnapshot? Snapshot => _snapshot;

    public BenchmarkReport? Benchmark => _benchmark;

    public NetworkTestResult? Network => _network;

    public HagsExperimentView? Hags => _hags;

    /// <summary>
    /// Собирает новый снимок и перечитывает A/B-тест HAGS: экран «Система» могли не открывать, а отчёт должен знать этап теста.
    /// Одновременные вызовы выполняются по очереди.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var started = Stopwatch.GetTimestamp();
            _snapshot = await collector.CollectAsync(ct).ConfigureAwait(false);
            LogSnapshotCollected((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            _hags = await ReadHagsAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetBenchmark(BenchmarkReport benchmark)
    {
        ArgumentNullException.ThrowIfNull(benchmark);
        _benchmark = benchmark;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetNetworkTest(NetworkTestResult network)
    {
        ArgumentNullException.ThrowIfNull(network);
        _network = network;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Вид, прочитанный панелью HAGS последним; приходит при каждом её обновлении.</summary>
    public void UpdateHags(HagsExperimentView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _hags = view;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Отчёт по текущему снимку; рекомендации и замечания строятся по действующим порогам, справочнику и предупреждениям
    /// конфигурации, сбои игр — по играм из профилей.
    /// </summary>
    public SystemReport BuildReport()
    {
        var snapshot = _snapshot ?? throw new InvalidOperationException("No snapshot yet: call RefreshAsync first");
        var current = config.Current;
        var benchmark = _benchmark;
        var network = _network;
        var games = GameExecutables();
        var series = benchmark?.Series;
        var advice = SystemAdvisor.Advise(snapshot, network, current.Thresholds, current.Catalog, games, series);
        var tweaks = TweakAdvisor.Propose(snapshot, series, games, current.Catalog.RegistryTweaks);
        var findings = FindingsBuilder.Build(snapshot, current.Thresholds, current.Warnings, benchmark, advice);
        var metadata = new ReportMetadata(ReportMetadata.CurrentSchemaVersion, AppVersion, time.GetUtcNow(), snapshot.Privileges);
        return new SystemReport(metadata, snapshot, findings, benchmark, advice, network, tweaks, _hags);
    }

    /// <summary>Сбой чтения не мешает снимку; прежний вид мог устареть, поэтому отчёт получает «не прочитано», а не его.</summary>
    private async Task<HagsExperimentView?> ReadHagsAsync(CancellationToken ct)
    {
        try
        {
            return await hags.GetAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHagsFailed(ex);
            return null;
        }
    }

    /// <summary>Игры всех профилей. Профили не загрузились — сбои игр в рекомендациях не выделяются.</summary>
    private IReadOnlyList<string> GameExecutables()
    {
        try
        {
            return [.. profiles.Current.Profiles.SelectMany(p => p.Games).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            LogProfilesFailed(ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "System snapshot collected in {ElapsedMs} ms")]
    private partial void LogSnapshotCollected(long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Profiles could not be read; game crashes are not singled out in the report")]
    private partial void LogProfilesFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HAGS experiment state could not be read; the report shows it as unknown")]
    private partial void LogHagsFailed(Exception ex);
}
