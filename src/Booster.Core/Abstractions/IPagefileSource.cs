namespace Booster.Core.Abstractions;

public sealed record PagefileSetting(string Path, int InitialSizeMb, int MaximumSizeMb);

public sealed record PagefileUsage(string Path, int AllocatedMb, int CurrentUsageMb, int PeakUsageMb);

public sealed record PagefileConfiguration(Reading<bool> AutomaticManaged, IReadOnlyList<PagefileSetting> Settings,
    IReadOnlyList<PagefileUsage> Usage);

public interface IPagefileSource
{
    Task<PagefileConfiguration> ReadAsync(CancellationToken ct);
}
