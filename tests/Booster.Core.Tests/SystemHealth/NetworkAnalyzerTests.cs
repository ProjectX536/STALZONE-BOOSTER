using Booster.Core.Abstractions;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.SystemHealth;

public class NetworkAnalyzerTests
{
    private const string Gateway = "192.168.1.1";
    private const string Host = "1.1.1.1";
    private static readonly Booster.Core.Configuration.NetworkThresholds Thresholds = FakeConfiguration.DefaultThresholds.Network;

    private static NetworkTargetResult Target(NetworkTargetKind kind, params double?[] replies) =>
        NetworkAnalyzer.Summarize(kind, kind == NetworkTargetKind.Gateway ? Gateway : Host, replies);

    private static NetworkTestResult Result(NetworkMedium medium, params NetworkTargetResult[] targets) =>
        new(DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30), FakeNetworkAdapters.Adapter(medium: medium, gateways: Gateway), targets,
            Reading<double>.Unknown());

    private static IReadOnlyList<SystemAdvice> Advise(NetworkMedium medium, params NetworkTargetResult[] targets) =>
        NetworkAnalyzer.Advise(Result(medium, targets), Thresholds);

    [Fact]
    public void Median_jitter_and_loss()
    {
        var target = Target(NetworkTargetKind.Gateway, 10, 12, null, 14, 10);

        Assert.Equal(5, target.Sent);
        Assert.Equal(4, target.Received);
        Assert.Equal(11, target.MedianMs.Value);
        Assert.Equal(8.0 / 3, target.JitterMs.Value, 6);
        Assert.Equal(20, target.LossPercent.Value);
    }

    [Fact]
    public void Target_without_replies_is_unknown_not_total_loss()
    {
        var target = Target(NetworkTargetKind.Host, null, null, null);

        Assert.Equal(3, target.Sent);
        Assert.Equal(0, target.Received);
        Assert.All(new[] { target.MedianMs, target.JitterMs, target.LossPercent }, reading =>
        {
            Assert.Equal(DataStatus.Unknown, reading.Status);
            Assert.Equal("no replies: the host may not answer ICMP", reading.Note);
        });
    }

    [Fact]
    public void Single_reply_has_unknown_jitter()
    {
        var target = Target(NetworkTargetKind.Gateway, null, 15);

        Assert.Equal(15, target.MedianMs.Value);
        Assert.Equal(DataStatus.Unknown, target.JitterMs.Status);
        Assert.Equal(50, target.LossPercent.Value);
    }

    [Fact]
    public void High_gateway_latency_is_warning()
    {
        var advice = Assert.Single(Advise(NetworkMedium.Ethernet, Target(NetworkTargetKind.Gateway, 12, 12, 12)));

        Assert.Equal(AdviceId.NetworkHighLatency, advice.Id);
        Assert.Equal(AdviceModule.Network, advice.Module);
        Assert.Equal(FindingSeverity.Warning, advice.Severity);
        Assert.Equal(SafetyLevel.L3ManualOnly, advice.Level);
        Assert.Equal([Gateway, "12"], advice.Arguments);
        Assert.Empty(Advise(NetworkMedium.Ethernet, Target(NetworkTargetKind.Gateway, 9, 9, 9)));
    }

    [Fact]
    public void Host_latency_uses_host_threshold()
    {
        Assert.Empty(Advise(NetworkMedium.Ethernet, Target(NetworkTargetKind.Host, 50, 50, 50)));

        var advice = Assert.Single(Advise(NetworkMedium.Ethernet, Target(NetworkTargetKind.Host, 120, 120, 121)));

        Assert.Equal([Host, "120"], advice.Arguments);
    }

    [Fact]
    public void Jitter_and_loss_are_warnings()
    {
        var advice = Advise(NetworkMedium.Ethernet, Target(NetworkTargetKind.Host, 20, 40, null, 20, 40));

        Assert.Equal([AdviceId.NetworkJitter, AdviceId.NetworkLoss], advice.Select(a => a.Id));
        Assert.Equal([Host, "20"], advice[0].Arguments);
        Assert.Equal([Host, "20"], advice[1].Arguments);
    }

    [Fact]
    public void Loss_without_replies_gives_no_advice()
    {
        Assert.Empty(Advise(NetworkMedium.Wireless, Target(NetworkTargetKind.Host, null, null, null)));
    }

    [Fact]
    public void Wireless_note_only_with_other_advice()
    {
        Assert.Empty(Advise(NetworkMedium.Wireless, Target(NetworkTargetKind.Gateway, 2, 2, 2)));

        var advice = Advise(NetworkMedium.Wireless, Target(NetworkTargetKind.Gateway, 30, 30, 30));

        Assert.Equal([AdviceId.NetworkHighLatency, AdviceId.NetworkWireless], advice.Select(a => a.Id));
        Assert.Equal(FindingSeverity.Info, advice[1].Severity);
        Assert.Empty(advice[1].Arguments);
    }
}
