using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu;

public abstract class VendorStubProvider(IFileProbe files, string providerId, GpuVendor vendor, string libraryName) : IGpuProvider
{
    public string ProviderId => providerId;

    public GpuVendor? Vendor => vendor;

    protected abstract string LicenseNote { get; }

    protected virtual string? Remark => null;

    public Task<bool> IsAvailableAsync(GpuAdapterInfo adapter, CancellationToken ct) => Task.FromResult(false);

    public Task<GpuCapabilities> GetCapabilitiesAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
        Task.FromResult(new GpuCapabilities(providerId, adapter.Luid is { IsAvailable: true, Value: var luid }
            ? [new CapabilityReport(CapabilityIds.GpuVendorApi, CapabilityScopes.Adapter(luid), CapabilityStatus.NotSupported, providerId, Note())]
            : []));

    public Task<GpuTelemetry> ReadTelemetryAsync(GpuAdapterInfo adapter, CancellationToken ct)
    {
        var note = Note();
        return Task.FromResult(new GpuTelemetry(
            Reading<double>.Unsupported(providerId, note),
            Reading<double>.Unsupported(providerId, note),
            Reading<double>.Unsupported(providerId, note),
            Reading<double>.Unsupported(providerId, note),
            Reading<double>.Unsupported(providerId, note),
            Reading<long>.Unsupported(providerId, note),
            Reading<long>.Unsupported(providerId, note),
            Reading<double>.Unsupported(providerId, note),
            Reading<int>.Unsupported(providerId, note),
            Reading<GpuThrottleReasons>.Unsupported(providerId, note)));
    }

    public Task<GpuSettingsResult> ReadSettingsAsync(GpuAdapterInfo adapter, string? application, CancellationToken ct) =>
        Task.FromResult(new GpuSettingsResult(providerId, application, null, Reading<IReadOnlyList<GpuSetting>>.Unsupported(providerId, Note())));

    private string Note()
    {
        var installed = files.FileExists(Path.Combine(Environment.SystemDirectory, libraryName));
        var library = Format(installed ? ProviderStrings.VendorLibraryInstalledFormat : ProviderStrings.VendorLibraryMissingFormat, libraryName);
        var note = Format(ProviderStrings.NoteJoinFormat, LicenseNote, library);
        return Remark is { } remark ? Format(ProviderStrings.NoteJoinFormat, note, remark) : note;
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
