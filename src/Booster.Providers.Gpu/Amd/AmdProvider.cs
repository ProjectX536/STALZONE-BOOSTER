using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Amd;

/// <summary>
/// AMD без ADLX: лицензия ADLX требует отдельного соглашения с пользователем, поэтому SDK не подключён (решение плана
/// Этапа 3). Телеметрию адаптера AMD даёт общий провайдер.
/// </summary>
public sealed class AmdProvider(IFileProbe files) : VendorStubProvider(files, Id, GpuVendor.Amd, LibraryName)
{
    public const string Id = "providers.gpu.amd";
    public const string LibraryName = "amdadlx64.dll";

    protected override string LicenseNote => ProviderStrings.AmdLicenseNote;
}
