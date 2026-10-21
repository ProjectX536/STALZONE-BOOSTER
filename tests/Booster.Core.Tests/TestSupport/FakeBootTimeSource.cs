using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeBootTimeSource : IBootTimeSource
{
    public static readonly DateTimeOffset DefaultBoot = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    public Reading<DateTimeOffset> Boot { get; set; } = Reading<DateTimeOffset>.Known(DefaultBoot, "test-boot");

    public Reading<DateTimeOffset> LastBootTime() => Boot;
}
