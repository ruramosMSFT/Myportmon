using System.Net;
using System.Runtime.InteropServices;
using PortMonitor.Models;

namespace PortMonitor.Services;

/// <summary>
/// P/Invoke wrapper around iphlpapi.dll providing TCP and UDP connection tables
/// with owning-PID information — something .NET's IPGlobalProperties does not expose.
/// </summary>
internal static class IpHelper
{
    // ── Win32 enumerations ──────────────────────────────────────────────────

    private enum TcpTableClass
    {
        TcpTableBasicListener,
        TcpTableBasicConnections,
        TcpTableBasicAll,
        TcpTableOwnerPidListener,
        TcpTableOwnerPidConnections,
        TcpTableOwnerPidAll,
        TcpTableOwnerModuleListener,
        TcpTableOwnerModuleConnections,
        TcpTableOwnerModuleAll
    }

    private enum UdpTableClass
    {
        UdpTableBasic,
        UdpTableOwnerPid,
        UdpTableOwnerModule
    }

    // ── Win32 structs ───────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    // ── P/Invoke declarations ───────────────────────────────────────────────

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        TcpTableClass tblClass,
        uint reserved = 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        UdpTableClass tblClass,
        uint reserved = 0);

    private const int AfInet = 2;      // IPv4

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves all active TCP connections and listeners with their owning PID.
    /// </summary>
    /// <returns>Sequence of (State, LocalAddr, LocalPort, RemoteAddr, RemotePort, Pid).</returns>
    public static IEnumerable<(uint State, string LocalAddr, int LocalPort,
                                string RemoteAddr, int RemotePort, int Pid)> GetTcpConnections()
    {
        int bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableClass.TcpTableOwnerPidAll);

        IntPtr table = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedTcpTable(table, ref bufferSize, true, AfInet, TcpTableClass.TcpTableOwnerPidAll);
            if (result != 0) yield break;

            int numEntries = Marshal.ReadInt32(table);
            IntPtr rowPtr  = table + 4;
            int    rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (int i = 0; i < numEntries; i++)
            {
                var row       = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                var localAddr = new IPAddress(row.dwLocalAddr).ToString();
                var remoteAddr= new IPAddress(row.dwRemoteAddr).ToString();
                int localPort = NetworkToHostPort(row.dwLocalPort);
                int remotePort= NetworkToHostPort(row.dwRemotePort);

                yield return (row.dwState, localAddr, localPort, remoteAddr, remotePort, (int)row.dwOwningPid);
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    /// <summary>
    /// Retrieves all active UDP listeners with their owning PID.
    /// </summary>
    /// <returns>Sequence of (LocalAddr, LocalPort, Pid).</returns>
    public static IEnumerable<(string LocalAddr, int LocalPort, int Pid)> GetUdpListeners()
    {
        int bufferSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AfInet, UdpTableClass.UdpTableOwnerPid);

        IntPtr table = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedUdpTable(table, ref bufferSize, true, AfInet, UdpTableClass.UdpTableOwnerPid);
            if (result != 0) yield break;

            int numEntries = Marshal.ReadInt32(table);
            IntPtr rowPtr  = table + 4;
            int    rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();

            for (int i = 0; i < numEntries; i++)
            {
                var row       = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPtr);
                var localAddr = new IPAddress(row.dwLocalAddr).ToString();
                int localPort = NetworkToHostPort(row.dwLocalPort);

                yield return (localAddr, localPort, (int)row.dwOwningPid);
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    /// <summary>Maps an iphlpapi TCP state code to a <see cref="ConnectionState"/> value.</summary>
    public static ConnectionState MapTcpState(uint state) => state switch
    {
        1  => ConnectionState.Closed,
        2  => ConnectionState.Listen,
        3  => ConnectionState.SynSent,
        4  => ConnectionState.SynReceived,
        5  => ConnectionState.Established,
        6  => ConnectionState.FinWait1,
        7  => ConnectionState.FinWait2,
        8  => ConnectionState.CloseWait,
        9  => ConnectionState.Closing,
        10 => ConnectionState.LastAck,
        11 => ConnectionState.TimeWait,
        12 => ConnectionState.DeleteTcb,
        _  => ConnectionState.Unknown
    };

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Converts a DWORD port value from network byte order to host byte order.</summary>
    private static int NetworkToHostPort(uint dwPort) =>
        (int)(((dwPort & 0xFF) << 8) | ((dwPort >> 8) & 0xFF));
}
