using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.SystemHealth;

public class TweakAdvisorTests
{
    private const string Integrated = "Intel(R) UHD Graphics 770";
    private const string Discrete = "NVIDIA GeForce RTX 4060 Laptop GPU";
    private const string GamePath = @"D:\Games\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";
    private const string Target = "cyberpunk2077.exe";
    private const string CopyMode = "Composed: Copy with GPU GDI";

    // Профиль хранит имя с другим регистром и без «.exe»: сравнение идёт по нормализованному имени.
    private static readonly IReadOnlyList<string> Games = ["Cyberpunk2077"];

    private static readonly IReadOnlyList<RegistryTweakDefinition> Catalog = FakeConfiguration.DefaultCatalog.RegistryTweaks;

    private static Reading<T> Known<T>(T value) => Reading<T>.Known(value, SystemSamples.Source);

    /// <summary>Ноутбук со встроенной и дискретной видеокартой; Game Mode включён.</summary>
    private static SystemSnapshot Laptop(IReadOnlyList<AppGpuPreference>? preferences = null, Reading<int>? build = null,
        Reading<GamingSettings>? gaming = null)
    {
        var snapshot = SnapshotFactory.Healthy();
        var hardware = snapshot.Hardware;
        var adapter = hardware.Gpu.Value!.Adapters[0];
        var gpu = new GpuInventory(
            [
                adapter with { Name = Integrated, Vendor = GpuVendor.Intel, Kind = Known(GpuAdapterKind.Integrated) },
                adapter with { Name = Discrete },
            ],
            Known(true), Known(preferences ?? []));
        var os = hardware.Os.Value! with { BuildNumber = build ?? Known(26300) };
        return snapshot with
        {
            Hardware = hardware with { Gpu = Known(gpu), Os = Known(os) },
            System = gaming is { } value ? snapshot.System with { Gaming = value } : snapshot.System,
        };
    }

    private static Reading<GamingSettings> GameMode(Reading<bool> gameMode) => Known(new GamingSettings(gameMode, Known(false)));

    private static BenchmarkSeries Series(string adapter = Integrated, IReadOnlyList<string>? modes = null, string target = Target)
    {
        var series = SampleSeries.Full();
        return series with
        {
            Conditions = series.Conditions with
            {
                Target = target,
                RenderAdapter = Known(adapter),
                PresentModes = modes ?? ["Hardware: Independent Flip"],
            },
        };
    }

    private static TweakProposal Proposal(IReadOnlyList<TweakProposal> proposals, string id) =>
        Assert.Single(proposals, p => p.Definition.Id == id);

