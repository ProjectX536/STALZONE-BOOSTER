using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Drivers;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Drivers;

public class DriverInventoryMapperTests
{
    private const string GtxId = @"PCI\VEN_10DE&DEV_1C82&SUBSYS_8C971462&REV_A1\4&1A2B3C4D&0&0008";
    private const string NoDriverId = @"PCI\VEN_8086&DEV_A0F0&SUBSYS_00748086&REV_20\3&11583659&0&A3";
    private const string DisabledId = @"HID\VID_046D&PID_C52B&MI_02&COL01\8&2A4F1C3B&0&0000";

    private static DriverInventoryResult MapFixture(string folder) => DriverInventoryMapper.Map(
        WmiFixture.Load($"{folder}/Win32_PnPSignedDriver.json"), WmiFixture.Load($"{folder}/Win32_PnPEntity.json"));

    private static DeviceDriverInfo Device(DriverInventoryResult result, string deviceId) =>
        Assert.Single(result.Devices, d => d.DeviceInstanceId == deviceId);

    private static CimRow Entity(string deviceId, int? errorCode = 0, string? name = "Device", object? compatibleIds = null) =>
        WmiFixture.Row(("DeviceID", deviceId), ("Name", name), ("PNPClass", "System"),
            ("HardwareID", new object?[] { deviceId.Split('\\')[0] + @"\X" }), ("CompatibleID", compatibleIds),
            ("ConfigManagerErrorCode", errorCode is null ? null : (long)errorCode), ("Present", true));

    private static CimRow Driver(string deviceId, object? isSigned = null, string version = "1.0.0.0") =>
        WmiFixture.Row(("DeviceID", deviceId), ("DriverProviderName", "Vendor"), ("DriverVersion", version),
            ("DriverDate", "20240814000000.******+***"), ("InfName", "oem1.inf"), ("IsSigned", isSigned), ("Signer", "Signer"));

    [Fact]
    public void Nvidia_gpu_on_this_machine_has_its_driver_and_is_ok()
    {
        var result = MapFixture("desktop-nvidia");

        var gpu = Assert.Single(result.Devices, d => d.DeviceInstanceId.StartsWith(@"PCI\VEN_10DE&DEV_2489", StringComparison.Ordinal));
        Assert.Equal(22, result.Devices.Count);
        Assert.Equal("NVIDIA GeForce RTX 3060 Ti", gpu.DeviceName.Value);
        Assert.Equal("Display", gpu.DeviceClass.Value);
        Assert.Equal("NVIDIA", gpu.DriverProvider.Value);
        Assert.Equal(Reading<string>.Known("32.0.15.6094", "WMI Win32_PnPSignedDriver.DriverVersion"), gpu.DriverVersion);
        Assert.Equal(new DateOnly(2024, 8, 14), gpu.DriverDate.Value);
        Assert.Equal("oem3.inf", gpu.InfName.Value);
        Assert.Equal(Reading<bool>.Known(true, "WMI Win32_PnPSignedDriver.IsSigned"), gpu.IsSigned);
        Assert.Equal("Microsoft Windows Hardware Compatibility Publisher", gpu.Signer.Value);
        Assert.Equal(Reading<DeviceState>.Known(DeviceState.Ok, "WMI Win32_PnPEntity.ConfigManagerErrorCode"), gpu.State);
        Assert.Equal(0, gpu.ProblemCode.Value);
        Assert.Contains(@"PCI\VEN_10DE&DEV_2489&SUBSYS_C9721462&REV_A1", gpu.HardwareIds);
        Assert.Contains(@"PCI\VEN_10DE", gpu.CompatibleIds);
    }

    [Fact]
    public void Device_without_a_driver_row_is_listed_as_an_error_with_unknown_driver_fields()
    {
        var device = Device(MapFixture("problems.synthetic"), NoDriverId);

        Assert.Equal(DeviceState.Error, device.State.Value);
        Assert.Equal(Reading<int>.Known(28, "WMI Win32_PnPEntity.ConfigManagerErrorCode"), device.ProblemCode);
        Assert.Equal("Network Controller", device.DeviceName.Value);
        Assert.Equal(DataStatus.Unknown, device.DeviceClass.Status);
        Assert.Equal(DataStatus.Unknown, device.DriverVersion.Status);
        Assert.Equal("device has no row in Win32_PnPSignedDriver", device.DriverVersion.Note);
        Assert.Equal(DataStatus.Unknown, device.DriverDate.Status);
        Assert.Equal(DataStatus.Unknown, device.IsSigned.Status);
        Assert.Equal(DataStatus.Unknown, device.DriverProvider.Status);
        Assert.Equal(2, device.HardwareIds.Count);
    }

