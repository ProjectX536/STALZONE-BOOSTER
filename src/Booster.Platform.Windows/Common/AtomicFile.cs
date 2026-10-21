using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Booster.Platform.Windows.Common;

/// <summary>
/// Атомарная замена файла (docs/ARCHITECTURE.md, раздел 7): запись в <c>&lt;имя&gt;.tmp</c> рядом,
/// сброс на диск, затем <c>MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)</c>. Основной файл в любой
/// момент целиком старый или целиком новый.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string TempPathFor(string path) => path + ".tmp";

    public static async Task WriteAllTextAsync(string path, string contents, CancellationToken ct)
    {
        var temp = TempPathFor(path);
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                             FileOptions.Asynchronous))
            {
                await stream.WriteAsync(Utf8NoBom.GetBytes(contents), ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            Replace(temp, path);
        }
        catch
        {
            DeleteTemp(path);
            throw;
        }
    }

    /// <summary>Остаток прерванной записи: основной файл не тронут, .tmp не нужен.</summary>
    public static void DeleteTemp(string path)
    {
        try
        {
            File.Delete(TempPathFor(path));
        }
        catch (IOException)
        {
            // Занят параллельной записью — её собственная замена его и уберёт.
        }
    }

    private static void Replace(string source, string destination)
    {
        if (!PInvoke.MoveFileEx(source, destination,
                MOVE_FILE_FLAGS.MOVEFILE_REPLACE_EXISTING | MOVE_FILE_FLAGS.MOVEFILE_WRITE_THROUGH))
            throw new IOException($"Cannot replace '{destination}'", new Win32Exception(Marshal.GetLastPInvokeError()));
    }
}
