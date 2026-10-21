namespace Booster.Core.Abstractions;

public interface IHagsSource
{
    Reading<bool> ReadEnabled();

    Reading<DateTimeOffset> ReadChangedAt();
}
