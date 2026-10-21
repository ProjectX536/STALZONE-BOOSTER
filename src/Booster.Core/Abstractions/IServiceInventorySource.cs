namespace Booster.Core.Abstractions;

public enum ServiceStartMode
{
    Boot,
    System,
    Automatic,
    Manual,
    Disabled,
    Unknown,
}

public enum ServiceRunState
{
    Running,
    Stopped,
    Paused,

    Pending,
    Unknown,
}

public sealed record ServiceInfo(string Name, string DisplayName, ServiceRunState State, ServiceStartMode StartMode, bool DelayedAutoStart,
    string? ImagePath, int? ProcessId, Reading<string> Publisher, IReadOnlyList<string> DependentServices);

public interface IServiceInventorySource
{
    Task<IReadOnlyList<ServiceInfo>> ReadAsync(CancellationToken ct);
}
