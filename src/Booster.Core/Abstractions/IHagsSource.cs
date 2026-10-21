namespace Booster.Core.Abstractions;

/// <summary>
/// Аппаратное планирование GPU (HAGS). Только чтение: Booster значение не пишет, переключает его пользователь в «Параметрах».
/// </summary>
public interface IHagsSource
{
    /// <summary>
    /// HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode: 2 → true, 1 → false, иначе Unknown.
    /// Значения нет — Windows решает по GPU и драйверу, и включено ли планирование, не известно.
    /// </summary>
    Reading<bool> ReadEnabled();

    /// <summary>
    /// Время последней записи ключа GraphicsDrivers, UTC. Ключ меняется при записи любого своего значения, поэтому это время
    /// не раньше последнего изменения HwSchMode: оно позже загрузки — значение в реестре может ещё не действовать.
    /// </summary>
    Reading<DateTimeOffset> ReadChangedAt();
}
