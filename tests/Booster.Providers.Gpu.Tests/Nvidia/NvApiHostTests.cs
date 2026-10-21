using Booster.Platform.Windows.Hardware;
using Booster.Providers.Gpu.Nvidia;

namespace Booster.Providers.Gpu.Tests.Nvidia;

/// <summary>Настоящий nvapi64.dll: только функции чтения из белого списка, DRS-сессия без сохранения.</summary>
[Trait("Category", "Host")]
public sealed class NvApiHostTests
{
    private const uint PowerMizerMode = 0x1057EB71; // NvApiDriverSettings.h: PREFERRED_PSTATE_ID

    private static INvApi Open()
    {
        Assert.SkipUnless(File.Exists(Path.Combine(Environment.SystemDirectory, NvApiLibrary.LibraryName)),
            "nvapi64.dll нет в System32");
        var api = new NvApiLibraryFactory().TryCreate(out var status);
        Assert.SkipWhen(status == NvApiStatus.NvidiaDeviceNotFound, "драйвер NVIDIA есть, видеокарты NVIDIA нет");
        Assert.True(api is not null, $"NvAPI_Initialize: {status}");
        return api;
    }

    private static void Log(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    [Fact]
    public void Initializes_and_enumerates_gpus()
    {
        using var api = Open();

        var gpus = api.EnumeratePhysicalGpus();

        Assert.True(gpus.Ok, $"EnumPhysicalGPUs: {gpus.Status}");
        Assert.NotEmpty(gpus.Value!);
        Assert.Matches(@"^\d+\.\d{2}$", api.DriverVersion);
        Log($"driver {api.DriverVersion}");
        foreach (var gpu in gpus.Value!)
        {
            Assert.NotEqual(0, gpu.Handle);
            Assert.NotEmpty(gpu.Name);
            Log($"{gpu.Name}: LUID {gpu.Luid.Value} ({gpu.Luid.Status}), {gpu.Pci.Value}");
        }
    }

    [Fact]
    public void Gpu_luid_matches_a_dxgi_adapter()
    {
        using var api = Open();
        var dxgi = new DxgiAdapterSource().Enumerate();

        foreach (var gpu in api.EnumeratePhysicalGpus().Value!)
        {
            Assert.True(gpu.Luid.IsAvailable, gpu.Luid.Note);
            var adapter = Assert.Single(dxgi, a => a.Luid == gpu.Luid.Value);
            var pci = gpu.Pci.Value;
            Assert.Equal((adapter.VendorId, adapter.DeviceId, adapter.SubSysId, adapter.Revision),
                ((uint)pci.VendorId, (uint)pci.DeviceId, pci.SubsystemId, (uint)pci.Revision));
        }
    }

    [Fact]
    public void Temperature_and_graphics_clock_are_plausible()
    {
        using var api = Open();
        var gpu = api.EnumeratePhysicalGpus().Value![0].Handle;

        var temperature = api.GetGpuTemperatureC(gpu);
        var clocks = api.GetCurrentClocks(gpu);
        var utilization = api.GetGraphicsUtilizationPercent(gpu);

        Assert.True(temperature.Ok, $"GetThermalSettings: {temperature.Status}");
        Assert.InRange(temperature.Value!.Value, 5, 120);
        Assert.True(clocks.Ok, $"GetAllClockFrequencies: {clocks.Status}");
        Assert.True(clocks.Value.GraphicsKhz > 0);
        Assert.True(utilization.Ok, $"GetDynamicPstatesInfoEx: {utilization.Status}");
        Assert.InRange(utilization.Value!.Value, 0u, 100u);
        Log($"{temperature.Value} °C, graphics {clocks.Value.GraphicsKhz} kHz, memory {clocks.Value.MemoryKhz} kHz, load {utilization.Value} %");
        Log($"perf decrease 0x{api.GetPerfDecreaseFlags(gpu).Value:X}, fan {api.GetFanRpm(gpu).Value} rpm ({api.GetFanRpm(gpu).Status})");
    }

    [Fact]
    public void Global_drs_profile_is_readable()
    {
        using var api = Open();

        var read = api.ReadDrs(null, [PowerMizerMode]);

        Assert.True(read.Ok, $"DRS: {read.Status}");
        Assert.False(string.IsNullOrEmpty(read.Value!.ProfileName));
        Log($"global profile «{read.Value.ProfileName}»: " +
            string.Join(", ", read.Value.Values.Values.Select(v => $"0x{v.SettingId:X8}={v.Value} predefined={v.IsCurrentPredefined} location={v.Location}")));
    }

    [Fact]
    public void Unknown_application_has_no_profile()
    {
        using var api = Open();

        var read = api.ReadDrs("booster-no-such-game-7f3a.exe", [PowerMizerMode]);

        Assert.True(read.Ok, $"DRS: {read.Status}");
        Assert.Null(read.Value!.ProfileName);
        Assert.Empty(read.Value.Values);
    }
}
