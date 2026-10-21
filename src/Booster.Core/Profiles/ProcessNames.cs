namespace Booster.Core.Profiles;

public static class ProcessNames
{
    private const string ExeSuffix = ".exe";

    // Явный список: вне Windows Path.GetInvalidFileNameChars возвращает другой набор.
    private static readonly char[] InvalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

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
