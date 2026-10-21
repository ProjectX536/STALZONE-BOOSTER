using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.SystemHealth;

public class MemoryAdvisorTests
{
    private const double LowFreeSpacePercent = 10;
    private const string PagefileC = @"C:\pagefile.sys";
    private static readonly MemoryThresholds Thresholds = new(CommitPercent: 90);

    private static Reading<MemoryStatus> Memory(long commitTotal = 40, long commitLimit = 100, long commitPeak = 50) =>
        Known(new MemoryStatus(32 * GiB, 16 * GiB, commitTotal * GiB, commitLimit * GiB, commitPeak * GiB, 4 * GiB, GiB, GiB, 200, 3000,
            100_000));

    private static Reading<PagefileConfiguration> Pagefile(Reading<bool> automatic, IReadOnlyList<PagefileSetting> settings,
        IReadOnlyList<PagefileUsage> usage) => Known(new PagefileConfiguration(automatic, settings, usage));

    private static Reading<PagefileConfiguration> SystemManaged(string path = PagefileC) =>
        Pagefile(Known(true), [], [new PagefileUsage(path, 4096, 512, 1024)]);

    private static Reading<PagefileConfiguration> ManualC() =>
        Pagefile(Known(false), [new PagefileSetting(PagefileC, 4096, 8192)], [new PagefileUsage(PagefileC, 4096, 512, 1024)]);

    private static IReadOnlyList<SystemAdvice> Advise(Reading<MemoryStatus>? memory = null, Reading<PagefileConfiguration>? pagefile = null,
        HardwareInventory? hardware = null) =>
        MemoryAdvisor.Advise(memory ?? Memory(), pagefile ?? SystemManaged(), hardware ?? Hardware(), Thresholds, LowFreeSpacePercent);

    private static StorageDeviceInfo Disk(StorageMediaKind media, params VolumeInfo[] volumes) =>
        new(Known("disk"), Known(media), Known(StorageBusKind.Sata), Known(1000 * GiB), Known(StorageHealth.Healthy), volumes);

    private static HardwareInventory WithStorage(params StorageDeviceInfo[] devices) =>
        Hardware() with { Storage = Known<IReadOnlyList<StorageDeviceInfo>>(devices) };

    private static MemoryModule Module(long capacityGib = 16, int? rated = 3200, int? configured = 3200, string part = "KF3200C16D4/16GX") =>
        new(Known(capacityGib * GiB), Known("DDR4"), Speed(rated), Speed(configured), Known("Kingston"), Known(part), Known("DIMM"),
            Known("BANK"));

    private static Reading<int> Speed(int? mts) => mts is { } value ? Known(value) : Reading<int>.Unknown(Source);

    private static HardwareInventory WithModules(params MemoryModule[] modules) =>
        Hardware() with
        {
            Memory = Known(new MemoryInventory(Known(32 * GiB), modules, Known(4), Reading<bool>.Unknown(Source), ChannelLabelHint.None)),
        };

    private static SystemAdvice Single(IReadOnlyList<SystemAdvice> advice, AdviceId id) => Assert.Single(advice, a => a.Id == id);

    [Fact]
    public void System_managed_pagefile_with_normal_commit_gives_no_advice()
    {
        Assert.Empty(Advise());
        Assert.Equal(PagefileMode.SystemManaged, MemoryAdvisor.Mode(SystemManaged()).Value);
    }

    [Fact]
    public void Disabled_pagefile_is_warning()
    {
        var pagefile = Pagefile(Known(false), [], []);

        var advice = Single(Advise(pagefile: pagefile), AdviceId.PagefileDisabled);

        Assert.Equal(PagefileMode.Disabled, MemoryAdvisor.Mode(pagefile).Value);
        Assert.Equal(FindingSeverity.Warning, advice.Severity);
        Assert.Equal(AdviceModule.Memory, advice.Module);
        Assert.Equal(SafetyLevel.L3ManualOnly, advice.Level);
        Assert.Empty(advice.Arguments);
    }

