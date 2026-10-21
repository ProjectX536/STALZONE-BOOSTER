using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public sealed record OsInfo(
    Reading<string> Caption,
    Reading<string> Version,
    Reading<int> BuildNumber,
    Reading<int> Ubr,
    Reading<string> DisplayVersion,
    Reading<string> Architecture,
    Reading<DateTimeOffset> LastBootTime);
