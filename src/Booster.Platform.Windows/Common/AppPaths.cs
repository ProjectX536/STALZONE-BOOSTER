namespace Booster.Platform.Windows.Common;

public interface IAppPaths
{
    string DataRoot { get; }

    string Logs { get; }

    string Reports { get; }

    string Captures { get; }

    string Tools { get; }

    string UserConfig { get; }

    string Journal { get; }

    string Benchmarks { get; }

    string Traces { get; }

    string NetworkMeasurements { get; }

    string State { get; }

    string BaseDirectory { get; }

    string BuiltInConfig { get; }

    string BuiltInProfiles { get; }

    string UserProfiles { get; }
}

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
        : this(DefaultDataRoot, AppContext.BaseDirectory)
    {
    }

    public AppPaths(string dataRoot, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        DataRoot = Path.GetFullPath(dataRoot);
        BaseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        BuiltInConfig = Path.Combine(BaseDirectory, "config");
        BuiltInProfiles = Path.Combine(BuiltInConfig, "Profiles");
    }

    public static string DefaultDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Booster");

    public string DataRoot { get; }

    public string Logs => Ensure("logs");

    public string Reports => Ensure("reports");

    public string Captures => Ensure("captures");

    public string Tools => Ensure("tools");

    public string UserConfig => Ensure("config");

    public string Journal => Ensure("journal");

    public string Benchmarks => Ensure("benchmarks");

    public string Traces => Ensure("traces");

    public string NetworkMeasurements => Ensure("network");

    public string State => Ensure("state");

    public string BaseDirectory { get; }

    public string BuiltInConfig { get; }

    public string BuiltInProfiles { get; }

    public string UserProfiles => Path.Combine(DataRoot, "config", "Profiles");

    private string Ensure(string name)
    {
        var path = Path.Combine(DataRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
