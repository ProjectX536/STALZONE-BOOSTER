#Requires -Version 5.1
<#
.SYNOPSIS
    Общие функции диагностических скриптов Booster (00–05, 07).

.DESCRIPTION
    Скрипты только читают систему. Каждый собирает документ
    { schemaVersion, script, collectedAt, isElevated, sections } и выводит его как JSON в stdout или в файл.
    Секция — { status, value } при успехе или { status, note } с причиной. Статусы совпадают с DataStatus
    приложения: Available, RequiresAdmin, NotSupported, Failed, Unknown.
    В вывод не попадают серийные номера, имя компьютера и имя пользователя: отчёт могут переслать для помощи.
    Работает в Windows PowerShell 5.1 и PowerShell 7.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:Invariant = [Globalization.CultureInfo]::InvariantCulture
$script:Utf8NoBom = New-Object Text.UTF8Encoding $false
$script:ConfigDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'config'

#region Документ и секции

function Test-BoosterElevated {
    [CmdletBinding()]
    [OutputType([bool])]
    param()
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    finally {
        $identity.Dispose()
    }
}

<#
.SYNOPSIS
    Get-CimInstance с таймаутом; сбой — исключение, которое New-BoosterSection превращает в статус Failed.
#>
function Invoke-BoosterCim {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ClassName,
        [string]$Namespace = 'root/cimv2',
        [string]$Filter,
        [string[]]$Property,
        [int]$TimeoutSec = 15
    )
    $query = @{ ClassName = $ClassName; Namespace = $Namespace; OperationTimeoutSec = $TimeoutSec; ErrorAction = 'Stop' }
    if ($Filter) { $query.Filter = $Filter }
    if ($Property) { $query.Property = $Property }
    Get-CimInstance @query
}

<#
.SYNOPSIS
    Выполняет блок и возвращает секцию: { status: Available, value } или { status: Failed, note }.

.DESCRIPTION
    Блок возвращает одно значение: словарь, объект или массив. Массив возвращается через запятую (,@(...)),
    иначе PowerShell развернёт его, а пустой превратит в $null. Другой статус блок задаёт через New-BoosterStatus.
#>
function New-BoosterSection {
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$ScriptBlock)
    try {
        $value = & $ScriptBlock
    }
    catch {
        return [ordered]@{ status = 'Failed'; note = $_.Exception.Message }
    }
    if ($null -ne $value -and $value.PSObject.TypeNames -contains 'Booster.Status') {
        return [ordered]@{ status = $value.status; note = $value.note }
    }
    # Массив из конвейера в 5.1 обёрнут в PSObject, и ConvertTo-Json пишет его как { value, Count }; @() снимает обёртку.
    if ($value -is [array]) {
        $value = @($value)
    }
    [ordered]@{ status = 'Available'; value = $value }
}

function New-BoosterStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('RequiresAdmin', 'NotSupported', 'Failed', 'Unknown')][string]$Status,
        [Parameter(Mandatory)][string]$Note
    )
    [pscustomobject]@{ PSTypeName = 'Booster.Status'; status = $Status; note = $Note }
}

function New-BoosterDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][Collections.Specialized.OrderedDictionary]$Sections
    )
    [ordered]@{
        schemaVersion = 1
        script        = $Script
        collectedAt   = [DateTimeOffset]::Now.ToString('o', $script:Invariant)
        isElevated    = Test-BoosterElevated
        sections      = $Sections
    }
}

<#
.SYNOPSIS
    JSON глубиной 8; с -AsciiOnly символы вне ASCII записываются как \uXXXX.

.DESCRIPTION
    Кодировка консоли зависит от системы (в русской Windows — 866), поэтому в stdout идёт только ASCII:
    его одинаково прочтёт любая программа. Кодировку консоли скрипт не меняет.
#>
function ConvertTo-BoosterJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()]$InputObject,
        [switch]$AsciiOnly
    )
    $json = ConvertTo-Json -InputObject $InputObject -Depth 8
    if ($AsciiOnly) {
        $json = [regex]::Replace($json, '[^\x00-\x7F]', { param($m) '\u{0:x4}' -f [int][char]$m.Value })
    }
    $json
}

