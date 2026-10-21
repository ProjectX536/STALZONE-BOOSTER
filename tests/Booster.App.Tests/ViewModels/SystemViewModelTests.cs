using System.Globalization;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Hags;
using Booster.Core.Latency;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Booster.App.Tests.ViewModels;

public sealed class SystemViewModelTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(2000); // FakeConfiguration: activityIntervalMs

    private const string DiscordPath = @"C:\Users\Player\AppData\Local\Discord\Discord.exe";

    /// <summary>HKCU Run, включён; процесс Discord.exe из <see cref="SystemSamples.Processes"/> тяжелее порога — Candidate.</summary>
    private static readonly StartupEntry Discord = new("Discord", StartupLocation.UserRun, $"\"{DiscordPath}\"", DiscordPath,
        StartupState.Enabled);

    private readonly FakeGamingSettingsSource _gamingSettings = new();
    private readonly ScreenSession _screen;
    private readonly FakeSystemPressureSource _pressure = new();
    private readonly FakeProcessResourceSource _processes = new();
    private readonly FakePing _ping = new();
    private readonly FakeNetworkAdapters _adapters = new();
    private readonly FakeTimeProvider _networkTime = new(SessionFactory.Start);
    private readonly FakeUserRegistry _registry = new();
    private readonly ChangeJournal _journal = TestMaintenance.Journal();
    private readonly FakeGaming _gaming = new();
    private readonly FakeGamePicker _picker = new();
    private readonly FakeSettingsOpener _settings = new();
    private readonly InMemoryStateFileStore _state = new();
    private SystemViewModel? _system;

    public SystemViewModelTests()
    {
        _screen = new ScreenSession(collect: true, startup: new FakeStartupSource(Discord), gaming: _gamingSettings);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>Тест сети идёт на своих часах: пауза 0 мс — раунды без ожидания, иначе пауза ждёт, пока тест не сдвинет время.</summary>
    private SystemViewModel System(int pingCount = 2, int pingIntervalMs = 0)
    {
        _adapters.Adapters.Add(FakeNetworkAdapters.Adapter(gateways: "192.168.1.1"));
        var network = new NetworkTester(_ping, _adapters,
            new FakeConfiguration(network: new NetworkSettings(pingCount, pingIntervalMs, 1000, "example.org")), _networkTime);
        var maintenance = TestMaintenance.Create(_journal, _registry, _screen.Config, _screen.Time);
        var latency = new LatencyViewModel(new TraceRecorder(new FakeWprRunner(), _state, new FakeBootTimeSource(), _screen.Time,
            NullLogger<TraceRecorder>.Instance), _settings, NullLogger<LatencyViewModel>.Instance);
        var hags = new HagsViewModel(new HagsExperimentService(_state, new FakeHagsSource(), new FakeBootTimeSource(), new FakeBenchmarkStore(),
            _screen.Config, _screen.Time, NullLogger<HagsExperimentService>.Instance), _screen.Session, _settings, NullLogger<HagsViewModel>.Instance);
        _system = ScreenSession.OnUiContext(() => new SystemViewModel(_screen.Report, _screen.Session, _processes, _pressure, network,
            maintenance, _registry, _gaming.Coordinator, _picker, _settings, latency, hags, _screen.Config, _screen.Time,
            NullLogger<SystemViewModel>.Instance));
        return _system;
    }

    private async Task<SystemViewModel> CollectedAsync()
    {
        var system = System();
        await _screen.Session.RefreshAsync(Ct);
        await ScreenSession.WaitUntilAsync(() => system.HasData, "sections did not appear");
        return system;
    }

    private static Reading<ProcessIoActivity> Io(double readBytesPerSecond) => Reading<ProcessIoActivity>.Known(new ProcessIoActivity(
        [new ProcessIoUsage("game.exe", 42, readBytesPerSecond, 2048, 0)],
        [new ProcessIoUsage("indexer.exe", 7, 0, 0, 12.5)]), "test-io");

    private static string PerSecond(string value) => string.Format(Culture, Strings.SystemPerSecondFormat, value);

    [Fact]
    public async Task Sections_appear_after_snapshot()
    {
        var system = System();
        Assert.False(system.HasData);

        await _screen.Session.RefreshAsync(Ct);
        await ScreenSession.WaitUntilAsync(() => system.HasData, "sections did not appear");

        Assert.Equal([ReportSectionIds.SystemMemory, ReportSectionIds.Services, ReportSectionIds.Stability, ReportSectionIds.Network,
            ReportSectionIds.Startup, ReportSectionIds.Tweaks], system.Sections.Select(s => s.Id));
    }

    [Fact]
    public async Task Candidate_startup_row_can_be_disabled_after_confirmation()
    {
        var system = await CollectedAsync();
        var row = Assert.Single(system.StartupRows);
        Assert.Equal("Discord", row.Name);
        Assert.Equal(EnumText.Format(StartupVerdict.Candidate), row.Verdict);
        Assert.True(row.CanDisable);

        await system.RequestDisableStartupCommand.ExecuteAsync(row);
        Assert.Contains("Discord", system.PendingConfirmation, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.AdviceRisk_HeavyStartupItems, system.PendingConfirmation, StringComparison.Ordinal);

        await system.ConfirmMaintenanceCommand.ExecuteAsync(null);

        Assert.Null(system.PendingConfirmation);
        Assert.Equal(Strings.MaintenanceApplied, system.MaintenanceStatus);
        var value = Assert.IsType<UserRegistryValue>(_registry[StartupApprovedKeys.Run, "Discord"]);
        Assert.Equal(StartupState.Disabled, StartupApproval.Parse(value.ToBytes()));
        var session = Assert.Single((await _journal.LoadAsync(Ct)).Sessions);
        Assert.Equal(SessionKind.Maintenance, session.Kind);
        Assert.Equal(ChangeStatus.Applied, Assert.Single(session.Changes).Status);
    }

    [Fact]
    public async Task View_with_maintenance_rows_and_confirmation_loads()
    {
        var system = await CollectedAsync();
        await system.RequestDisableStartupCommand.ExecuteAsync(Assert.Single(system.StartupRows));
        Assert.NotNull(system.PendingConfirmation);

        Assert.Null(WpfHost.TryRender(() => new Booster.App.Views.SystemView { DataContext = system }));
    }

    [Fact]
    public async Task Opening_the_screen_reads_the_trace_marker()
    {
        var system = System();
        _state.Put(TraceRecorder.MarkerFileName, TraceMarkers.Json(FakeBootTimeSource.DefaultBoot.AddMinutes(5)));

        system.Activate();

        await ScreenSession.WaitUntilAsync(() => !system.Latency.IsBusy, "trace state was not read");
        Assert.Equal(TraceState.Recording, system.Latency.State);
        Assert.NotNull(system.Latency.Banner);
    }

    [Fact]
    public async Task Opening_the_screen_reads_the_hags_experiment()
    {
        var system = System();

        system.Activate();

        await ScreenSession.WaitUntilAsync(() => system.Hags.View is not null, "HAGS experiment was not read");
        Assert.Equal(HagsCheck.NotStarted, system.Hags.View!.Check);
        Assert.Same(system.Hags.View, _screen.Session.Hags);
    }

    [Fact]
    public async Task Request_without_confirmation_writes_nothing()
    {
        var system = await CollectedAsync();

        await system.RequestDisableStartupCommand.ExecuteAsync(system.StartupRows[0]);

        Assert.NotNull(system.PendingConfirmation);
        Assert.True(system.ConfirmMaintenanceCommand.CanExecute(null));
        Assert.Equal(0, _registry.Writes);
        Assert.Empty((await _journal.LoadAsync(Ct)).Sessions);
    }

    [Fact]
    public async Task Cancel_changes_nothing()
    {
        var system = await CollectedAsync();
        await system.RequestDisableStartupCommand.ExecuteAsync(system.StartupRows[0]);

        system.CancelMaintenanceCommand.Execute(null);
        // Команда без ожидающего изменения ничего не делает, даже если её вызвать в обход кнопки.
        await system.ConfirmMaintenanceCommand.ExecuteAsync(null);

        Assert.Null(system.PendingConfirmation);
        Assert.False(system.ConfirmMaintenanceCommand.CanExecute(null));
        Assert.Equal(0, _registry.Writes);
        Assert.Empty((await _journal.LoadAsync(Ct)).Sessions);
    }

    [Fact]
    public async Task Tweak_without_path_asks_for_executable_and_rejects_other_file()
    {
        var system = await CollectedAsync();
        var definition = _screen.Config.Current.Catalog.RegistryTweaks.Single(t => t.Id == RegistryTweakIds.WindowedFlipModel);
        var row = new TweakRow(new TweakProposal(definition, TweakState.Proposed, null, "game.exe", ["game.exe", "Composed: Copy with GPU GDI"]),
            "name", "state", "evidence", CanApply: true);

        _picker.Result = null;
        await system.RequestApplyTweakCommand.ExecuteAsync(row);
        Assert.Equal(["game.exe"], _picker.Requests);
        Assert.Null(system.PendingConfirmation);
        Assert.Null(system.MaintenanceStatus);

        _picker.Result = @"C:\Games\Launcher.exe";
        await system.RequestApplyTweakCommand.ExecuteAsync(row);
        Assert.Equal(string.Format(Culture, Strings.MaintenanceWrongFileFormat, "game.exe"), system.MaintenanceStatus);
        Assert.Null(system.PendingConfirmation);
        Assert.Equal(0, _registry.Reads);

        _picker.Result = @"C:\Games\Game.EXE";
        await system.RequestApplyTweakCommand.ExecuteAsync(row);
        Assert.Contains(@"C:\Games\Game.EXE", system.PendingConfirmation, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.AdviceRisk_LegacyWindowedPresent, system.PendingConfirmation, StringComparison.Ordinal);
        Assert.Null(system.MaintenanceStatus);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public async Task Game_mode_tweak_is_applied_after_confirmation()
    {
        _gamingSettings.Settings = new(Reading<bool>.Known(false, SystemSamples.Source), Reading<bool>.Known(false, SystemSamples.Source));
        var system = await CollectedAsync();
        var row = Assert.Single(system.TweakRows);
        Assert.Equal(RegistryTweakIds.GameMode, row.Proposal.Definition.Id);
        Assert.True(row.CanApply);

        await system.RequestApplyTweakCommand.ExecuteAsync(row);
        Assert.Contains(ReportStrings.AdviceRisk_GameModeOff, system.PendingConfirmation, StringComparison.Ordinal);
        await system.ConfirmMaintenanceCommand.ExecuteAsync(null);

        Assert.Empty(_picker.Requests);
        Assert.Equal(UserRegistryValue.FromDWord(1), _registry[@"Software\Microsoft\GameBar", "AutoGameModeEnabled"]);
        Assert.Equal(Strings.MaintenanceApplied, system.MaintenanceStatus);
    }

    [Fact]
    public void Settings_page_opens_through_settings_opener()
    {
        var system = System();

        system.OpenGamingSettingsCommand.Execute("gaming-gamemode");

        Assert.Equal(["gaming-gamemode"], _settings.Pages);
    }

    [Fact]
    public async Task Live_activity_updates_on_interval_after_activate()
    {
        _processes.Io = call => Io(call * 1024 * 1024);
        var system = System();

        system.Activate();
        await ScreenSession.WaitUntilAsync(() => _pressure.Calls == 1 && _processes.IoCalls == 1, "first sample was not taken");
        // Первый замер после открытия экрана — точка отсчёта скоростей, он не показывается.
        Assert.Empty(system.Pressure);
        Assert.Empty(system.TopIo);
        Assert.Equal(Strings.SystemActivityWaiting, system.ActivityStatus);

        _screen.Time.Advance(Interval);
        await ScreenSession.WaitUntilAsync(() => system.TopIo.Count > 0, "activity did not appear");

        var io = Assert.Single(system.TopIo);
        Assert.Equal(new ActivityRow("game.exe", 42, PerSecond(ByteSizeFormatter.Format(2 * 1024 * 1024, Culture)),
            PerSecond(ByteSizeFormatter.Format(2048, Culture)), PerSecond("0")), io);
        var faults = Assert.Single(system.TopHardFaults);
        Assert.Equal(PerSecond(12.5.ToString("0.#", Culture)), faults.HardFaults);
        Assert.Null(system.ActivityStatus);
        Assert.Equal([Strings.SystemAvailableMemory, Strings.SystemCommitPercent, Strings.SystemPageReads, Strings.SystemDiskBusy,
            Strings.SystemDiskLatency, Strings.SystemDpcTime, Strings.SystemInterruptTime], system.Pressure.Select(f => f.Label));
        Assert.Equal(SensorFieldFormatter.Percent(50, Culture), system.Pressure[0].Value.Text);
        Assert.Equal("test-pressure", system.Pressure[0].Value.Tooltip);

        _screen.Time.Advance(Interval);
        await ScreenSession.WaitUntilAsync(() => system.TopIo is [{ Read: var read }]
            && read == PerSecond(ByteSizeFormatter.Format(3 * 1024 * 1024, Culture)), "activity did not update");
        Assert.Equal(Interval.TotalMilliseconds, FakeConfiguration.DefaultDiagnostics.ActivityIntervalMs);
        Assert.Equal(FakeConfiguration.DefaultDiagnostics.TopProcessCount, _processes.LastTopCount);
    }

    [Fact]
    public async Task Live_activity_stops_on_deactivate()
    {
        var system = System();
        system.Activate();
        await ScreenSession.WaitUntilAsync(() => _pressure.Calls == 1, "first sample was not taken");

        system.Deactivate();
        _screen.Time.Advance(Interval);
        _screen.Time.Advance(Interval);
        await Task.Delay(50, Ct);

        Assert.Equal(1, _pressure.Calls);
        Assert.Equal(1, _processes.IoCalls);
    }

    [Fact]
    public async Task Pressure_shows_dpc_and_interrupt_time()
    {
        // Доли процента: целые проценты показали бы 0 % там, где DPC уже заметны.
        _pressure.Answer = _ => FakeSystemPressureSource.Pressure(50, 40, 0, 0, 0, dpcTime: 0.4, interruptTime: 1.25);
        var system = System();

        system.Activate();
        await ScreenSession.WaitUntilAsync(() => _pressure.Calls == 1, "first sample was not taken");
        _screen.Time.Advance(Interval);
        await ScreenSession.WaitUntilAsync(() => system.Pressure.Count > 0, "pressure did not appear");

        Assert.Equal(SensorFieldFormatter.FractionPercent(0.4, Culture), system.Pressure.Single(f => f.Label == Strings.SystemDpcTime).Value.Text);
        Assert.Equal(SensorFieldFormatter.FractionPercent(1.25, Culture),
            system.Pressure.Single(f => f.Label == Strings.SystemInterruptTime).Value.Text);
        Assert.Equal(string.Format(Culture, ReportStrings.UnitPercent, 0.4.ToString("0.0#", Culture)), SensorFieldFormatter.FractionPercent(0.4, Culture));
    }

    [Fact]
    public async Task Live_activity_survives_source_failure()
    {
        _processes.Io = call => Io(1024);
        _processes.FailIoOnCall = 2;
        _pressure.FailOnCall = 2;
        var system = System();

        system.Activate();
        await ScreenSession.WaitUntilAsync(() => _pressure.Calls == 1, "first sample was not taken");
        _screen.Time.Advance(Interval);
        await ScreenSession.WaitUntilAsync(() => system.Pressure.Count > 0, "pressure status did not appear");

        var failed = ReadingFormatter.StatusText(DataStatus.Failed);
        Assert.All(system.Pressure, f => Assert.Equal(failed, f.Value.Text));
        Assert.Empty(system.TopIo);
        Assert.Empty(system.TopHardFaults);
        Assert.Equal(failed, system.ActivityStatus);

        _screen.Time.Advance(Interval);
        await ScreenSession.WaitUntilAsync(() => system.TopIo.Count > 0, "activity did not recover");
        Assert.Equal(SensorFieldFormatter.Percent(50, Culture), system.Pressure[0].Value.Text);
    }

    [Fact]
    public async Task Network_test_result_goes_to_session_and_report()
    {
        var system = System();
        await _screen.Session.RefreshAsync(Ct);
        await ScreenSession.WaitUntilAsync(() => system.HasData, "sections did not appear");

        await system.RunNetworkTestCommand.ExecuteAsync(null);

        var network = Assert.IsType<NetworkTestResult>(_screen.Session.Network);
        Assert.Equal([NetworkTargetKind.Gateway, NetworkTargetKind.Host], network.Targets.Select(t => t.Kind));
        Assert.Equal(Strings.SystemNetworkDone, system.NetworkStatus);
        await ScreenSession.WaitUntilAsync(() => system.Sections.FirstOrDefault(s => s.Id == ReportSectionIds.Network) is { } section
            && section.Blocks.OfType<ReportTable>().Any(t => t.Headers[0] == ReportStrings.ColumnTarget), "network test is not in the report");
    }

    [Fact]
    public async Task Network_test_cancel_sets_status()
    {
        var system = System(pingCount: 30, pingIntervalMs: 1000);

        var run = system.RunNetworkTestCommand.ExecuteAsync(null);
        var firstRound = string.Format(Culture, Strings.SystemNetworkRoundFormat, 1, 30);
        await ScreenSession.WaitUntilAsync(() => system.NetworkStatus == firstRound, "progress was not shown");
        system.RunNetworkTestCancelCommand.Execute(null);
        await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(Strings.SystemNetworkCancelled, system.NetworkStatus);
        Assert.Null(_screen.Session.Network);
    }

    public async ValueTask DisposeAsync()
    {
        _system?.Deactivate();
        _screen.Dispose();
        await _gaming.DisposeAsync();
    }

    private sealed class FakeGamePicker : IGameExecutablePicker
    {
        public List<string> Requests { get; } = [];

        /// <summary>Что «выбрал» пользователь; null — отмена.</summary>
        public string? Result { get; set; }

        public string? Pick(string expectedFileName)
        {
            Requests.Add(expectedFileName);
            return Result;
        }
    }
}
