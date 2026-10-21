namespace Booster.Core.Abstractions;

/// <summary>Насколько можно доверять значению. Значение по умолчанию — <see cref="Unknown"/>.</summary>
public enum DataStatus
{
    Unknown = 0,
    Available,
    RequiresAdmin,
    NotSupported,
    Failed,
}
