using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

/// <summary>Версия Windows. <see cref="Ubr"/> — номер накопительного обновления (последняя часть сборки 26100.<b>1234</b>).</summary>
public sealed record OsInfo(
    Reading<string> Caption,
    Reading<string> Version,
    Reading<int> BuildNumber,
    Reading<int> Ubr,
    Reading<string> DisplayVersion,
    Reading<string> Architecture,
    Reading<DateTimeOffset> LastBootTime);
