#Requires -Version 5.1
<#
.SYNOPSIS
    Сверка журнала игровой сессии с текущим состоянием системы. Только чтение.

.DESCRIPTION
    По каждому изменению сессии: значения из журнала (исходное и применённое), текущее значение и совпадает ли
    оно с исходным. Скрипт ничего не восстанавливает — это делает приложение.
    - power-plan: активная схема из вывода powercfg /getactivescheme.
    - game-priority: приоритет процесса игры. Процесса нет или время его старта отличается больше чем на секунду
      (PID достался другому процессу) — приоритет не пережил игру, это совпадение с исходным.
    - user-selected-background-apps: необратимое действие, сверять нечего.
    Действия, которых скрипт не знает, не сверяются.

.PARAMETER JournalDir
    Каталог журнала. По умолчанию — %LOCALAPPDATA%\Booster\journal.

.PARAMETER SessionId
    Сессия — имя файла журнала без .json. По умолчанию — самая новая по имени файла.

.PARAMETER OutFile
    Файл для JSON (UTF-8). Без него JSON выводится в stdout, символы вне ASCII экранируются как \uXXXX.

.EXAMPLE
    .\06-Verify-Restore.ps1

.EXAMPLE
    .\06-Verify-Restore.ps1 -SessionId 20261019-120000-0123abcd -OutFile verify.json