<#
.SYNOPSIS
    Документ — в файл (UTF-8 без BOM) или в stdout (ASCII).
#>
function Write-BoosterDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Document,
        [string]$OutFile
    )
    if ($OutFile) {
        [IO.File]::WriteAllText((Resolve-BoosterPath $OutFile), (ConvertTo-BoosterJson $Document), $script:Utf8NoBom)
    }
    else {
        ConvertTo-BoosterJson $Document -AsciiOnly
    }
}

#endregion

#region Значения

<#
.SYNOPSIS
    Путь относительно текущего каталога PowerShell. [IO.File] считает от каталога процесса, а он может отличаться.
#>
function Resolve-BoosterPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

<#
.SYNOPSIS
    Путь внутри профиля пользователя записывается от %USERPROFILE%: в профиле есть имя пользователя.
#>
function ConvertTo-BoosterSafePath {
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$Path)
    $userProfile = [Environment]::GetFolderPath('UserProfile').TrimEnd('\')
    if ($Path -and $userProfile -and $Path.StartsWith("$userProfile\", [StringComparison]::OrdinalIgnoreCase)) {
        return '%USERPROFILE%' + $Path.Substring($userProfile.Length)
    }
    $Path
}

<#
.SYNOPSIS
    Строка без пробелов по краям; пустая — $null.
#>
function ConvertTo-BoosterText {
    [CmdletBinding()]
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return $null }
    $text = ([string]$Value).Trim()
    if ($text) { $text } else { $null }
}

<#
.SYNOPSIS
    Дата CIM → ISO 8601 со смещением; с -DateOnly — yyyy-MM-dd.

.DESCRIPTION
    Даты драйверов и BIOS WMI хранит как полночь UTC, а Get-CimInstance переводит их в местное время.
    Для -DateOnly дата берётся в UTC, иначе к западу от Гринвича она сдвинулась бы на день назад.
#>
function ConvertTo-BoosterDate {
    [CmdletBinding()]
    param(
        [AllowNull()]$Value,
        [switch]$DateOnly
    )
    if ($Value -isnot [datetime]) { return $null }
    if ($DateOnly) {
        return $Value.ToUniversalTime().ToString('yyyy-MM-dd', $script:Invariant)
    }
    ([DateTimeOffset]$Value).ToString('o', $script:Invariant)
}

#endregion

#region Конфигурация

<#
.SYNOPSIS
    Читает JSON из config/. Как и приложение, допускает комментарии // и /* */ и висячие запятые.
#>
function Read-BoosterConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $text = [IO.File]::ReadAllText((Resolve-BoosterPath $Path))
    # Группа 1 — строка в кавычках: внутри неё // и запятые остаются как есть.
    $string = '("(?:\\.|[^"\\])*")'
    $text = [regex]::Replace($text, "$string|//[^\r\n]*|/\*[\s\S]*?\*/", { param($m) $m.Groups[1].Value })
    $text = [regex]::Replace($text, "$string|,(?=\s*[}\]])", { param($m) $m.Groups[1].Value })
    ConvertFrom-Json -InputObject $text
}

<#
.SYNOPSIS
    Значение из файла config/ репозитория по пути через точку, например detectors.timeoutsMs.drivers.
    Пользовательские файлы из %LOCALAPPDATA%\Booster\config не читаются: скрипты работают и без приложения.
#>
function Get-BoosterSetting {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$File,
        [Parameter(Mandatory)][string]$Name
    )
    $node = Read-BoosterConfig -Path (Join-Path $script:ConfigDirectory $File)
    foreach ($part in $Name.Split('.')) {
        $property = if ($null -ne $node) { $node.PSObject.Properties[$part] } else { $null }
        if ($null -eq $property -or $null -eq $property.Value) {
            throw "config/$File has no value $Name"
        }
        $node = $property.Value
    }
    $node
}

#endregion

#region Драйверы и официальные ссылки

<#
.SYNOPSIS
    Устройства и их драйверы — так же, как в приложении (DriverInventory): устройства из Win32_PnPEntity,
    драйвер из Win32_PnPSignedDriver по DeviceID. Отсутствующие сейчас устройства (Present = false) пропускаются.

