using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Видеоадаптеры из четырёх независимых источников. Сбой одного источника не мешает остальным:
/// он становится <see cref="DataStatus.Failed"/> с причиной и предупреждением в логе.
/// </summary>
public sealed partial class GpuDetector(
    ICimClient cim,
    IDxgiAdapterSource dxgi,
    IKmtAdapterTypeSource kmt,
    IRegistryReader registry,
    ICapabilityRegistry capabilities,
    ILogger<GpuDetector> logger) : IInventoryDetector<GpuInventory>
{
    public const string ProviderId = "platform.windows.gpu";

    private const string WmiSource = "WMI Win32_VideoController";
    private const string WmiQuery =
        "SELECT Name, PNPDeviceID, AdapterCompatibility, DriverVersion, DriverDate FROM Win32_VideoController";
    private const string PreferencesSource = @"HKCU\" + UserGpuPreferenceParser.RegistryKey;

    public string SectionId => SectionIds.Gpu;

    public async Task<GpuInventory> DetectAsync(CancellationToken ct)
    {
        Reading<IReadOnlyList<CimRow>> wmi;
        try
        {
            wmi = Reading<IReadOnlyList<CimRow>>.Known(
                await cim.QueryAsync(WmiNamespaces.CimV2, WmiQuery, ct).ConfigureAwait(false), WmiSource);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            wmi = Failed<IReadOnlyList<CimRow>>(WmiSource, ex);
        }

        var adapters = Read(DxgiAdapterSource.Source, dxgi.Enumerate, ct, out var dxgiStatus);
        var flags = Read(KmtAdapterTypeSource.Source, kmt.Query, ct, out var kmtStatus);
        var preferences = Read(PreferencesSource,
            () => UserGpuPreferenceParser.Parse(registry.GetValues(RegistryRoot.CurrentUser, UserGpuPreferenceParser.RegistryKey)),
            ct, out _);

        capabilities.Report(new CapabilityReport(CapabilityIds.DxgiAdapters, CapabilityScopes.System, dxgiStatus, ProviderId,
            adapters.Note));
        capabilities.Report(new CapabilityReport(CapabilityIds.HybridGpuFlags, CapabilityScopes.System, kmtStatus, ProviderId,
            flags.Note));

        return GpuAdapterMerger.Merge(wmi, adapters, flags, preferences);
    }

    private Reading<T> Read<T>(string source, Func<T> read, CancellationToken ct, out CapabilityStatus status)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var value = read();
            status = CapabilityStatus.Supported;
            return Reading<T>.Known(value, source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Нет библиотеки, функции или версии Windows — возможности нет; иной сбой не говорит, есть ли она.
            status = ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException
                ? CapabilityStatus.NotSupported
                : CapabilityStatus.Unknown;
            return Failed<T>(source, ex);
        }
    }

    private Reading<T> Failed<T>(string source, Exception ex)
    {
        LogSourceFailed(source, ex.GetType().Name, ex.Message);
        // Тип исключения нужен в причине: по нему видно, отсутствует ли API или он отказал.
        return Reading<T>.Failure(source, $"{ex.GetType().Name}: {ex.Message}");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "GPU source {Source} failed: {ErrorType}: {Error}")]
    private partial void LogSourceFailed(string source, string errorType, string error);
}
