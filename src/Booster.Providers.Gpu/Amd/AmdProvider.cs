using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Amd;

public sealed class AmdProvider(IFileProbe files) : VendorStubProvider(files, Id, GpuVendor.Amd, LibraryName)
{
    public const string Id = "providers.gpu.amd";
    public const string LibraryName = "amdadlx64.dll";

    protected override string LicenseNote => ProviderStrings.AmdLicenseNote;
}