.DESCRIPTION
    DeviceID в вывод не попадает: в нём бывают серийные номера USB-устройств.
    Состояние по ConfigManagerErrorCode: 0 — Ok, 22 — Disabled (отключено, не сбой), иначе Error; нет кода — $null.
#>
function Get-BoosterDeviceInventory {
    [CmdletBinding()]
    param([int]$TimeoutSec = 90)
    # По очереди, только нужные свойства: два тяжёлых перечисления сразу нагружают WMI.
    $entities = @(Invoke-BoosterCim -ClassName Win32_PnPEntity -TimeoutSec $TimeoutSec `
            -Property DeviceID, Name, PNPClass, HardwareID, ConfigManagerErrorCode, Present)
    $signed = @(Invoke-BoosterCim -ClassName Win32_PnPSignedDriver -TimeoutSec $TimeoutSec `
            -Property DeviceID, DriverProviderName, DriverVersion, DriverDate, InfName, IsSigned, Signer)

    $drivers = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $signed) {
        if ($row.DeviceID -and -not $drivers.ContainsKey($row.DeviceID)) {
            $drivers.Add($row.DeviceID, $row)
        }
    }

    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $devices = foreach ($entity in $entities) {
        if (-not $entity.DeviceID -or $entity.Present -eq $false -or -not $seen.Add($entity.DeviceID)) {
            continue
        }
        $code = $entity.ConfigManagerErrorCode
        $state = if ($null -eq $code) { $null } elseif ($code -eq 0) { 'Ok' } elseif ($code -eq 22) { 'Disabled' } else { 'Error' }
        $row = $null
        $driver = if ($drivers.TryGetValue($entity.DeviceID, [ref]$row)) {
            [ordered]@{
                provider = ConvertTo-BoosterText $row.DriverProviderName
                version  = ConvertTo-BoosterText $row.DriverVersion
                date     = ConvertTo-BoosterDate $row.DriverDate -DateOnly
                infName  = ConvertTo-BoosterText $row.InfName
                isSigned = $row.IsSigned
                signer   = ConvertTo-BoosterText $row.Signer
            }
        }
        [ordered]@{
            name        = ConvertTo-BoosterText $entity.Name
            deviceClass = ConvertTo-BoosterText $entity.PNPClass
            hardwareIds = @($entity.HardwareID | Where-Object { $_ })
            state       = $state
            errorCode   = $code
            driver      = $driver
        }
    }
    $devices | Sort-Object { $_.deviceClass }, { $_.name }
}

<#
.SYNOPSIS
    Повторяет OfficialSourceResolver.IsAllowed: https на разрешённом хосте или его поддомене, без имени пользователя
    и нестандартного порта; либо ровно адрес Windows Update из конфигурации.
