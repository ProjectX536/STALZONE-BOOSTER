using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;
using Microsoft.Win32;

namespace Booster.Platform.Windows.Tests.Common;

public class UserRegistryTests
{
    private const string NotAllowed = @"Software\Booster-Test-Not-Allowed";

    private int _opened;

    private UserRegistry Guarded() => new(() =>
    {
        _opened++;
        throw new InvalidOperationException("HKCU must not be opened for a key outside the list");
    });

    public static TheoryData<string> RejectedKeys =>
    [
        NotAllowed,
        @"Software\Microsoft\GameBar\Nested",
        @"\Software\Microsoft\GameBar",
        @"Software\Microsoft\GameBar\",
        @"Software\Microsoft\Windows\CurrentVersion\Run",
    ];

    [Theory]
    [MemberData(nameof(RejectedKeys))]
    public void Write_outside_allowed_keys_throws_before_opening(string subKey)
    {
        var registry = Guarded();

        Assert.Throws<UnauthorizedAccessException>(() => registry.Write(subKey, "x", UserRegistryValue.FromDWord(1)));
        Assert.Throws<UnauthorizedAccessException>(() => registry.Read(subKey, "x"));
        Assert.Equal(0, _opened);
    }

    [Fact]
    public void Delete_outside_allowed_keys_throws()
    {
        Assert.Throws<UnauthorizedAccessException>(() => Guarded().Delete(NotAllowed, "x"));
        Assert.Equal(0, _opened);
    }

    [Fact]
    public void Allowed_key_reaches_the_registry()
    {
        Assert.Throws<InvalidOperationException>(() => Guarded().Write(@"SOFTWARE\Microsoft\GameBar", "x", UserRegistryValue.FromDWord(1)));
        Assert.Equal(1, _opened);
    }

    [Fact]
    public void Allowed_keys_are_the_four_documented()
    {
        Assert.Equal(
            [
                StartupApprovedKeys.Run,
                StartupApprovedKeys.StartupFolder,
                @"Software\Microsoft\GameBar",
                @"Software\Microsoft\DirectX\UserGpuPreferences",
            ],
            UserRegistry.AllowedKeys);
    }

    [Fact]
    [Trait("Category", "Host")]
    public void Reads_allowed_keys_on_host()
    {
        var registry = new UserRegistry();

        var gameMode = registry.Read(@"Software\Microsoft\GameBar", "AutoGameModeEnabled");
        Assert.True(gameMode is null || gameMode.Kind == UserRegistryValueKind.DWord);
        Assert.Null(registry.Read(StartupApprovedKeys.Run, "Booster-host-test-absent-value"));
        using var key = Registry.CurrentUser.OpenSubKey(NotAllowed);
        Assert.Null(key);
    }
}
