using Booster.Core.Abstractions;

namespace Booster.Core.GameNetwork;

public static class TunnelDetector
{
    public static bool IsTunnel(NetworkAdapterInfo adapter, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(keywords);
        if (adapter.Medium == NetworkMedium.Tunnel)
            return true;
        return keywords.Any(k => !string.IsNullOrWhiteSpace(k)
            && (adapter.Description.Contains(k, StringComparison.OrdinalIgnoreCase)
                || adapter.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }
}
