using System.Text.Json;

namespace Booster.Core.Abstractions;

public interface IReversibleAction
{
    string Id { get; }

    ActionDescriptor Descriptor { get; }

    Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken);

    Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken);

    Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken);
}

public sealed record ActionSupport(bool IsSupported, string? Reason, string Summary);

public sealed record ApplyResult(JsonElement? Data, string? Detail)
{
    public static ApplyResult Done { get; } = new(null, null);

    public bool Applied { get; init; } = true;

    public static ApplyResult NotApplied(string reason) => new(null, reason) { Applied = false };
}

public sealed record ActionSnapshot(
    int SchemaVersion,
    string ActionId,
    JsonElement OriginalValue,
    JsonElement? AppliedValue,
    DateTimeOffset CapturedAt);