#>
[CmdletBinding()]
param(
    [string]$JournalDir = (Join-Path $env:LOCALAPPDATA 'Booster\journal'),
    [string]$SessionId,
    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Booster.Diagnostics.psm1')

# Как SessionRecord.CurrentSchemaVersion и SessionIds в приложении: файл сессии — <yyyyMMdd-HHmmss-8 hex>.json.
$supportedSchemaVersion = 1
$sessionFilePattern = '^\d{8}-\d{6}-[0-9a-f]{8}\.json$'
# Время старта в журнале и у Get-Process берётся из одного источника; секунда — запас на округление при записи.
$startTimeToleranceSec = 1
$invariant = [Globalization.CultureInfo]::InvariantCulture
$journal = $null
$activeScheme = $null

<#
.SYNOPSIS
    Дата из JSON — строкой ISO 8601: Windows PowerShell 5.1 оставляет её строкой, PowerShell 7 превращает в DateTime.
#>
function ConvertTo-IsoText($Value) {
    if ($Value -is [datetime]) { return ([DateTimeOffset]$Value).ToString('o', $invariant) }
    $Value
}

function ConvertTo-Offset($Value) {
    if ($Value -is [datetime]) { return [DateTimeOffset]$Value }
    [DateTimeOffset]::Parse([string]$Value, $invariant)
}

function Get-ActiveSchemeGuid {
    if (-not $script:activeScheme) {
        $lines = & powercfg.exe /getactivescheme
        if ($LASTEXITCODE -ne 0) {
            throw "powercfg /getactivescheme exited with code $LASTEXITCODE"
        }
        # GUID — единственная часть вывода, которая не зависит от языка Windows.
        $match = [regex]::Match(($lines -join "`n"), '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', 'IgnoreCase')
        if (-not $match.Success) {
            throw 'powercfg /getactivescheme printed no scheme GUID'
        }
        $script:activeScheme = $match.Value.ToLowerInvariant()
    }
    $script:activeScheme
}

function Test-PowerPlan($Original) {
    $current = Get-ActiveSchemeGuid
    $same = $current -eq [string]$Original.scheme
    [ordered]@{
        current         = $current
        matchesOriginal = $same
        note            = if ($same) { $null } else { 'активна другая схема питания' }
    }
}

function Test-GamePriority($Original) {
    $process = Get-Process -Id ([int]$Original.processId) -ErrorAction SilentlyContinue
    $startedAt = $null
    if ($process) {
        try { $startedAt = [DateTimeOffset]$process.StartTime } catch { $startedAt = $null }
        if ($null -eq $startedAt) {
            return [ordered]@{ current = $null; matchesOriginal = $null; note = 'нет доступа к процессу: время старта не читается' }
        }
    }
    if (-not $process -or [Math]::Abs(($startedAt - (ConvertTo-Offset $Original.startTime)).TotalSeconds) -gt $startTimeToleranceSec) {
        return [ordered]@{ current = $null; matchesOriginal = $true; note = 'процесс завершён — приоритет не сохраняется' }
    }

    $current = $null
    try { $current = [string]$process.PriorityClass } catch { $current = $null }
    if (-not $current) {
        return [ordered]@{ current = $null; matchesOriginal = $null; note = 'нет доступа к процессу: приоритет не читается' }
    }
    $same = $current -eq [string]$Original.priority
    [ordered]@{
        current         = $current
        matchesOriginal = $same
        note            = if ($same) { $null } else { 'приоритет отличается от исходного' }
    }
}

function Test-Change($Change) {
    $original = $Change.snapshot.originalValue
    try {
        $check = switch ([string]$Change.actionId) {
            'power-plan' { Test-PowerPlan $original }
            'game-priority' { Test-GamePriority $original }
            'user-selected-background-apps' {
                [ordered]@{ current = $null; matchesOriginal = $null; note = 'необратимое действие: закрытые приложения не перезапускаются' }
            }
            default { [ordered]@{ current = $null; matchesOriginal = $null; note = 'неизвестное действие' } }
        }
    }
    catch {
        $check = [ordered]@{ current = $null; matchesOriginal = $null; note = "не удалось проверить: $($_.Exception.Message)" }
    }
    [ordered]@{
        actionId        = $Change.actionId
        journalStatus   = $Change.status
        original        = $original
        applied         = $Change.snapshot.appliedValue
        current         = $check.current
        matchesOriginal = $check.matchesOriginal
        note            = $check.note
    }
}

$sections = [ordered]@{}

$sections.session = New-BoosterSection {
    if (-not (Test-Path -LiteralPath $JournalDir -PathType Container)) {
        return New-BoosterStatus -Status Unknown -Note "каталог журнала не найден: $(ConvertTo-BoosterSafePath $JournalDir)"
    }
    $files = @(Get-ChildItem -LiteralPath $JournalDir -File | Where-Object { $_.Name -match $sessionFilePattern } | Sort-Object Name -Descending)
    $file = if ($SessionId) {
        $files | Where-Object { $_.Name -eq "$SessionId.json" } | Select-Object -First 1
    }
    else {
        $files | Select-Object -First 1
    }
    if (-not $file) {
        $note = if ($SessionId) { "сессии $SessionId нет в журнале" } else { 'в журнале нет сессий' }
        return New-BoosterStatus -Status Unknown -Note $note
    }

    # Журнал пишется в UTF-8; Get-Content в Windows PowerShell 5.1 прочитал бы его в кодировке ANSI.
    $record = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($record.schemaVersion -gt $supportedSchemaVersion) {
        return New-BoosterStatus -Status NotSupported -Note "schemaVersion $($record.schemaVersion): журнал записан более новой версией Booster"
    }
    $script:journal = $record
    [ordered]@{
        sessionId  = $record.sessionId
        profileId  = $record.profileId
        gameName   = $record.gameName
        startedAt  = ConvertTo-IsoText $record.startedAt
        state      = $record.state
        outcome    = $record.outcome
        finishedAt = ConvertTo-IsoText $record.finishedAt
    }
}

if ($sections.session.status -ne 'Available') {
    $sections.changes = $sections.session
    $sections.summary = $sections.session
}
else {
    $sections.changes = New-BoosterSection {
        , @(@($journal.changes) | ForEach-Object { Test-Change $_ })
    }
    $sections.summary = if ($sections.changes.status -ne 'Available') {
        $sections.changes
    }
    else {
        New-BoosterSection {
            $checked = @($sections.changes.value)
            [ordered]@{
                total        = $checked.Count
                matching     = @($checked | Where-Object { $_.matchesOriginal -eq $true }).Count
                differing    = @($checked | Where-Object { $_.matchesOriginal -eq $false }).Count
                notCheckable = @($checked | Where-Object { $null -eq $_.matchesOriginal }).Count
            }
        }
    }
}

Write-BoosterDocument -Document (New-BoosterDocument -Script (Split-Path -Leaf $PSCommandPath) -Sections $sections) -OutFile $OutFile
