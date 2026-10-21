@echo off
setlocal
rem Restore the caller's console code page on exit.
for /f "tokens=2 delims=:." %%c in ('chcp') do set "BOOSTER_OLD_CP=%%c"
chcp 65001 >nul

rem Аргументы передаются как есть: Start-Booster.cmd --export <папка> пишет отчёт без окна.

set "BOOSTER_ROOT=%~dp0.."
set "BOOSTER_BIN=%BOOSTER_ROOT%\src\Booster.App\bin"
set "BOOSTER_RC=0"

if defined DOTNET_ROOT goto find_exe
if exist "%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\10.*" goto find_exe
if exist "%USERPROFILE%\tools\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%USERPROFILE%\tools\dotnet"
    goto find_exe
)

echo Не найдена среда .NET 10 Desktop Runtime x64.
echo Скачайте её с официальной страницы Microsoft: https://dotnet.microsoft.com/download/dotnet/10.0
echo На странице выберите «.NET Desktop Runtime 10», Windows x64.
set "BOOSTER_RC=1"
goto finish

:find_exe
set "BOOSTER_EXE=%BOOSTER_BIN%\Release\net10.0-windows\Booster.App.exe"
if exist "%BOOSTER_EXE%" goto run
set "BOOSTER_EXE=%BOOSTER_BIN%\Debug\net10.0-windows\Booster.App.exe"
if exist "%BOOSTER_EXE%" goto run

echo Booster ещё не собран. Соберите его из корня репозитория:
echo     dotnet build Booster.sln -c Release
set "BOOSTER_RC=1"
goto finish

:run
rem С аргументами ждём выхода Booster и возвращаем его код (нужно для --export).
if "%~1"=="" (
    start "" "%BOOSTER_EXE%"
    goto finish
)
"%BOOSTER_EXE%" %*
set "BOOSTER_RC=%ERRORLEVEL%"

:finish
rem Иначе при двойном щелчке ошибка исчезнет вместе с окном.
if not "%BOOSTER_RC%"=="0" if "%~1"=="" pause
chcp %BOOSTER_OLD_CP% >nul
endlocal & exit /b %BOOSTER_RC%
