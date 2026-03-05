using PortMonitor.Models;

namespace PortMonitor.Services;

/// <summary>
/// Detects new and closed connections by comparing consecutive poll snapshots.
/// Closed entries are retained for up to 2 additional poll cycles before being
/// removed from the display (fade-out effect).
/// </summary>
public class DiffEngine
{
    /// <summary>Most recent live snapshot, used to detect newly-opened connections.</summary>
    private Dictionary<string, ConnectionEntry> _active = new();

    /// <summary>Connections currently in the fade-out queue, keyed by their connection key.</summary>
    private Dictionary<string, ConnectionEntry> _closed = new();

    /// <summary>
    /// Merges the latest poll snapshot with the previous one, annotating entries
    /// as <see cref="ConnectionEntry.IsNew"/> or <see cref="ConnectionEntry.IsClosed"/>.
    /// </summary>
    /// <param name="current">The latest poll snapshot from <see cref="ConnectionPoller"/>.</param>
    /// <returns>
    /// A merged list containing all live connections (marked [NEW] where applicable)
    /// plus recently-closed connections (marked [CLOSED], fading after 2 cycles).
    /// </returns>
    public IReadOnlyList<ConnectionEntry> Apply(IReadOnlyList<ConnectionEntry> current)
    {
        // Build a dedup-safe dictionary — in case the OS returns duplicates
        // that were not caught earlier (e.g. UDP|0.0.0.0|3702|*|0).
        var currentMap = new Dictionary<string, ConnectionEntry>(current.Count);
        foreach (var e in current)
            currentMap.TryAdd(e.Key, e);   // silently skip any exact-duplicate key
        var result     = new List<ConnectionEntry>(current.Count + 8);

        // ── Add live entries, marking brand-new ones ──────────────────────────
        foreach (var entry in current)
        {
            if (!_active.ContainsKey(entry.Key))
                entry.IsNew = true;

            result.Add(entry);
        }

        // ── Purge from fade-out queue any entries that came back to life ──────
        foreach (var key in currentMap.Keys)
            _closed.Remove(key);

        // ── Advance existing fade-out entries by one cycle ────────────────────
        var nextClosed = new Dictionary<string, ConnectionEntry>();

        foreach (var prev in _closed.Values)
        {
            if (prev.ClosedCycles >= 2)
                continue; // fully faded — drop

            var advanced = new ConnectionEntry
            {
                Protocol      = prev.Protocol,
                LocalAddress  = prev.LocalAddress,
                LocalPort     = prev.LocalPort,
                RemoteAddress = prev.RemoteAddress,
                RemotePort    = prev.RemotePort,
                State         = prev.State,
                Pid           = prev.Pid,
                ProcessName   = prev.ProcessName,
                RemoteHost    = prev.RemoteHost,
                IsClosed      = true,
                ClosedCycles  = prev.ClosedCycles + 1
            };
            result.Add(advanced);
            nextClosed[advanced.Key] = advanced;
        }

        // ── Detect newly-closed entries (were active, not in current poll) ────
        foreach (var prev in _active.Values)
        {
            if (currentMap.ContainsKey(prev.Key))
                continue; // still alive

            var closed = new ConnectionEntry
            {
                Protocol      = prev.Protocol,
                LocalAddress  = prev.LocalAddress,
                LocalPort     = prev.LocalPort,
                RemoteAddress = prev.RemoteAddress,
                RemotePort    = prev.RemotePort,
                State         = prev.State,
                Pid           = prev.Pid,
                ProcessName   = prev.ProcessName,
                RemoteHost    = prev.RemoteHost,
                IsClosed      = true,
                ClosedCycles  = 1
            };
            result.Add(closed);
            nextClosed[closed.Key] = closed;
        }

        // ── Advance state ─────────────────────────────────────────────────────
        _active = currentMap;
        _closed = nextClosed;

        return result;
    }

    /// <summary>Resets diff state, clearing all previously-seen and fade-out entries.</summary>
    public void Reset()
    {
        _active.Clear();
        _closed.Clear();
    }
}
