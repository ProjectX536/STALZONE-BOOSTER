#Requires -Version 5.1
<#
.SYNOPSIS
    Отчёт о драйверах со ссылками на официальные страницы загрузки. Только чтение.

.PARAMETER OutFile
    Файл для JSON (UTF-8). Без него JSON выводится в stdout, символы вне ASCII экранируются как \uXXXX.

.PARAMETER MarkdownFile
    Файл для отчёта в Markdown (drivers.md). Без него Markdown не пишется.

.PARAMETER SourcesPath
    Файл со ссылками. По умолчанию — config/official-sources.json репозитория.

.EXAMPLE
    .\07-Generate-Driver-Report.ps1 -OutFile driver-report.json -MarkdownFile drivers.md
#>
[CmdletBinding()]
param(
    [string]$OutFile,
    [string]$MarkdownFile,
    [string]$SourcesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Booster.Diagnostics.psm1')

# В 5.1 при запуске через -File $PSScriptRoot в значениях по умолчанию param() пуст.
if (-not $SourcesPath) {
    $SourcesPath = Join-Path $PSScriptRoot '..\config\official-sources.json'
}

$windowsUpdateTitle = 'Центр обновления Windows'

function ConvertTo-MarkdownCell([AllowNull()]$Value) {
    if ($null -eq $Value -or [string]$Value -eq '') { return '—' }
    $text = ([string]$Value) -replace '[\r\n]+', ' '
    [regex]::Replace($text, '[\\|*_`\[\]<>]', { param($m) '\' + $m.Value })
}

function ConvertTo-MarkdownLinks($Links) {
    $parts = @($Links | ForEach-Object { '[{0}]({1})' -f (ConvertTo-MarkdownCell $_.title), $_.url })
    if ($parts.Count -eq 0) { '—' } else { $parts -join ' · ' }
}

$sections = [ordered]@{}

$sections.board = New-BoosterSection {
    $board = @(Invoke-BoosterCim -ClassName Win32_BaseBoard -Property Manufacturer, Product) | Select-Object -First 1
    if ($null -eq $board) {
        return New-BoosterStatus -Status Unknown -Note 'Win32_BaseBoard returned no instances'
    }
    [ordered]@{
        manufacturer = ConvertTo-BoosterText $board.Manufacturer
        product      = ConvertTo-BoosterText $board.Product
    }
}
$boardManufacturer = if ($sections.board.status -eq 'Available') { $sections.board.value.manufacturer } else { $null }

$sections.drivers = New-BoosterSection {
    $sources = Read-BoosterConfig -Path $SourcesPath
    $timeoutSec = [int][Math]::Ceiling((Get-BoosterSetting -File settings.json -Name detectors.timeoutsMs.drivers) / 1000)
    , @(Get-BoosterDeviceInventory -TimeoutSec $timeoutSec | ForEach-Object {
            $_.links = @(Get-BoosterOfficialLinks -Device $_ -BoardManufacturer $boardManufacturer -Sources $sources `
                    -WindowsUpdateTitle $windowsUpdateTitle)
            $_
        })
}

$document = New-BoosterDocument -Script (Split-Path -Leaf $PSCommandPath) -Sections $sections

if ($MarkdownFile) {
    $md = New-Object Text.StringBuilder
    $collected = [DateTimeOffset]::Parse($document.collectedAt, [Globalization.CultureInfo]::InvariantCulture)
    [void]$md.AppendLine('# Драйверы').AppendLine()
    [void]$md.AppendLine(('Собрано: {0:yyyy-MM-dd HH:mm} (UTC{0:zzz}).' -f $collected))
    if ($sections.board.status -eq 'Available') {
        [void]$md.AppendLine(('Системная плата: {0} {1}.' -f (ConvertTo-MarkdownCell $sections.board.value.manufacturer),
                (ConvertTo-MarkdownCell $sections.board.value.product)))
    }
    [void]$md.AppendLine('Ссылки ведут только на официальные сайты из config/official-sources.json. ' +
        'Скрипт ничего не скачивает и не устанавливает.').AppendLine()

    if ($sections.drivers.status -ne 'Available') {
        [void]$md.AppendLine(('Список устройств не получен: {0} — {1}' -f $sections.drivers.status, (ConvertTo-MarkdownCell $sections.drivers.note)))
    }
    else {
        $devices = @($sections.drivers.value)
        $attention = @($devices | Where-Object { ($null -ne $_.state -and $_.state -ne 'Ok') -or ($null -ne $_.driver -and $_.driver.isSigned -eq $false) })
        [void]$md.AppendLine(('Устройств: {0}, с замечаниями: {1}.' -f $devices.Count, $attention.Count)).AppendLine()

        [void]$md.AppendLine('## С замечаниями').AppendLine()
        if ($attention.Count -eq 0) {
            [void]$md.AppendLine('Нет.').AppendLine()
        }
        else {
            [void]$md.AppendLine('| Устройство | Класс | Состояние | Код ошибки | Драйвер подписан |')
            [void]$md.AppendLine('|---|---|---|---|---|')
            foreach ($device in $attention) {
                $signed = if ($null -eq $device.driver -or $null -eq $device.driver.isSigned) { $null } elseif ($device.driver.isSigned) { 'да' } else { 'нет' }
                [void]$md.AppendLine(('| {0} | {1} | {2} | {3} | {4} |' -f (ConvertTo-MarkdownCell $device.name), (ConvertTo-MarkdownCell $device.deviceClass),
                        (ConvertTo-MarkdownCell $device.state), (ConvertTo-MarkdownCell $device.errorCode), (ConvertTo-MarkdownCell $signed)))
            }
            [void]$md.AppendLine()
        }

        [void]$md.AppendLine('## Все устройства').AppendLine()
        [void]$md.AppendLine('| Устройство | Класс | Поставщик драйвера | Версия | Дата | Где обновить |')
        [void]$md.AppendLine('|---|---|---|---|---|---|')
        foreach ($device in $devices) {
            $driver = $device.driver
            $provider = if ($driver) { $driver.provider } else { $null }
            $version = if ($driver) { $driver.version } else { $null }
            $date = if ($driver) { $driver.date } else { $null }
            [void]$md.AppendLine(('| {0} | {1} | {2} | {3} | {4} | {5} |' -f (ConvertTo-MarkdownCell $device.name), (ConvertTo-MarkdownCell $device.deviceClass),
                    (ConvertTo-MarkdownCell $provider), (ConvertTo-MarkdownCell $version), (ConvertTo-MarkdownCell $date), (ConvertTo-MarkdownLinks $device.links)))
        }
    }
    [IO.File]::WriteAllText((Resolve-BoosterPath $MarkdownFile), $md.ToString(), (New-Object Text.UTF8Encoding $false))
}

Write-BoosterDocument -Document $document -OutFile $OutFile
