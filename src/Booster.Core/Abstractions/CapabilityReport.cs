namespace Booster.Core.Abstractions;

public enum CapabilityStatus
{
    Supported,
    NotSupported,
    RequiresAdmin,
    Unknown,
}

public sealed record CapabilityReport(string CapabilityId, string Scope, CapabilityStatus Status, string ProviderId, string? Note);
