namespace Booster.Core.Abstractions;

public enum CapabilityStatus
{
    Supported,
    NotSupported,
    RequiresAdmin,
    Unknown,
}

/// <summary>Сообщение провайдера о том, доступна ли возможность в заданной области (система или адаптер).</summary>
public sealed record CapabilityReport(string CapabilityId, string Scope, CapabilityStatus Status, string ProviderId, string? Note);
