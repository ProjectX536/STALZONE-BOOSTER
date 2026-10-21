using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Intel;

/// <summary>
/// Intel без IGCL: условия лицензии Intel, SDK не подключён (решение плана Этапа 3). Гибридные флаги и выбор GPU
/// приложением уже показывает инвентаризация Этапа 1 — здесь только ссылка на неё.
/// </summary>
public sealed class IntelGpuProvider(IFileProbe files) : VendorStubProvider(files, Id, GpuVendor.Intel, LibraryName)
{
    public const string Id = "providers.gpu.intel";
    public const string LibraryName = "ControlLib.dll";

    protected override string LicenseNote => ProviderStrings.IntelLicenseNote;

    protected override string? Remark => ProviderStrings.IntelHybridSeeInventory;
}
