using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Providers.GameNetwork;
using Windows.Win32.System.Diagnostics.Etw;

namespace Booster.Platform.Windows.Tests.Providers.GameNetwork;

public class EtwTrafficSourceTests
{
    [Theory]
    [InlineData(5u, DataStatus.RequiresAdmin)]
    [InlineData(1450u, DataStatus.Unknown)]
    [InlineData(87u, DataStatus.Failed)]
    public void Start_errors_map_to_readings(uint win32Error, DataStatus expected)
    {
        var reading = EtwTrafficSource.MapStartError(win32Error);

        Assert.Equal(expected, reading.Status);
        Assert.Null(reading.Value);
        Assert.Contains(win32Error.ToString(CultureInfo.InvariantCulture), reading.Note);
    }

    // Значения — раскладка evntrace.h и evntcons.h для x64; OpenTrace получает маршалированную копию EVENT_TRACE_LOGFILEW.
    [Fact]
    public void Native_layouts_match_the_x64_headers()
    {
        Assert.SkipUnless(RuntimeInformation.ProcessArchitecture == Architecture.X64, "layout values are for x64");

        Assert.Equal(448, Marshal.SizeOf<EVENT_TRACE_LOGFILEW>());
        Assert.Equal(424, Marshal.OffsetOf<EVENT_TRACE_LOGFILEW>(nameof(EVENT_TRACE_LOGFILEW.Anonymous2)));
        Assert.Equal(440, Marshal.OffsetOf<EVENT_TRACE_LOGFILEW>(nameof(EVENT_TRACE_LOGFILEW.Context)));

        Assert.Equal(120, Unsafe.SizeOf<EVENT_TRACE_PROPERTIES>());
        Assert.Equal(116, Marshal.OffsetOf<EVENT_TRACE_PROPERTIES>(nameof(EVENT_TRACE_PROPERTIES.LoggerNameOffset)));

        Assert.Equal(112, Unsafe.SizeOf<EVENT_RECORD>());
        Assert.Equal(86, Marshal.OffsetOf<EVENT_RECORD>(nameof(EVENT_RECORD.UserDataLength)));
        Assert.Equal(96, Marshal.OffsetOf<EVENT_RECORD>(nameof(EVENT_RECORD.UserData)));
        Assert.Equal(104, Marshal.OffsetOf<EVENT_RECORD>(nameof(EVENT_RECORD.UserContext)));
    }
}
