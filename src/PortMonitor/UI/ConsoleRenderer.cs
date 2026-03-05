using PortMonitor.Models;
using System.Text;

namespace PortMonitor.UI;

/// <summary>
/// Renders the connection table to the terminal using in-place cursor repositioning
/// (no flicker from Console.Clear) and ANSI-style <see cref="ConsoleColor"/> coloring.
/// </summary>
public class ConsoleRenderer
{
    private readonly AppState  _state;
    private readonly int       _interval;
    private readonly string?   _logPath;
    private readonly object    _renderLock = new();

    // Column widths (characters)
    private const int TagW      = 6;   // "[NEW] " or "[CLS] " or "      "
    private const int ProtoW    = 5;
    private const int AddrW     = 16;
    private const int PortW     = 7;
    private const int StateW    = 14;
    private const int PidW      = 7;
    private const int ProcessW  = 18;

    private static readonly string Separator = new('-', 100);

    public ConsoleRenderer(AppState state, int interval, string? logPath)
    {
        _state    = state;
        _interval = interval;
        _logPath  = logPath;
    }

    /// <summary>Renders the full UI to the terminal for the supplied connection list.</summary>
    /// <param name="entries">Merged, diff-annotated connection list from <see cref="Services.DiffEngine"/>.</param>
    public void Render(IReadOnlyList<ConnectionEntry> entries)
    {
        lock (_renderLock)
        {
            // ── Apply view filter ────────────────────────────────────────────
            var filtered = ApplyFilter(entries);

            // ── Apply sort ───────────────────────────────────────────────────
            var sorted = ApplySort(filtered);

            // ── Pagination ───────────────────────────────────────────────────
            _state.PageSize = Math.Max(5, Console.WindowHeight - 8);
            int totalPages  = Math.Max(0, (sorted.Count - 1) / _state.PageSize);
            _state.MaxPage  = totalPages;
            if (_state.Page > totalPages) _state.Page = totalPages;

            var page = sorted
                .Skip(_state.Page * _state.PageSize)
                .Take(_state.PageSize)
                .ToList();

            // ── Suppress cursor movement flicker ─────────────────────────────
            Console.SetCursorPosition(0, 0);

            // ── Header ───────────────────────────────────────────────────────
            RenderHeader(entries.Count, sorted.Count, totalPages);

            // ── Column headers ───────────────────────────────────────────────
            RenderColumnHeaders();

            // ── Rows ─────────────────────────────────────────────────────────
            foreach (var entry in page)
                RenderRow(entry);

            // ── Pad remaining lines if page shrank ───────────────────────────
            int usedRows = page.Count;
            int maxRows  = _state.PageSize;
            for (int i = usedRows; i < maxRows; i++)
                WriteBlankLine();

            // ── Footer ───────────────────────────────────────────────────────
            RenderFooter();

            // ── Log new/closed events ─────────────────────────────────────────
            if (_logPath is not null)
                LogEvents(entries);
        }
    }

    // ── Private rendering helpers ────────────────────────────────────────────

    private void RenderHeader(int total, int filtered, int totalPages)
    {
        int    conn     = filtered;
        string filter   = _state.Filter.Length > 0 ? $"Filter: {_state.Filter}" : "Filter: none";
        string viewMode = _state.View switch
        {
            ViewMode.ListenOnly      => "LISTEN",
            ViewMode.EstablishedOnly => "ESTABLISHED",
            _                        => "ALL"
        };
        string sort    = $"Sort: {_state.Sort}";
        string page    = totalPages > 0 ? $"  Page {_state.Page + 1}/{totalPages + 1}" : string.Empty;
        string header  = $"  PortMonitor v1.0  |  {DateTime.Now:HH:mm:ss}  |  Refresh: {_interval}s  " +
                         $"|  {filter}  |  View: {viewMode}  |  {conn} conn  |  {sort}{page}  ";

        Console.ForegroundColor = ConsoleColor.Cyan;
        WriteFullLine("╔" + new string('═', Math.Max(0, Console.WindowWidth - 2)) + "╗");
        WriteFullLine("║" + header.PadRight(Console.WindowWidth - 2) + "║");
        WriteFullLine("╚" + new string('═', Math.Max(0, Console.WindowWidth - 2)) + "╝");
        Console.ResetColor();
    }

    private void RenderColumnHeaders()
    {
        Console.ForegroundColor = ConsoleColor.White;
        var sb = new StringBuilder();
        sb.Append("Tag   ");
        sb.Append("Proto".PadRight(ProtoW));
        sb.Append("Local Address".PadRight(AddrW));
        sb.Append("L.Port".PadRight(PortW));
        sb.Append("Remote Address".PadRight(AddrW));
        sb.Append("R.Port".PadRight(PortW));
        sb.Append("State".PadRight(StateW));
        sb.Append("PID".PadRight(PidW));
        sb.Append("Process".PadRight(ProcessW));
        WriteFullLine(sb.ToString());
        WriteFullLine(Separator);
        Console.ResetColor();
    }

