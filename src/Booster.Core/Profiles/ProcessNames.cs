namespace Booster.Core.Profiles;

/// <summary>Имена процессов в профиле — как <c>Process.ProcessName</c>: без пути и без «.exe».</summary>
public static class ProcessNames
{
    private const string ExeSuffix = ".exe";

    // Запрещённые в имени файла Windows символы. Список задан явно: Core не зависит от платформы,
    // а Path.GetInvalidFileNameChars вне Windows возвращает другой набор.
    private static readonly char[] InvalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>Обрезает пробелы и «.exe»; null — пусто или не имя файла.</summary>
    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^ExeSuffix.Length].TrimEnd();
        if (trimmed.Length == 0 || trimmed is "." or ".." || trimmed.IndexOfAny(InvalidChars) >= 0 || trimmed.Any(char.IsControl))
            return null;
        return trimmed;
    }
}
