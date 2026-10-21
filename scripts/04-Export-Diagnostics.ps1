#Requires -Version 5.1
<#
.SYNOPSIS
    Полная диагностика в одну папку: запускает 00–03 и 07, складывает JSON и пишет summary.md. Только чтение.

.PARAMETER OutDir
    Папка для результатов; создаётся, если её нет.

.EXAMPLE
    .\04-Export-Diagnostics.ps1 -OutDir "$env:USERPROFILE\Desktop\booster-diagnostics"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Booster.Diagnostics.psm1')

$directory = [IO.Directory]::CreateDirectory((Resolve-BoosterPath $OutDir)).FullName

$runs = @(
    [ordered]@{ script = '00-Collect-Hardware.ps1'; file = 'hardware.json' }
    [ordered]@{ script = '01-Collect-Drivers.ps1'; file = 'drivers.json' }
    [ordered]@{ script = '02-Collect-Performance.ps1'; file = 'performance.json' }
    [ordered]@{ script = '03-Collect-Storage.ps1'; file = 'storage.json' }
    [ordered]@{ script = '07-Generate-Driver-Report.ps1'; file = 'driver-report.json'; markdown = 'drivers.md' }
)
$documents = @{}
$failures = New-Object Collections.Generic.List[string]
foreach ($run in $runs) {
    $arguments = @{ OutFile = Join-Path $directory $run.file }
    if ($run.Contains('markdown')) {
        $arguments.MarkdownFile = Join-Path $directory $run.markdown
    }
    try {
        & (Join-Path $PSScriptRoot $run.script) @arguments
        $documents[$run.file] = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($arguments.OutFile))
    }
    catch {
        $failures.Add(('{0}: {1}' -f $run.script, $_.Exception.Message))
    }
}

# StrictMode не даёт обращаться к несуществующим свойствам.
function Get-JsonValue($Node, [string]$Path) {
    foreach ($part in $Path.Split('.')) {
        if ($null -eq $Node) { return $null }
        $property = $Node.PSObject.Properties[$part]
        if ($null -eq $property) { return $null }
        $Node = $property.Value
    }
    , $Node
}

function Get-Section([string]$File, [string]$Name) {
    $section = Get-JsonValue $documents[$File] "sections.$Name"
    if ((Get-JsonValue $section 'status') -eq 'Available') { , (Get-JsonValue $section 'value') } else { $null }
}

function Format-Size($Bytes) {
    if ($null -eq $Bytes) { return '?' }
    '{0:N1} ГБ' -f ([double]$Bytes / 1GB)
}

function Format-Flag($Value, [string]$On, [string]$Off, [string]$Unknown = 'не задано в реестре (действует умолчание Windows)') {
    if ($Value -eq $true) { $On } elseif ($Value -eq $false) { $Off } else { $Unknown }
}

# PowerShell 7 превращает строки-даты из JSON в DateTime, 5.1 оставляет строками.
function Format-Date($Value, [string]$Format = 'yyyy-MM-dd') {
    if ($Value -is [datetime]) { $Value.ToString($Format, [Globalization.CultureInfo]::InvariantCulture) } else { $Value }
}

$noData = 'нет данных — см. таблицу секций ниже'
$lines = New-Object Collections.Generic.List[string]
function Add-Fact([string]$Label, $Text) {
    $lines.Add(('- {0}: {1}' -f $Label, $(if ($Text) { $Text } else { $noData })))
}

$cpus = Get-Section 'hardware.json' 'cpu'
if ($null -eq $cpus) { Add-Fact 'Процессор' $null }
foreach ($cpu in @($cpus)) {
    Add-Fact 'Процессор' ('{0} — ядер: {1}, потоков: {2}' -f $cpu.name, $cpu.cores, $cpu.logicalProcessors)
}
$gpus = Get-Section 'hardware.json' 'gpu'
if ($null -eq $gpus) { Add-Fact 'Видеокарта' $null }
foreach ($gpu in @($gpus)) {
    Add-Fact 'Видеокарта' ('{0}, драйвер {1} от {2}' -f $gpu.name, $gpu.driverVersion, (Format-Date $gpu.driverDate))
}

$memory = Get-Section 'hardware.json' 'memory'
Add-Fact 'Память' $(if ($memory) {
        $modules = @($memory.modules | Group-Object { '{0} {1} {2} {3}' -f $_.capacityBytes, $_.memoryType, $_.speedMhz, $_.configuredClockMhz } | ForEach-Object {
                $module = $_.Group[0]
                $type = if ($module.memoryType) { $module.memoryType } else { 'тип не определён' }
                '{0} × {1} {2} (номинал {3} МГц, работает на {4} МГц)' -f $_.Count, (Format-Size $module.capacityBytes), $type, $module.speedMhz, $module.configuredClockMhz
            })
        '{0} доступно системе; модули: {1}' -f (Format-Size $memory.totalPhysicalBytes), ($modules -join '; ')
    })

$board = Get-Section 'hardware.json' 'board'
Add-Fact 'Системная плата' $(if ($board) { '{0} {1}' -f $board.manufacturer, $board.product })
$bios = Get-Section 'hardware.json' 'bios'
Add-Fact 'BIOS' $(if ($bios) { '{0} от {1}' -f $bios.version, (Format-Date $bios.releaseDate) })
$os = Get-Section 'hardware.json' 'os'
Add-Fact 'ОС' $(if ($os) { '{0} {1}, сборка {2}.{3}' -f $os.caption, $os.displayVersion, $os.build, $os.ubr })

