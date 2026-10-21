namespace Booster.Core.Abstractions;

/// <summary>
/// Значение вместе с его статусом и источником. Если значение не получено надёжно,
/// статус отличен от <see cref="DataStatus.Available"/>, а <see cref="Value"/> не используется.
/// </summary>
public readonly record struct Reading<T>(T? Value, DataStatus Status, string? Source = null, string? Note = null)
{
    public bool IsAvailable => Status == DataStatus.Available;

    public static Reading<T> Known(T value, string source)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, DataStatus.Available, source);
    }

    public static Reading<T> Unknown(string? source = null, string? note = null) =>
        new(default, DataStatus.Unknown, source, note);

    public static Reading<T> NeedsAdmin(string source, string? note = null) =>
        new(default, DataStatus.RequiresAdmin, source, note);

    public static Reading<T> Unsupported(string source, string? note = null) =>
        new(default, DataStatus.NotSupported, source, note);

    public static Reading<T> Failure(string source, string note) =>
        new(default, DataStatus.Failed, source, note);
}
