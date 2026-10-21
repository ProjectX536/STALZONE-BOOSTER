using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.SystemHealth;

public class StartupAnalyzerTests
{
    private const long Mb = 1024 * 1024;

    private static readonly StartupThresholds Thresholds = new(MinPrivateMemoryMb: 50);
    private static readonly ServiceCatalog Catalog = FakeConfiguration.DefaultCatalog.Services;

    private static StartupItem Item(string name, StartupLocation location = StartupLocation.UserRun, long? privateBytes = 200 * Mb,
        StartupState state = StartupState.Enabled, string? executable = null, string? command = null)
    {
        var path = executable ?? $@"C:\Program Files\{name}\{name}.exe";
        return new StartupItem(new StartupEntry(name, location, command ?? $"\"{path}\" --tray", path, state), privateBytes);
    }

    private static StartupVerdict Verdict(StartupItem item) =>
        Assert.Single(StartupAnalyzer.Assess([item], Thresholds, Catalog)).Verdict;

    [Fact]
    public void Heavy_user_item_is_candidate()
    {
        Assert.Equal(StartupVerdict.Candidate, Verdict(Item("Spotify")));
        Assert.Equal(StartupVerdict.Candidate, Verdict(Item("Spotify.lnk", StartupLocation.UserFolder, executable: @"C:\Spotify\Spotify.exe",
            command: @"C:\Startup\Spotify.lnk")));
    }

    [Theory]
    [InlineData(StartupLocation.MachineRun)]
    [InlineData(StartupLocation.MachineRun32)]
    [InlineData(StartupLocation.CommonFolder)]
    public void Heavy_machine_item_needs_admin(StartupLocation location)
    {
        Assert.Equal(StartupVerdict.NeedsAdmin, Verdict(Item("Steam", location)));
    }

