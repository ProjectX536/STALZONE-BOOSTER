namespace Booster.Core.Abstractions;

/// <summary>
/// Сбор одной секции инвентаризации. Детектор только читает систему. Ошибки отдельных полей он
/// отражает в их <c>Reading</c>; исключение означает, что секцию собрать не удалось целиком,
/// и <c>SectionRunner</c> превращает его в статус секции.
/// </summary>
public interface IInventoryDetector<T>
{
    string SectionId { get; }

    Task<T> DetectAsync(CancellationToken ct);
}
