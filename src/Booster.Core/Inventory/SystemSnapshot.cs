using Booster.Core.Abstractions;
using Booster.Core.Sensors;

namespace Booster.Core.Inventory;

/// <param name="Sensors">Один замер датчиков и параметры драйвера; не получен — статус и причина секции sensors.</param>
/// <param name="System">Память, подкачка, процессы, службы, журнал стабильности и сетевые адаптеры.</param>
public sealed record SystemSnapshot(
    DateTimeOffset TakenAt,
    PrivilegeState Privileges,
    HardwareInventory Hardware,
    Reading<DriverInventoryResult> Drivers,
    IReadOnlyList<CapabilityReport> Capabilities,
    Reading<SensorSurvey> Sensors,
    SystemDiagnostics System);
