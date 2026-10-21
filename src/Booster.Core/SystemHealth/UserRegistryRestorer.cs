using Booster.Core.Abstractions;
using Booster.Core.Recovery;
using Booster.Core.Resources;

namespace Booster.Core.SystemHealth;

public abstract class UserRegistryRestorer(IRegistryValues registry) : IActionRestorer
{
    public abstract string ActionId { get; }

    public int SnapshotSchemaVersion => UserRegistrySnapshot.SchemaVersion;

    protected abstract bool IsAllowed(string subKey, string valueName);

    public Task<ObservedState> InspectAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var (original, applied) = ReadChecked(snapshot);
        UserRegistryValue? current;
        try
        {
            current = registry.Read(original.SubKey, original.ValueName);
        }
        catch (InvalidDataException)
        {
            return Task.FromResult(ObservedState.Other);
        }

        return Task.FromResult(current == original.Value ? ObservedState.Original
            : applied is not null && current == applied.Value ? ObservedState.Applied
            : ObservedState.Other);
    }

    public Task RevertAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var (original, _) = ReadChecked(snapshot);
        try
        {
            if (original.Value is null)
                registry.Delete(original.SubKey, original.ValueName);
            else
                registry.Write(original.SubKey, original.ValueName, original.Value);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(ReportStrings.Restore_RegistryAccessDenied, ex);
        }

        return Task.CompletedTask;
    }

    private (UserRegistrySnapshot Original, UserRegistrySnapshot? Applied) ReadChecked(ActionSnapshot snapshot)
    {
        var original = UserRegistrySnapshot.Read(snapshot.OriginalValue);
        var applied = snapshot.AppliedValue is { } value ? UserRegistrySnapshot.Read(value) : null;
        if (!IsAllowed(original.SubKey, original.ValueName)
            || (applied is not null && !(string.Equals(applied.SubKey, original.SubKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(applied.ValueName, original.ValueName, StringComparison.OrdinalIgnoreCase))))
        {
            throw new UnauthorizedAccessException($"Snapshot of {ActionId} points outside its registry value");
        }

        return (original, applied);
    }
}
