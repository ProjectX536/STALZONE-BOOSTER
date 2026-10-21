#Requires -Version 5.1
<#
.SYNOPSIS
    Скачивает PresentMon 2.6.0 с официального релиза GitHub и проверяет SHA256.

.DESCRIPTION
    Версия и хеш закреплены: значение SHA256 взято из API релиза
    https://api.github.com/repos/GameTechDev/PresentMon/releases/tags/v2.6.0 (поле digest).
    Файл сначала скачивается во временный файл рядом с целевым и переименовывается только после
    совпадения хеша, поэтому непроверенный exe под настоящим именем не появляется.
    Скрипт ничего не меняет в системе: только создаёт каталог и файл в профиле пользователя.

.PARAMETER Destination
    Каталог установки. По умолчанию — %LOCALAPPDATA%\Booster\tools\PresentMon (там его ищет Booster).

.OUTPUTS
    Полный путь к проверенному exe.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Booster\tools\PresentMon')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = '2.6.0'
$fileName = "PresentMon-$version-x64.exe"
$url = "https://github.com/GameTechDev/PresentMon/releases/download/v$version/$fileName"
$expectedSha256 = 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'

$target = Join-Path $Destination $fileName

function Test-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $expectedSha256
}

if ((Test-Path -LiteralPath $target -PathType Leaf) -and (Test-Sha256 $target)) {
    Write-Verbose "PresentMon $version уже установлен: $target"
    return $target
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$partial = "$target.download"

# Windows PowerShell 5.1 по умолчанию может не включать TLS 1.2, а GitHub принимает только его и новее.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

try {
    Write-Verbose "Скачивание $url"
    # Без индикатора прогресса Invoke-WebRequest в 5.1 работает в разы быстрее.
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing

    if (-not (Test-Sha256 $partial)) {
        throw "SHA256 скачанного файла не совпадает с ожидаемым $expectedSha256. Файл не установлен."
    }
    Move-Item -LiteralPath $partial -Destination $target -Force
}
finally {
    # Остаётся только при сбое: недокачанный или не прошедший проверку файл.
    if (Test-Path -LiteralPath $partial) {
        Remove-Item -LiteralPath $partial -Force
    }
}

Write-Verbose "PresentMon $version установлен: $target"
return $target
