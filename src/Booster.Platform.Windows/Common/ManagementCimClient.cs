using System.Management;
using EnumerationOptions = System.Management.EnumerationOptions;

namespace Booster.Platform.Windows.Common;

public sealed class ManagementCimClient : ICimClient
{
    private readonly TimeSpan _timeout;

    public ManagementCimClient(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
    }

    public Task<IReadOnlyList<CimRow>> QueryAsync(string wmiNamespace, string wql, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wmiNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(wql);
        return Task.Run(() => Query(wmiNamespace, wql, ct), ct);
    }

    private IReadOnlyList<CimRow> Query(string wmiNamespace, string wql, CancellationToken ct)
    {
        try
        {
            return Read(wmiNamespace, wql, ct);
        }
        // С ReturnImmediately ошибка схемы приходит на первой строке, а не в Get().
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass)
        {
            throw new CimSchemaMissingException(wmiNamespace, $"{ex.ErrorCode}: {wql}", ex);
        }
    }

    private IReadOnlyList<CimRow> Read(string wmiNamespace, string wql, CancellationToken ct)
    {
        var options = new EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = _timeout };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(wmiNamespace), new ObjectQuery(wql), options);
        using var results = searcher.Get();

        var rows = new List<CimRow>();
        foreach (var item in results)
        {
            using (item)
            {
                ct.ThrowIfCancellationRequested();
                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.Properties)
                    properties[property.Name] = property.Value;
                rows.Add(new CimRow(properties));
            }
        }

        return rows;
    }
}
