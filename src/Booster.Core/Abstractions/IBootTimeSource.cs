namespace Booster.Core.Abstractions;

public interface IBootTimeSource
{
    Reading<DateTimeOffset> LastBootTime();
}