    private void RenderRow(ConnectionEntry e)
    {
        // Choose row color
        ConsoleColor rowColor = e.IsClosed ? ConsoleColor.DarkGray :
                                e.IsNew    ? ConsoleColor.White     :
                                StateColor(e.State);

        Console.ForegroundColor = rowColor;

        // Tag
        string tag = e.IsNew    ? "[NEW] " :
                     e.IsClosed ? "[CLS] " :
                                  "      ";

        var sb = new StringBuilder();
        sb.Append(tag);
        sb.Append(e.Protocol.PadRight(ProtoW));
        sb.Append(Truncate(e.LocalAddress,  AddrW));
        sb.Append(e.LocalPort.ToString().PadRight(PortW));
        sb.Append(Truncate(e.RemoteAddress, AddrW));
        string rport = e.RemotePort > 0 ? e.RemotePort.ToString() : (e.State == ConnectionState.Udp ? "*" : "0");
        sb.Append(rport.PadRight(PortW));
        sb.Append(Truncate(e.StateDisplay,  StateW));
        sb.Append((e.Pid > 0 ? e.Pid.ToString() : "-").PadRight(PidW));
        sb.Append(Truncate(e.ProcessName,   ProcessW));

        WriteFullLine(sb.ToString());
        Console.ResetColor();
    }

    private void RenderFooter()
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        WriteFullLine(new string('─', Math.Min(80, Console.WindowWidth - 1)));

        if (_state.IsEnteringFilter)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            WriteFullLine($"  Filter: {_state.FilterInput}_   (Enter=confirm  Esc=cancel)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            WriteFullLine("  [F]ilter  [S]ort  [L]isten-only  [E]stablished-only  [R]eset  [Q]uit  [↑↓] Scroll");
        }

        Console.ResetColor();
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    private List<ConnectionEntry> ApplyFilter(IReadOnlyList<ConnectionEntry> entries)
    {
        var list = entries.AsEnumerable();

        // View mode filter
        list = _state.View switch
        {
            ViewMode.ListenOnly      => list.Where(e => e.State == ConnectionState.Listen),
            ViewMode.EstablishedOnly => list.Where(e => e.State == ConnectionState.Established),
            _                        => list
        };

        // Text filter
        if (_state.Filter.Length > 0)
        {
            string f = _state.Filter.ToUpperInvariant();
            list = list.Where(e =>
                e.LocalAddress.Contains(f,  StringComparison.OrdinalIgnoreCase) ||
                e.RemoteAddress.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.LocalPort.ToString().Contains(f)                              ||
                e.RemotePort.ToString().Contains(f)                             ||
                e.ProcessName.Contains(f,   StringComparison.OrdinalIgnoreCase) ||
                e.StateDisplay.Contains(f,  StringComparison.OrdinalIgnoreCase) ||
                e.Pid.ToString().Contains(f));
        }

        return list.ToList();
    }

    // ── Sorting ──────────────────────────────────────────────────────────────

    private List<ConnectionEntry> ApplySort(List<ConnectionEntry> entries)
    {
        return _state.Sort switch
        {
            SortColumn.Pid     => entries.OrderBy(e => e.Pid).ToList(),
            SortColumn.State   => entries.OrderBy(e => e.StateDisplay).ToList(),
            SortColumn.Process => entries.OrderBy(e => e.ProcessName).ToList(),
            _                  => entries.OrderBy(e => e.LocalPort).ToList() // Port (default)
        };
    }

    // ── Logging ──────────────────────────────────────────────────────────────

    private void LogEvents(IReadOnlyList<ConnectionEntry> entries)
    {
        var events = entries.Where(e => e.IsNew || (e.IsClosed && e.ClosedCycles == 1));
        if (!events.Any()) return;

        try
        {
            using var sw = File.AppendText(_logPath!);
            foreach (var e in events)
            {
                string tag  = e.IsNew ? "NEW   " : "CLOSED";
                sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {tag}  {e.Protocol,-4}" +
                             $"  {e.LocalAddress}:{e.LocalPort,-6}" +
                             $"  → {e.RemoteAddress}:{e.RemotePort,-6}" +
                             $"  {e.StateDisplay,-14}  PID:{e.Pid}  {e.ProcessName}");
            }
        }
        catch { /* ignore log write failures */ }
    }

    // ── Terminal helpers ─────────────────────────────────────────────────────

    private static ConsoleColor StateColor(ConnectionState state) => state switch
    {
        ConnectionState.Listen      => ConsoleColor.Yellow,
        ConnectionState.Established => ConsoleColor.Green,
        ConnectionState.TimeWait    => ConsoleColor.Red,
        ConnectionState.CloseWait   => ConsoleColor.Red,
        ConnectionState.Udp         => ConsoleColor.Cyan,
        ConnectionState.SynSent     => ConsoleColor.Magenta,
        ConnectionState.SynReceived => ConsoleColor.Magenta,
        _                           => ConsoleColor.Gray
    };

    /// <summary>Writes a line padded to the full terminal width to overwrite stale characters.</summary>
    private static void WriteFullLine(string text)
    {
        int width = Console.WindowWidth - 1;
        if (text.Length > width)
            text = text[..width];
        Console.WriteLine(text.PadRight(width));
    }

    private static void WriteBlankLine() => WriteFullLine(string.Empty);

    private static string Truncate(string s, int maxLen)
    {
        if (s.Length >= maxLen)
            return (s[..(maxLen - 1)] + "…").PadRight(maxLen);
        return s.PadRight(maxLen);
    }
}
