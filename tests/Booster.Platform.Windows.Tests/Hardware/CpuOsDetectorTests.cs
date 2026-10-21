using System.ComponentModel;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class CpuOsDetectorTests
{
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private sealed class FakeTopology(Func<IReadOnlyList<byte>> classes) : ICpuTopologySource
    {
        public IReadOnlyList<byte> GetEfficiencyClassPerCore() => classes();
    }

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cpu_detector_combines_wmi_and_topology()
    {
        var detector = new CpuDetector(FakeCimClient.FromFixture("desktop-nvidia"), new FakeTopology(() => [1, 1, 1, 1, 1, 1, 0, 0, 0, 0]));

        var cpu = await detector.DetectAsync(Ct);

        Assert.Equal(SectionIds.Cpu, detector.SectionId);
        Assert.Equal("Intel(R) Core(TM) i5-14400F", Assert.Single(cpu.Packages).Name);
        Assert.Equal(new(6, 4, true), cpu.Topology.Value);
    }

    [Fact]
    public async Task Topology_failure_keeps_packages_and_marks_topology_failed()
    {
        var detector = new CpuDetector(FakeCimClient.FromFixture("desktop-nvidia"),
            new FakeTopology(() => throw new Win32Exception(87)));

        var cpu = await detector.DetectAsync(Ct);

        Assert.Single(cpu.Packages);
        Assert.Equal(DataStatus.Failed, cpu.Topology.Status);
        Assert.NotNull(cpu.Topology.Note);
    }

    [Fact]
    public async Task Os_detector_reads_display_version_and_ubr_from_registry()
    {
        var registry = new FakeRegistryReader()
            .Set(RegistryRoot.LocalMachine, CurrentVersion, "DisplayVersion", "25H2")
            .Set(RegistryRoot.LocalMachine, CurrentVersion, "UBR", 1234);
        var detector = new OsDetector(FakeCimClient.FromFixture("desktop-nvidia"), registry);

        var os = await detector.DetectAsync(Ct);

        Assert.Equal(SectionIds.Os, detector.SectionId);
        Assert.Equal(26300, os.BuildNumber.Value);
        Assert.Equal(1234, os.Ubr.Value);
        Assert.Equal("25H2", os.DisplayVersion.Value);
    }

    [Fact]
    public async Task Unreadable_registry_leaves_wmi_fields_available()
    {
        var registry = new FakeRegistryReader().Deny(RegistryRoot.LocalMachine, CurrentVersion);
        var detector = new OsDetector(FakeCimClient.FromFixture("desktop-nvidia"), registry);

        var os = await detector.DetectAsync(Ct);

        Assert.Equal(DataStatus.Available, os.Caption.Status);
        Assert.Equal(DataStatus.Failed, os.Ubr.Status);
        Assert.Equal(DataStatus.Failed, os.DisplayVersion.Status);
    }
}