    [Fact]
    public void Empty_settings_with_active_usage_is_disabled_pending_reboot()
    {
        var pagefile = Pagefile(Known(false), [], [new PagefileUsage(PagefileC, 4096, 512, 1024)]);

        Assert.Equal(PagefileMode.Disabled, MemoryAdvisor.Mode(pagefile).Value);
    }

    [Fact]
    public void Manual_pagefile_is_info_with_paths()
    {
        var pagefile = Pagefile(Known(false),
            [new PagefileSetting(PagefileC, 4096, 8192), new PagefileSetting(@"D:\pagefile.sys", 2048, 2048)], []);

        var advice = Single(Advise(pagefile: pagefile), AdviceId.PagefileManual);

        Assert.Equal(FindingSeverity.Info, advice.Severity);
        Assert.Equal([@"C:\pagefile.sys, D:\pagefile.sys"], advice.Arguments);
    }

    [Fact]
    public void Pending_setting_without_usage_is_reported_as_manual_not_disabled()
    {
        var pagefile = Pagefile(Known(false), [new PagefileSetting(PagefileC, 4096, 8192)], []);

        Assert.Equal(PagefileMode.Manual, MemoryAdvisor.Mode(pagefile).Value);
        Assert.DoesNotContain(Advise(pagefile: pagefile), a => a.Id == AdviceId.PagefileDisabled);
    }

    [Fact]
    public void System_chosen_size_on_every_file_is_system_managed_size()
    {
        var pagefile = Pagefile(Known(false), [new PagefileSetting(PagefileC, 0, 0)], [new PagefileUsage(PagefileC, 4096, 512, 1024)]);

        Assert.Equal(PagefileMode.SystemManagedSize, MemoryAdvisor.Mode(pagefile).Value);
        Assert.Empty(Advise(pagefile: pagefile));
    }

    [Fact]
    public void Commit_at_threshold_is_warning()
    {
        var advice = Single(Advise(memory: Memory(commitTotal: 90, commitLimit: 100, commitPeak: 95)), AdviceId.CommitNearLimit);

        Assert.Equal(FindingSeverity.Warning, advice.Severity);
        Assert.Equal(["90"], advice.Arguments);
        Assert.DoesNotContain(Advise(memory: Memory(commitTotal: 90, commitPeak: 95)), a => a.Id == AdviceId.CommitPeakNearLimit);
        Assert.Empty(Advise(memory: Memory(commitTotal: 89, commitPeak: 89)));
    }

    [Fact]
    public void Commit_peak_near_limit_is_info()
    {
        var advice = Single(Advise(memory: Memory(commitTotal: 50, commitPeak: 97)), AdviceId.CommitPeakNearLimit);

        Assert.Equal(FindingSeverity.Info, advice.Severity);
        Assert.Equal(["97"], advice.Arguments);
    }

    [Fact]
    public void Unknown_memory_gives_no_commit_advice()
    {
        Assert.Empty(Advise(memory: Reading<MemoryStatus>.Failure(Source, "failed")));
    }

    [Fact]
    public void Low_space_on_pagefile_volume_is_warning_only_for_system_managed()
    {
        var hardware = WithStorage(Disk(StorageMediaKind.Ssd, Volume("C:", 500 * GiB, 10 * GiB)));

        var advice = Single(Advise(hardware: hardware), AdviceId.PagefileVolumeLowSpace);

        Assert.Equal(FindingSeverity.Warning, advice.Severity);
        Assert.Equal(["C:"], advice.Arguments);
        Assert.DoesNotContain(Advise(pagefile: ManualC(), hardware: hardware), a => a.Id == AdviceId.PagefileVolumeLowSpace);
    }

    [Fact]
    public void Pagefile_path_falls_back_to_settings_when_usage_is_empty()
    {
        var hardware = WithStorage(Disk(StorageMediaKind.Ssd, Volume("C:", 500 * GiB, 10 * GiB)));
        var pagefile = Pagefile(Known(false), [new PagefileSetting(@"c:\pagefile.sys", 0, 0)], []);

        var advice = Single(Advise(pagefile: pagefile, hardware: hardware), AdviceId.PagefileVolumeLowSpace);

        Assert.Equal(["C:"], advice.Arguments);
    }

