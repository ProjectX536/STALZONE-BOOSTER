using System.ComponentModel;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Tests.Hardware;

public class StorageDisplayDetectorTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly CapabilityRegistry _capabilities = new();
    private readonly ListLogger<StorageDetector> _storageLogger = new();
    private readonly ListLogger<DisplayDetector> _displayLogger = new();

    private sealed class FakeDisplayConfig(Func<IReadOnlyList<DisplayPathRecord>> query) : IDisplayConfigSource
    {
        public IReadOnlyList<DisplayPathRecord> QueryActivePaths() => query();
    }

    private StorageDetector Storage(FakeCimClient cim) => new(cim, _capabilities, _storageLogger);

    private DisplayDetector Display(Func<IReadOnlyList<DisplayPathRecord>> query) =>
        new(new FakeDisplayConfig(query), _capabilities, _displayLogger);

    private static DisplayPathRecord Monitor() =>
        new("MSI G27C4", 1920, 1080, 144000, 1000, new AdapterLuid(0x0000D2E3, 0), false, false, true);

    [Fact]
    public async Task Storage_uses_msft_classes_when_available()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia");
        var detector = Storage(cim);

        var disks = await detector.DetectAsync(Ct);

        Assert.Equal(SectionIds.Storage, detector.SectionId);
        Assert.Equal(2, disks.Count);
        Assert.All(disks, d => Assert.True(d.Bus.IsAvailable));
        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.StorageWmi).Status);
        Assert.Contains(cim.Queries, q => q.Namespace == WmiNamespaces.Storage && q.Wql.Contains("MSFT_PhysicalDisk"));
        Assert.DoesNotContain(cim.Queries, q => q.Wql.Contains("Win32_DiskDrive"));
        Assert.Empty(_storageLogger.Entries);
    }

    [Fact]
    public async Task Missing_storage_namespace_falls_back_to_win32_disk_drive()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia")
            .Throw("MSFT_PhysicalDisk", new CimSchemaMissingException(WmiNamespaces.Storage, "Invalid namespace"));

        var disks = await Storage(cim).DetectAsync(Ct);

        var spcc = disks.Single(d => d.FriendlyName.Value == "SPCC Solid State Disk");
        Assert.Equal(512_105_932_800L, spcc.SizeBytes.Value);
        Assert.Equal(DataStatus.Unknown, spcc.Bus.Status);
        Assert.Equal(CapabilityStatus.NotSupported, _capabilities.Get(CapabilityIds.StorageWmi).Status);
        Assert.Equal(LogLevel.Warning, Assert.Single(_storageLogger.Entries).Level);
    }

    [Fact]
    public async Task Other_storage_failure_also_falls_back_but_capability_is_unknown()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia")
            .Throw("MSFT_Partition", new UnauthorizedAccessException("Access denied"));

        var disks = await Storage(cim).DetectAsync(Ct);

        Assert.Contains(disks, d => d.FriendlyName.Value == "Patriot M.2 P300 512GB");
        var capability = _capabilities.Get(CapabilityIds.StorageWmi);
        Assert.Equal(CapabilityStatus.Unknown, capability.Status);
        Assert.Contains("UnauthorizedAccessException", capability.Note);
    }

    [Fact]
    public async Task Logical_disk_failure_fails_the_storage_section()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia")
            .Throw("Win32_LogicalDisk", new InvalidOperationException("WMI broken"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Storage(cim).DetectAsync(Ct));
    }

    [Fact]
    public async Task Cancellation_during_msft_query_does_not_fall_back()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia")
            .Throw("MSFT_PhysicalDisk", new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Storage(cim).DetectAsync(Ct));

        Assert.DoesNotContain(cim.Queries, q => q.Wql.Contains("Win32_DiskDrive"));
        Assert.Empty(_capabilities.All());
    }

    [Fact]
    public async Task Display_paths_are_mapped_and_capability_supported()
    {
        var detector = Display(() => [Monitor()]);

        var displays = await detector.DetectAsync(Ct);

        Assert.Equal(SectionIds.Displays, detector.SectionId);
        Assert.Equal(144.0, Assert.Single(displays.Value!).RefreshRateHz.Value);
        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.DisplayConfig).Status);
    }

    [Fact]
    public async Task No_active_paths_is_unknown_and_the_api_still_supported()
    {
        var displays = await Display(() => []).DetectAsync(Ct);

        Assert.Equal(DataStatus.Unknown, displays.Status);
        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.DisplayConfig).Status);
    }

    public static TheoryData<Exception> MissingApi => new()
    {
        new EntryPointNotFoundException("QueryDisplayConfig"),
        new DllNotFoundException("user32.dll"),
        new PlatformNotSupportedException("Windows 10 required"),
    };

    [Theory]
    [MemberData(nameof(MissingApi))]
    public async Task Missing_display_api_is_not_supported(Exception exception)
    {
        var displays = await Display(() => throw exception).DetectAsync(Ct);

        Assert.Equal(DataStatus.Failed, displays.Status);
        Assert.Equal(CapabilityStatus.NotSupported, _capabilities.Get(CapabilityIds.DisplayConfig).Status);
    }

    [Fact]
    public async Task Display_api_failure_is_failed_logged_and_unknown_capability()
    {
        var displays = await Display(() => throw new Win32Exception(5, "Access is denied")).DetectAsync(Ct);

        Assert.Equal(DataStatus.Failed, displays.Status);
        Assert.Contains("Win32Exception: Access is denied", displays.Note);
        Assert.Equal(CapabilityStatus.Unknown, _capabilities.Get(CapabilityIds.DisplayConfig).Status);
        Assert.Equal(LogLevel.Warning, Assert.Single(_displayLogger.Entries).Level);
    }

    [Fact]
    public async Task Display_detector_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Display(() => [Monitor()]).DetectAsync(cts.Token));
    }
}
