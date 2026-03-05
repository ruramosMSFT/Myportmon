namespace PortMonitor.Models;

/// <summary>TCP/UDP connection states, with a special UDP marker.</summary>
public enum ConnectionState
{
    Unknown,
    Closed,
    Listen,
    SynSent,
    SynReceived,
    Established,
    FinWait1,
    FinWait2,
    CloseWait,
    Closing,
    LastAck,
    TimeWait,
    DeleteTcb,
    Udp
}

/// <summary>Represents a single network connection or endpoint captured during a poll cycle.</summary>
public class ConnectionEntry
{
    /// <summary>Protocol string: "TCP" or "UDP".</summary>
    public string Protocol { get; init; } = string.Empty;

    /// <summary>Local IP address as a string.</summary>
    public string LocalAddress { get; init; } = string.Empty;

    /// <summary>Local port number.</summary>
    public int LocalPort { get; init; }

    /// <summary>Remote IP address as a string, or "*" for UDP.</summary>
    public string RemoteAddress { get; init; } = string.Empty;

    /// <summary>Remote port number, or 0 for UDP/listeners.</summary>
    public int RemotePort { get; init; }

    /// <summary>Connection state.</summary>
    public ConnectionState State { get; init; }

    /// <summary>Owning process ID.</summary>
    public int Pid { get; init; }

    /// <summary>Resolved process name, or "[N/A]" when access is denied.</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Reverse DNS hostname of the remote address, or empty if unresolved.</summary>
    public string RemoteHost { get; set; } = string.Empty;

    // ── Delta tracking ──────────────────────────────────────────────────────

    /// <summary>True when this entry appeared in the latest poll cycle.</summary>
    public bool IsNew { get; set; }

    /// <summary>True when this entry was present last cycle but is now gone.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Number of poll cycles the entry has been in the closed state.</summary>
    public int ClosedCycles { get; set; }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Unique key used for diff comparison across poll cycles.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Must be called after init properties are set to compute the cached key.</summary>
    public void ComputeKey() => Key = $"{Protocol}|{LocalAddress}|{LocalPort}|{RemoteAddress}|{RemotePort}";

    /// <summary>Human-readable state string.</summary>
    public string StateDisplay => State switch
    {
        ConnectionState.Listen      => "LISTEN",
        ConnectionState.Established => "ESTABLISHED",
        ConnectionState.TimeWait    => "TIME_WAIT",
        ConnectionState.CloseWait   => "CLOSE_WAIT",
        ConnectionState.SynSent     => "SYN_SENT",
        ConnectionState.SynReceived => "SYN_RCVD",
        ConnectionState.FinWait1    => "FIN_WAIT1",
        ConnectionState.FinWait2    => "FIN_WAIT2",
        ConnectionState.Closing     => "CLOSING",
        ConnectionState.LastAck     => "LAST_ACK",
        ConnectionState.Closed      => "CLOSED",
        ConnectionState.DeleteTcb   => "DELETE_TCB",
        ConnectionState.Udp         => "UDP",
        _                           => "UNKNOWN"
    };
}
