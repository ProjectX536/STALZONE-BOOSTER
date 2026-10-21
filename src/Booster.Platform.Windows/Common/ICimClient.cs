namespace Booster.Platform.Windows.Common;

public interface ICimClient
{
    Task<IReadOnlyList<CimRow>> QueryAsync(string wmiNamespace, string wql, CancellationToken ct);
}

public static class WmiNamespaces
{
    public const string CimV2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
}
