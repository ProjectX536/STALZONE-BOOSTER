using Booster.Core.Inventory;
using Booster.Core.Sensors;

namespace Booster.Core.Abstractions;

public interface ISensorProvider
{
    Task<SensorSurvey> SurveyAsync(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<string> applications, CancellationToken ct);

    Task<SensorSnapshot> ReadAsync(CancellationToken ct);
}
