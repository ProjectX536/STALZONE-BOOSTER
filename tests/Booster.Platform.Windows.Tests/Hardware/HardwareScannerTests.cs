using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Hardware;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Platform.Windows.Tests.Hardware;

public class HardwareScannerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly ListLogger<SectionRunner> _logger = new();

    private sealed class FakeDetector<T>(string sectionId, Func<CancellationToken, Task<T>> detect) : IInventoryDetector<T>
    {
        public string SectionId => sectionId;

        public Task<T> DetectAsync(CancellationToken ct) => detect(ct);
    }

    private static FakeDetector<T> Returns<T>(string sectionId, T value) => new(sectionId, _ => Task.FromResult(value));

    private static readonly CpuInventory Cpu = new([], Reading<CoreTopology>.Unknown());
    private static readonly GpuInventory Gpu = new([], Reading<bool>.Unknown(), Reading<IReadOnlyList<AppGpuPreference>>.Unknown());
    private static readonly IReadOnlyList<StorageDeviceInfo> Disks = [];
    private static readonly Reading<IReadOnlyList<DisplayInfo>> Displays = Reading<IReadOnlyList<DisplayInfo>>.Known([], "test");

    private HardwareScanner Scanner(
        IInventoryDetector<CpuInventory>? cpu = null,
        IInventoryDetector<GpuInventory>? gpu = null,
        IInventoryDetector<IReadOnlyList<StorageDeviceInfo>>? storage = null,
        IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>? displays = null) => new(
        cpu ?? Returns(SectionIds.Cpu, Cpu),
        gpu ?? Returns(SectionIds.Gpu, Gpu),
        Returns(SectionIds.Memory, Memory()),
        Returns(SectionIds.Board, Board()),
        Returns(SectionIds.Os, Os()),
        storage ?? Returns(SectionIds.Storage, Disks),
        displays ?? Returns(SectionIds.Displays, Displays),
        new SectionRunner(_time, _logger),
        new FakeConfiguration(defaultTimeoutMs: 15000),
        _time);

    private static MemoryInventory Memory() => new(Reading<long>.Unknown(), [], Reading<int>.Unknown(),
        Reading<bool>.Unknown(), ChannelLabelHint.None);

    private static SystemBoardInfo Board() => new(Reading<string>.Unknown(), Reading<string>.Unknown(), Reading<PcFormFactor>.Unknown(),
        Reading<string>.Unknown(), Reading<string>.Unknown(), Reading<string>.Unknown(), Reading<string>.Unknown(),
        Reading<DateOnly>.Unknown(), Reading<FirmwareKind>.Unknown());

    private static OsInfo Os() => new(Reading<string>.Unknown(), Reading<string>.Unknown(), Reading<int>.Unknown(),
        Reading<int>.Unknown(), Reading<string>.Unknown(), Reading<string>.Unknown(), Reading<DateTimeOffset>.Unknown());

    private static IEnumerable<DataStatus> Statuses(HardwareInventory inventory) =>
    [
        inventory.Cpu.Status, inventory.Gpu.Status, inventory.Memory.Status, inventory.Board.Status, inventory.Os.Status,
        inventory.Storage.Status, inventory.Displays.Status,
    ];

    [Fact]
    public async Task All_successful_detectors_give_seven_available_sections()
    {
        var inventory = await Scanner().ScanAsync(Ct);

        Assert.Equal(7, Statuses(inventory).Count(s => s == DataStatus.Available));
        Assert.Same(Cpu, inventory.Cpu.Value);
        Assert.Equal(Start, inventory.CollectedAt);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Throwing_gpu_detector_fails_only_its_section()
    {
        var gpu = new FakeDetector<GpuInventory>(SectionIds.Gpu, _ => throw new InvalidOperationException("DXGI exploded"));

        var inventory = await Scanner(gpu: gpu).ScanAsync(Ct);

        Assert.Equal(DataStatus.Failed, inventory.Gpu.Status);
        Assert.Contains("InvalidOperationException", inventory.Gpu.Note);
        Assert.Equal(6, Statuses(inventory).Count(s => s == DataStatus.Available));
    }

    [Fact]
    public async Task Hanging_storage_detector_times_out_and_the_scan_completes()
    {
        var hang = new TaskCompletionSource<IReadOnlyList<StorageDeviceInfo>>();
        var storage = new FakeDetector<IReadOnlyList<StorageDeviceInfo>>(SectionIds.Storage, _ => hang.Task);

        var scan = Scanner(storage: storage).ScanAsync(Ct);
        Assert.False(scan.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(15));
        var inventory = await scan.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(DataStatus.Failed, inventory.Storage.Status);
        Assert.Contains("timeout", inventory.Storage.Note);
        Assert.Equal(6, Statuses(inventory).Count(s => s == DataStatus.Available));
    }

    [Fact]
    public async Task Detectors_start_in_parallel()
    {
        var gate = new TaskCompletionSource();
        var started = new CountdownEvent(2);
        var cpu = new FakeDetector<CpuInventory>(SectionIds.Cpu, async _ =>
        {
            started.Signal();
            await gate.Task;
            return Cpu;
        });
        var gpu = new FakeDetector<GpuInventory>(SectionIds.Gpu, async _ =>
        {
            started.Signal();
            await gate.Task;
            return Gpu;
        });

        var scan = Scanner(cpu: cpu, gpu: gpu).ScanAsync(Ct);

        Assert.True(started.Wait(TimeSpan.FromSeconds(10), Ct));
        Assert.False(scan.IsCompleted);
        gate.SetResult();
        var inventory = await scan.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(inventory.Cpu.IsAvailable && inventory.Gpu.IsAvailable);
    }

    [Fact]
    public async Task External_cancellation_is_thrown()
    {
        using var cts = new CancellationTokenSource();
        var storage = new FakeDetector<IReadOnlyList<StorageDeviceInfo>>(SectionIds.Storage,
            ct => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => Disks, TaskScheduler.Default));

        var scan = Scanner(storage: storage).ScanAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
    }

    [Fact]
    public async Task Display_reading_from_the_detector_is_passed_through()
    {
        var unknown = Reading<IReadOnlyList<DisplayInfo>>.Unknown("QueryDisplayConfig", "no active display paths");

        var inventory = await Scanner(displays: Returns(SectionIds.Displays, unknown)).ScanAsync(Ct);

        Assert.Equal(DataStatus.Unknown, inventory.Displays.Status);
        Assert.Equal("no active display paths", inventory.Displays.Note);
    }

    [Fact]
    public async Task Failed_display_section_keeps_the_runner_status()
    {
        var displays = new FakeDetector<Reading<IReadOnlyList<DisplayInfo>>>(SectionIds.Displays, _ => throw new InvalidOperationException("boom"));

        var inventory = await Scanner(displays: displays).ScanAsync(Ct);

        Assert.Equal(DataStatus.Failed, inventory.Displays.Status);
        Assert.Equal(SectionIds.Displays, inventory.Displays.Source);
    }
}
