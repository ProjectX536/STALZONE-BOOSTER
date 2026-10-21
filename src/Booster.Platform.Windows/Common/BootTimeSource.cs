using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Common;

/// <summary>
/// Время загрузки = сейчас − <see cref="Environment.TickCount64"/>. Счётчик идёт и во сне, и в гибернации, а быстрый запуск —
/// это выход из гибернации, поэтому ни то, ни другое загрузкой не считается.
/// </summary>
public sealed class BootTimeSource(TimeProvider time) : IBootTimeSource
{
    public const string Source = "Environment.TickCount64";

    public Reading<DateTimeOffset> LastBootTime() =>
        Reading<DateTimeOffset>.Known(Compute(time.GetUtcNow(), Environment.TickCount64), Source);

    /// <summary>В UTC.</summary>
    public static DateTimeOffset Compute(DateTimeOffset now, long tickCount64) =>
        now.ToUniversalTime() - TimeSpan.FromMilliseconds(tickCount64);
}
