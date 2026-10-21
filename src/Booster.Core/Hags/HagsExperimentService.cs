using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Hags;

public sealed partial class HagsExperimentService(IStateFileStore state, IHagsSource hags, IBootTimeSource boot,
    IBenchmarkStore series, IBoosterConfiguration config, TimeProvider time, ILogger<HagsExperimentService> logger)
{
    public const string FileName = "hags-experiment.json";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<HagsExperimentView> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return (await EvaluateAsync(ct).ConfigureAwait(false)).View;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HagsStartRefusal?> StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await state.ReadAsync(FileName, ct).ConfigureAwait(false) is not null)
                return HagsStartRefusal.AlreadyStarted;
            var current = hags.ReadEnabled();
            var changedAt = hags.ReadChangedAt();
            var lastBoot = boot.LastBootTime();
            if (!current.IsAvailable || !changedAt.IsAvailable || !lastBoot.IsAvailable)
                return HagsStartRefusal.StateUnknown;
            if (changedAt.Value >= lastBoot.Value)
                return HagsStartRefusal.NeedsRestart;

            var baseline = (await series.LoadAllAsync(ct).ConfigureAwait(false)).FirstOrDefault(IsCompleted);
            if (baseline is null)
                return HagsStartRefusal.NoCompletedSeries;
            if (baseline.StartedAt <= lastBoot.Value)
                return HagsStartRefusal.SeriesBeforeBoot;

            var now = time.GetUtcNow();
            await SaveAsync(new HagsExperiment(HagsExperiment.CurrentSchemaVersion, now, baseline.Conditions.Target, baseline.Id, current.Value,
                HagsExperimentStage.AwaitingSwitch, SwitchedBootTime: null, CandidateSeriesId: null, UnchangedSeenAt: now), ct).ConfigureAwait(false);
            LogStarted(baseline.Id, current.Value);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HagsCandidateRefusal?> TakeCandidateAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (view, all) = await EvaluateAsync(ct).ConfigureAwait(false);
            if (view.Check == HagsCheck.SwitchedBack)
                return HagsCandidateRefusal.SwitchedBack;
            if (view.Check != HagsCheck.ReadyForCandidate || view.Experiment is not { } experiment)
                return HagsCandidateRefusal.NotReady;

            var candidate = all.FirstOrDefault(s => IsCompleted(s)
                && !string.Equals(s.Id, experiment.BaselineSeriesId, StringComparison.Ordinal)
                && string.Equals(s.Conditions.Target, experiment.Target, StringComparison.OrdinalIgnoreCase)
                && s.StartedAt > experiment.SwitchedBootTime);
            if (candidate is null)
                return HagsCandidateRefusal.NoCandidateSeries;

            await SaveAsync(experiment with { Stage = HagsExperimentStage.Completed, CandidateSeriesId = candidate.Id }, ct).ConfigureAwait(false);
            LogCandidateTaken(candidate.Id);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            state.Delete(FileName);
            LogReset();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(HagsExperimentView View, IReadOnlyList<BenchmarkSeries> Series)> EvaluateAsync(CancellationToken ct)
    {
        var current = hags.ReadEnabled();
        var json = await state.ReadAsync(FileName, ct).ConfigureAwait(false);
        if (json is null)
            return (new HagsExperimentView(null, HagsCheck.NotStarted, current, null, null), []);
        if (HagsExperimentJson.TryDeserialize(json) is not { } experiment)
        {
            LogUnreadable();
            return (new HagsExperimentView(null, HagsCheck.Unreadable, current, null, null), []);
        }

        var all = await series.LoadAllAsync(ct).ConfigureAwait(false);
        HagsExperimentView View(HagsExperiment shown, HagsCheck check, BenchmarkComparison? comparison = null) =>
            new(shown, check, current, comparison, comparison is null ? null : HagsVerdicts.From(comparison));

        if (Find(all, experiment.BaselineSeriesId) is not { } baseline)
            return (View(experiment, HagsCheck.BaselineMissing), all);

        switch (experiment.Stage)
        {
            case HagsExperimentStage.AwaitingSwitch:
                var (checkedExperiment, check) = await CheckSwitchAsync(experiment, current, ct).ConfigureAwait(false);
                return (View(checkedExperiment, check), all);
            case HagsExperimentStage.AwaitingCandidate when !current.IsAvailable:
                return (View(experiment, HagsCheck.StateUnknown), all);
            case HagsExperimentStage.AwaitingCandidate:
                return (View(experiment, current.Value == experiment.BaselineEnabled ? HagsCheck.SwitchedBack : HagsCheck.ReadyForCandidate), all);
            default:
                if (Find(all, experiment.CandidateSeriesId!) is not { } candidate)
                    return (View(experiment, HagsCheck.CandidateMissing), all);
                return (View(experiment, HagsCheck.Completed, BenchmarkComparer.Compare(baseline, candidate, config.Current.Thresholds.Comparison)),
                    all);
        }
    }

    private async Task<(HagsExperiment Experiment, HagsCheck Check)> CheckSwitchAsync(HagsExperiment experiment, Reading<bool> current,
        CancellationToken ct)
    {
        if (!current.IsAvailable)
            return (experiment, HagsCheck.StateUnknown);

        var lastBoot = boot.LastBootTime();
        if (current.Value == experiment.BaselineEnabled)
        {
            if (lastBoot.IsAvailable && lastBoot.Value > experiment.UnchangedSeenAt)
            {
                experiment = experiment with { UnchangedSeenAt = time.GetUtcNow() };
                await SaveAsync(experiment, ct).ConfigureAwait(false);
            }

            return (experiment, HagsCheck.WaitingForSwitch);
        }

        var changedAt = hags.ReadChangedAt();
        if (!lastBoot.IsAvailable || !changedAt.IsAvailable)
            return (experiment, HagsCheck.StateUnknown);
        if (lastBoot.Value <= experiment.UnchangedSeenAt || lastBoot.Value <= changedAt.Value)
            return (experiment, HagsCheck.NeedsRestart);

        experiment = experiment with { Stage = HagsExperimentStage.AwaitingCandidate, SwitchedBootTime = lastBoot.Value };
        await SaveAsync(experiment, ct).ConfigureAwait(false);
        LogSwitched(current.Value, lastBoot.Value);
        return (experiment, HagsCheck.ReadyForCandidate);
    }

    private Task SaveAsync(HagsExperiment experiment, CancellationToken ct) => state.WriteAsync(FileName, HagsExperimentJson.Serialize(experiment), ct);

    private static bool IsCompleted(BenchmarkSeries s) => s.Status == FrameCaptureStatus.Completed && s.HasValidRuns;

    private static BenchmarkSeries? Find(IReadOnlyList<BenchmarkSeries> all, string id) =>
        all.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    [LoggerMessage(Level = LogLevel.Information, Message = "HAGS experiment started: baseline series {SeriesId}, HAGS enabled {Enabled}")]
    private partial void LogStarted(string seriesId, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "HAGS enabled {Enabled} is effective since the boot at {BootTime}")]
    private partial void LogSwitched(bool enabled, DateTimeOffset bootTime);

    [LoggerMessage(Level = LogLevel.Information, Message = "HAGS candidate series {SeriesId} taken")]
    private partial void LogCandidateTaken(string seriesId);

    [LoggerMessage(Level = LogLevel.Information, Message = "HAGS experiment reset")]
    private partial void LogReset();

    [LoggerMessage(Level = LogLevel.Warning, Message = "HAGS experiment file is unreadable or has a newer schema")]
    private partial void LogUnreadable();
}
