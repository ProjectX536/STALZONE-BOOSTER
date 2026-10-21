#Requires -Version 5.1
<#
.SYNOPSIS
    Драйверы устройств и устройства с ошибками. Только чтение.

.PARAMETER OutFile
    Файл для JSON (UTF-8). Без него JSON выводится в stdout, символы вне ASCII экранируются как \uXXXX.

.EXAMPLE
    .\01-Collect-Drivers.ps1 -OutFile drivers.json
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

$sections.drivers = New-BoosterSection {
    $timeoutSec = [int][Math]::Ceiling((Get-BoosterSetting -File settings.json -Name detectors.timeoutsMs.drivers) / 1000)
    , @(Get-BoosterDeviceInventory -TimeoutSec $timeoutSec)
}

$sections.deviceErrors = if ($sections.drivers.status -ne 'Available') {
    $sections.drivers
}
else {
    New-BoosterSection {
        , @($sections.drivers.value | Where-Object { $null -ne $_.errorCode -and $_.errorCode -ne 0 } | ForEach-Object {
                [ordered]@{
                    name        = $_.name
                    deviceClass = $_.deviceClass
                    hardwareId  = @($_.hardwareIds) | Select-Object -First 1
                    state       = $_.state
                    errorCode   = $_.errorCode
                }
            })
    }
}

Write-BoosterDocument -Document (New-BoosterDocument -Script (Split-Path -Leaf $PSCommandPath) -Sections $sections) -OutFile $OutFile
