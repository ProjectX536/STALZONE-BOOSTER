namespace Booster.Core.Configuration;

public sealed record SystemCatalog(int SchemaVersion, ServiceCatalog Services, IReadOnlyList<OverlayDefinition> Overlays,
    IReadOnlyList<RegistryTweakDefinition> RegistryTweaks)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record OverlayDefinition(string Process, string Name);

public sealed record RegistryTweakDefinition(string Id, string SubKey, string? ValueName, string Value, int MinBuild);

public static class RegistryTweakIds
{
    public const string GameMode = "game-mode";
    public const string GpuHighPerformance = "gpu-high-performance";
    public const string WindowedFlipModel = "windowed-flip-model";
}

public sealed record ServiceCatalog(
    IReadOnlyList<string> SystemCritical,
    IReadOnlyList<string> Security,
    IReadOnlyList<string> SecurityKeywords,
    IReadOnlyList<string> AntiCheat,
    IReadOnlyList<string> AntiCheatKeywords,
    IReadOnlyList<string> DeviceKeywords);
