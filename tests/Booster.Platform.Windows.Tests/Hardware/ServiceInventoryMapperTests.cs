using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using static Booster.Platform.Windows.Tests.TestSupport.WmiFixture;

namespace Booster.Platform.Windows.Tests.Hardware;

public class ServiceInventoryMapperTests
{
    private static CimRow Service(string? name, string state = "Running", string startMode = "Auto", uint processId = 100) =>
        Row(("Name", name), ("DisplayName", $"{name} display"), ("State", state), ("StartMode", startMode), ("DelayedAutoStart", false),
            ("PathName", $@"C:\Program Files\{name}\{name}.exe -service"), ("ProcessId", processId));

    private static CimRow Dependency(string antecedent, string dependent) =>
        Row(("Antecedent", antecedent), ("Dependent", dependent));

    private static string ServiceRef(string name) => $@"\\PC\root\cimv2:Win32_Service.Name=""{name}""";

    private static IReadOnlyList<ServiceInfo> Map(IReadOnlyList<CimRow> services, IReadOnlyList<CimRow>? dependencies = null) =>
        ServiceInventoryMapper.Map(services, dependencies ?? [], _ => null, _ => Reading<string>.Unknown("test"));

    [Fact]
    public void Maps_state_and_start_mode()
    {
        var services = Map(
        [
            Service("A", "Running", "Auto"),
            Service("B", "Stopped", "Manual"),
            Service("C", "Start Pending", "Disabled"),
            Service("D", "Paused", "Boot"),
            Service("E", "Weird", "System"),
            Service("F", "Stop Pending", "Unexpected"),
        ]);

        Assert.Equal(
            [ServiceRunState.Running, ServiceRunState.Stopped, ServiceRunState.Pending, ServiceRunState.Paused, ServiceRunState.Unknown,
                ServiceRunState.Pending],
            services.Select(s => s.State));
        Assert.Equal(
            [ServiceStartMode.Automatic, ServiceStartMode.Manual, ServiceStartMode.Disabled, ServiceStartMode.Boot, ServiceStartMode.System,
                ServiceStartMode.Unknown],
            services.Select(s => s.StartMode));
        Assert.Equal("A display", services[0].DisplayName);
        Assert.Equal(@"C:\Program Files\A\A.exe", services[0].ImagePath);
        Assert.Equal(100, services[0].ProcessId);
    }

    [Fact]
    public void Dependents_are_attached_to_antecedent()
    {
        var services = Map([Service("RpcSs"), Service("Contoso"), Service("Fabrikam")],
        [
            Dependency(ServiceRef("RpcSs"), ServiceRef("Contoso")),
            Dependency(ServiceRef("rpcss"), ServiceRef("Fabrikam")),
            Dependency(@"\\PC\root\cimv2:Win32_SystemDriver.Name=""Tcpip""", ServiceRef("Contoso")),
            Dependency(ServiceRef("Contoso"), @"\\PC\root\cimv2:Win32_SystemDriver.Name=""Filter"""),
        ]);

        Assert.Equal(["Contoso", "Fabrikam"], services.Single(s => s.Name == "RpcSs").DependentServices);
        Assert.Empty(services.Single(s => s.Name == "Contoso").DependentServices);
    }

    [Fact]
    public void Zero_process_id_is_null()
    {
        var service = Assert.Single(Map([Service("A", "Stopped", processId: 0)]));

        Assert.Null(service.ProcessId);
    }

    [Fact]
    public void Row_without_name_is_skipped()
    {
        Assert.Empty(Map([Service(null), Service(" ")]));
    }

    [Fact]
    public void Service_dll_is_read_only_for_shared_host()
    {
        var asked = new List<string>();
        var rows = new[]
        {
            Row(("Name", "wuauserv"), ("PathName", @"C:\WINDOWS\system32\svchost.exe -k netsvcs -p")),
            Row(("Name", "Contoso"), ("PathName", @"C:\Program Files\Contoso\svc.exe")),
        };

        var services = ServiceInventoryMapper.Map(rows, [], name =>
        {
            asked.Add(name);
            return @"C:\WINDOWS\system32\wuaueng.dll";
        }, path => Reading<string>.Known($"publisher of {path}", "test"));

        Assert.Equal(["wuauserv"], asked);
        Assert.Equal(@"C:\WINDOWS\system32\wuaueng.dll", services[0].ImagePath);
        Assert.Equal(@"publisher of C:\WINDOWS\system32\wuaueng.dll", services[0].Publisher.Value);
    }
}
