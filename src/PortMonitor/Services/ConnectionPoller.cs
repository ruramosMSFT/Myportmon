using PortMonitor.Models;
using System.Diagnostics;
using System.Net;

namespace PortMonitor.Services;

/// <summary>
/// Polls the OS for all active TCP connections and UDP endpoints, resolving
/// each owning PID to a process name.
/// </summary>
public class ConnectionPoller
{
    private readonly Dictionary<int, string> _processCache = new();
    private readonly Dictionary<string, string> _dnsCache = new();
    private DateTime _cacheExpiry = DateTime.MinValue;

    /// <summary>
    /// Returns a snapshot of all current TCP and UDP connections with resolved process names.
    /// </summary>
    public IReadOnlyList<ConnectionEntry> Poll()
    {
        RefreshProcessCacheIfNeeded();

        // Use a dict keyed on the connection key to naturally deduplicate
        // entries the OS may report more than once (common with UDP sockets).
        var seen    = new HashSet<string>();
        var entries = new List<ConnectionEntry>();

        // TCP ----------------------------------------------------------------
        foreach (var tcp in IpHelper.GetTcpConnections())
        {
            var entry = new ConnectionEntry
            {
                Protocol      = "TCP",
                LocalAddress  = tcp.LocalAddr,
                LocalPort     = tcp.LocalPort,
                RemoteAddress = tcp.RemoteAddr,
                RemotePort    = tcp.RemotePort,
                State         = IpHelper.MapTcpState(tcp.State),
                Pid           = tcp.Pid,
                ProcessName   = ResolveProcessName(tcp.Pid),
                RemoteHost    = ResolveDns(tcp.RemoteAddr)
            };
            if (seen.Add(entry.Key))
                entries.Add(entry);
        }

        // UDP ----------------------------------------------------------------
        foreach (var udp in IpHelper.GetUdpListeners())
        {
            var entry = new ConnectionEntry
            {
                Protocol      = "UDP",
                LocalAddress  = udp.LocalAddr,
                LocalPort     = udp.LocalPort,
                RemoteAddress = "*",
                RemotePort    = 0,
                State         = ConnectionState.Udp,
                Pid           = udp.Pid,
                ProcessName   = ResolveProcessName(udp.Pid),
                RemoteHost    = string.Empty
            };
            if (seen.Add(entry.Key))
                entries.Add(entry);
        }

        return entries;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>Refreshes the PID→name cache every 5 seconds to reduce overhead.</summary>
    private void RefreshProcessCacheIfNeeded()
    {
        if (DateTime.UtcNow < _cacheExpiry) return;

        _processCache.Clear();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try { _processCache[proc.Id] = proc.ProcessName; }
                catch { /* individual process may have exited */ }
                finally { proc.Dispose(); }
            }
        }
        catch { /* access denied at enumeration level */ }

        _cacheExpiry = DateTime.UtcNow.AddSeconds(5);
    }

    /// <summary>
    /// Resolves a PID to a process name. Returns "[N/A]" if access is denied.
    /// </summary>
    private string ResolveProcessName(int pid)
    {
        if (pid == 0) return "Idle";
        if (pid == 4) return "System";

        if (_processCache.TryGetValue(pid, out string? name))
            return name;

        try
        {
            using var proc = Process.GetProcessById(pid);
            _processCache[pid] = proc.ProcessName;
            return proc.ProcessName;
        }
        catch
        {
            _processCache[pid] = "[N/A]";
            return "[N/A]";
        }
    }

    /// <summary>
    /// Reverse-DNS lookup with persistent cache. Returns the FQDN or empty string.
    /// Skips non-routable addresses (0.0.0.0, 127.*, *).
    /// </summary>
    private string ResolveDns(string ip)
    {
        if (string.IsNullOrEmpty(ip) || ip == "*" || ip == "0.0.0.0" || ip.StartsWith("127."))
            return string.Empty;

        if (_dnsCache.TryGetValue(ip, out string? cached))
            return cached;

        try
        {
            var entry = Dns.GetHostEntry(ip);
            string host = entry.HostName;
            // If GetHostEntry just returns the IP back, store empty
            if (host == ip) host = string.Empty;
            _dnsCache[ip] = host;
            return host;
        }
        catch
        {
            _dnsCache[ip] = string.Empty;
            return string.Empty;
        }
    }
}
