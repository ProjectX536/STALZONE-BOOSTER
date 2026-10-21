using System.ComponentModel;
using Booster.Platform.Windows.Providers;

namespace Booster.Platform.Windows.Tests.Providers;

/// <summary>Без системы: только сопоставление кодов ошибок; процессы не открываются.</summary>
public class ProcessProviderTests
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;

    [Fact]
    public void Access_denied_becomes_UnauthorizedAccessException()
    {
        var error = ProcessProvider.PriorityError(ErrorAccessDenied);

        Assert.IsType<UnauthorizedAccessException>(error);
        Assert.Equal(ErrorAccessDenied, Assert.IsType<Win32Exception>(error.InnerException).NativeErrorCode);
    }

    [Fact]
    public void Other_errors_stay_Win32Exception()
    {
        Assert.Equal(ErrorInvalidParameter, Assert.IsType<Win32Exception>(ProcessProvider.PriorityError(ErrorInvalidParameter)).NativeErrorCode);
    }
}
