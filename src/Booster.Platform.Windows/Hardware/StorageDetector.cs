using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Накопители: сначала Storage Management API (MSFT_*), при любом его сбое — Win32_DiskDrive без типа носителя и шины.
/// Тома из Win32_LogicalDisk нужны обоим путям, поэтому их сбой проваливает секцию целиком.
/// </summary>
public sealed partial class StorageDetector(ICimClient cim, ICapabilityRegistry capabilities, ILogger<StorageDetector> logger)
    : IInventoryDetector<IReadOnlyList<StorageDeviceInfo>>
{
    public const string ProviderId = "platform.windows.storage";

    private const string PhysicalDiskQuery = "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk";
    private const string PartitionQuery = "SELECT DiskNumber, DriveLetter, Size FROM MSFT_Partition";
    private const string LogicalDiskQuery = "SELECT DeviceID, DriveType, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk";
    private const string DiskDriveQuery = "SELECT Index, Model, Size FROM Win32_DiskDrive";

    public string SectionId => SectionIds.Storage;

    public async Task<IReadOnlyList<StorageDeviceInfo>> DetectAsync(CancellationToken ct)
    {
        var logical = await cim.QueryAsync(WmiNamespaces.CimV2, LogicalDiskQuery, ct).ConfigureAwait(false);

        try
        {
            var disks = await cim.QueryAsync(WmiNamespaces.Storage, PhysicalDiskQuery, ct).ConfigureAwait(false);
            var partitions = await cim.QueryAsync(WmiNamespaces.Storage, PartitionQuery, ct).ConfigureAwait(false);
            Report(CapabilityStatus.Supported, note: null);
            return StorageMapper.MapMsft(disks, partitions, logical);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFallback(ex.GetType().Name, ex.Message);
            // Нет пространства имён или класса — возможности нет; иной сбой (доступ, провайдер) не говорит, есть ли она.
            Report(ex is CimSchemaMissingException ? CapabilityStatus.NotSupported : CapabilityStatus.Unknown,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        var drives = await cim.QueryAsync(WmiNamespaces.CimV2, DiskDriveQuery, ct).ConfigureAwait(false);
        return StorageMapper.MapFallback(drives, logical);
    }

    private void Report(CapabilityStatus status, string? note) =>
        capabilities.Report(new CapabilityReport(CapabilityIds.StorageWmi, CapabilityScopes.System, status, ProviderId, note));

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Storage Management API unavailable, falling back to Win32_DiskDrive: {ErrorType}: {Error}")]
    private partial void LogFallback(string errorType, string error);
}