    [Fact]
    public void Pagefile_on_unknown_volume_gives_no_volume_advice()
    {
        var hardware = WithStorage(Disk(StorageMediaKind.Ssd, Volume("C:", 500 * GiB, 10 * GiB)));

        var advice = Advise(pagefile: SystemManaged(@"D:\pagefile.sys"), hardware: hardware);

        Assert.DoesNotContain(advice, a => a.Id is AdviceId.PagefileVolumeLowSpace or AdviceId.PagefileOnHdd);
    }

    [Fact]
    public void Pagefile_on_hdd_with_ssd_present_is_info()
    {
        var hardware = WithStorage(
            Disk(StorageMediaKind.Ssd, Volume("C:", 500 * GiB, 250 * GiB)),
            Disk(StorageMediaKind.Hdd, Volume("D:", 2000 * GiB, 1000 * GiB)));

        var advice = Single(Advise(pagefile: SystemManaged(@"D:\pagefile.sys"), hardware: hardware), AdviceId.PagefileOnHdd);

        Assert.Equal(FindingSeverity.Info, advice.Severity);
        Assert.Equal(["D:"], advice.Arguments);
        Assert.DoesNotContain(Advise(pagefile: SystemManaged(@"D:\pagefile.sys"),
            hardware: WithStorage(Disk(StorageMediaKind.Hdd, Volume("D:", 2000 * GiB, 1000 * GiB)))), a => a.Id == AdviceId.PagefileOnHdd);
    }

    [Fact]
    public void Configured_speed_below_rated_is_info()
    {
        var advice = Single(Advise(hardware: WithModules(Module(configured: 2133), Module(configured: 2133))), AdviceId.MemoryBelowRatedSpeed);

        Assert.Equal(FindingSeverity.Info, advice.Severity);
        Assert.Equal(["2133", "3200"], advice.Arguments);
    }

    [Fact]
    public void Unknown_speeds_give_no_speed_advice()
    {
        var advice = Advise(hardware: WithModules(Module(rated: null, configured: 2133), Module(rated: 3200, configured: null)));

        Assert.DoesNotContain(advice, a => a.Id == AdviceId.MemoryBelowRatedSpeed);
    }

    [Fact]
    public void Single_module_is_info()
    {
        var advice = Single(Advise(hardware: WithModules(Module())), AdviceId.SingleMemoryModule);

        Assert.Equal(FindingSeverity.Info, advice.Severity);
    }

    [Fact]
    public void Mixed_modules_are_info()
    {
        Single(Advise(hardware: WithModules(Module(capacityGib: 16), Module(capacityGib: 8))), AdviceId.MixedMemoryModules);
        Single(Advise(hardware: WithModules(Module(part: "A"), Module(part: "B"))), AdviceId.MixedMemoryModules);
        Assert.Empty(Advise(hardware: WithModules(Module(), Module())));
    }

    [Fact]
    public void Warnings_come_before_info()
    {
        var advice = Advise(memory: Memory(commitTotal: 95), pagefile: ManualC(), hardware: WithModules(Module()));

        Assert.Equal([AdviceId.CommitNearLimit, AdviceId.PagefileManual, AdviceId.SingleMemoryModule], advice.Select(a => a.Id));
    }

    [Fact]
    public void Unknown_automatic_flag_gives_unknown_mode()
    {
        var pagefile = Pagefile(Reading<bool>.Failure(PagefileMapperSource, "access denied"), [], []);

        var mode = MemoryAdvisor.Mode(pagefile);

        Assert.False(mode.IsAvailable);
        Assert.Equal(PagefileMapperSource, mode.Source);
        Assert.DoesNotContain(Advise(pagefile: pagefile), a => a.Id == AdviceId.PagefileDisabled);
    }

    private const string PagefileMapperSource = "WMI Win32_ComputerSystem.AutomaticManagedPagefile";
}