$power = Get-Section 'performance.json' 'powerPlan'
Add-Fact 'Схема питания' $(if ($power) { '{0} ({1})' -f $power.activeName, $power.activeGuid })
$pageFile = Get-Section 'performance.json' 'pageFile'
Add-Fact 'Файл подкачки' $(if ($pageFile) { Format-Flag $pageFile.automaticManaged 'размер выбирает Windows' 'размер задан вручную' 'неизвестно, кто выбирает размер' })
$cpuLoad = Get-Section 'performance.json' 'cpuLoad'
Add-Fact 'Загрузка процессора' $(if ($cpuLoad) { '{0:N1} % — среднее трёх замеров по 1 с' -f $cpuLoad.averagePercent })
$gameMode = Get-Section 'performance.json' 'gameMode'
Add-Fact 'Режим игры (Game Mode)' $(if ($gameMode) { Format-Flag $gameMode.enabled 'включён' 'выключен' })
$hags = Get-Section 'performance.json' 'hags'
Add-Fact 'Планирование GPU с аппаратным ускорением (HAGS)' $(if ($hags) { Format-Flag $hags.enabled 'включено' 'выключено' })

$disks = Get-Section 'storage.json' 'disks'
if ($null -eq $disks) { Add-Fact 'Диски' $null }
foreach ($disk in @($disks)) {
    Add-Fact ('Диск {0}' -f $disk.deviceId) ('{0} — {1}, {2}, {3}, состояние {4}' -f $disk.friendlyName, $disk.mediaType, $disk.busType,
        (Format-Size $disk.sizeBytes), $disk.healthStatus)
}
$volumes = Get-Section 'storage.json' 'volumes'
if ($null -eq $volumes) { Add-Fact 'Тома' $null }
foreach ($volume in @($volumes)) {
    if (-not $volume.driveLetter -or -not $volume.sizeBytes) { continue }
    Add-Fact ('Том {0}' -f $volume.driveLetter) ('свободно {0} из {1} ({2:N0} %)' -f (Format-Size $volume.sizeRemainingBytes), (Format-Size $volume.sizeBytes),
        (100.0 * $volume.sizeRemainingBytes / $volume.sizeBytes))
}

$deviceErrors = Get-Section 'drivers.json' 'deviceErrors'
Add-Fact 'Устройства с ошибками или отключённые' $(if ($null -ne $deviceErrors) { '{0} (список — в drivers.md)' -f @($deviceErrors).Count })
$reportDrivers = Get-Section 'driver-report.json' 'drivers'
Add-Fact 'Устройств в отчёте о драйверах' $(if ($null -ne $reportDrivers) { '{0} (drivers.md)' -f @($reportDrivers).Count })

$md = New-Object Text.StringBuilder
$hardwareDoc = $documents['hardware.json']
$collected = if ($null -eq $hardwareDoc) { [DateTimeOffset]::Now }
elseif ($hardwareDoc.collectedAt -is [datetime]) { [DateTimeOffset]$hardwareDoc.collectedAt }
else { [DateTimeOffset]::Parse($hardwareDoc.collectedAt, [Globalization.CultureInfo]::InvariantCulture) }
[void]$md.AppendLine('# Диагностика системы').AppendLine()
[void]$md.AppendLine(('Собрано: {0:yyyy-MM-dd HH:mm} (UTC{0:zzz}). Права администратора: {1}.' -f $collected, $(if (Test-BoosterElevated) { 'да' } else { 'нет' })))
[void]$md.AppendLine('Скрипты только читали систему, ничего не изменялось. Подробности — в JSON-файлах рядом, драйверы и ссылки на официальные сайты — в drivers.md.').AppendLine()
[void]$md.AppendLine('## Кратко').AppendLine()
foreach ($line in $lines) { [void]$md.AppendLine($line) }
[void]$md.AppendLine()

[void]$md.AppendLine('## Секции').AppendLine()
[void]$md.AppendLine('| Файл | Секция | Статус | Примечание |')
[void]$md.AppendLine('|---|---|---|---|')
foreach ($run in $runs) {
    $sections = Get-JsonValue $documents[$run.file] 'sections'
    if ($null -eq $sections) {
        [void]$md.AppendLine(('| {0} | — | не создан | см. «Сбои» |' -f $run.file))
        continue
    }
    foreach ($section in $sections.PSObject.Properties) {
        $note = [string](Get-JsonValue $section.Value 'note') -replace '[\r\n|]+', ' '
        [void]$md.AppendLine(('| {0} | {1} | {2} | {3} |' -f $run.file, $section.Name, (Get-JsonValue $section.Value 'status'), $note))
    }
}

if ($failures.Count -gt 0) {
    [void]$md.AppendLine().AppendLine('## Сбои').AppendLine()
    foreach ($failure in $failures) { [void]$md.AppendLine("- $failure") }
}

$summary = Join-Path $directory 'summary.md'
[IO.File]::WriteAllText($summary, $md.ToString(), (New-Object Text.UTF8Encoding $false))

$summary
if ($failures.Count -gt 0) {
    exit 1
}
