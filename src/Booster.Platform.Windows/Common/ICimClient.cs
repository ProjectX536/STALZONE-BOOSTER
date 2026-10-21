namespace Booster.Platform.Windows.Common;

/// <summary>Запросы WMI/CIM. Реализация только читает; в тестах подменяется фикстурами.</summary>
public interface ICimClient
{
    Task<IReadOnlyList<CimRow>> QueryAsync(string wmiNamespace, string wql, CancellationToken ct);
}

public static class WmiNamespaces
{
    public const string CimV2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
}
