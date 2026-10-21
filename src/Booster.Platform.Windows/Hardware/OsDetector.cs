using System.Security;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public sealed class OsDetector(ICimClient cim, IRegistryReader registry) : IInventoryDetector<OsInfo>
{
    private const string Query =
        "SELECT Caption, Version, BuildNumber, OSArchitecture, LastBootUpTime FROM Win32_OperatingSystem";
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public string SectionId => SectionIds.Os;

    public async Task<OsInfo> DetectAsync(CancellationToken ct)
    {
        var rows = await cim.QueryAsync(WmiNamespaces.CimV2, Query, ct).ConfigureAwait(false);

        IReadOnlyList<KeyValuePair<string, object?>> values;
        try
        {
            values = registry.GetValues(RegistryRoot.LocalMachine, CurrentVersionKey);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Поля из WMI от реестра не зависят: отмечаем сбоем только то, что читалось из реестра.
            var note = $"{ex.GetType().Name}: {ex.Message}";
            return OsMapper.Map(rows.FirstOrDefault(), null, null) with
            {
                Ubr = Reading<int>.Failure($@"{OsMapper.RegistryKey}\UBR", note),
                DisplayVersion = Reading<string>.Failure($@"{OsMapper.RegistryKey}\DisplayVersion", note),
            };
        }

        object? Value(string name) => values.FirstOrDefault(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        return OsMapper.Map(rows.FirstOrDefault(), Value("DisplayVersion"), Value("UBR"));
    }
}
