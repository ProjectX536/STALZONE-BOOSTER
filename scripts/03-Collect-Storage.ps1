#Requires -Version 5.1
<#
.SYNOPSIS
    Физические диски, тома и счётчики надёжности дисков. Только чтение.

.DESCRIPTION
    Get-PhysicalDisk и Get-Volume. Get-StorageReliabilityCounter требует прав администратора: без них секция
    reliability получает статус RequiresAdmin. Серийные номера дисков, метки и пути томов не выводятся.

.PARAMETER OutFile
    Файл для JSON (UTF-8). Без него JSON выводится в stdout, символы вне ASCII экранируются как \uXXXX.

.EXAMPLE
    .\03-Collect-Storage.ps1 -OutFile storage.json
#>
[CmdletBinding()]
param(
    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Booster.Diagnostics.psm1')

$sections = [ordered]@{}

$sections.disks = New-BoosterSection {
    , @(Get-PhysicalDisk -ErrorAction Stop | ForEach-Object {
            [ordered]@{
                deviceId          = [string]$_.DeviceId
                friendlyName      = ConvertTo-BoosterText $_.FriendlyName
                mediaType         = [string]$_.MediaType
                busType           = [string]$_.BusType
                sizeBytes         = $_.Size
                healthStatus      = [string]$_.HealthStatus
                operationalStatus = @($_.OperationalStatus | ForEach-Object { [string]$_ })
            }
        })
}

$sections.volumes = New-BoosterSection {
    , @(Get-Volume -ErrorAction Stop | ForEach-Object {
            $letter = $_.DriveLetter
            [ordered]@{
                # У тома без буквы DriveLetter — $null или символ с кодом 0.
                driveLetter        = if ($null -ne $letter -and [int][char]$letter -ne 0) { [string]$letter } else { $null }
                fileSystem         = ConvertTo-BoosterText $_.FileSystemType
                driveType          = [string]$_.DriveType
                healthStatus       = [string]$_.HealthStatus
                sizeBytes          = $_.Size
                sizeRemainingBytes = $_.SizeRemaining
            }
        })
}

$sections.reliability = New-BoosterSection {
    if (-not (Test-BoosterElevated)) {
        return New-BoosterStatus -Status RequiresAdmin -Note 'Get-StorageReliabilityCounter requires administrator rights'
    }
    , @(Get-PhysicalDisk -ErrorAction Stop | ForEach-Object {
            $disk = $_
            try {
                $counter = $disk | Get-StorageReliabilityCounter -ErrorAction Stop
                [ordered]@{
                    deviceId         = [string]$disk.DeviceId
                    temperatureC     = $counter.Temperature
                    temperatureMaxC  = $counter.TemperatureMax
                    wearPercent      = $counter.Wear
                    readErrorsTotal  = $counter.ReadErrorsTotal
                    writeErrorsTotal = $counter.WriteErrorsTotal
                    powerOnHours     = $counter.PowerOnHours
                    note             = $null
                }
            }
            catch {
                # Часть контроллеров (USB-мосты, RAID) счётчики не отдаёт — это не мешает остальным дискам.
                [ordered]@{ deviceId = [string]$disk.DeviceId; note = $_.Exception.Message }
            }
        })
}

Write-BoosterDocument -Document (New-BoosterDocument -Script (Split-Path -Leaf $PSCommandPath) -Sections $sections) -OutFile $OutFile
