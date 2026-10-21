using Booster.Platform.Windows.Hardware;

namespace Booster.Platform.Windows.Tests.Hardware;

public class ServiceImagePathTests
{
    private static readonly string SystemRoot = Environment.GetEnvironmentVariable("SystemRoot")!;

    [Fact]
    public void Quoted_path_with_arguments()
    {
        Assert.Equal(@"C:\Program Files\Contoso\svc.exe", ServiceImagePath.Resolve(@"""C:\Program Files\Contoso\svc.exe"" --run", null));
    }

    [Fact]
    public void Unquoted_path_with_arguments()
    {
        Assert.Equal(@"C:\Program Files\Contoso\svc.exe", ServiceImagePath.Resolve(@"C:\Program Files\Contoso\svc.exe /service", null));
        Assert.Equal(@"C:\WINDOWS\system32\svchost.exe", ServiceImagePath.Resolve(@"C:\WINDOWS\system32\svchost.exe -k netsvcs -p", null));
        Assert.Equal(@"C:\Tools\agent.bin", ServiceImagePath.Resolve(@"C:\Tools\agent.bin", null));
    }

    [Fact]
    public void SystemRoot_prefix_is_expanded()
    {
        Assert.Equal(SystemRoot + @"\System32\svc.exe", ServiceImagePath.Resolve(@"\SystemRoot\System32\svc.exe", null));
        Assert.Equal(SystemRoot + @"\System32\svc.exe", ServiceImagePath.Resolve(@"%SystemRoot%\System32\svc.exe -x", null));
        Assert.Equal(@"C:\Tools\svc.exe", ServiceImagePath.Resolve(@"\??\C:\Tools\svc.exe", null));
    }

    [Fact]
    public void Svchost_uses_service_dll()
    {
        Assert.Equal(SystemRoot + @"\System32\wuaueng.dll",
            ServiceImagePath.Resolve(@"C:\WINDOWS\system32\svchost.exe -k netsvcs -p", @"%SystemRoot%\System32\wuaueng.dll"));
        Assert.Equal(@"C:\Program Files\Contoso\svc.exe",
            ServiceImagePath.Resolve(@"C:\Program Files\Contoso\svc.exe", @"C:\Other\ignored.dll"));
        Assert.True(ServiceImagePath.IsSharedHost(@"C:\WINDOWS\System32\SVCHOST.EXE -k LocalService"));
        Assert.False(ServiceImagePath.IsSharedHost(@"C:\Program Files\Contoso\svc.exe"));
    }

    [Fact]
    public void Null_path_is_null()
    {
        Assert.Null(ServiceImagePath.Resolve(null, null));
        Assert.Null(ServiceImagePath.Resolve("  ", null));
        Assert.Null(ServiceImagePath.Resolve(@"""""", null));
        Assert.False(ServiceImagePath.IsSharedHost(null));
    }
}
