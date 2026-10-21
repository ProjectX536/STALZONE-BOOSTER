using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public sealed class MotherboardDetector(ICimClient cim, IFirmwareTypeSource firmware) : IInventoryDetector<SystemBoardInfo>
{
    private const string SystemQuery = "SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem";
    private const string BoardQuery = "SELECT Manufacturer, Product FROM Win32_BaseBoard";
    private const string BiosQuery = "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS";
    private const string EnclosureQuery = "SELECT ChassisTypes FROM Win32_SystemEnclosure";

    public string SectionId => SectionIds.Board;

    public async Task<SystemBoardInfo> DetectAsync(CancellationToken ct)
    {
        var system = await QuerySingleAsync(SystemQuery, ct).ConfigureAwait(false);
        var board = await QuerySingleAsync(BoardQuery, ct).ConfigureAwait(false);
        var bios = await QuerySingleAsync(BiosQuery, ct).ConfigureAwait(false);
        var enclosure = await QuerySingleAsync(EnclosureQuery, ct).ConfigureAwait(false);
        return MotherboardMapper.Map(system, board, bios, enclosure, firmware.Get());
    }

    private async Task<CimRow?> QuerySingleAsync(string wql, CancellationToken ct) =>
        (await cim.QueryAsync(WmiNamespaces.CimV2, wql, ct).ConfigureAwait(false)).FirstOrDefault();
}
