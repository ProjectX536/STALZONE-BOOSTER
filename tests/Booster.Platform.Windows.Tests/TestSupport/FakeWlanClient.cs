using Booster.Platform.Windows.Providers.GameNetwork;
using Windows.Win32.Foundation;

namespace Booster.Platform.Windows.Tests.TestSupport;

public sealed class FakeWlanClient : IWlanClient
{
    private readonly Dictionary<WlanOpcode, (uint Error, int Value)> _queries = [];

    public Guid InterfaceId { get; } = Guid.NewGuid();

    public bool Connected { get; set; } = true;

    public uint OpenError { get; set; }

    public Exception? OpenException { get; set; }

    public uint EnumError { get; set; }

    public Exception? QueryException { get; set; }

    public bool IsDisposed { get; private set; }

    public FakeWlanClient Set(WlanOpcode opcode, int value)
    {
        _queries[opcode] = (0, value);
        return this;
    }

    public FakeWlanClient Fail(WlanOpcode opcode, uint error)
    {
        _queries[opcode] = (error, 0);
        return this;
    }

    public uint Open() => OpenException is null ? OpenError : throw OpenException;

    public uint IsConnected(Guid interfaceId, out bool connected)
    {
        connected = false;
        if (EnumError != 0)
            return EnumError;
        if (interfaceId != InterfaceId)
            return (uint)WIN32_ERROR.ERROR_NOT_FOUND;

        connected = Connected;
        return 0;
    }

    public uint Query(Guid interfaceId, WlanOpcode opcode, out int value)
    {
        if (QueryException is not null)
            throw QueryException;

        value = 0;
        if (interfaceId != InterfaceId)
            return (uint)WIN32_ERROR.ERROR_INVALID_PARAMETER;
        if (!_queries.TryGetValue(opcode, out var query))
            return (uint)WIN32_ERROR.ERROR_NOT_SUPPORTED;

        value = query.Value;
        return query.Error;
    }

    public void Dispose() => IsDisposed = true;
}
