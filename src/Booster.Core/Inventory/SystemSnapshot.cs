using Booster.Core.Abstractions;
using Booster.Core.Sensors;

namespace Booster.Core.Inventory;

public sealed record SystemSnapshot(
    DateTimeOffset TakenAt,
    PrivilegeState Privileges,
    HardwareInventory Hardware,
    Reading<DriverInventoryResult> Drivers,
    IReadOnlyList<CapabilityReport> Capabilities,
    Reading<SensorSurvey> Sensors,
    SystemDiagnostics System);
