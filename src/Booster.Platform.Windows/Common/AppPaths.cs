namespace Booster.Platform.Windows.Common;

/// <summary>Каталоги Booster (docs/ARCHITECTURE.md, раздел 7). Изменяемые данные — только в <see cref="DataRoot"/>.</summary>
public interface IAppPaths
{
    string DataRoot { get; }

    string Logs { get; }

    string Reports { get; }

    string Captures { get; }

    string Tools { get; }

    string UserConfig { get; }

    /// <summary>Журнал игровых сессий.</summary>
    string Journal { get; }

    /// <summary>История серий бенчмарка.</summary>
    string Benchmarks { get; }

    /// <summary>Трассы WPR (*.etl).</summary>
    string Traces { get; }

    /// <summary>Файлы состояния: маркер записи WPR, эксперимент HAGS.</summary>
    string State { get; }

    /// <summary>Каталог exe; только чтение.</summary>
    string BaseDirectory { get; }

    /// <summary>Встроенная конфигурация рядом с exe; только чтение.</summary>
    string BuiltInConfig { get; }

    /// <summary>Встроенные игровые профили (<c>config\Profiles</c> рядом с exe); только чтение.</summary>
    string BuiltInProfiles { get; }

    /// <summary>Пользовательские файлы профилей; каталог создаётся при записи, а не при обращении.</summary>
    string UserProfiles { get; }
}

/// <summary>Каталоги данных создаются при первом обращении к свойству, а не при старте.</summary>
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
