using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Common;

public sealed class BootTimeSource(TimeProvider time) : IBootTimeSource
{
    public const string Source = "Environment.TickCount64";

    public Reading<DateTimeOffset> LastBootTime() =>
        Reading<DateTimeOffset>.Known(Compute(time.GetUtcNow(), Environment.TickCount64), Source);

    public static DateTimeOffset Compute(DateTimeOffset now, long tickCount64) =>
        now.ToUniversalTime() - TimeSpan.FromMilliseconds(tickCount64);
}
