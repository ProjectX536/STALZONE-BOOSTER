using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Tests.Hardware;

public class GpuDetectorTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly AdapterLuid NvidiaLuid = new(0x0000D2E3, 0);

    private static readonly DxgiAdapterRecord Nvidia = new("NVIDIA GeForce RTX 3060 Ti", 0x10DE, 0x2489, 0xC9721462, 0xA1,
        NvidiaLuid, 8_589_934_592, 8_506_955_776, false, 0, false);

    private sealed class FakeDxgi(Func<IReadOnlyList<DxgiAdapterRecord>> enumerate) : IDxgiAdapterSource
    {
        public IReadOnlyList<DxgiAdapterRecord> Enumerate() => enumerate();
    }

    private sealed class FakeKmt(Func<IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags>> query) : IKmtAdapterTypeSource
    {
        public IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> Query() => query();
    }

    private readonly CapabilityRegistry _capabilities = new();
    private readonly ListLogger<GpuDetector> _logger = new();
    private readonly FakeRegistryReader _registry = new FakeRegistryReader()
        .Set(RegistryRoot.CurrentUser, UserGpuPreferenceParser.RegistryKey, @"C:\Games\Game.exe", "GpuPreference=2;");

    private GpuDetector Detector(FakeCimClient? cim = null, Func<IReadOnlyList<DxgiAdapterRecord>>? dxgi = null,
        Func<IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags>>? kmt = null) => new(
        cim ?? FakeCimClient.FromFixture("desktop-nvidia"),
        new FakeDxgi(dxgi ?? (() => [Nvidia])),
        new FakeKmt(kmt ?? (() => new Dictionary<AdapterLuid, KmtAdapterFlags>())),
        _registry, _capabilities, _logger);

    [Fact]
    public async Task All_sources_are_merged_and_capabilities_reported()
    {
        var detector = Detector();

        var gpu = await detector.DetectAsync(Ct);

        Assert.Equal(SectionIds.Gpu, detector.SectionId);
        var adapter = Assert.Single(gpu.Adapters);
        Assert.Equal("32.0.15.6094", adapter.DriverVersion.Value);
        Assert.Equal(8_589_934_592L, adapter.DedicatedVideoMemoryBytes.Value);
        Assert.Equal(AppGpuPreferenceKind.HighPerformance, Assert.Single(gpu.AppPreferences.Value!).Preference);
        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.DxgiAdapters).Status);
        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.HybridGpuFlags).Status);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Dxgi_failure_keeps_wmi_adapters_and_is_logged()
    {
        var gpu = await Detector(dxgi: () => throw new COMException("no factory", unchecked((int)0x887A0004))).DetectAsync(Ct);

        var adapter = Assert.Single(gpu.Adapters);
        Assert.Equal("NVIDIA GeForce RTX 3060 Ti", adapter.Name);
        Assert.Contains("COMException: no factory", adapter.DedicatedVideoMemoryBytes.Note);
        var capability = _capabilities.Get(CapabilityIds.DxgiAdapters);
        Assert.NotEqual(CapabilityStatus.Supported, capability.Status);
        Assert.Contains("COMException", capability.Note);
        var warning = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(DxgiAdapterSource.Source, warning.Message);
    }

    public static TheoryData<Exception> MissingApi =>
        [new DllNotFoundException("dxgi.dll"), new EntryPointNotFoundException("CreateDXGIFactory2"),
         new PlatformNotSupportedException("Windows 10")];

    [Theory]
    [MemberData(nameof(MissingApi))]
    public async Task Missing_dxgi_api_is_reported_as_not_supported(Exception error)
    {
        await Detector(dxgi: () => throw error).DetectAsync(Ct);

        Assert.Equal(CapabilityStatus.NotSupported, _capabilities.Get(CapabilityIds.DxgiAdapters).Status);
    }

    [Fact]
    public async Task Kmt_failure_leaves_hybrid_flags_unavailable()
    {
        var gpu = await Detector(kmt: () => throw new InvalidOperationException("NTSTATUS 0xC0000001")).DetectAsync(Ct);

        Assert.Single(gpu.Adapters);
        Assert.Equal(GpuAdapterKind.Discrete, gpu.Adapters[0].Kind.Value);
        Assert.NotEqual(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.HybridGpuFlags).Status);
        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.DxgiAdapters).Status);
        Assert.Single(_logger.Entries);
    }

    [Fact]
    public async Task Wmi_failure_keeps_dxgi_adapters()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia")
            .Throw("Win32_VideoController", new InvalidOperationException("WMI is broken"));

        var gpu = await Detector(cim).DetectAsync(Ct);

        var adapter = Assert.Single(gpu.Adapters);
        Assert.Equal("NVIDIA GeForce RTX 3060 Ti", adapter.Name);
        Assert.Contains("WMI is broken", adapter.DriverVersion.Note);
    }

    [Fact]
    public async Task Unreadable_preferences_are_failed_and_adapters_kept()
    {
        _registry.Deny(RegistryRoot.CurrentUser, UserGpuPreferenceParser.RegistryKey);

        var gpu = await Detector().DetectAsync(Ct);

        Assert.Equal(DataStatus.Failed, gpu.AppPreferences.Status);
        Assert.Contains("UnauthorizedAccessException", gpu.AppPreferences.Note);
        Assert.Single(gpu.Adapters);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var detector = Detector(dxgi: () =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => detector.DetectAsync(cts.Token));
    }
}
