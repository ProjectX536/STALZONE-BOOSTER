using Booster.Core.Abstractions;
using Booster.Core.SystemHealth;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Hardware;

public class StartupSourceTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";

    private static readonly byte[] Off = StartupApproval.Disabled(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly byte[] On = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private readonly FakeRegistryReader _registry = new();
    private readonly FakeStartupFolders _folders = new();

    private StartupSource Source() => new(_registry, _folders, NullLogger<StartupSource>.Instance);

    private sealed class FakeStartupFolders : IStartupFolders
    {
        public Dictionary<StartupLocation, string[]> Files { get; } = [];

        public Dictionary<string, string?> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Fail { get; set; }

        public IReadOnlyList<string> List(StartupLocation location) =>
            Fail ? throw new IOException("folder unavailable") : Files.GetValueOrDefault(location) ?? [];

        public string? ResolveShortcut(string path) => Targets.GetValueOrDefault(path);
    }

    [Fact]
    public void Reads_all_locations_with_approval_state()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, Run, "Discord", @"""C:\Discord\Update.exe"" --processStart Discord.exe")
            .Set(RegistryRoot.CurrentUser, StartupApprovedKeys.Run, "discord", Off)
            .Set(RegistryRoot.LocalMachine, Run, "Audio", @"C:\Program Files\Audio\tray.exe /min")
            .Set(RegistryRoot.LocalMachine, StartupApprovedKeys.Run, "Audio", On)
            .Set(RegistryRoot.LocalMachine, Run32, "Steam", @"""C:\Steam\steam.exe"" -silent")
            .Set(RegistryRoot.LocalMachine, ApprovedRun32, "Steam", Off)
            .Set(RegistryRoot.LocalMachine, StartupApprovedKeys.StartupFolder, "tool.exe", Array.Empty<byte>());
        _folders.Files[StartupLocation.UserFolder] = [@"C:\Startup\Spotify.lnk", @"C:\Startup\desktop.ini"];
        _folders.Targets[@"C:\Startup\Spotify.lnk"] = @"C:\Spotify\Spotify.exe";
        _folders.Files[StartupLocation.CommonFolder] = [@"C:\Common\tool.exe"];
        _registry.Set(RegistryRoot.CurrentUser, StartupApprovedKeys.StartupFolder, "Spotify.lnk", Off);

        var entries = Source().Read();

        Assert.Equal(
            [
                new StartupEntry("Discord", StartupLocation.UserRun, @"""C:\Discord\Update.exe"" --processStart Discord.exe",
                    @"C:\Discord\Update.exe", StartupState.Disabled),
                new StartupEntry("Audio", StartupLocation.MachineRun, @"C:\Program Files\Audio\tray.exe /min",
                    @"C:\Program Files\Audio\tray.exe", StartupState.Enabled),
                new StartupEntry("Steam", StartupLocation.MachineRun32, @"""C:\Steam\steam.exe"" -silent", @"C:\Steam\steam.exe",
                    StartupState.Disabled),
                new StartupEntry("Spotify.lnk", StartupLocation.UserFolder, @"C:\Startup\Spotify.lnk", @"C:\Spotify\Spotify.exe",
                    StartupState.Disabled),
                new StartupEntry("tool.exe", StartupLocation.CommonFolder, @"C:\Common\tool.exe", @"C:\Common\tool.exe", StartupState.Unknown),
            ],
            entries);
    }

    [Fact]
    public void Run_once_is_enabled_without_approval()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, RunOnce, "Setup", @"C:\Setup\finish.exe /quiet")
            .Set(RegistryRoot.CurrentUser, StartupApprovedKeys.Run, "Setup", Off)
            .Set(RegistryRoot.LocalMachine, RunOnce, "Cleanup", @"C:\Setup\cleanup.exe");

        var entries = Source().Read();

        Assert.Equal([StartupLocation.UserRunOnce, StartupLocation.MachineRunOnce], entries.Select(e => e.Location));
        Assert.All(entries, e => Assert.Equal(StartupState.Enabled, e.State));
    }

    [Fact]
    public void Quoted_and_unquoted_commands_resolve_executable()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, Run, "Quoted", @"""C:\Program Files\App\app.exe"" --tray")
            .Set(RegistryRoot.CurrentUser, Run, "Unquoted", @"C:\Program Files\Tool\tool.exe -background")
            .Set(RegistryRoot.CurrentUser, Run, "Rundll", "rundll32.exe shell32.dll,Control_RunDLL")
            .Set(RegistryRoot.CurrentUser, Run, "Empty", "")
            .Set(RegistryRoot.CurrentUser, Run, "Binary", new byte[] { 1, 2 });

        var entries = Source().Read().ToDictionary(e => e.Name, e => e.ExecutablePath);

        Assert.Equal(@"C:\Program Files\App\app.exe", entries["Quoted"]);
        Assert.Equal(@"C:\Program Files\Tool\tool.exe", entries["Unquoted"]);
        Assert.Equal("rundll32.exe", entries["Rundll"]);
        Assert.Null(entries["Empty"]);
        Assert.False(entries.ContainsKey("Binary"));
    }

    [Fact]
    public void Shortcut_without_target_has_unknown_executable()
    {
        _folders.Files[StartupLocation.UserFolder] = [@"C:\Startup\Broken.lnk", @"C:\Startup\run.bat", @"C:\Startup\app.exe"];

        var entries = Source().Read().ToDictionary(e => e.Name, e => e.ExecutablePath);

        Assert.Null(entries["Broken.lnk"]);
        Assert.Null(entries["run.bat"]);
        Assert.Equal(@"C:\Startup\app.exe", entries["app.exe"]);
    }

    [Fact]
    public void Unreadable_approval_key_gives_unknown_state()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, Run, "Discord", @"C:\Discord\Discord.exe")
            .Deny(RegistryRoot.CurrentUser, StartupApprovedKeys.Run);

        Assert.Equal(StartupState.Unknown, Assert.Single(Source().Read()).State);
    }

    [Fact]
    public void Failed_location_is_skipped_and_all_failed_throws()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, Run, "Discord", @"C:\Discord\Discord.exe")
            .Deny(RegistryRoot.LocalMachine, Run);
        _folders.Fail = true;

        Assert.Equal("Discord", Assert.Single(Source().Read()).Name);

        _registry
            .Deny(RegistryRoot.CurrentUser, Run)
            .Deny(RegistryRoot.CurrentUser, RunOnce)
            .Deny(RegistryRoot.LocalMachine, Run32)
            .Deny(RegistryRoot.LocalMachine, RunOnce);

        Assert.Throws<IOException>(() => Source().Read());
    }

    /// <summary>Ярлык создаётся во временной папке теста — реестр и папки автозагрузки не трогаются.</summary>
    [Fact]
    [Trait("Category", "Host")]
    public void Shortcut_resolver_reads_target_of_real_shortcut()
    {
        var folder = Directory.CreateTempSubdirectory("Booster.Tests.");
        try
        {
            var path = Path.Combine(folder.FullName, "notepad.lnk");
            var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = target;
            shortcut.Save();

            Assert.Equal(target, new StartupFolders().ResolveShortcut(path), ignoreCase: true);
            Assert.Null(new StartupFolders().ResolveShortcut(Path.Combine(folder.FullName, "missing.lnk")));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>Только чтение: реестр открывается на чтение, ярлыки — с STGM_READ.</summary>
    [Fact]
    [Trait("Category", "Host")]
    public void Reads_real_startup_entries()
    {
        var entries = new StartupSource(new RegistryReader(), new StartupFolders(), NullLogger<StartupSource>.Instance).Read();

        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
    }
}
