#Requires -Version 5.1
<#
.SYNOPSIS
    Базовый замер FPS через PresentMon или пересчёт метрик по готовому CSV PresentMon.

.PARAMETER PresentMonPath
    Путь к PresentMon. По умолчанию — самый новый PresentMon-*.exe в %LOCALAPPDATA%\Booster\tools\PresentMon
    (туда его кладёт Install-PresentMon.ps1).

.PARAMETER ProcessName
    Имя exe игры, например game.exe; без расширения .exe оно добавляется.

.PARAMETER ProcessId
    PID процесса игры.

.PARAMETER DurationSec
    Длительность замера. По умолчанию — presentMon.defaultCaptureSeconds из config/settings.json.

.PARAMETER CsvPath
    Куда PresentMon пишет CSV. По умолчанию — новый файл во временной папке; после замера файл остаётся.

.PARAMETER InputCsv
    Готовый CSV PresentMon: метрики пересчитываются без замера.

.PARAMETER MinimumFrames
    Минимум годных кадров для метрик. По умолчанию — minimumFramesForMetrics из config/thresholds.json.

.PARAMETER OutFile
    Файл для JSON (UTF-8). Без него JSON выводится в stdout, символы вне ASCII экранируются как \uXXXX.

.EXAMPLE
    .\05-Run-Baseline.ps1 -ProcessName game.exe -DurationSec 60

.EXAMPLE
    .\05-Run-Baseline.ps1 -InputCsv capture.csv -ProcessName game.exe
#>
[CmdletBinding(DefaultParameterSetName = 'CaptureByName')]
param(
    [Parameter(ParameterSetName = 'CaptureByName')]
    [Parameter(ParameterSetName = 'CaptureById')]
    [string]$PresentMonPath,

    [Parameter(Mandatory, ParameterSetName = 'CaptureByName')]
    [Parameter(Mandatory, ParameterSetName = 'CsvByName')]
    [string]$ProcessName,

    [Parameter(Mandatory, ParameterSetName = 'CaptureById')]
    [Parameter(Mandatory, ParameterSetName = 'CsvById')]
    [int]$ProcessId,

    [Parameter(ParameterSetName = 'CaptureByName')]
    [Parameter(ParameterSetName = 'CaptureById')]
    [ValidateRange(1, 86400)]
    [int]$DurationSec,

    [Parameter(ParameterSetName = 'CaptureByName')]
    [Parameter(ParameterSetName = 'CaptureById')]
    [string]$CsvPath,

    [Parameter(Mandatory, ParameterSetName = 'CsvByName')]
    [Parameter(Mandatory, ParameterSetName = 'CsvById')]
    [string]$InputCsv,

    [ValidateRange(1, 2147483647)]
    [int]$MinimumFrames,

    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Booster.Diagnostics.psm1')

$byName = $PSCmdlet.ParameterSetName -like '*ByName'
$fromCsv = $PSCmdlet.ParameterSetName -like 'Csv*'
if ($byName -and -not $ProcessName.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
    $ProcessName += '.exe'
}
if (-not $PSBoundParameters.ContainsKey('MinimumFrames')) {
    $MinimumFrames = Get-BoosterSetting -File thresholds.json -Name minimumFramesForMetrics
}
# Внутри блока секции $PSBoundParameters пуст.
$durationGiven = $PSBoundParameters.ContainsKey('DurationSec')

function Find-PresentMon {
    $directory = Join-Path $env:LOCALAPPDATA 'Booster\tools\PresentMon'
    if (Test-Path -LiteralPath $directory -PathType Container) {
        Get-ChildItem -LiteralPath $directory -Filter 'PresentMon-*.exe' -File |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

function Test-CanTraceWithoutElevation {
    # Performance Log Users (S-1-5-32-559) может писать ETW без повышения.
    if (Test-BoosterElevated) { return $true }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        @($identity.Groups | Where-Object { $_.Value -eq 'S-1-5-32-559' }).Count -gt 0
    }
    finally {
        $identity.Dispose()
    }
}

function Start-PresentMon([string]$Path, [string[]]$Arguments, [bool]$Elevate) {
    # Start-Process склеивает аргументы через пробел без кавычек.
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ } })
    $start = @{ FilePath = $Path; ArgumentList = $quoted; PassThru = $true; WindowStyle = 'Hidden' }
    if ($Elevate) { $start.Verb = 'RunAs' }
    $process = Start-Process @start
    # Без обращения к Handle в 5.1 ExitCode после выхода бывает пустым.
    $null = $process.Handle
    $process
}

