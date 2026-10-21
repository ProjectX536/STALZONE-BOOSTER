#Requires -Version 5.1
<#
.SYNOPSIS
    Оборудование: процессор, видеокарты, память, системная плата, BIOS, ОС. Только чтение.

.DESCRIPTION
    Источники — WMI (Win32_Processor, Win32_VideoController, Win32_PhysicalMemory, Win32_BaseBoard, Win32_BIOS,
    Win32_OperatingSystem) и раздел реестра HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion.
    Серийные номера, имя компьютера и имя пользователя не выводятся.

.PARAMETER OutFile
    Файл для JSON (UTF-8). Без него JSON выводится в stdout, символы вне ASCII экранируются как \uXXXX.

.EXAMPLE
    .\00-Collect-Hardware.ps1 -OutFile hardware.json
#>
[CmdletBinding()]
param(
    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Booster.Diagnostics.psm1')

# SMBIOS Memory Device, поле Memory Type — те же коды, что в приложении (RamMapper).
$memoryTypes = @{ 24 = 'DDR3'; 26 = 'DDR4'; 30 = 'LPDDR4'; 34 = 'DDR5'; 35 = 'LPDDR5' }

$sections = [ordered]@{}

$sections.cpu = New-BoosterSection {
    , @(Invoke-BoosterCim -ClassName Win32_Processor `
            -Property Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, SocketDesignation, L2CacheSize, L3CacheSize |
            ForEach-Object {
                [ordered]@{
                    name              = ConvertTo-BoosterText $_.Name
                    manufacturer      = ConvertTo-BoosterText $_.Manufacturer
                    cores             = $_.NumberOfCores
                    logicalProcessors = $_.NumberOfLogicalProcessors
                    maxClockMhz       = $_.MaxClockSpeed
                    socket            = ConvertTo-BoosterText $_.SocketDesignation
                    l2CacheKb         = $_.L2CacheSize
                    l3CacheKb         = $_.L3CacheSize
                }
            })
}

# AdapterRAM не выводится: поле 32-битное и для карт больше 4 ГБ показывает неверный объём.
$sections.gpu = New-BoosterSection {
    , @(Invoke-BoosterCim -ClassName Win32_VideoController `
            -Property Name, AdapterCompatibility, VideoProcessor, DriverVersion, DriverDate, PNPDeviceID, `
            CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, Status |
            ForEach-Object {
                # Из PNPDeviceID берётся только идентификатор модели: хвост после VEN/DEV/SUBSYS/REV — экземпляр устройства.
                $pci = [regex]::Match([string]$_.PNPDeviceID, '^PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}(&SUBSYS_[0-9A-F]{8})?(&REV_[0-9A-F]{2})?', 'IgnoreCase')
                [ordered]@{
                    name                 = ConvertTo-BoosterText $_.Name
                    adapterCompatibility = ConvertTo-BoosterText $_.AdapterCompatibility
                    videoProcessor       = ConvertTo-BoosterText $_.VideoProcessor
                    driverVersion        = ConvertTo-BoosterText $_.DriverVersion
                    driverDate           = ConvertTo-BoosterDate $_.DriverDate -DateOnly
                    pciId                = if ($pci.Success) { $pci.Value.ToUpperInvariant() } else { $null }
                    horizontalResolution = $_.CurrentHorizontalResolution
                    verticalResolution   = $_.CurrentVerticalResolution
                    refreshRateHz        = $_.CurrentRefreshRate
                    status               = ConvertTo-BoosterText $_.Status
                }
            })
}

$sections.memory = New-BoosterSection {
    $system = Invoke-BoosterCim -ClassName Win32_ComputerSystem -Property TotalPhysicalMemory
    [ordered]@{
        totalPhysicalBytes = $system.TotalPhysicalMemory
        modules            = @(Invoke-BoosterCim -ClassName Win32_PhysicalMemory `
                -Property Capacity, SMBIOSMemoryType, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber, DeviceLocator, BankLabel |
                ForEach-Object {
                    $code = $_.SMBIOSMemoryType
                    [ordered]@{
                        capacityBytes      = $_.Capacity
                        memoryType         = if ($null -ne $code -and $memoryTypes.ContainsKey([int]$code)) { $memoryTypes[[int]$code] } else { $null }
                        smbiosMemoryType   = $code
                        speedMhz           = $_.Speed
                        configuredClockMhz = $_.ConfiguredClockSpeed
                        manufacturer       = ConvertTo-BoosterText $_.Manufacturer
                        partNumber         = ConvertTo-BoosterText $_.PartNumber
                        deviceLocator      = ConvertTo-BoosterText $_.DeviceLocator
                        bankLabel          = ConvertTo-BoosterText $_.BankLabel
                    }
                })
    }
}

$sections.board = New-BoosterSection {
    $board = @(Invoke-BoosterCim -ClassName Win32_BaseBoard -Property Manufacturer, Product, Version) | Select-Object -First 1
    if ($null -eq $board) {
        return New-BoosterStatus -Status Unknown -Note 'Win32_BaseBoard returned no instances'
    }
    $system = Invoke-BoosterCim -ClassName Win32_ComputerSystem -Property Manufacturer, Model
    [ordered]@{
        manufacturer       = ConvertTo-BoosterText $board.Manufacturer
        product            = ConvertTo-BoosterText $board.Product
        version            = ConvertTo-BoosterText $board.Version
        systemManufacturer = ConvertTo-BoosterText $system.Manufacturer
        systemModel        = ConvertTo-BoosterText $system.Model
    }
}

$sections.bios = New-BoosterSection {
    $bios = @(Invoke-BoosterCim -ClassName Win32_BIOS -Property Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion) |
        Select-Object -First 1
    if ($null -eq $bios) {
        return New-BoosterStatus -Status Unknown -Note 'Win32_BIOS returned no instances'
    }
    [ordered]@{
        manufacturer  = ConvertTo-BoosterText $bios.Manufacturer
        version       = ConvertTo-BoosterText $bios.SMBIOSBIOSVersion
        releaseDate   = ConvertTo-BoosterDate $bios.ReleaseDate -DateOnly
        smbiosVersion = if ($null -ne $bios.SMBIOSMajorVersion) { '{0}.{1}' -f $bios.SMBIOSMajorVersion, $bios.SMBIOSMinorVersion } else { $null }
    }
}

$sections.os = New-BoosterSection {
    $os = Invoke-BoosterCim -ClassName Win32_OperatingSystem -Property Caption, Version, BuildNumber, OSArchitecture, LastBootUpTime
    $currentVersion = 'HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    [ordered]@{
        caption        = ConvertTo-BoosterText $os.Caption
        displayVersion = [Microsoft.Win32.Registry]::GetValue($currentVersion, 'DisplayVersion', $null)
        version        = ConvertTo-BoosterText $os.Version
        build          = ConvertTo-BoosterText $os.BuildNumber
        ubr            = [Microsoft.Win32.Registry]::GetValue($currentVersion, 'UBR', $null)
        architecture   = ConvertTo-BoosterText $os.OSArchitecture
        lastBootUpTime = ConvertTo-BoosterDate $os.LastBootUpTime
    }
}

Write-BoosterDocument -Document (New-BoosterDocument -Script (Split-Path -Leaf $PSCommandPath) -Sections $sections) -OutFile $OutFile