#>
function Test-BoosterAllowedUrl {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyString()][string]$Url,
        [Parameter(Mandatory)]$Sources
    )
    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri)) {
        return $false
    }
    $windowsUpdate = $null
    if ([Uri]::TryCreate([string]$Sources.windowsUpdateUri, [UriKind]::Absolute, [ref]$windowsUpdate) `
            -and [string]::Equals($uri.AbsoluteUri, $windowsUpdate.AbsoluteUri, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    # Имя пользователя отсекает адреса вида https://www.nvidia.com@evil.io/, где настоящий хост — evil.io.
    if ($uri.Scheme -cne [Uri]::UriSchemeHttps -or $uri.UserInfo.Length -ne 0 -or -not $uri.IsDefaultPort) {
        return $false
    }
    foreach ($allowed in @($Sources.allowedHosts)) {
        if ([string]::IsNullOrWhiteSpace($allowed)) { continue }
        if ($uri.IdnHost.Equals($allowed, [StringComparison]::OrdinalIgnoreCase) `
                -or $uri.IdnHost.EndsWith(".$allowed", [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    $false
}

<#
.SYNOPSIS
    Повторяет OfficialSourceResolver.ForDevice: видеоадаптер (Display) — сайт производителя GPU по PCI Vendor ID,
    системное устройство (System) — сайт производителя платы; Windows Update — всегда последним.
#>
function Get-BoosterOfficialLinks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Device,
        [AllowNull()][AllowEmptyString()][string]$BoardManufacturer,
        [Parameter(Mandatory)]$Sources,
        [Parameter(Mandatory)][string]$WindowsUpdateTitle
    )
    $rules = @()
    if ([string]::Equals($Device.deviceClass, 'Display', [StringComparison]::OrdinalIgnoreCase)) {
        $vendorId = $null
        foreach ($id in $Device.hardwareIds) {
            $match = [regex]::Match($id, '^PCI\\VEN_(?<ven>[0-9A-F]{4})', 'IgnoreCase, CultureInvariant')
            if ($match.Success) {
                $vendorId = $match.Groups['ven'].Value
                break
            }
        }
        if ($vendorId) {
            $rules = @(@($Sources.gpuVendors) | Where-Object { [string]::Equals($_.match, $vendorId, [StringComparison]::OrdinalIgnoreCase) })
        }
    }
    elseif ([string]::Equals($Device.deviceClass, 'System', [StringComparison]::OrdinalIgnoreCase) -and $BoardManufacturer) {
        $rules = @(@($Sources.boardVendors) | Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.match) -and $BoardManufacturer.IndexOf($_.match, [StringComparison]::OrdinalIgnoreCase) -ge 0
            })
    }
    # Правило с недопустимым адресом пропускается, как будто его нет.
    foreach ($rule in $rules) {
        if (Test-BoosterAllowedUrl -Url $rule.url -Sources $Sources) {
            [ordered]@{ title = $rule.title; url = ([Uri]$rule.url).AbsoluteUri }
            break
        }
    }
    $windowsUpdate = $null
    if ([Uri]::TryCreate([string]$Sources.windowsUpdateUri, [UriKind]::Absolute, [ref]$windowsUpdate)) {
        [ordered]@{ title = $WindowsUpdateTitle; url = $windowsUpdate.AbsoluteUri }
    }
}

#endregion

#region PresentMon и метрики кадров

<#
.SYNOPSIS
    CSV PresentMon → интервалы между кадрами одной swap chain одного процесса. Повторяет PresentMonCsvParser.

.DESCRIPTION
    Колонки ищутся без учёта регистра. Интервал — MsBetweenPresents, а в формате --v2_metrics — FrameTime.
    NA, пустое, нечисловое и не больше нуля отбрасывается и считается. Из нескольких PID берётся процесс с
    наибольшим числом годных кадров, из его swap chain — с наибольшим числом интервалов; при равенстве —
    встреченный первым. Статусы: Completed, TargetNotFound, NoFrames, UnsupportedFormat.
