using System.Runtime.CompilerServices;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Providers.GameNetwork;
using Booster.Platform.Windows.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Windows.Win32.NetworkManagement.WiFi;

namespace Booster.Platform.Windows.Tests.Providers.GameNetwork;

public class WifiSourceTests
{
    private readonly FakeWlanClient _client = new FakeWlanClient()
        .Set(WlanOpcode.Rssi, -55)
        .Set(WlanOpcode.ChannelNumber, 6)
        .Set(WlanOpcode.BackgroundScanEnabled, 1)
        .Set(WlanOpcode.MediaStreamingMode, 0);

    private readonly ListLogger<WifiSource> _logger = new();

    [Fact]
    public void Connected_adapter_reads_all_fields()
    {
        var wifi = Read();

        Assert.Equal(DataStatus.Available, wifi.Status);
        Assert.Equal(Reading<int>.Known(-55, WifiSource.Source), wifi.Value!.RssiDbm);
        Assert.Equal(Reading<int>.Known(6, WifiSource.Source), wifi.Value.Channel);
        Assert.True(wifi.Value.Band.IsAvailable);
        Assert.Equal(WifiBand.Band24GHz, wifi.Value.Band.Value);
        Assert.Equal(Reading<bool>.Known(true, WifiSource.Source), wifi.Value.BackgroundScanEnabled);
        Assert.Equal(Reading<bool>.Known(false, WifiSource.Source), wifi.Value.StreamingMode);
    }

    [Fact]
    public void Not_connected_is_unknown()
    {
        _client.Connected = false;

        var wifi = Read();

        Assert.Equal(DataStatus.Unknown, wifi.Status);
        Assert.Null(wifi.Value);
    }

    [Fact]
    public void Service_not_running_is_unsupported()
    {
        _client.OpenError = 1062;

        Assert.Equal(DataStatus.NotSupported, Read().Status);
    }

    [Fact]
    public void Missing_wlanapi_is_unsupported()
    {
        var source = new WifiSource(() => throw new DllNotFoundException("wlanapi.dll"), _logger);

        Assert.Equal(DataStatus.NotSupported, source.Read(_client.InterfaceId.ToString()).Status);
    }

    [Theory]
    [InlineData(typeof(DllNotFoundException))]
    [InlineData(typeof(EntryPointNotFoundException))]
    public void Missing_wlanapi_on_open_is_unsupported(Type exceptionType)
    {
        _client.OpenException = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.Equal(DataStatus.NotSupported, Read().Status);
    }

    [Fact]
    public void Open_error_is_failure_with_code()
    {
        _client.OpenError = 87;

        var wifi = Read();

        Assert.Equal(DataStatus.Failed, wifi.Status);
        Assert.Contains("87", wifi.Note);
    }

    [Fact]
    public void Enumeration_error_is_failure_with_code()
    {
        _client.EnumError = 1450;

        var wifi = Read();

        Assert.Equal(DataStatus.Failed, wifi.Status);
        Assert.Contains("1450", wifi.Note);
    }

    [Fact]
    public void Access_denied_on_one_field_keeps_others()
    {
        _client.Fail(WlanOpcode.Rssi, 5);

        var wifi = Read();

        Assert.Equal(DataStatus.Available, wifi.Status);
        Assert.Equal(Reading<int>.Unknown(WifiSource.Source, WifiSource.AccessDeniedNote), wifi.Value!.RssiDbm);
        Assert.Equal(Reading<int>.Known(6, WifiSource.Source), wifi.Value.Channel);
        Assert.True(wifi.Value.Band.IsAvailable);
        Assert.Equal(Reading<bool>.Known(true, WifiSource.Source), wifi.Value.BackgroundScanEnabled);
        Assert.Equal(Reading<bool>.Known(false, WifiSource.Source), wifi.Value.StreamingMode);
    }

    [Fact]
    public void Channel_query_error_leaves_channel_and_band_unknown()
    {
        _client.Fail(WlanOpcode.ChannelNumber, 50);

        var wifi = Read();

        Assert.Equal(DataStatus.Available, wifi.Status);
        Assert.Equal(Reading<int>.Unknown(WifiSource.Source, "WLAN query error 50"), wifi.Value!.Channel);
        Assert.Equal(DataStatus.Unknown, wifi.Value.Band.Status);
        Assert.Equal(Reading<int>.Known(-55, WifiSource.Source), wifi.Value.RssiDbm);
    }

    [Fact]
    public void Any_nonzero_bool_is_true()
    {
        _client.Set(WlanOpcode.BackgroundScanEnabled, -1).Set(WlanOpcode.MediaStreamingMode, 2);

        var wifi = Read();

        Assert.Equal(Reading<bool>.Known(true, WifiSource.Source), wifi.Value!.BackgroundScanEnabled);
        Assert.Equal(Reading<bool>.Known(true, WifiSource.Source), wifi.Value.StreamingMode);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("{6B29FC40-CA47-1067-B31D-00DD010662DA}")]
    public void Unknown_adapter_id_is_unknown(string adapterId)
    {
        var wifi = new WifiSource(() => _client, _logger).Read(adapterId);

        Assert.Equal(DataStatus.Unknown, wifi.Status);
        Assert.Null(wifi.Value);
    }

    [Fact]
    public void Unexpected_exception_is_failure_and_logged()
    {
        _client.QueryException = new InvalidOperationException("query broke");

        var wifi = Read();

        Assert.Equal(DataStatus.Failed, wifi.Status);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("query broke", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("connected")]
    [InlineData("open error")]
    [InlineData("open throws")]
    [InlineData("not connected")]
    [InlineData("unknown interface")]
    [InlineData("query throws")]
    public void Client_is_disposed_after_read(string path)
    {
        var adapterId = _client.InterfaceId.ToString();
        switch (path)
        {
            case "open error":
                _client.OpenError = 1062;
                break;
            case "open throws":
                _client.OpenException = new DllNotFoundException("wlanapi.dll");
                break;
            case "not connected":
                _client.Connected = false;
                break;
            case "unknown interface":
                adapterId = Guid.NewGuid().ToString();
                break;
            case "query throws":
                _client.QueryException = new InvalidOperationException("query broke");
                break;
        }

        new WifiSource(() => _client, _logger).Read(adapterId);

        Assert.True(_client.IsDisposed);
    }

    // Раскладка wlanapi.h (одинакова для x86 и x64): GUID, WCHAR[256], WLAN_INTERFACE_STATE; список — два DWORD, затем массив.
    // WlanClient читает структуры по указателю, поэтому важна раскладка в памяти, а не маршалинга.
    [Fact]
    public void Native_layouts_match_the_headers()
    {
        var list = default(WLAN_INTERFACE_INFO_LIST);
        ref var info = ref list.InterfaceInfo[0];

        Assert.Equal(532, Unsafe.SizeOf<WLAN_INTERFACE_INFO>());
        Assert.Equal(528, Offset(ref info, ref info.isState));
        Assert.Equal(8, Offset(ref list, ref info));
        Assert.Equal(540, Unsafe.SizeOf<WLAN_INTERFACE_INFO_LIST>());
    }

    private static int Offset<TStruct, TField>(ref TStruct owner, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TStruct, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));

    private Reading<WifiStatus> Read() => new WifiSource(() => _client, _logger).Read(_client.InterfaceId.ToString());
}
