using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

// В настоящий реестр не пишем: открытие HKLM подменено и бросает исключение.
public class MachineRegistryTests
{
    private int _opened;

    private MachineRegistry Guarded() => new(() =>
    {
        _opened++;
        throw new InvalidOperationException("HKLM must not be opened for a key outside the list");
    });

    public static TheoryData<string> RejectedKeys =>
    [
        @"Software\Booster-Test-Not-Allowed",
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\Nested",
        @"\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"Software\Microsoft\GameBar",
        @"Software\Microsoft\DirectX\UserGpuPreferences",
    ];

    [Theory]
    [MemberData(nameof(RejectedKeys))]
    public void Access_outside_allowed_keys_throws_before_opening(string subKey)
    {
        var registry = Guarded();

        Assert.Throws<UnauthorizedAccessException>(() => registry.Write(subKey, "x", UserRegistryValue.FromDWord(1)));
        Assert.Throws<UnauthorizedAccessException>(() => registry.Read(subKey, "x"));
        Assert.Throws<UnauthorizedAccessException>(() => registry.Delete(subKey, "x"));
        Assert.Equal(0, _opened);
    }

    [Fact]
    public void Allowed_key_reaches_the_registry()
    {
        Assert.Throws<InvalidOperationException>(() => Guarded().Write(StartupApprovedKeys.Run32.ToUpperInvariant(), "x",
            UserRegistryValue.FromBinary(new byte[12])));
        Assert.Equal(1, _opened);
    }

    [Fact]
    public void Allowed_keys_are_the_three_startup_approved()
    {
        Assert.Equal([StartupApprovedKeys.Run, StartupApprovedKeys.Run32, StartupApprovedKeys.StartupFolder], MachineRegistry.AllowedKeys);
    }

    [Fact]
    [Trait("Category", "Host")]
    public void Reads_allowed_keys_on_host()
    {
        var registry = new MachineRegistry();

        Assert.Null(registry.Read(StartupApprovedKeys.Run, "Booster-host-test-absent-value"));
        Assert.Null(registry.Read(StartupApprovedKeys.Run32, "Booster-host-test-absent-value"));
    }
}