#>
function Read-PresentMonCsv {
    [CmdletBinding(DefaultParameterSetName = 'Name')]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory, ParameterSetName = 'Name')][string]$ProcessName,
        [Parameter(Mandatory, ParameterSetName = 'Id')][int]$ProcessId
    )
    $byId = $PSCmdlet.ParameterSetName -eq 'Id'
    $result = [ordered]@{
        status         = $null
        detail         = $null
        processName    = $null
        processId      = $null
        intervalColumn = $null
        intervalsMs    = [double[]]@()
        discardedRows  = 0
        frameTypes     = [string[]]@()
    }

    Add-Type -AssemblyName Microsoft.VisualBasic
    $parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser -ArgumentList (Resolve-BoosterPath $Path), ([Text.Encoding]::UTF8), $true
    try {
        $parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
        $parser.SetDelimiters(',')
        $parser.HasFieldsEnclosedInQuotes = $true
        $parser.TrimWhiteSpace = $false

        $header = if ($parser.EndOfData) { $null } else { $parser.ReadFields() }
        if ($null -eq $header) {
            $result.status = 'UnsupportedFormat'
            $result.detail = 'CSV is empty'
            return $result
        }
        $column = @{}
        foreach ($name in 'Application', 'ProcessID', 'SwapChainAddress', 'MsBetweenPresents', 'FrameTime', 'FrameType') {
            $column[$name] = -1
            for ($i = 0; $i -lt $header.Length; $i++) {
                if ([string]::Equals($header[$i].Trim(), $name, [StringComparison]::OrdinalIgnoreCase)) {
                    $column[$name] = $i
                    break
                }
            }
        }
        if ($column.Application -lt 0 -or $column.ProcessID -lt 0 -or $column.SwapChainAddress -lt 0) {
            $result.status = 'UnsupportedFormat'
            $result.detail = 'CSV has no Application, ProcessID or SwapChainAddress column'
            return $result
        }
        $intervalColumn = if ($column.MsBetweenPresents -ge 0) { 'MsBetweenPresents' } else { 'FrameTime' }
        $intervalIndex = $column[$intervalColumn]
        if ($intervalIndex -lt 0) {
            $result.status = 'UnsupportedFormat'
            $result.detail = 'CSV has neither MsBetweenPresents nor FrameTime column'
            return $result
        }
        $appIndex = $column.Application
        $pidIndex = $column.ProcessID
        $chainIndex = $column.SwapChainAddress
        $typeIndex = $column.FrameType

        $processes = New-Object 'Collections.Generic.Dictionary[int,object]'
        $processOrder = New-Object 'Collections.Generic.List[object]'
        $targetRows = 0
        while (-not $parser.EndOfData) {
            $row = $parser.ReadFields()
            if ($null -eq $row) { break }
            $id = 0
            $idText = if ($pidIndex -lt $row.Length) { $row[$pidIndex] } else { '' }
            if (-not [int]::TryParse($idText, [Globalization.NumberStyles]::None, $script:Invariant, [ref]$id)) {
                continue
            }
            $app = if ($appIndex -lt $row.Length) { $row[$appIndex] } else { '' }
            if ($byId) {
                if ($id -ne $ProcessId) { continue }
            }
            elseif (-not [string]::Equals($app, $ProcessName, [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $targetRows++
            $process = $null
            if (-not $processes.TryGetValue($id, [ref]$process)) {
                $process = [pscustomobject]@{
                    Id          = $id
                    Application = $app
                    ValidFrames = 0
                    Chains      = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
                    ChainOrder  = New-Object 'Collections.Generic.List[object]'
                }
                $processes.Add($id, $process)
                $processOrder.Add($process)
            }
            $chainKey = if ($chainIndex -lt $row.Length) { $row[$chainIndex] } else { '' }
            $chain = $null
            if (-not $process.Chains.TryGetValue($chainKey, [ref]$chain)) {
                $chain = [pscustomobject]@{
                    Intervals  = New-Object 'Collections.Generic.List[double]'
                    Discarded  = 0
                    FrameTypes = New-Object 'Collections.Generic.List[string]'
                    Seen       = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
                }
                $process.Chains.Add($chainKey, $chain)
                $process.ChainOrder.Add($chain)
            }

            $ms = 0.0
            $text = if ($intervalIndex -lt $row.Length) { $row[$intervalIndex] } else { '' }
            if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, $script:Invariant, [ref]$ms) `
                    -and -not [double]::IsNaN($ms) -and -not [double]::IsInfinity($ms) -and $ms -gt 0) {
                $chain.Intervals.Add($ms)
                $process.ValidFrames++
                $frameType = if ($typeIndex -ge 0 -and $typeIndex -lt $row.Length) { $row[$typeIndex] } else { $null }
                if (-not [string]::IsNullOrEmpty($frameType) -and $chain.Seen.Add($frameType)) {
                    $chain.FrameTypes.Add($frameType)
                }
            }
            else {
                $chain.Discarded++
            }
        }
    }
    finally {
        $parser.Dispose()
    }

    $target = if ($byId) { "process ID $ProcessId" } else { "process $ProcessName" }
    if ($targetRows -eq 0) {
        $result.status = 'TargetNotFound'
        $result.detail = "CSV has no rows for $target"
        return $result
    }
    # Несколько PID — процесс перезапускался; несколько swap chain — оверлей или второе окно. Берётся основной поток кадров.
    $chosen = $processOrder[0]
    foreach ($candidate in $processOrder) {
        if ($candidate.ValidFrames -gt $chosen.ValidFrames) { $chosen = $candidate }
    }
    $chain = $chosen.ChainOrder[0]
    foreach ($candidate in $chosen.ChainOrder) {
        if ($candidate.Intervals.Count -gt $chain.Intervals.Count) { $chain = $candidate }
    }
    if ($chain.Intervals.Count -eq 0) {
        $result.status = 'NoFrames'
        $result.detail = "All $targetRows rows of $target have no usable $intervalColumn (NA, empty or not positive)"
        return $result
    }

    $result.status = 'Completed'
    $result.processName = $chosen.Application
    $result.processId = $chosen.Id
    $result.intervalColumn = $intervalColumn
    $result.intervalsMs = $chain.Intervals.ToArray()
    $result.discardedRows = $chain.Discarded
    $result.frameTypes = $chain.FrameTypes.ToArray()
    $result
}

<#
.SYNOPSIS
    Метрики по интервалам между кадрами — как FrameMetricsCalculator (docs/ARCHITECTURE.md, раздел 10).

.DESCRIPTION
    Средний FPS = 1000·N / Σинтервалов; перцентили — по ближайшему рангу ⌈p·N/100⌉;
    1% low = 1000 / среднее k = ⌈N/100⌉ самых долгих интервалов.
    Интервалы ≤ 0, NaN и бесконечные отбрасываются и считаются в invalidIntervals.
    При N < MinimumFrames — status InsufficientData и metrics = $null.
#>
function Get-BoosterFrameMetrics {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][double[]]$IntervalsMs,
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$MinimumFrames
    )
    $valid = New-Object 'Collections.Generic.List[double]'
    foreach ($interval in $IntervalsMs) {
        if (-not [double]::IsNaN($interval) -and -not [double]::IsInfinity($interval) -and $interval -gt 0) {
            $valid.Add($interval)
        }
    }
    $sorted = $valid.ToArray()
    $n = $sorted.Length
    $result = [ordered]@{
        status           = 'InsufficientData'
        validIntervals   = $n
        invalidIntervals = $IntervalsMs.Length - $n
        minimumFrames    = $MinimumFrames
        metrics          = $null
    }
    if ($n -lt $MinimumFrames) {
        return $result
    }

    # Суммы по возрастанию, как у приложения: порядок сложения влияет на последние знаки.
    [Array]::Sort($sorted)
    $duration = 0.0
    foreach ($interval in $sorted) { $duration += $interval }
    $tail = [int][Math]::Ceiling($n / 100.0)
    $tailSum = 0.0
    for ($i = $n - $tail; $i -lt $n; $i++) { $tailSum += $sorted[$i] }

    $result.status = 'Ok'
    $result.metrics = [ordered]@{
        frameCount       = $n
        durationMs       = $duration
        averageFps       = 1000.0 * $n / $duration
        onePercentLowFps = 1000.0 / ($tailSum / $tail)
        frameTimeP50Ms   = Get-NearestRank $sorted 50
        frameTimeP95Ms   = Get-NearestRank $sorted 95
        frameTimeP99Ms   = Get-NearestRank $sorted 99
    }
    $result
}

function Get-NearestRank([double[]]$Sorted, [int]$Percent) {
    # p·N считается в целых, чтобы 0,99·300 не превратилось в 297,000…1 и ранг не сдвинулся.
    $rank = [int][Math]::Ceiling($Percent * [long]$Sorted.Length / 100.0)
    $Sorted[[Math]::Max($rank, 1) - 1]
}

#endregion

Export-ModuleMember -Function Test-BoosterElevated, Invoke-BoosterCim, New-BoosterSection, New-BoosterStatus,
    New-BoosterDocument, ConvertTo-BoosterJson, Write-BoosterDocument, Resolve-BoosterPath, ConvertTo-BoosterSafePath,
    ConvertTo-BoosterText, ConvertTo-BoosterDate, Read-BoosterConfig, Get-BoosterSetting, Get-BoosterDeviceInventory,
    Test-BoosterAllowedUrl, Get-BoosterOfficialLinks, Read-PresentMonCsv, Get-BoosterFrameMetrics
