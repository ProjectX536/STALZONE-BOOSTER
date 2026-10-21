using Booster.App.Services;

namespace Booster.App.Tests.TestSupport;

/// <summary>Вместо звука — запись сигналов: «start» в начале прогона, «end» в конце.</summary>
internal sealed class FakeCuePlayer : IBenchmarkCuePlayer
{
    private readonly Lock _lock = new();
    private readonly List<string> _events = [];

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_lock)
                return [.. _events];
        }
    }

    public void RunStarted()
    {
        lock (_lock)
            _events.Add("start");
    }

    public void RunEnded()
    {
        lock (_lock)
            _events.Add("end");
    }
}
