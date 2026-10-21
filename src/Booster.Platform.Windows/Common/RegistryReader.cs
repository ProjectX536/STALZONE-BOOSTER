using System.ComponentModel;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Booster.Platform.Windows.Common;

public enum RegistryRoot
{
    LocalMachine,
    CurrentUser,
}

public interface IRegistryReader
{
    object? GetValue(RegistryRoot root, string subKey, string valueName);

    IReadOnlyList<KeyValuePair<string, object?>> GetValues(RegistryRoot root, string subKey);

    DateTimeOffset? GetLastWriteTime(RegistryRoot root, string subKey);
}

public sealed class RegistryReader : IRegistryReader
{
    public object? GetValue(RegistryRoot root, string subKey, string valueName)
    {
        ArgumentNullException.ThrowIfNull(valueName);
        using var key = OpenReadOnly(root, subKey);
        return key?.GetValue(valueName);
    }

    public IReadOnlyList<KeyValuePair<string, object?>> GetValues(RegistryRoot root, string subKey)
    {
        using var key = OpenReadOnly(root, subKey);
        if (key is null)
            return [];
        return key.GetValueNames().Select(name => new KeyValuePair<string, object?>(name, key.GetValue(name))).ToArray();
    }

    public DateTimeOffset? GetLastWriteTime(RegistryRoot root, string subKey)
    {
        using var key = OpenReadOnly(root, subKey);
        if (key is null)
            return null;

        uint classLength = 0;
        var error = PInvoke.RegQueryInfoKey(key.Handle, [], ref classLength, out _, out _, out _, out _, out _, out _, out _, out var lastWrite);
        if (error != WIN32_ERROR.ERROR_SUCCESS)
            throw new IOException($"Cannot query '{subKey}'", new Win32Exception((int)error));
        var fileTime = ((long)lastWrite.dwHighDateTime << 32) | (uint)lastWrite.dwLowDateTime;
        return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero);
    }

    private static RegistryKey? OpenReadOnly(RegistryRoot root, string subKey)
    {
        ArgumentNullException.ThrowIfNull(subKey);
        var hive = root switch
        {
            RegistryRoot.LocalMachine => RegistryHive.LocalMachine,
            RegistryRoot.CurrentUser => RegistryHive.CurrentUser,
            _ => throw new ArgumentOutOfRangeException(nameof(root), root, null),
        };
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return baseKey.OpenSubKey(subKey, writable: false);
    }
}
