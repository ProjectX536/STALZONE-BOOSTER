using System.Text.RegularExpressions;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Scripts;

/// <summary>
/// Диагностические скрипты только читают систему (docs/PRINCIPLES.md, «Безопасность машины разработчика»). Проверяется текст файлов, скрипты не запускаются.
/// Install-PresentMon.ps1 не входит: он по назначению скачивает файл в профиль пользователя.
/// </summary>
public partial class ScriptSafetyTests
{
    private static readonly string[] Expected =
    [
        "00-Collect-Hardware.ps1", "01-Collect-Drivers.ps1", "02-Collect-Performance.ps1", "03-Collect-Storage.ps1",
        "04-Export-Diagnostics.ps1", "05-Run-Baseline.ps1", "06-Verify-Restore.ps1", "07-Generate-Driver-Report.ps1",
        "Booster.Diagnostics.psm1",
    ];

    public static TheoryData<string> DiagnosticScripts => [.. Expected];

    [Fact]
    public void Every_diagnostic_script_is_checked()
    {
        var present = Directory.EnumerateFiles(PowerShellScript.ScriptsDirectory, "0*.ps1")
            .Concat(Directory.EnumerateFiles(PowerShellScript.ScriptsDirectory, "*.psm1"))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(Expected.Order(StringComparer.Ordinal), present);
    }

    [Theory]
    [MemberData(nameof(DiagnosticScripts))]
    public void Script_has_no_mutating_commands(string script)
    {
        var lines = File.ReadAllLines(Path.Combine(PowerShellScript.ScriptsDirectory, script));

        var hits = lines.Select((line, index) => (line, number: index + 1))
            .Where(x => Mutating().IsMatch(x.line))
            .Select(x => $"{script}:{x.number}: {x.line.Trim()}");

        Assert.Empty(hits);
    }

    [Theory]
    [InlineData(@"Set-ItemProperty -Path HKCU:\Software\X -Name Y -Value 1")]
    [InlineData(@"New-ItemProperty -Path HKLM:\SYSTEM\X -Name Y")]
    [InlineData("Remove-Item -LiteralPath $path -Recurse")]
    [InlineData("New-Item -ItemType Directory $dir")]
    [InlineData("Stop-Service SysMain")]
    [InlineData("Set-Service -Name WSearch -StartupType Disabled")]
    [InlineData("Stop-Process -Name explorer")]
    [InlineData("Disable-ScheduledTask -TaskName X")]
    [InlineData("Invoke-CimMethod -ClassName Win32_Process -MethodName Create")]
    [InlineData("powercfg /setactive SCHEME_MIN")]
    [InlineData("& powercfg.exe -change -monitor-timeout-ac 0")]
    [InlineData("powercfg /hibernate off")]
    [InlineData(@"reg add HKLM\SOFTWARE\X /v Y /d 1")]
    [InlineData("bcdedit /set useplatformclock true")]
    [InlineData("sc config SysMain start= disabled")]
    [InlineData(@"[Microsoft.Win32.Registry]::SetValue('HKEY_CURRENT_USER\X', 'Y', 1)")]
    [InlineData("$key.CreateSubKey('X')")]
    [InlineData("$hklm.OpenSubKey('SYSTEM\\X', $true)")]
    [InlineData("(Get-Process game).PriorityClass = 'High'")]
    [InlineData("Set-PhysicalDisk -FriendlyName X -MediaType SSD")]
    [InlineData("Optimize-Volume -DriveLetter C")]
    public void Pattern_catches_mutating_command(string line) => Assert.Matches(Mutating(), line);

    [Theory]
    [InlineData("powercfg /getactivescheme")]
    [InlineData("& powercfg.exe /list")]
    [InlineData(@"Get-ItemProperty -Path HKCU:\Software\Microsoft\GameBar")]
    [InlineData(@"[Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\X', 'Y', $null)")]
    [InlineData("Get-Service SysMain")]
    [InlineData("Get-PhysicalDisk | Get-StorageReliabilityCounter")]
    [InlineData("[IO.File]::WriteAllText($path, $json, $utf8)")]
    public void Pattern_allows_read_only_command(string line) => Assert.DoesNotMatch(Mutating(), line);

    [GeneratedRegex("""
        \b(?:Set|New|Remove|Rename|Move|Copy|Clear)-Item(?:Property)?\b
        | \b(?:Start|Stop|Restart|Suspend|Resume|Set|New|Remove)-Service\b
        | \bStop-Process\b
        | \b(?:Set|New|Remove)-(?:Cim|Wmi)Instance\b | \bInvoke-(?:Cim|Wmi)Method\b
        | \b(?:Enable|Disable)-[a-z]+
        | \b(?:Set|Initialize|Clear|Format|Optimize|Repair|Resize|Reset|Update)-(?:Disk|PhysicalDisk|Volume|Partition)\b
        | \b(?:Set-ExecutionPolicy|Set-MpPreference|Add-MpPreference|Register-ScheduledTask|Unregister-ScheduledTask
            |Set-ProcessMitigation|Restart-Computer|Stop-Computer)\b
        | \bpowercfg(?:\.exe)?\b[^\r\n]*?\s[/-](?:setactive|s|change|x|setacvalueindex|setdcvalueindex|duplicatescheme|delete|d
            |deletesetting|import|hibernate|h|attributes|setsecuritydescriptor|restoredefaultschemes|changename)\b
        | \breg(?:\.exe)?\s+(?:add|delete|import|load|unload|restore|copy)\b
        | \b(?:bcdedit|schtasks|fsutil|dism|netsh|wmic)\b
        | \bsc(?:\.exe)?\s+(?:config|stop|start|delete|create|failure|pause)\b
        | \b(?:CreateSubKey|DeleteSubKey|DeleteSubKeyTree|DeleteValue|SetValue|SetAccessControl)\s*\(
        | \bOpenSubKey\s*\([^)]*,\s*\$true
        | \.(?:PriorityClass|ProcessorAffinity)\s*=
        | \[(?:System\.)?IO\.(?:File|Directory)\]::(?:Delete|Move)\b
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex Mutating();
}