$sections = [ordered]@{}
$parse = $null

$sections.capture = New-BoosterSection {
    $info = [ordered]@{
        source         = if ($fromCsv) { 'Csv' } else { 'PresentMon' }
        target         = if ($byName) { $ProcessName } else { $ProcessId }
        csvPath        = $null
        presentMonPath = $null
        durationSec    = $null
    }

    if ($fromCsv) {
        $csv = Resolve-BoosterPath $InputCsv
        if (-not (Test-Path -LiteralPath $csv -PathType Leaf)) {
            throw "CSV not found: $(ConvertTo-BoosterSafePath $csv)"
        }
    }
    else {
        $presentMon = if ($PresentMonPath) { Resolve-BoosterPath $PresentMonPath } else { Find-PresentMon }
        if (-not $presentMon -or -not (Test-Path -LiteralPath $presentMon -PathType Leaf)) {
            throw 'PresentMon not found. Install it with Install-PresentMon.ps1 or pass -PresentMonPath.'
        }
        $duration = if ($durationGiven) { $DurationSec } else { [int](Get-BoosterSetting -File settings.json -Name presentMon.defaultCaptureSeconds) }
        $session = [string](Get-BoosterSetting -File settings.json -Name presentMon.sessionName)
        $csv = if ($CsvPath) { Resolve-BoosterPath $CsvPath } else {
            Join-Path ([IO.Path]::GetTempPath()) ('booster-baseline-{0:yyyyMMdd-HHmmss}.csv' -f (Get-Date))
        }
        $info.presentMonPath = ConvertTo-BoosterSafePath $presentMon
        $info.durationSec = $duration

        $target = if ($byName) { '--process_name', $ProcessName } else { '--process_id', [string]$ProcessId }
        $arguments = @($target) + @('--output_file', $csv, '--timed', [string]$duration, '--terminate_after_timed',
            '--no_console_stats', '--session_name', $session, '--stop_existing_session')
        $elevate = -not (Test-CanTraceWithoutElevation)
        $process = Start-PresentMon $presentMon $arguments $elevate
        if (-not $process.WaitForExit(($duration + 30) * 1000)) {
            $stop = Start-PresentMon $presentMon @('--terminate_existing_session', '--session_name', $session) $elevate
            [void]$stop.WaitForExit(30000)
            throw "PresentMon did not finish within $($duration + 30) s; the ETW session was stopped"
        }
        if ($process.ExitCode -ne 0) {
            throw "PresentMon exited with code $($process.ExitCode)"
        }
    }
    $info.csvPath = ConvertTo-BoosterSafePath $csv

    $script:parse = if (Test-Path -LiteralPath $csv -PathType Leaf) {
        if ($byName) { Read-PresentMonCsv -Path $csv -ProcessName $ProcessName } else { Read-PresentMonCsv -Path $csv -ProcessId $ProcessId }
    }
    else {
        [ordered]@{ status = 'TargetNotFound'; detail = 'PresentMon finished without writing a CSV'; processName = $null; processId = $null
            intervalColumn = $null; intervalsMs = [double[]]@(); discardedRows = 0; frameTypes = [string[]]@()
        }
    }
    $info.captureStatus = $script:parse.status
    $info.detail = $script:parse.detail
    $info.processName = $script:parse.processName
    $info.processId = $script:parse.processId
    $info.intervalColumn = $script:parse.intervalColumn
    $info.frames = $script:parse.intervalsMs.Length
    $info.discardedRows = $script:parse.discardedRows
    $info.frameTypes = $script:parse.frameTypes
    $info
}

$sections.metrics = New-BoosterSection {
    if ($null -eq $parse) {
        return New-BoosterStatus -Status Unknown -Note 'No capture: see the capture section'
    }
    if ($parse.status -ne 'Completed') {
        return New-BoosterStatus -Status Unknown -Note "No frame intervals: capture status $($parse.status) ($($parse.detail))"
    }
    Get-BoosterFrameMetrics -IntervalsMs $parse.intervalsMs -MinimumFrames $MinimumFrames
}

Write-BoosterDocument -Document (New-BoosterDocument -Script (Split-Path -Leaf $PSCommandPath) -Sections $sections) -OutFile $OutFile
