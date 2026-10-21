<#
.SYNOPSIS
    Записывает WMI-фикстуры для тестов Booster с текущей машины. Скрипт только читает.

.DESCRIPTION
    Запускается в Windows PowerShell 5.1: Get-WmiObject отдаёт даты в сыром DMTF — так же,
    как System.Management в Booster.

    Записываются только свойства, которые читает Booster. Серийные номера, имя компьютера и
    пользователя, UUID, идентификаторы томов и метки дисков не выбираются. Идентификатор экземпляра
    PnP, который задаёт само устройство (часто это серийный номер), заменяется на SANITIZED.
    Устройства PnP ограничены шинами PCI, HDAUDIO и DISPLAY, а также устройствами с ошибкой.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Booster.Platform.Windows.Tests\Fixtures\record-wmi-fixtures.ps1 -Out tests\Booster.Platform.Windows.Tests\Fixtures\Wmi\desktop-nvidia
#>
param(
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if ($PSVersionTable.PSVersion.Major -ne 5) {
    throw "Нужен Windows PowerShell 5.1 (powershell.exe), запущен PowerShell $($PSVersionTable.PSVersion)."
}

$cimv2 = 'root\cimv2'
$storage = 'root\Microsoft\Windows\Storage'
$pnpBuses = '^(PCI|HDAUDIO|DISPLAY)\\'

$classes = [ordered]@{
    'Win32_Processor'           = @{ Ns = $cimv2; Props = 'Name', 'Manufacturer', 'NumberOfCores', 'NumberOfLogicalProcessors', 'MaxClockSpeed', 'SocketDesignation', 'Architecture' }
    'Win32_OperatingSystem'     = @{ Ns = $cimv2; Props = 'Caption', 'Version', 'BuildNumber', 'OSArchitecture', 'LastBootUpTime' }
    'Win32_ComputerSystem'      = @{ Ns = $cimv2; Props = 'Manufacturer', 'Model', 'SystemFamily', 'PCSystemType', 'TotalPhysicalMemory', 'NumberOfProcessors', 'NumberOfLogicalProcessors', 'HypervisorPresent' }
    'Win32_BaseBoard'           = @{ Ns = $cimv2; Props = 'Manufacturer', 'Product', 'Version' }
    'Win32_BIOS'                = @{ Ns = $cimv2; Props = 'Manufacturer', 'SMBIOSBIOSVersion', 'ReleaseDate', 'SMBIOSMajorVersion', 'SMBIOSMinorVersion' }
    'Win32_SystemEnclosure'     = @{ Ns = $cimv2; Props = 'Manufacturer', 'ChassisTypes' }
    'Win32_PhysicalMemory'      = @{ Ns = $cimv2; Props = 'Capacity', 'SMBIOSMemoryType', 'ConfiguredClockSpeed', 'Speed', 'Manufacturer', 'PartNumber', 'DeviceLocator', 'BankLabel', 'FormFactor' }
    'Win32_PhysicalMemoryArray' = @{ Ns = $cimv2; Props = 'MemoryDevices', 'MaxCapacityEx', 'Use' }
    'Win32_VideoController'     = @{ Ns = $cimv2; Props = 'Name', 'PNPDeviceID', 'AdapterCompatibility', 'DriverVersion', 'DriverDate', 'AdapterRAM', 'VideoProcessor', 'Status' }
    'Win32_LogicalDisk'         = @{ Ns = $cimv2; Props = 'DeviceID', 'DriveType', 'FileSystem', 'Size', 'FreeSpace' }
    'Win32_DiskDrive'           = @{ Ns = $cimv2; Props = 'Index', 'Model', 'Size', 'InterfaceType', 'MediaType' }
    'MSFT_PhysicalDisk'         = @{ Ns = $storage; Props = 'DeviceId', 'FriendlyName', 'MediaType', 'BusType', 'Size', 'HealthStatus', 'FirmwareVersion', 'SpindleSpeed' }
    'MSFT_Partition'            = @{ Ns = $storage; Props = 'DiskNumber', 'PartitionNumber', 'DriveLetter', 'Size', 'IsBoot', 'IsSystem' }
    'Win32_PnPEntity'           = @{ Ns = $cimv2; Props = 'DeviceID', 'Name', 'PNPClass', 'Manufacturer', 'HardwareID', 'CompatibleID', 'ConfigManagerErrorCode', 'Status', 'Present'; Pnp = $true }
    'Win32_PnPSignedDriver'     = @{ Ns = $cimv2; Props = 'DeviceID', 'DeviceName', 'DeviceClass', 'Manufacturer', 'DriverProviderName', 'DriverVersion', 'DriverDate', 'InfName', 'IsSigned', 'Signer'; Pnp = $true }
}

function ConvertTo-PlainValue($value) {
    if ($null -eq $value) { return $null }
    # DriveLetter приходит как [char]; символ 0 означает «буквы нет».
    if ($value -is [char]) {
        if ([int]$value -eq 0) { return $null }
        return [string]$value
    }
    if ($value -is [array]) {
        $items = @(foreach ($item in $value) { ConvertTo-PlainValue $item })
        return , $items
    }
    return $value
}

function Protect-InstanceId([string]$id) {
    if ([string]::IsNullOrEmpty($id)) { return $id }
    $parts = $id.Split('\')
    # Сгенерированный Windows идентификатор экземпляра содержит '&' (4&12033776&0&0008); без '&' его задаёт устройство.
    if ($parts.Count -ge 3 -and $parts[-1] -notmatch '&' -and $parts[0] -ne 'ROOT') {
        $parts[-1] = 'SANITIZED'
    }
    return $parts -join '\'
}

# Набор устройств PnP общий для обоих классов, чтобы фикстуры можно было объединять по DeviceID.
$pnpProblemIds = @(Get-WmiObject -Namespace $cimv2 -Class Win32_PnPEntity -Property DeviceID, ConfigManagerErrorCode |
    Where-Object { $_.ConfigManagerErrorCode -ne 0 } | ForEach-Object { $_.DeviceID })

function Test-KeepPnp([string]$deviceId) {
    if ([string]::IsNullOrEmpty($deviceId)) { return $false }
    return ($deviceId -match $pnpBuses) -or ($pnpProblemIds -contains $deviceId)
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

foreach ($className in $classes.Keys) {
    $spec = $classes[$className]
    $raw = @(Get-WmiObject -Namespace $spec.Ns -Class $className -Property $spec.Props)
    if ($spec.ContainsKey('Pnp')) {
        $raw = @($raw | Where-Object { Test-KeepPnp $_.DeviceID } | Sort-Object DeviceID)
    }

    $rows = @(foreach ($obj in $raw) {
            $row = [ordered]@{}
            foreach ($p in $spec.Props) {
                $value = ConvertTo-PlainValue $obj.$p
                if ($p -in 'DeviceID', 'PNPDeviceID' -and $value -is [string]) { $value = Protect-InstanceId $value }
                $row[$p] = $value
            }
            [pscustomobject]$row
        })

    $json = ConvertTo-Json -InputObject $rows -Depth 4
    $path = Join-Path $Out "$className.json"
    [System.IO.File]::WriteAllText($path, $json + "`n", $utf8NoBom)
    Write-Host ("{0,-28} {1,4} rows" -f $className, $rows.Count)
}
