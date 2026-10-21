#Requires -Version 5.1
<#
.SYNOPSIS
    Настройки и состояние, влияющие на производительность: схема питания, память и commit, файл подкачки,
    загрузка процессора, Game Mode, HAGS. Только чтение.

.PARAMETER OutFile
    Файл для JSON (UTF-8). Без него JSON выводится в stdout, символы вне ASCII экранируются как \uXXXX.

.EXAMPLE
    .\02-Collect-Performance.ps1 -OutFile performance.json
#>
[CmdletBinding()]
param(
    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Booster.Diagnostics.psm1')

function Get-PowerSchemes([string]$Argument) {
    $lines = & powercfg.exe $Argument
    if ($LASTEXITCODE -ne 0) {
        throw "powercfg $Argument exited with code $LASTEXITCODE"
    }
    # «<подпись>: <GUID>  (<имя>)», у активной схемы в /list в конце звёздочка.
    foreach ($line in $lines) {
        $match = [regex]::Match($line, '(?<guid>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\s*\((?<name>.*)\)\s*(?<active>\*)?\s*$', 'IgnoreCase')
        if ($match.Success) {
            [ordered]@{
                guid     = $match.Groups['guid'].Value.ToLowerInvariant()
                name     = $match.Groups['name'].Value
                isActive = $match.Groups['active'].Success
            }
        }
    }
}

function Get-RegistryValue([string]$Key, [string]$Name) {
    [Microsoft.Win32.Registry]::GetValue($Key, $Name, $null)
}

$sections = [ordered]@{}

$sections.powerPlan = New-BoosterSection {
    $active = @(Get-PowerSchemes '/getactivescheme') | Select-Object -First 1
    if ($null -eq $active) {
        throw 'powercfg /getactivescheme printed no scheme GUID'
    }
    [ordered]@{
        activeGuid = $active.guid
        activeName = $active.name
        schemes    = @(Get-PowerSchemes '/list')
    }
}

$sections.memory = New-BoosterSection {
    $memory = @(Invoke-BoosterCim -ClassName Win32_PerfFormattedData_PerfOS_Memory `
            -Property AvailableMBytes, CommittedBytes, CommitLimit, PercentCommittedBytesInUse, PagesPerSec, CacheBytes) |
        Select-Object -First 1
    if ($null -eq $memory) {
        return New-BoosterStatus -Status Unknown -Note 'Win32_PerfFormattedData_PerfOS_Memory returned no instances'
    }
    [ordered]@{
        availableMBytes            = $memory.AvailableMBytes
        committedBytes             = $memory.CommittedBytes
        commitLimitBytes           = $memory.CommitLimit
        percentCommittedBytesInUse = $memory.PercentCommittedBytesInUse
        pagesPerSec                = $memory.PagesPerSec
        cacheBytes                 = $memory.CacheBytes
    }
}

$sections.pageFile = New-BoosterSection {
    $system = Invoke-BoosterCim -ClassName Win32_ComputerSystem -Property AutomaticManagedPagefile
    [ordered]@{
        automaticManaged = $system.AutomaticManagedPagefile
        usage            = @(Invoke-BoosterCim -ClassName Win32_PageFileUsage -Property Name, AllocatedBaseSize, CurrentUsage, PeakUsage |
                ForEach-Object {
                    [ordered]@{
                        path                = $_.Name
                        allocatedBaseSizeMb = $_.AllocatedBaseSize
                        currentUsageMb      = $_.CurrentUsage
                        peakUsageMb         = $_.PeakUsage
                    }
                })
        settings         = @(Invoke-BoosterCim -ClassName Win32_PageFileSetting -Property Name, InitialSize, MaximumSize |
                ForEach-Object {
                    [ordered]@{
                        path          = $_.Name
                        initialSizeMb = $_.InitialSize
                        maximumSizeMb = $_.MaximumSize
                    }
                })
    }
}

$sections.cpuLoad = New-BoosterSection {
    $counter = '\Processor(_Total)\% Processor Time'
    $samples = @(Get-Counter -Counter $counter -SampleInterval 1 -MaxSamples 3 -ErrorAction Stop |
            ForEach-Object { $_.CounterSamples[0].CookedValue })
    [ordered]@{
        counter           = $counter
        sampleIntervalSec = 1
        samplesPercent    = $samples
        averagePercent    = ($samples | Measure-Object -Average).Average
    }
}

$sections.gameMode = New-BoosterSection {
    $key = 'HKEY_CURRENT_USER\Software\Microsoft\GameBar'
    $auto = Get-RegistryValue $key 'AutoGameModeEnabled'
    [ordered]@{
        enabled             = if ($auto -eq 1) { $true } elseif ($auto -eq 0) { $false } else { $null }
        autoGameModeEnabled = $auto
        allowAutoGameMode   = Get-RegistryValue $key 'AllowAutoGameMode'
        source              = 'HKCU\Software\Microsoft\GameBar'
    }
}

$sections.hags = New-BoosterSection {
    $mode = Get-RegistryValue 'HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode'
    [ordered]@{
        # 2 — HAGS включён, 1 — выключен.
        enabled   = if ($mode -eq 2) { $true } elseif ($mode -eq 1) { $false } else { $null }
        hwSchMode = $mode
        source    = 'HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode'
    }
}

Write-BoosterDocument -Document (New-BoosterDocument -Script (Split-Path -Leaf $PSCommandPath) -Sections $sections) -OutFile $OutFile