    [Fact]
    public void Game_mode_off_is_proposed()
    {
        var proposals = TweakAdvisor.Propose(Laptop(gaming: GameMode(Known(false))), null, Games, Catalog);

        Assert.Equal([RegistryTweakIds.GameMode, RegistryTweakIds.GpuHighPerformance, RegistryTweakIds.WindowedFlipModel],
            proposals.Select(p => p.Definition.Id));
        var gameMode = Proposal(proposals, RegistryTweakIds.GameMode);
        Assert.Equal(TweakState.Proposed, gameMode.State);
        Assert.Equal("AutoGameModeEnabled", gameMode.ValueName);
        Assert.Null(gameMode.Game);
        Assert.Empty(gameMode.Evidence);
        // Без серии правила по серии не срабатывают.
        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.GpuHighPerformance).State);
        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.WindowedFlipModel).State);

        var advice = Assert.Single(TweakAdvisor.Advise(proposals));
        Assert.Equal((AdviceId.GameModeOff, AdviceModule.Tweaks, FindingSeverity.Info, SafetyLevel.L2Confirmed),
            (advice.Id, advice.Module, advice.Severity, advice.Level));
        Assert.Empty(advice.Arguments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Game_mode_unknown_is_not_needed(bool sectionAvailable)
    {
        var gaming = sectionAvailable
            ? GameMode(Reading<bool>.Unknown(SystemSamples.Source, "value 7"))
            : Reading<GamingSettings>.Failure(SystemSamples.Source, "timeout");

        var proposals = TweakAdvisor.Propose(Laptop(gaming: gaming), null, Games, Catalog);

        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.GameMode).State);
        Assert.Empty(TweakAdvisor.Advise(proposals));
    }

    [Fact]
    public void Game_mode_on_is_not_needed()
    {
        var proposals = TweakAdvisor.Propose(Laptop(), null, Games, Catalog);

        Assert.All(proposals, p => Assert.Equal(TweakState.NotNeeded, p.State));
        Assert.Empty(TweakAdvisor.Advise(proposals));
    }

    [Fact]
    public void Integrated_render_adapter_on_hybrid_is_proposed_with_known_path()
    {
        var snapshot = Laptop([new AppGpuPreference(@"C:\Tools\other.exe", AppGpuPreferenceKind.PowerSaving),
            new AppGpuPreference(GamePath, AppGpuPreferenceKind.SystemDefault)]);

        var proposals = TweakAdvisor.Propose(snapshot, Series(), Games, Catalog);

        var gpu = Proposal(proposals, RegistryTweakIds.GpuHighPerformance);
        Assert.Equal(TweakState.Proposed, gpu.State);
        Assert.Equal(GamePath, gpu.ValueName);
        Assert.Equal(Target, gpu.Game);
        Assert.Equal([Target, Integrated], gpu.Evidence);

        var advice = Assert.Single(TweakAdvisor.Advise(proposals));
        Assert.Equal((AdviceId.GpuOnIntegrated, AdviceModule.Tweaks, FindingSeverity.Warning, SafetyLevel.L2Confirmed),
            (advice.Id, advice.Module, advice.Severity, advice.Level));
        Assert.Equal([Target, Integrated], advice.Arguments);
    }

    [Fact]
    public void Integrated_render_adapter_without_path_is_proposed_without_value_name()
    {
        var proposals = TweakAdvisor.Propose(Laptop(), Series(), Games, Catalog);

        var gpu = Proposal(proposals, RegistryTweakIds.GpuHighPerformance);
        Assert.Equal(TweakState.Proposed, gpu.State);
        Assert.Null(gpu.ValueName);
        Assert.Equal(Target, gpu.Game);
    }

    [Fact]
    public void Explicit_power_saving_choice_is_not_overridden()
    {
        var snapshot = Laptop([new AppGpuPreference(GamePath.ToUpperInvariant(), AppGpuPreferenceKind.PowerSaving)]);

        var proposals = TweakAdvisor.Propose(snapshot, Series(), Games, Catalog);

        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.GpuHighPerformance).State);
        Assert.Empty(TweakAdvisor.Advise(proposals));
    }

    [Fact]
    public void Discrete_render_adapter_is_not_needed()
    {
        var proposals = TweakAdvisor.Propose(Laptop(), Series(adapter: Discrete), Games, Catalog);

        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.GpuHighPerformance).State);
    }

    [Fact]
    public void Single_integrated_gpu_is_not_needed()
    {
        var snapshot = Laptop();
        var gpu = snapshot.Hardware.Gpu.Value!;
        snapshot = snapshot with
        {
            Hardware = snapshot.Hardware with { Gpu = Known(gpu with { Adapters = [gpu.Adapters[0]], IsHybrid = Known(false) }) },
        };

        var proposals = TweakAdvisor.Propose(snapshot, Series(), Games, Catalog);

        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.GpuHighPerformance).State);
    }

    [Fact]
    public void Series_of_non_profile_game_is_ignored()
    {
        var series = Series(modes: [CopyMode], target: "launcher.exe");

        var proposals = TweakAdvisor.Propose(Laptop(), series, Games, Catalog);

        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.GpuHighPerformance).State);
        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.WindowedFlipModel).State);
    }

    [Fact]
    public void Legacy_copy_present_mode_is_proposed()
    {
        var snapshot = Laptop([new AppGpuPreference(GamePath, AppGpuPreferenceKind.HighPerformance)]);

        var proposals = TweakAdvisor.Propose(snapshot, Series(adapter: Discrete, modes: ["Hardware: Independent Flip", CopyMode]), Games,
            Catalog);

        var flip = Proposal(proposals, RegistryTweakIds.WindowedFlipModel);
        Assert.Equal(TweakState.Proposed, flip.State);
        Assert.Equal(GamePath, flip.ValueName);
        Assert.Equal(Target, flip.Game);
        Assert.Equal([Target, CopyMode], flip.Evidence);

        var advice = Assert.Single(TweakAdvisor.Advise(proposals));
        Assert.Equal((AdviceId.LegacyWindowedPresent, FindingSeverity.Info, SafetyLevel.L2Confirmed),
            (advice.Id, advice.Severity, advice.Level));
        Assert.Equal([Target, CopyMode], advice.Arguments);
    }

    [Fact]
    public void Composed_flip_is_not_needed()
    {
        var proposals = TweakAdvisor.Propose(Laptop(), Series(adapter: Discrete, modes: ["Composed: Flip"]), Games, Catalog);

        Assert.Equal(TweakState.NotNeeded, Proposal(proposals, RegistryTweakIds.WindowedFlipModel).State);
    }

    [Fact]
    public void Old_or_unknown_build_is_manual_only()
    {
        // Сборка 19045 (Windows 10 22H2): Game Mode и выбор GPU уже есть, оптимизации оконного режима ещё нет.
        var windows10 = Laptop(build: Known(19045), gaming: GameMode(Known(false)));
        var unknown = Laptop(build: Reading<int>.Unknown(SystemSamples.Source), gaming: GameMode(Known(false)));
        var series = Series(modes: [CopyMode]);

        var old = TweakAdvisor.Propose(windows10, series, Games, Catalog);
        var unread = TweakAdvisor.Propose(unknown, series, Games, Catalog);

        Assert.Equal([TweakState.Proposed, TweakState.Proposed, TweakState.ManualOnly], old.Select(p => p.State));
        Assert.All(unread, p => Assert.Equal(TweakState.ManualOnly, p.State));
        Assert.Equal([SafetyLevel.L3ManualOnly, SafetyLevel.L3ManualOnly, SafetyLevel.L3ManualOnly],
            TweakAdvisor.Advise(unread).Select(a => a.Level));
        Assert.Equal([AdviceId.GameModeOff, AdviceId.GpuOnIntegrated, AdviceId.LegacyWindowedPresent],
            TweakAdvisor.Advise(old).Select(a => a.Id));
    }

    [Fact]
    public void Series_without_valid_runs_is_ignored()
    {
        var series = Series(modes: [CopyMode]) with { Summary = null };

        var proposals = TweakAdvisor.Propose(Laptop(), series, Games, Catalog);

        Assert.All(proposals, p => Assert.Equal(TweakState.NotNeeded, p.State));
    }

    [Fact]
    public void Definitions_with_other_ids_are_skipped()
    {
        IReadOnlyList<RegistryTweakDefinition> catalog = [.. Catalog, new RegistryTweakDefinition("network-throttling",
            @"Software\Microsoft\GameBar", "Unknown", "1", 0)];

        var proposals = TweakAdvisor.Propose(Laptop(), Series(), Games, catalog);

        Assert.DoesNotContain(proposals, p => p.Definition.Id == "network-throttling");
    }
}
