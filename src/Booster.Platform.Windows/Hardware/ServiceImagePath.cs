namespace Booster.Platform.Windows.Hardware;

public static class ServiceImagePath
{
    private const string SharedHost = "svchost.exe";
    private const string ExecutableExtension = ".exe";
    private const string NtPrefix = @"\??\";
    private const string SystemRootPrefix = @"\SystemRoot\";

    public static string? Resolve(string? pathName, string? serviceDll)
    {
        var executable = Executable(pathName);
        return IsSharedHostFile(executable) && Normalize(serviceDll) is { } dll ? dll : executable;
    }

    public static bool IsSharedHost(string? pathName) => IsSharedHostFile(Executable(pathName));

    private static bool IsSharedHostFile(string? path) =>
        path is not null && string.Equals(Path.GetFileName(path), SharedHost, StringComparison.OrdinalIgnoreCase);

    public static string? Executable(string? pathName)
    {
        var text = pathName?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return Normalize(end < 0 ? text[1..] : text[1..end]);
        }

        var extension = text.IndexOf(ExecutableExtension, StringComparison.OrdinalIgnoreCase);
        return Normalize(extension < 0 ? text : text[..(extension + ExecutableExtension.Length)]);
    }

    private static string? Normalize(string? path)
    {
        var text = path?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        if (text.StartsWith(NtPrefix, StringComparison.Ordinal))
            text = text[NtPrefix.Length..];
        if (text.StartsWith(SystemRootPrefix, StringComparison.OrdinalIgnoreCase))
            text = "%SystemRoot%" + text[(SystemRootPrefix.Length - 1)..];
        return Environment.ExpandEnvironmentVariables(text);
    }
}
