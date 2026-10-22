using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.TestSupport;

public sealed class FakeAppPaths(string root = @"C:\Fake") : IAppPaths
{
    public string DataRoot { get; } = Path.Combine(root, "data");

    public string Logs => Path.Combine(DataRoot, "logs");

    public string Reports => Path.Combine(DataRoot, "reports");

    public string Captures => Path.Combine(DataRoot, "captures");

    public string Tools => Path.Combine(DataRoot, "tools");

    public string UserConfig => Path.Combine(DataRoot, "config");

    public string Journal => Path.Combine(DataRoot, "journal");

    public string Benchmarks => Path.Combine(DataRoot, "benchmarks");

    public string Traces => Path.Combine(DataRoot, "traces");

    public string NetworkMeasurements => Path.Combine(DataRoot, "network");

    public string State => Path.Combine(DataRoot, "state");

    public string BaseDirectory { get; } = Path.Combine(root, "app");

    public string BuiltInConfig => Path.Combine(BaseDirectory, "config");

    public string BuiltInProfiles => Path.Combine(BuiltInConfig, "Profiles");

    public string UserProfiles => Path.Combine(UserConfig, "Profiles");
}
