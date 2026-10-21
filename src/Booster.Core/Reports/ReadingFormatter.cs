using Booster.Core.Abstractions;

namespace Booster.Core.Reports;

/// <summary>Текст значения для человека: само значение или статус («Неизвестно», «Нужны права администратора», ...).</summary>
public static class ReadingFormatter
{
    public static string Format<T>(Reading<T> reading, Func<T, string> formatValue)
    {
        ArgumentNullException.ThrowIfNull(formatValue);
        if (!reading.IsAvailable)
            return StatusText(reading.Status);
        return reading.Value is { } value ? formatValue(value) : StatusText(DataStatus.Unknown);
    }

    public static string StatusText(DataStatus status) => EnumText.Format(status);

    /// <summary>Подсказка к значению: «источник: пояснение», одна из частей или null, если нет обеих.</summary>
    public static string? SourceText(string? source, string? note)
    {
        var parts = new[] { source, note }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join(": ", parts);
    }
}
