using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.GameNetwork;

public class ServerResolverTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTrafficSource _traffic = new();
    private readonly FakeTcpConnections _tcp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServerResolver Resolver() => new(_traffic, _tcp, new FakeConfiguration(), _time);

    private async Task<ResolvedServer?> DriveAsync(Task<ResolvedServer?> run)
    {
        for (var i = 0; i < 20000 && !run.IsCompleted; i++)
        {
            await Task.Delay(1, Ct);
            _time.Advance(TimeSpan.FromSeconds(1));
        }
        return await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Manual_server_still_opens_traffic_session()
    {
        var resolved = await Resolver().ResolveAsync("203.0.113.5", new HashSet<int>(), Ct);

        Assert.NotNull(resolved);
        Assert.Equal(ServerSource.Manual, resolved.Server.Source);
        Assert.Equal("203.0.113.5", resolved.Server.Address);
        Assert.Same(_traffic.Session, resolved.Session);
        Assert.Equal(1, _traffic.Starts);
        Assert.False(_traffic.Session.Disposed);
    }

    [Fact]
    public async Task No_server_disposes_session()
    {
        var resolved = await DriveAsync(Resolver().ResolveAsync(null, new HashSet<int> { 100 }, Ct));

        Assert.Null(resolved);
        Assert.True(_traffic.Session.Disposed);
    }

    [Fact]
    public async Task Cancel_while_waiting_disposes_session()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Resolver().ResolveAsync(null, new HashSet<int> { 100 }, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(_traffic.Session.Disposed);
    }

    [Fact]
    public async Task Tcp_fallback_keeps_session_when_traffic_has_no_flows()
    {
        _tcp.Result = Reading<IReadOnlyList<TcpConnection>>.Known([new TcpConnection(100, IPAddress.Parse("203.0.113.9"), 27015, true)], "fake");

        var resolved = await DriveAsync(Resolver().ResolveAsync(null, new HashSet<int> { 100 }, Ct));

        Assert.NotNull(resolved);
        Assert.Equal(ServerSource.TcpTable, resolved.Server.Source);
        Assert.Same(_traffic.Session, resolved.Session);
        Assert.False(_traffic.Session.Disposed);
    }
}
