namespace Booster.Core.Abstractions;

public interface IInventoryDetector<T>
{
    string SectionId { get; }

    Task<T> DetectAsync(CancellationToken ct);
}
