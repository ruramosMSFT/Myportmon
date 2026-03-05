using PortMonitor.Services;
using PortMonitor.UI;
using System.Security.Principal;

// ── --help ──────────────────────────────────────────────────────────────────
if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

// ── Parse CLI arguments ──────────────────────────────────────────────────────
int interval  = 2;
string? logPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--interval" && i + 1 < args.Length && int.TryParse(args[i + 1], out int iv))
        interval = Math.Max(1, iv);
    else if (args[i] == "--log" && i + 1 < args.Length)
        logPath = args[i + 1];
}

// ── Admin elevation check ────────────────────────────────────────────────────
bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
    .IsInRole(WindowsBuiltInRole.Administrator);

if (!isAdmin)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("WARNING: Not running as Administrator.");
    Console.WriteLine("         Some process names will appear as [N/A].");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Press any key to continue (or run as Administrator for full info)...");
    Console.ReadKey(true);
}

// ── Terminal setup ───────────────────────────────────────────────────────────
Console.CursorVisible = false;
Console.Clear();

// ── Services & UI ────────────────────────────────────────────────────────────
var poller   = new ConnectionPoller();
var diff     = new DiffEngine();
var state    = new AppState { PageSize = Math.Max(5, Console.WindowHeight - 8) };
var renderer = new ConsoleRenderer(state, interval, logPath);

using var cts = new CancellationTokenSource();

// ── Key-input task (non-blocking) ────────────────────────────────────────────
var keyTask = Task.Run(() =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        if (!Console.KeyAvailable)
        {
            Thread.Sleep(50);
            continue;
        }

        var key = Console.ReadKey(true);

        // Filter-entry mode
        if (state.IsEnteringFilter)
        {
            HandleFilterInput(key, state);
            continue;
        }

        switch (key.Key)
        {
            case ConsoleKey.Q:
                cts.Cancel();
                break;

            case ConsoleKey.F:
                state.IsEnteringFilter = true;
                state.FilterInput = state.Filter;
                break;

            case ConsoleKey.S:
                state.CycleSortColumn();
                break;

            case ConsoleKey.L:
                state.View = state.View == ViewMode.ListenOnly
                    ? ViewMode.All
                    : ViewMode.ListenOnly;
                state.Page = 0;
                break;

            case ConsoleKey.E:
                state.View = state.View == ViewMode.EstablishedOnly
                    ? ViewMode.All
                    : ViewMode.EstablishedOnly;
                state.Page = 0;
                break;

            case ConsoleKey.R:
                state.ResetFilters();
                diff.Reset();
                break;

            case ConsoleKey.UpArrow:
                state.ScrollUp();
                break;

            case ConsoleKey.DownArrow:
                state.ScrollDown();
                break;
        }
    }
}, cts.Token);

// ── Main polling loop ────────────────────────────────────────────────────────
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var raw     = poller.Poll();
        var entries = diff.Apply(raw);
        renderer.Render(entries);

        await Task.Delay(interval * 1000, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Normal exit via Q key
}
catch (Exception ex)
{
    Console.ResetColor();
    Console.CursorVisible = true;
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Fatal error: {ex.Message}");
    Console.ResetColor();
    return 1;
}

// ── Cleanup ──────────────────────────────────────────────────────────────────
Console.CursorVisible = true;
Console.ResetColor();
Console.Clear();
Console.WriteLine("PortMonitor closed.");
return 0;

// ── Local functions ──────────────────────────────────────────────────────────

static void HandleFilterInput(ConsoleKeyInfo key, AppState s)
{
    switch (key.Key)
    {
        case ConsoleKey.Escape:
            s.IsEnteringFilter = false;
            s.FilterInput = string.Empty;
            break;

        case ConsoleKey.Enter:
            s.Filter = s.FilterInput;
            s.IsEnteringFilter = false;
            s.FilterInput = string.Empty;
            s.Page = 0;
            break;

        case ConsoleKey.Backspace:
            if (s.FilterInput.Length > 0)
                s.FilterInput = s.FilterInput[..^1];
            break;

        default:
            if (!char.IsControl(key.KeyChar))
                s.FilterInput += key.KeyChar;
            break;
    }
}

static void PrintHelp()
{
    Console.WriteLine("PortMonitor v1.0 — Real-time interactive Windows port monitor");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  portmonitor [--interval <seconds>] [--log <path>] [--help]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --interval <n>   Refresh interval in seconds (default: 2, min: 1)");
    Console.WriteLine("  --log <path>     Append new/closed events to the specified log file");
    Console.WriteLine("  --help, -h       Show this help message and exit");
    Console.WriteLine();
    Console.WriteLine("Keyboard shortcuts (while running):");
    Console.WriteLine("  F         Enter a filter string (matches IP, port, process, state)");
    Console.WriteLine("  S         Cycle sort column: Port → PID → State → Process");
    Console.WriteLine("  L         Toggle LISTEN-only view");
    Console.WriteLine("  E         Toggle ESTABLISHED-only view");
    Console.WriteLine("  R         Reset all filters and sort");
    Console.WriteLine("  Q         Quit");
    Console.WriteLine("  ↑ / ↓     Scroll pages");
    Console.WriteLine();
    Console.WriteLine("Notes:");
    Console.WriteLine("  Run as Administrator for full PID-to-process-name resolution.");
    Console.WriteLine("  New connections are highlighted with [NEW], recently closed with [CLS].");
}
