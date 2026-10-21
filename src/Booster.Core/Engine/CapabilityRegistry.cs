using System.Collections.Concurrent;
using Booster.Core.Abstractions;

namespace Booster.Core.Engine;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly ConcurrentDictionary<(string Id, string Scope), CapabilityReport> _reports = new();

    public void Report(CapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _reports[(report.CapabilityId, report.Scope)] = report;
    }

    public CapabilityReport Get(string capabilityId, string scope = CapabilityScopes.System) =>
        _reports.TryGetValue((capabilityId, scope), out var report)
            ? report
            : new CapabilityReport(capabilityId, scope, CapabilityStatus.Unknown, "none", null);

    public IReadOnlyList<CapabilityReport> All() =>
        _reports.Values
            .OrderBy(r => r.CapabilityId, StringComparer.Ordinal)
            .ThenBy(r => r.Scope, StringComparer.Ordinal)
            .ToArray();
}
