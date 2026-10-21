using System.Diagnostics.Eventing.Reader;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>
/// События стабильности из журналов System и Application через EventLogReader. Только чтение. Каждый журнал читается
/// от новых к старым не дальше предела; затем оба сливаются и остаются самые новые <c>maxEvents</c>.
/// </summary>
public sealed class StabilityEventSource : IStabilityEventSource
{
    public Task<StabilityLog> ReadAsync(DateTimeOffset since, int maxEvents, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEvents);
        return Task.Run(() => Read(since, maxEvents, ct), ct);
    }

    private static StabilityLog Read(DateTimeOffset since, int maxEvents, CancellationToken ct)
    {
        var system = ReadLog(StabilityQueries.SystemLog, StabilityQueries.SystemXPath(since), maxEvents, ct);
        var application = ReadLog(StabilityQueries.ApplicationLog, StabilityQueries.ApplicationXPath(since), maxEvents, ct);

        var merged = system.Events.Concat(application.Events).OrderByDescending(e => e.Time).ToArray();
        var truncated = system.More || application.More || merged.Length > maxEvents;
        return new StabilityLog(since, merged.Take(maxEvents).ToArray(), truncated);
    }

    private static (List<StabilityEvent> Events, bool More) ReadLog(string log, string xpath, int maxEvents, CancellationToken ct)
    {
        var events = new List<StabilityEvent>();
        var query = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        while (reader.ReadEvent() is { } record)
        {
            using (record)
            {
                ct.ThrowIfCancellationRequested();
                if (events.Count >= maxEvents)
                    return (events, true);
                if (ToRecord(record) is { } parsed && StabilityQueries.ToEvent(parsed) is { } stabilityEvent)
                    events.Add(stabilityEvent);
            }
        }
        return (events, false);
    }

    private static StabilityRecord? ToRecord(EventRecord record)
    {
        if (record.TimeCreated is not { } time || string.IsNullOrEmpty(record.ProviderName))
            return null;

        var subject = record.Properties.Select(p => p.Value).OfType<string>().FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        return new StabilityRecord(record.ProviderName, record.Id, record.Level, new DateTimeOffset(time), subject);
    }
}