    [Fact]
    public void Security_and_anti_cheat_items_are_protected()
    {
        Assert.Equal(StartupVerdict.Protected, Verdict(Item("SecurityHealth", StartupLocation.MachineRun)));
        Assert.Equal(StartupVerdict.Protected, Verdict(Item("Tray", command: @"""C:\Program Files\Kaspersky Lab\avpui.exe"" -tray")));
        Assert.Equal(StartupVerdict.Protected, Verdict(Item("FACEIT")));
        // Ярлык с безобидным именем, цель которого — античит.
        Assert.Equal(StartupVerdict.Protected, Verdict(Item("Helper.lnk", StartupLocation.UserFolder,
            executable: @"C:\Program Files\EasyAntiCheat\launcher.exe", command: @"C:\Startup\Helper.lnk")));
    }

    [Fact]
    public void Item_below_threshold_is_light()
    {
        Assert.Equal(StartupVerdict.Light, Verdict(Item("Tool", privateBytes: 50 * Mb - 1)));
        Assert.Equal(StartupVerdict.Light, Verdict(Item("Steam", StartupLocation.MachineRun, privateBytes: 10 * Mb)));
        Assert.Equal(StartupVerdict.Candidate, Verdict(Item("Tool", privateBytes: 50 * Mb)));
    }

    [Fact]
    public void Item_without_process_is_not_running()
    {
        Assert.Equal(StartupVerdict.NotRunning, Verdict(Item("Discord", privateBytes: null)));
    }

    [Fact]
    public void Disabled_item_is_disabled_even_if_running()
    {
        Assert.Equal(StartupVerdict.Disabled, Verdict(Item("Spotify", state: StartupState.Disabled)));
        Assert.Equal(StartupVerdict.Disabled, Verdict(Item("Kaspersky", state: StartupState.Disabled)));
    }

    [Fact]
    public void Run_once_unknown_executable_and_unknown_state_are_unknown()
    {
        Assert.Equal(StartupVerdict.Unknown, Verdict(Item("Setup", StartupLocation.UserRunOnce)));
        Assert.Equal(StartupVerdict.Unknown, Verdict(Item("Cleanup", StartupLocation.MachineRunOnce)));
        Assert.Equal(StartupVerdict.Unknown, Verdict(Item("Spotify", state: StartupState.Unknown)));
        Assert.Equal(StartupVerdict.Unknown, Verdict(
            new StartupItem(new StartupEntry("Broken.lnk", StartupLocation.UserFolder, @"C:\Startup\Broken.lnk", null, StartupState.Enabled),
                200 * Mb)));
    }

    [Fact]
    public void Advice_lists_candidates_and_admin_items_separately()
    {
        var assessed = StartupAnalyzer.Assess(
            [
                Item("Spotify"),
                Item("Steam", StartupLocation.MachineRun),
                Item("Light", privateBytes: Mb),
                Item("Teams.lnk", StartupLocation.UserFolder, executable: @"C:\Teams\ms-teams.exe", command: @"C:\Startup\Teams.lnk"),
                Item("Audio", StartupLocation.CommonFolder),
                Item("Kaspersky"),
            ],
            Thresholds, Catalog);

        Assert.Equal(
            [
                (AdviceId.HeavyStartupItems, AdviceModule.Startup, FindingSeverity.Info, SafetyLevel.L2Confirmed, "Spotify, Teams.lnk"),
                (AdviceId.HeavyMachineStartupItems, AdviceModule.Startup, FindingSeverity.Info, SafetyLevel.L2Confirmed, "Steam, Audio"),
            ],
            StartupAnalyzer.Advise(assessed).Select(a => (a.Id, a.Module, a.Severity, a.Level, Assert.Single(a.Arguments))));
    }

    [Fact]
    public void No_advice_when_nothing_is_heavy()
    {
        var assessed = StartupAnalyzer.Assess(
            [
                Item("Light", privateBytes: Mb),
                Item("Discord", privateBytes: null),
                Item("Spotify", state: StartupState.Disabled),
                Item("Setup", StartupLocation.UserRunOnce),
            ],
            Thresholds, Catalog);

        Assert.Empty(StartupAnalyzer.Advise(assessed));
    }

    [Theory]
    [InlineData(StartupLocation.UserRun)]
    [InlineData(StartupLocation.UserFolder)]
    public void Any_enabled_user_item_can_be_disabled_whatever_its_weight(StartupLocation location)
    {
        Assert.Equal(StartupDisableAvailability.Available, Availability(Item("Light", location, privateBytes: Mb), isElevated: false));
        Assert.Equal(StartupDisableAvailability.Available, Availability(Item("Discord", location, privateBytes: null), isElevated: false));
        Assert.Equal(StartupDisableAvailability.Available, Availability(Item("Spotify", location), isElevated: false));
    }

    [Theory]
    [InlineData(StartupLocation.MachineRun)]
    [InlineData(StartupLocation.MachineRun32)]
    [InlineData(StartupLocation.CommonFolder)]
    public void Machine_item_needs_elevated_booster(StartupLocation location)
    {
        Assert.Equal(StartupDisableAvailability.NeedsAdmin, Availability(Item("Steam", location), isElevated: false));
        Assert.Equal(StartupDisableAvailability.Available, Availability(Item("Steam", location), isElevated: true));
    }

    [Theory]
    [InlineData(StartupLocation.UserRunOnce)]
    [InlineData(StartupLocation.MachineRunOnce)]
    public void Run_once_item_cannot_be_disabled(StartupLocation location)
    {
        Assert.Equal(StartupDisableAvailability.RunOnce, Availability(Item("Setup", location), isElevated: true));
    }

    [Fact]
    public void Disabled_and_unknown_state_items_cannot_be_disabled()
    {
        Assert.Equal(StartupDisableAvailability.AlreadyDisabled,
            Availability(Item("Spotify", state: StartupState.Disabled), isElevated: true));
        Assert.Equal(StartupDisableAvailability.StateUnknown, Availability(Item("Spotify", state: StartupState.Unknown), isElevated: true));
    }

    [Fact]
    public void Protection_and_anti_cheat_are_never_offered()
    {
        Assert.Equal(StartupDisableAvailability.Protected, Availability(Item("SecurityHealth", StartupLocation.MachineRun), isElevated: true));
        Assert.Equal(StartupDisableAvailability.Protected, Availability(Item("FACEIT", privateBytes: Mb), isElevated: true));
        // Exe неизвестен (вердикт Unknown), но команда говорит о защите: проверка защиты от вердикта не зависит.
        var unknownExe = new StartupItem(new StartupEntry("Tray", StartupLocation.UserRun, @"""C:\Program Files\Kaspersky Lab\avpui.exe"" -tray",
            null, StartupState.Enabled), null);
        Assert.Equal(StartupDisableAvailability.Protected, Availability(unknownExe, isElevated: true));
    }

    private static StartupDisableAvailability Availability(StartupItem item, bool isElevated) =>
        StartupAnalyzer.DisableAvailability(item.Entry, Catalog, isElevated);
}