    [Fact]
    public void Code_22_is_disabled()
    {
        var device = Device(MapFixture("problems.synthetic"), DisabledId);

        Assert.Equal(DeviceState.Disabled, device.State.Value);
        Assert.Equal(22, device.ProblemCode.Value);
        Assert.Equal("10.0.26100.1", device.DriverVersion.Value);
    }

    [Fact]
    public void Code_43_is_an_error_and_keeps_its_driver()
    {
        var device = Device(MapFixture("problems.synthetic"), GtxId);

        Assert.Equal(DeviceState.Error, device.State.Value);
        Assert.Equal(43, device.ProblemCode.Value);
        Assert.Equal("31.0.15.3623", device.DriverVersion.Value);
    }

    [Fact]
    public void Disconnected_device_and_driver_rows_without_a_device_are_left_out()
    {
        var result = MapFixture("problems.synthetic");

        Assert.Equal([NoDriverId, DisabledId, GtxId], result.Devices.Select(d => d.DeviceInstanceId));
    }

    [Fact]
    public void Missing_IsSigned_is_unknown_not_false()
    {
        var result = DriverInventoryMapper.Map([Driver(@"PCI\A\1&2", isSigned: null)], [Entity(@"PCI\A\1&2")]);

        var device = Assert.Single(result.Devices);
        Assert.Equal(DataStatus.Unknown, device.IsSigned.Status);
        Assert.Equal("1.0.0.0", device.DriverVersion.Value);
    }

    [Fact]
    public void Device_ids_differing_only_in_case_are_merged()
    {
        var result = DriverInventoryMapper.Map([Driver(@"pci\ven_10de&dev_2489\4&abc&0&0008", isSigned: false, version: "9.9")],
            [Entity(@"PCI\VEN_10DE&DEV_2489\4&ABC&0&0008")]);

        var device = Assert.Single(result.Devices);
        Assert.Equal(@"PCI\VEN_10DE&DEV_2489\4&ABC&0&0008", device.DeviceInstanceId);
        Assert.Equal("9.9", device.DriverVersion.Value);
        Assert.Equal(Reading<bool>.Known(false, "WMI Win32_PnPSignedDriver.IsSigned"), device.IsSigned);
    }

    [Fact]
    public void Null_compatible_ids_give_an_empty_list()
    {
        var result = DriverInventoryMapper.Map([], [Entity(@"ACPI\PNP0A08\0", compatibleIds: null)]);

        Assert.Empty(Assert.Single(result.Devices).CompatibleIds);
    }

    [Fact]
    public void Markup_in_a_device_name_is_kept_verbatim()
    {
        var result = DriverInventoryMapper.Map([], [Entity(@"ROOT\X\0000", name: "<script>x</script>")]);

        Assert.Equal("<script>x</script>", Assert.Single(result.Devices).DeviceName.Value);
    }

    [Fact]
    public void Missing_error_code_gives_unknown_state_and_problem_code()
    {
        var result = DriverInventoryMapper.Map([], [Entity(@"ROOT\X\0000", errorCode: null)]);

        var device = Assert.Single(result.Devices);
        Assert.Equal(DataStatus.Unknown, device.State.Status);
        Assert.Equal(DataStatus.Unknown, device.ProblemCode.Status);
    }

    [Theory]
    [InlineData(0, DeviceState.Ok)]
    [InlineData(22, DeviceState.Disabled)]
    [InlineData(1, DeviceState.Error)]
    [InlineData(10, DeviceState.Error)]
    [InlineData(28, DeviceState.Error)]
    public void Error_code_maps_to_state(int code, DeviceState expected) =>
        Assert.Equal(expected, DriverInventoryMapper.MapState(code).Value);
}
