using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Цель ярлыка .lnk через COM ShellLink. Только чтение: файл открывается с STGM_READ, а IShellLink.Resolve не вызывается —
/// он ищет пропавшую цель и может переписать ярлык.
/// </summary>
internal static class ShortcutResolver
{
    // Длинные пути Windows — до 32 767 символов.
    private const int MaxPathLength = 32_767;

    /// <summary>null — ярлык не прочитан или цели-файла нет (ярлык на объект оболочки, ярлык установщика MSI).</summary>
    public static unsafe string? Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        object? link = null;
        try
        {
            link = new ShellLink();
            fixed (char* file = path)
                ((IPersistFile)link).Load(new PCWSTR(file), STGM.STGM_READ);

            var buffer = new char[MaxPathLength];
            fixed (char* target = buffer)
                ((IShellLinkW)link).GetPath(new PWSTR(target), buffer.Length, null, 0);
            var length = Array.IndexOf(buffer, '\0');
            return length <= 0 ? null : new string(buffer, 0, length);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException
            or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (link is not null)
                Marshal.FinalReleaseComObject(link);
        }
    }
}
