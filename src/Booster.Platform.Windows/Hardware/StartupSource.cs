using System.Security;
using Booster.Core.Abstractions;
using Booster.Core.SystemHealth;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Автозагрузка из ключей Run и RunOnce и папок «Автозагрузка» с состоянием по StartupApproved. Только чтение.
/// 32-битный HKLM Run читается как <c>Software\WOW6432Node\…</c> в 64-битном представлении.
/// </summary>
public sealed partial class StartupSource(IRegistryReader registry, IStartupFolders folders, ILogger<StartupSource> logger)
    : IStartupSource
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun32Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string DesktopIni = "desktop.ini";
    private const string ShortcutExtension = ".lnk";
    private const string ExecutableExtension = ".exe";

    public IReadOnlyList<StartupEntry> Read()
    {
        var locations = Enum.GetValues<StartupLocation>();
        var entries = new List<StartupEntry>();
        var failed = 0;
        Exception? lastFailure = null;
        foreach (var location in locations)
        {
            try
            {
                entries.AddRange(ReadLocation(location));
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                LogLocationFailed(ex, location);
                failed++;
                lastFailure = ex;
            }
        }

        return failed == locations.Length ? throw new IOException("No startup location could be read", lastFailure) : entries;
    }

    private List<StartupEntry> ReadLocation(StartupLocation location) => location switch
    {
        StartupLocation.UserRun => ReadRun(location, RegistryRoot.CurrentUser, RunKey, StartupApprovedKeys.Run),
        StartupLocation.UserRunOnce => ReadRun(location, RegistryRoot.CurrentUser, RunOnceKey, approvedKey: null),
        StartupLocation.MachineRun => ReadRun(location, RegistryRoot.LocalMachine, RunKey, StartupApprovedKeys.Run),
        StartupLocation.MachineRun32 => ReadRun(location, RegistryRoot.LocalMachine, Run32Key, ApprovedRun32Key),
        StartupLocation.MachineRunOnce => ReadRun(location, RegistryRoot.LocalMachine, RunOnceKey, approvedKey: null),
        StartupLocation.UserFolder => ReadFolder(location, RegistryRoot.CurrentUser),
        StartupLocation.CommonFolder => ReadFolder(location, RegistryRoot.LocalMachine),
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    private List<StartupEntry> ReadRun(StartupLocation location, RegistryRoot root, string runKey, string? approvedKey)
    {
        var values = registry.GetValues(root, runKey);
        var state = Approvals(root, approvedKey);
        var entries = new List<StartupEntry>();
        foreach (var (name, value) in values)
        {
            // Безымянное значение «(По умолчанию)» в списке не назвать; не строка — не команда.
            if (name.Length == 0 || value is not string command)
                continue;
            entries.Add(new StartupEntry(name, location, command, ServiceImagePath.Executable(command), state(name)));
        }

        return entries;
    }

    private List<StartupEntry> ReadFolder(StartupLocation location, RegistryRoot approvalRoot)
    {
        var files = folders.List(location);
        var state = Approvals(approvalRoot, StartupApprovedKeys.StartupFolder);
        var entries = new List<StartupEntry>();
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (string.Equals(name, DesktopIni, StringComparison.OrdinalIgnoreCase))
                continue;

            var extension = Path.GetExtension(path);
            var executable = string.Equals(extension, ShortcutExtension, StringComparison.OrdinalIgnoreCase) ? folders.ResolveShortcut(path)
                : string.Equals(extension, ExecutableExtension, StringComparison.OrdinalIgnoreCase) ? path
                : null;
            entries.Add(new StartupEntry(name, location, path, executable, state(name)));
        }

        return entries;
    }

    // RunOnce не отключается через StartupApproved — всегда Enabled. Сбой чтения StartupApproved не прячет элементы:
    // их состояние неизвестно.
    private Func<string, StartupState> Approvals(RegistryRoot root, string? approvedKey)
    {
        if (approvedKey is null)
            return _ => StartupState.Enabled;

        Dictionary<string, object?> values;
        try
        {
            values = registry.GetValues(root, approvedKey).ToDictionary(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            LogApprovalFailed(ex, root, approvedKey);
            return _ => StartupState.Unknown;
        }

        // Значение не REG_BINARY — формат неизвестен: пустой массив даёт Unknown.
        return name => StartupApproval.Parse(values.TryGetValue(name, out var value) ? value as byte[] ?? [] : null);
    }

    private static bool IsReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup location {Location} was not read")]
    private partial void LogLocationFailed(Exception exception, StartupLocation location);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup approval key {Key} in {Root} was not read; states are unknown")]
    private partial void LogApprovalFailed(Exception exception, RegistryRoot root, string key);
}
