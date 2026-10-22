using System.Net;
using System.Net.Sockets;
using Booster.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;

namespace Booster.Platform.Windows.Providers.GameNetwork;

public sealed unsafe class TcpConnectionSource : ITcpConnectionSource
{
    private const string Source = "GetExtendedTcpTable";
    private const int MaxAttempts = 5;

    public Reading<IReadOnlyList<TcpConnection>> Read()
    {
        var v4 = ReadFamily(AddressFamily.InterNetwork);
        var v6 = ReadFamily(AddressFamily.InterNetworkV6);
        if (v4.Rows is null && v6.Rows is null)
            return Reading<IReadOnlyList<TcpConnection>>.Failure(Source,
                $"GetExtendedTcpTable failed for both address families (IPv4 {v4.Error}, IPv6 {v6.Error})");

        IReadOnlyList<TcpConnection> all = [.. v4.Rows ?? [], .. v6.Rows ?? []];
        return Reading<IReadOnlyList<TcpConnection>>.Known(all, Source);
    }

    private static (List<TcpConnection>? Rows, uint Error) ReadFamily(AddressFamily family)
    {
        var ipv6 = family == AddressFamily.InterNetworkV6;
        uint size = 0;
        uint error = 0;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var buffer = new byte[Math.Max(size, 4)];
            fixed (byte* table = buffer)
            {
                size = (uint)buffer.Length;
                error = PInvoke.GetExtendedTcpTable(buffer, ref size, false, (uint)family,
                    TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
                if (error == (uint)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                    continue;
                if (error != 0)
                    return (null, error);
                return (ipv6 ? ParseV6((MIB_TCP6TABLE_OWNER_PID*)table) : ParseV4((MIB_TCPTABLE_OWNER_PID*)table), 0);
            }
        }

        return (null, error);
    }

    private static List<TcpConnection> ParseV4(MIB_TCPTABLE_OWNER_PID* table)
    {
        var result = new List<TcpConnection>((int)table->dwNumEntries);
        var rows = (MIB_TCPROW_OWNER_PID*)&table->table;
        for (var i = 0; i < table->dwNumEntries; i++)
        {
            var row = rows[i];
            var address = new IPAddress(row.dwRemoteAddr);
            result.Add(new TcpConnection((int)row.dwOwningPid, address, Port(row.dwRemotePort), row.dwState == MIB_TCP_STATE.MIB_TCP_STATE_ESTAB));
        }

        return result;
    }

    private static List<TcpConnection> ParseV6(MIB_TCP6TABLE_OWNER_PID* table)
    {
        var result = new List<TcpConnection>((int)table->dwNumEntries);
        var rows = (MIB_TCP6ROW_OWNER_PID*)&table->table;
        for (var i = 0; i < table->dwNumEntries; i++)
        {
            var row = rows[i];
            // Без ScopeId: адреса из событий ETW его не несут, а IPAddress.Equals его сравнивает (как в TrafficFlowAggregator).
            var address = new IPAddress(new ReadOnlySpan<byte>(row.ucRemoteAddr.Value, 16));
            result.Add(new TcpConnection((int)row.dwOwningPid, address, Port(row.dwRemotePort), row.dwState == MIB_TCP_STATE.MIB_TCP_STATE_ESTAB));
        }

        return result;
    }

    private static int Port(uint networkOrder) => (ushort)((networkOrder & 0xFF) << 8 | (networkOrder >> 8) & 0xFF);
}
