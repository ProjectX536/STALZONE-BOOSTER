using Booster.Core.Inventory;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class GpuEngineInstanceNameTests
{
    [Fact]
    public void Full_instance_name_is_parsed()
    {
        Assert.True(GpuEngineInstanceName.TryParse("pid_1234_luid_0x00000000_0x0000C2A1_phys_0_eng_0_engtype_3D", out var instance));

        Assert.Equal(new GpuEngineInstance(1234, new AdapterLuid(0xC2A1, 0), "3D"), instance);
    }

    [Theory]
    [InlineData("pid_10028_luid_0x00000000_0x0000DB0E_phys_0_eng_2_engtype_VideoDecode", "VideoDecode")]
    [InlineData("pid_7_luid_0x00000001_0x0000db0e_phys_0_eng_5_engtype_Compute_0", "Compute_0")]
    [InlineData("pid_7_luid_0x00000001_0x0000DB0E_engtype_Copy", "Copy")]
    public void Engine_type_is_everything_after_engtype(string name, string engineType)
    {
        Assert.True(GpuEngineInstanceName.TryParse(name, out var instance));

        Assert.Equal(engineType, instance.EngineType);
    }

    [Theory]
    [InlineData("pid_x_luid")]
    [InlineData("")]
    [InlineData("_Total")]
    [InlineData("pid_99999999999_luid_0x00000000_0x0000C2A1_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x0_0x0000C2A1_phys_0_eng_0_engtype_3D")]
    public void Malformed_names_are_rejected(string name) =>
        Assert.False(GpuEngineInstanceName.TryParse(name, out _));
}
