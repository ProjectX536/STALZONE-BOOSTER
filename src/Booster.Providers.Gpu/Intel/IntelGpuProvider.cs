using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Intel;

public sealed class IntelGpuProvider(IFileProbe files) : VendorStubProvider(files, Id, GpuVendor.Intel, LibraryName)
{
    public const string Id = "providers.gpu.intel";
    public const string LibraryName = "ControlLib.dll";

    protected override string LicenseNote => ProviderStrings.IntelLicenseNote;

    protected override string? Remark => ProviderStrings.IntelHybridSeeInventory;
}
