namespace Booster.Core.Abstractions;

/// <summary>Последняя полная загрузка Windows: выход из сна и быстрый запуск её не меняют.</summary>
public interface IBootTimeSource
{
    /// <summary>Время в UTC.</summary>
    Reading<DateTimeOffset> LastBootTime();
}
