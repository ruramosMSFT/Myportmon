using PortMonitor.Models;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, string> _dnsCache = new();
    private readonly HashSet<string> _dnsInFlight = [];
    private DateTime _cacheExpiry = DateTime.MinValue;
    private const int MaxDnsCacheSize = 4096;

    /// <summary>When false, DNS resolution is skipped entirely and RemoteHost is always empty.</summary>
    public bool DnsEnabled { get; set; } = true;

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
                RemoteHost    = DnsEnabled ? GetCachedDns(tcp.RemoteAddr) : string.Empty
            };
            entry.ComputeKey();
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
            entry.ComputeKey();
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
    /// Returns cached DNS result if available, otherwise fires a background resolve.
    /// Never blocks the calling thread.
    /// </summary>
    private string GetCachedDns(string ip)
    {
        if (string.IsNullOrEmpty(ip) || ip == "*" || ip == "0.0.0.0" || ip.StartsWith("127."))
            return string.Empty;

        if (_dnsCache.TryGetValue(ip, out string? cached))
            return cached;

        // Fire-and-forget background resolve — result appears on next poll cycle
        ScheduleDnsResolve(ip);
        return string.Empty;
    }

    /// <summary>Queues an async DNS lookup if not already in-flight.</summary>
    private void ScheduleDnsResolve(string ip)
    {
        lock (_dnsInFlight)
        {
            if (!_dnsInFlight.Add(ip)) return;   // already resolving
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Evict oldest entries if cache is too large
                if (_dnsCache.Count > MaxDnsCacheSize)
                {
                    int toRemove = _dnsCache.Count - MaxDnsCacheSize + 256;
                    foreach (var key in _dnsCache.Keys.Take(toRemove))
                        _dnsCache.TryRemove(key, out _);
                }

                var entry = await Dns.GetHostEntryAsync(ip).ConfigureAwait(false);
                string host = entry.HostName;
                if (host == ip) host = string.Empty;
                _dnsCache[ip] = host;
            }
            catch
            {
                _dnsCache[ip] = string.Empty;
            }
            finally
            {
                lock (_dnsInFlight) { _dnsInFlight.Remove(ip); }
            }
        });
    }
}
