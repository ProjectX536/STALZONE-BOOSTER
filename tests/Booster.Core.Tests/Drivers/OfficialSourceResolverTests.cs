using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.Inventory;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Drivers;

public class OfficialSourceResolverTests
{
    private const string NvidiaUrl = "https://www.nvidia.com/en-us/drivers/";
    private const string MsiUrl = "https://www.msi.com/support";
    private static readonly Uri WindowsUpdate = new("ms-settings:windowsupdate");

    private static readonly OfficialSources Sources = new(OfficialSources.CurrentSchemaVersion,
        ["nvidia.com", "msi.com", "microsoft.com"],
        [
            new VendorLinkRule("10DE", "NVIDIA — драйверы", NvidiaUrl),
            // Хоста нет в allowedHosts: такая ссылка не должна дойти до пользователя.
            new VendorLinkRule("1002", "AMD — драйверы", "https://amd-drivers.example.com/download"),
            new VendorLinkRule("8086", "Intel — по http", "http://www.intel.com/"),
        ],
        [
            new VendorLinkRule("Micro-Star", "MSI — поддержка", MsiUrl),
            new VendorLinkRule("Evil Boards", "Не официальный", "https://msi.com.evil.io/support"),
        ],
        "ms-settings:windowsupdate");

    private static OfficialSourceResolver Resolver() => new(new FakeConfiguration(sources: Sources));

    private static DeviceDriverInfo Device(string? deviceClass, params string[] hardwareIds) => new(
        hardwareIds.FirstOrDefault() ?? @"ROOT\X\0000",
        Reading<string>.Known("Device", "test"),
        deviceClass is null ? Reading<string>.Unknown("test") : Reading<string>.Known(deviceClass, "test"),
        hardwareIds, [],
        Reading<string>.Unknown("test"), Reading<string>.Unknown("test"), Reading<DateOnly>.Unknown("test"),
        Reading<string>.Unknown("test"), Reading<bool>.Unknown("test"), Reading<string>.Unknown("test"),
        Reading<DeviceState>.Known(DeviceState.Ok, "test"), Reading<int>.Known(0, "test"));

    private static SystemBoardInfo Board(string? boardManufacturer) => new(
        Reading<string>.Known("Micro-Star International Co., Ltd.", "test"), Reading<string>.Unknown("test"),
        Reading<PcFormFactor>.Unknown("test"),
        boardManufacturer is null ? Reading<string>.Unknown("test") : Reading<string>.Known(boardManufacturer, "test"),
        Reading<string>.Unknown("test"), Reading<string>.Unknown("test"), Reading<string>.Unknown("test"),
        Reading<DateOnly>.Unknown("test"), Reading<FirmwareKind>.Unknown("test"));

    [Fact]
    public void Nvidia_gpu_gets_the_nvidia_page_and_windows_update()
    {
        var links = Resolver().ForDevice(Device("Display", @"PCI\VEN_10DE&DEV_2489&SUBSYS_C9721462&REV_A1"), Board(null));

        Assert.Equal([new Uri(NvidiaUrl), WindowsUpdate], links.Select(l => l.Url));
        Assert.Equal("NVIDIA — драйверы", links[0].Title);
        Assert.False(string.IsNullOrWhiteSpace(links[1].Title));
    }

    [Fact]
    public void System_device_gets_the_board_vendor_page()
    {
        var links = Resolver().ForDevice(Device("System", @"PCI\VEN_8086&DEV_7A84&SUBSYS_7D251462&REV_11"),
            Board("Micro-Star International Co., Ltd."));

        Assert.Equal([new Uri(MsiUrl), WindowsUpdate], links.Select(l => l.Url));
    }

    [Fact]
    public void Board_vendor_matches_case_insensitively()
    {
        var links = Resolver().ForDevice(Device("system", @"ACPI\PNP0C02\1"), Board("MICRO-STAR INTERNATIONAL CO., LTD"));

        Assert.Equal(new Uri(MsiUrl), links[0].Url);
    }

    [Fact]
    public void Gpu_vendor_rule_needs_a_display_device_and_board_rule_a_system_device()
    {
        // Аудиоконтроллер NVIDIA в классе System — не видеокарта: ссылка на плату, а не на драйвер GPU.
        var audio = Resolver().ForDevice(Device("System", @"PCI\VEN_10DE&DEV_228B&SUBSYS_C9721462&REV_A1"), Board("Micro-Star"));
        var network = Resolver().ForDevice(Device("Net", @"PCI\VEN_10EC&DEV_8125"), Board("Micro-Star"));

        Assert.Equal([new Uri(MsiUrl), WindowsUpdate], audio.Select(l => l.Url));
        Assert.Equal([WindowsUpdate], network.Select(l => l.Url));
    }

    [Theory]
    [InlineData("Display", @"PCI\VEN_1234&DEV_1111")]
    [InlineData("Display", @"ROOT\BasicDisplay")]
    [InlineData(null, @"PCI\VEN_10DE&DEV_2489")]
    public void Unknown_vendor_or_class_gets_only_windows_update(string? deviceClass, string hardwareId)
    {
        var links = Resolver().ForDevice(Device(deviceClass, hardwareId), Board("Micro-Star"));

        Assert.Equal([WindowsUpdate], links.Select(l => l.Url));
    }

    [Fact]
    public void System_device_without_board_data_gets_only_windows_update()
    {
        Assert.Equal([WindowsUpdate], Resolver().ForDevice(Device("System", @"ACPI\PNP0C02\1"), null).Select(l => l.Url));
        Assert.Equal([WindowsUpdate], Resolver().ForDevice(Device("System", @"ACPI\PNP0C02\1"), Board(null)).Select(l => l.Url));
    }

    [Theory]
    [InlineData("Display", @"PCI\VEN_1002&DEV_73BF")]
    [InlineData("Display", @"PCI\VEN_8086&DEV_A780")]
    public void Rule_whose_url_is_not_allowed_is_dropped(string deviceClass, string hardwareId)
    {
        var links = Resolver().ForDevice(Device(deviceClass, hardwareId), null);

        Assert.Equal([WindowsUpdate], links.Select(l => l.Url));
    }

    [Fact]
    public void Board_rule_whose_url_is_not_allowed_is_dropped()
    {
        var links = Resolver().ForDevice(Device("System", @"ACPI\PNP0C02\1"), Board("Evil Boards Inc."));

        Assert.Equal([WindowsUpdate], links.Select(l => l.Url));
    }

    [Theory]
    [InlineData("https://www.nvidia.com/x", true)]
    [InlineData("https://nvidia.com", true)]
    [InlineData("https://WWW.NVIDIA.COM/x", true)]
    [InlineData("http://www.nvidia.com", false)]
    [InlineData("https://nvidia.com.evil.io", false)]
    [InlineData("https://evilnvidia.com", false)]
    [InlineData("https://www.nvidia.com@evil.io/", false)]
    [InlineData("ftp://www.nvidia.com/", false)]
    [InlineData("ms-settings:windowsupdate", true)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("ms-settings:windowsupdate-action", false)]
    public void Allow_list_accepts_only_https_on_allowed_hosts_or_windows_update(string url, bool expected) =>
        Assert.Equal(expected, Resolver().IsAllowed(new Uri(url)));

    [Fact]
    public void Relative_url_is_not_allowed() =>
        Assert.False(Resolver().IsAllowed(new Uri("/drivers", UriKind.Relative)));
}
