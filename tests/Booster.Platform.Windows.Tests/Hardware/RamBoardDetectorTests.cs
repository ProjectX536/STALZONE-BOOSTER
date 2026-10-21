using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class RamBoardDetectorTests
{
    private sealed class FakeFirmware(Reading<FirmwareKind> value) : IFirmwareTypeSource
    {
        public Reading<FirmwareKind> Get() => value;
    }

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ram_detector_reads_modules_slots_and_total()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia");
        var detector = new RamDetector(cim);

        var memory = await detector.DetectAsync(Ct);

        Assert.Equal(SectionIds.Memory, detector.SectionId);
        Assert.Equal(2, memory.Modules.Count);
        Assert.Equal(2, memory.SlotCount.Value);
        Assert.Equal(17_013_911_552L, memory.TotalPhysicalBytes.Value);
        Assert.Equal(ChannelLabelHint.DifferentChannelsByLabel, memory.ChannelHint);
        Assert.Equal(3, cim.Queries.Count);
    }

    [Fact]
    public async Task Board_detector_combines_wmi_and_firmware()
    {
        var firmware = Reading<FirmwareKind>.Known(FirmwareKind.Uefi, FirmwareTypeSource.Source);
        var detector = new MotherboardDetector(FakeCimClient.FromFixture("desktop-nvidia"), new FakeFirmware(firmware));

        var board = await detector.DetectAsync(Ct);

        Assert.Equal(SectionIds.Board, detector.SectionId);
        Assert.Equal("MS-7D48", board.SystemModel.Value);
        Assert.Equal("PRO B760M-E DDR4 (MS-7D48)", board.BoardProduct.Value);
        Assert.Equal(new DateOnly(2024, 9, 26), board.BiosReleaseDate.Value);
        Assert.Equal(PcFormFactor.Desktop, board.FormFactor.Value);
        Assert.Equal(firmware, board.Firmware);
    }

    [Fact]
    public async Task Board_detector_keeps_failed_firmware_reading()
    {
        var failed = Reading<FirmwareKind>.Failure(FirmwareTypeSource.Source, "Win32Exception: denied");
        var detector = new MotherboardDetector(FakeCimClient.FromFixture("desktop-nvidia"), new FakeFirmware(failed));

        var board = await detector.DetectAsync(Ct);

        Assert.Equal(DataStatus.Failed, board.Firmware.Status);
        Assert.Equal("MS-7D48", board.SystemModel.Value);
    }
}
