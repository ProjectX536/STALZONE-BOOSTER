namespace Booster.Platform.Windows.Common;

internal static class FileNames
{
    /// <summary>
    /// Имя файла без пути. Имена приходят из кода и из журнала, но путь в имени вывел бы чтение и запись
    /// за пределы каталога Booster.
    /// </summary>
    public static string CheckBare(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName is "." or "..")
            throw new ArgumentException($"'{fileName}' is not a bare file name", nameof(fileName));
        return fileName;
    }
}
