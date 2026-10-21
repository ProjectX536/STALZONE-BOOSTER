using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Hags;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.Reports;

public class EnumTextTests
{
    private static void AssertEveryValueHasText<TEnum>()
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var key = $"{typeof(TEnum).Name}_{value}";
            Assert.False(string.IsNullOrWhiteSpace(ReportStrings.ResourceManager.GetString(key, ReportStrings.Culture)),
                $"ReportStrings has no {key}");
        }
    }

    [Fact]
    public void Every_enum_value_shown_in_the_report_has_text()
    {
        AssertEveryValueHasText<DataStatus>();
        AssertEveryValueHasText<GpuVendor>();
        AssertEveryValueHasText<GpuAdapterKind>();
        AssertEveryValueHasText<AppGpuPreferenceKind>();
        AssertEveryValueHasText<ChannelLabelHint>();
        AssertEveryValueHasText<StorageMediaKind>();
        AssertEveryValueHasText<StorageBusKind>();
        AssertEveryValueHasText<StorageHealth>();
        AssertEveryValueHasText<PcFormFactor>();
        AssertEveryValueHasText<FirmwareKind>();
        AssertEveryValueHasText<DeviceState>();
        AssertEveryValueHasText<CapabilityStatus>();
        AssertEveryValueHasText<FindingKind>();
        AssertEveryValueHasText<FindingSeverity>();
        AssertEveryValueHasText<FrameCaptureStatus>();
        AssertEveryValueHasText<FrameMetricsStatus>();
        AssertEveryValueHasText<ConfigWarningKind>();
        AssertEveryValueHasText<ChangeStatus>();
        AssertEveryValueHasText<SessionState>();
        AssertEveryValueHasText<SessionOutcome>();
        AssertEveryValueHasText<SessionKind>();
        AssertEveryValueHasText<ObservedState>();
        AssertEveryValueHasText<JournalProblemKind>();
        AssertEveryValueHasText<PolicyVerdict>();
        AssertEveryValueHasText<PolicyReason>();
        AssertEveryValueHasText<ProcessPriority>();
        AssertEveryValueHasText<ActionMode>();
        AssertEveryValueHasText<SafetyLevel>();
        AssertEveryValueHasText<GpuSettingOrigin>();
        AssertEveryValueHasText<GpuThrottleReasons>();
        AssertEveryValueHasText<ThrottleSignal>();
        AssertEveryValueHasText<ThrottleConfidence>();
        AssertEveryValueHasText<PowerSource>();
        AssertEveryValueHasText<BottleneckClass>();
        AssertEveryValueHasText<AnalysisConfidence>();
        AssertEveryValueHasText<EvidenceKind>();
        AssertEveryValueHasText<GpuLoadState>();
        AssertEveryValueHasText<RecommendationId>();
        AssertEveryValueHasText<ComparedMetric>();
        AssertEveryValueHasText<ChangeVerdict>();
        AssertEveryValueHasText<ComparisonCaveat>();
        AssertEveryValueHasText<AdviceId>();
        AssertEveryValueHasText<AdviceModule>();
        AssertEveryValueHasText<PagefileMode>();
        AssertEveryValueHasText<ServiceCategory>();
        AssertEveryValueHasText<ServiceRunState>();
        AssertEveryValueHasText<ServiceStartMode>();
        AssertEveryValueHasText<StabilityEventKind>();
        AssertEveryValueHasText<NetworkMedium>();
        AssertEveryValueHasText<NetworkTargetKind>();
        AssertEveryValueHasText<HagsCheck>();
        AssertEveryValueHasText<HagsVerdict>();
    }

    [Fact]
    public void Text_comes_from_resources()
    {
        Assert.Equal("Дискретный", EnumText.Format(GpuAdapterKind.Discrete));
    }

    [Fact]
    public void Boxed_value_gets_the_same_text()
    {
        Enum boxed = GpuAdapterKind.Discrete;

        Assert.Equal("Дискретный", EnumText.Format(boxed));
        Assert.Equal("42", EnumText.Format((Enum)(GpuAdapterKind)42));
    }

    [Fact]
    public void Value_outside_the_enum_falls_back_to_its_number()
    {
        Assert.Equal("42", EnumText.Format((GpuAdapterKind)42));
    }
}
