# PortMonitor — Code Architecture & Design Decisions

This document explains what each part of the codebase does, why it was built this way, and what alternatives were considered.

---

## Table of Contents

1. [High-Level Architecture](#1-high-level-architecture)
2. [Core Library — PortMonitor](#2-core-library--portmonitor)
3. [WPF GUI — PortMonitor.Gui](#3-wpf-gui--portmonitorgui)
4. [CLI Terminal — PortMonitor.Cli](#4-cli-terminal--portmonitorcli)
5. [Design Decisions & Trade-offs](#5-design-decisions--trade-offs)
6. [Alternatives Considered](#6-alternatives-considered)

---

## 1. High-Level Architecture

```
┌──────────────────────────────────────────────────────────┐
│                    Presentation Layer                      │
│  ┌─────────────────┐          ┌─────────────────────┐    │
│  │  PortMonitor.Gui │          │  PortMonitor.Cli     │   │
│  │  (WPF / XAML)    │          │  (Console / Terminal) │   │
│  └────────┬─────────┘          └──────────┬───────────┘   │
│           │                               │               │
│           └───────────┬───────────────────┘               │
│                       ▼                                   │
│  ┌────────────────────────────────────────────────────┐   │
│  │              PortMonitor (Core Library)              │  │
│  │  ConnectionPoller → DiffEngine → ConnectionEntry    │  │
│  │  IpHelper (P/Invoke) · PrerequisiteChecker          │  │
│  └────────────────────────────────────────────────────┘   │
│                       ▼                                   │
│              iphlpapi.dll (Windows OS)                     │
└──────────────────────────────────────────────────────────┘
```

The project follows a **three-layer architecture**: a shared core library does all
the heavy lifting (polling, diffing, process resolution), and two independent
front-ends (GUI and CLI) consume it. This means the business logic is testable
and reusable without any UI dependency.

**Why this split?** A single monolithic WPF app would have been simpler, but
separating the core allows the CLI to exist for headless/scripted use (e.g., SSH
sessions, servers without a desktop), and the unit tests can exercise the
DiffEngine without instantiating any UI.

---

## 2. Core Library — PortMonitor

### 2.1 ConnectionEntry (Models)

**What it does:** A plain data class representing one TCP connection or UDP
endpoint captured during a poll. Contains protocol, addresses, ports, state, PID,
process name, and delta-tracking fields (`IsNew`, `IsClosed`, `ClosedCycles`).

**Key design — `ComputeKey()`:** The `Key` property is a composite string
(`"TCP|192.168.1.1|443|10.0.0.1|52000"`) used as a dictionary key for diffing.
It's computed once and cached rather than recalculated on every property access.

*Why?* The Key is accessed 3-4 times per entry per poll cycle (in `HashSet.Add`,
`Dictionary.ContainsKey`, `Dictionary.TryAdd`, `Dictionary.Remove`). With 400+
connections, that's 1600+ string allocations every 2 seconds. Caching eliminates
this entirely.

*Alternative:* Use a `record` type with value equality — this would give free
structural comparison but requires immutable objects, which conflicts with the
mutable `IsNew`/`IsClosed` fields needed by the diff engine.

### 2.2 IpHelper (P/Invoke)

**What it does:** Wraps Windows' `iphlpapi.dll` to call `GetExtendedTcpTable`
and `GetExtendedUdpTable`. These native APIs return all TCP connections and UDP
endpoints with their **owning PID** — something .NET's built-in
`IPGlobalProperties` class does **not** expose.

**How it works:**
1. First call with `bufferSize=0` to get the required buffer size.
2. Allocate unmanaged memory with `Marshal.AllocHGlobal`.
3. Second call fills the buffer with row data.
4. Marshal each row struct (`MibTcpRowOwnerPid` / `MibUdpRowOwnerPid`) into
   managed tuples.
5. Free the buffer in a `finally` block.

**Retry logic:** Between the sizing call and the data call, the table can grow
(new connections appeared). If the second call returns `ERROR_INSUFFICIENT_BUFFER`
(122), we retry up to 3 times. Without this, the function silently returns zero
results on busy systems.

**Port byte-swapping:** `NetworkToHostPort` swaps the two least-significant bytes
because iphlpapi stores port numbers in network byte order (big-endian) while
.NET uses host byte order (little-endian on x86/x64).

*Why P/Invoke instead of PowerShell cmdlets?* `Get-NetTCPConnection` is a
PowerShell cmdlet that shells out to WMI/CIM — it's ~100x slower per call
(300-500ms vs 3-5ms) and requires PowerShell to be hosted. P/Invoke gives
direct, fast access to the same underlying data.

*Alternative:* Use `netstat -anob` and parse the text output. This would avoid
P/Invoke but is extremely slow (spawns a process, parses text), unreliable
(output format varies by locale), and cannot be polled at 1-second intervals.

### 2.3 ConnectionPoller

**What it does:** Orchestrates a complete poll cycle:
1. Refreshes the PID→process-name cache (every 5 seconds).
2. Calls `IpHelper` for TCP and UDP data.
3. Resolves each PID to a process name.
4. Optionally resolves remote IPs to DNS hostnames.
5. Deduplicates entries by `Key`.

**Process name cache:** `Process.GetProcesses()` returns `Process` objects that
hold native handles. We enumerate once, extract all PID→name mappings, dispose
every `Process` object, and reuse the cache for 5 seconds. Individual lookups
fall back to `Process.GetProcessById` with `using` for proper disposal.

*Why cache?* Without it, 400+ calls to `Process.GetProcessById` per tick would
be expensive and leak handles (each `Process` object holds a native `HANDLE`).

**Async DNS resolution:** DNS lookups are fire-and-forget — `GetCachedDns`
returns immediately (empty string on cache miss) and schedules an async
background task via `Task.Run` + `Dns.GetHostEntryAsync`. The result appears on
the next poll cycle.

*Why async?* Synchronous `Dns.GetHostEntry` blocks for 2-5 seconds per failed
lookup. With 200+ unique IPs, the first poll would freeze the UI for minutes and
allocate hundreds of megabytes in socket buffers and exception objects. The async
approach keeps the UI responsive, and the `ConcurrentDictionary` cache means
each IP is resolved only once.

**DNS cache bounding:** Capped at 4096 entries. When exceeded, the oldest 256
entries are evicted. This prevents unbounded memory growth on servers with
thousands of unique remote IPs.

**`_dnsInFlight` tracking:** A `HashSet<string>` prevents duplicate async lookups
for the same IP. Without this, every poll cycle would fire a new background task
for every uncached IP.

### 2.4 DiffEngine

**What it does:** Compares consecutive poll snapshots to detect new and closed
connections, implementing a "fade-out" effect for closed entries.

**Algorithm per cycle:**
1. Build a map of current entries by `Key`.
2. Mark any entry not in `_active` (previous snapshot) as `IsNew = true`.
3. Remove from `_closed` any entries that reappeared.
4. Advance fade-out: entries in `_closed` get `ClosedCycles++`; drop at ≥2.
5. Detect newly-closed: entries in `_active` but not in current → add to
   `_closed` with `ClosedCycles = 1`.
6. Swap `_active = currentMap`.

**Why 2-cycle fade-out?** A single cycle would flash `[CLS]` for one refresh and
disappear — too fast to notice. Two cycles (at 2s interval = 4 seconds visible)
gives the user time to see what closed. Three+ cycles would clutter the grid
with stale data.

*Alternative:* Use timestamps instead of cycle counts. This would decouple
fade-out from refresh rate but adds complexity and `DateTime.UtcNow` calls.
Cycle-counting is simpler and behaves consistently across all intervals.

### 2.5 PrerequisiteChecker

**What it does:** Checks four requirements (Windows 10+, .NET 8, Administrator
elevation, winget availability) and optionally auto-installs missing components
via `winget`.

**Security:** The `WingetId` is validated against `^[\w.\-]+$` before being
interpolated into a command string, preventing command injection.

*Why not just let it fail?* Network engineers often run this on locked-down
servers where .NET might not be pre-installed. The checker with winget
auto-install saves a manual step.

---

## 3. WPF GUI — PortMonitor.Gui

### 3.1 App.xaml — Theme & Styles

**What it does:** Defines 24 `SolidColorBrush` resources and global styles for
all WPF controls (TextBlock, TextBox, Button, ToggleButton, ComboBox, DataGrid,
MenuItem, etc.) in a VS Code-inspired dark theme.

**Critical decision — `DynamicResource` everywhere:** All brush references in
styles use `{DynamicResource ...}` instead of `{StaticResource ...}`. This means
when `AppSettings.ApplyOne()` replaces a brush in `Application.Resources`, every
control using that key updates immediately — no app restart needed.

*Trade-off:* `DynamicResource` has slightly more overhead than `StaticResource`
(WPF maintains a listener for changes). For this app with <500 controls, the
difference is unmeasurable.

**ComboBox full ControlTemplate:** WPF's default ComboBox inherits Windows system
colors, making it unreadable in dark mode. We replace the entire `ControlTemplate`
with gray backgrounds (`#B8B8B8`/`#D8D8D8`), black bold text, and a custom
toggle button with a chevron path. This is the only way to reliably override
Windows dark mode rendering.

### 3.2 AppSettings — Persistent Configuration

**What it does:** Stores 24 color hex values and boolean flags (currently just
`DnsEnabled`) in a JSON file at `%AppData%\PortMonitor\settings.json`.

**Dual-format loading:** Supports both the current format
(`{ "colors": {...}, "flags": {...} }`) and the legacy flat format
(`{ "BgPrimaryBrush": "#FF...", ... }`) for backward compatibility when users
upgrade.

**`ApplyOne` always replaces:** Instead of mutating an existing brush (which
would throw because WPF freezes brushes after use), it creates a new
`SolidColorBrush` and assigns it to the resource key. Combined with
`DynamicResource`, this gives live color updates.

*Why JSON instead of the registry?* JSON is portable, human-editable, and easy
to reset (delete the file). Registry entries are harder to discover and manage.

### 3.3 MainWindow — Core UI

**What it does:** The main application window with a toolbar (filter + settings),
status bar (connections, refresh time, admin status, CPU%, memory, public IP),
state filter buttons, and a `DataGrid` showing all connections.

**DispatcherTimer:** WPF is single-threaded. The `DispatcherTimer` fires on the
UI thread, which means `Refresh()` can safely update `_rows` and UI controls
without marshaling. This is simpler and safer than using
`System.Timers.Timer` + `Dispatcher.Invoke`.

**In-place collection update:** Instead of `_rows.Clear()` + add-all (which
causes the DataGrid to flash), we update `_rows[i]` in place and only add/remove
at the edges. This reduces GC pressure from ~400 `ConnectionViewModel` objects
per tick to only changed rows.

**`_initialized` guard:** Several WPF events (`SelectionChanged`,
`TextChanged`) fire during `InitializeComponent()` before controls are fully
wired. The boolean guard prevents `Refresh()` from running before the constructor
completes.

**Public IP fetch:** A single `HttpClient` (static, reused) calls
`https://api.ipify.org` once on `Loaded`. The response is capped at 64 bytes and
45 characters to prevent memory issues from malicious/corrupted responses.

**CPU% calculation:** Uses `Process.TotalProcessorTime` delta divided by
wall-clock elapsed time, normalized by `Environment.ProcessorCount`. This gives
the app's actual CPU usage percentage. `Process.Refresh()` must be called first
to update the cached values.

### 3.4 SettingsPanel — Settings Dialog

**What it does:** A modal dialog with radio buttons (refresh interval), checkboxes
(DNS toggle), and action buttons (reset, colors, prerequisites).

**Design pattern:** The dialog sets public properties (`IntervalSeconds`,
`DnsEnabled`, `DidReset`, `OpenColors`, `OpenPrereqs`) and the caller
(`MainWindow.Settings_Click`) reads them after `ShowDialog()` returns. This
avoids the dialog needing to know about MainWindow's internal state.

*Why a dialog instead of a dropdown menu?* The original ContextMenu approach had
poor dark-theme styling (bright hover colors inherited from Windows), and the
dropdown disappeared on any click. A dialog provides a stable, familiar UI where
users can change multiple settings before closing.

### 3.5 SettingsWindow — Color Editor

**What it does:** A scrollable panel with 24 color rows. Each row has a label,
a colored rectangle swatch (click to open `System.Windows.Forms.ColorDialog`),
and a hex text box. Changes preview live via `DynamicResource`.

*Why WinForms ColorDialog?* WPF doesn't have a built-in color picker. The
WinForms `ColorDialog` is the standard Windows color picker, well-known to
users, and trivially accessed via `UseWindowsForms=true` in the csproj.

### 3.6 GlobalUsings — Type Conflict Resolution

**What it does:** Ten `global using` aliases resolve ambiguities caused by
`UseWindowsForms=true`. For example, `Color` exists in both
`System.Windows.Media` and `System.Drawing`; `MessageBox` exists in both
`System.Windows` and `System.Windows.Forms`.

*Without this file,* every source file would need explicit namespace qualifiers
like `System.Windows.Media.Color` instead of just `Color`.

### 3.7 DataGrid Row Coloring

**How it works:** `DataGridRow` style in App.xaml uses `DataTrigger` bindings
to `IsClosed`, `StateDisplay`, and `IsNew` properties of the ViewModel. Each
trigger sets `Background` and `Foreground` to the corresponding state brush.

**Trigger order matters:** `IsNew` is last so it wins over state-based colors
(a new LISTEN connection shows green `[NEW]` styling, not yellow LISTEN styling).

### 3.8 State Filter Buttons

**What it does:** Six `ToggleButton` controls at the bottom of the window, each
styled with the state's color and a custom `ControlTemplate` that shows
`Opacity="0.45"` when unchecked and `1.0` + white border when checked.

**OR logic:** When multiple state filters are active, connections matching ANY
active filter are shown (union, not intersection). This matches user expectations
— clicking LISTEN + ESTABLISHED shows both.

---

## 4. CLI Terminal — PortMonitor.Cli

**What it does:** A terminal-based interactive UI using `Console.SetCursorPosition`
for flicker-free updates, keyboard input for filtering/sorting/scrolling, and
the same core library as the GUI.

**Thread safety:** The key-input runs on a background `Task.Run` thread while
the main loop polls and renders on the main thread. A `lock(stateLock)` protects
all `AppState` mutations and the `Render()` call to prevent torn reads.

**`Console.SetCursorPosition(0,0)` instead of `Console.Clear()`:** `Clear()`
causes visible flicker because it blanks the entire buffer before redrawing.
Repositioning the cursor and overwriting each line with padding (to erase stale
characters) gives smooth, flicker-free updates.

---

## 5. Design Decisions & Trade-offs

| Decision | Rationale |
|----------|-----------|
| **P/Invoke to iphlpapi.dll** | Only way to get PID per connection; .NET's `IPGlobalProperties` doesn't expose PIDs |
| **Async DNS (fire-and-forget)** | Synchronous DNS blocks UI for minutes and leaks ~1 GB; async shows results next cycle |
| **`DynamicResource` for all brushes** | Enables live color updates without app restart; minimal perf cost |
| **`ObservableCollection` with in-place update** | Avoids DataGrid flicker from `Clear()`+rebuild; reduces GC pressure |
| **Cached `Key` string** | Eliminates ~1600 string allocations per tick |
| **Process handle disposal** | `Process.GetProcesses()` leaks handles without `Dispose()`; hundreds per 5s cache refresh |
| **JSON settings in %AppData%** | Portable, human-editable, easy to reset (delete file) |
| **WinForms `ColorDialog`** | WPF has no built-in color picker; WinForms dialog is Windows-native and familiar |
| **2-cycle closed-entry fade-out** | Fast enough to notice, not so long it clutters the grid |
| **Retry loop in IpHelper** | Buffer race between size query and data fetch; without retry, returns 0 results silently |
| **WingetId regex validation** | Prevents command injection when constructing `winget install` command |
| **Self-contained single-file publish** | Users get one EXE, no .NET install needed; `IncludeNativeLibrariesForSelfExtract` required for WPF |

---

## 6. Alternatives Considered

### 6.1 Different Data Sources

| Alternative | Why Not |
|-------------|---------|
| **`Get-NetTCPConnection` (PowerShell)** | ~100x slower (300-500ms per call vs 3-5ms for P/Invoke); requires PowerShell hosting |
| **`netstat -anob`** | Spawns a child process, parses locale-dependent text output; too slow and fragile for polling |
| **ETW (Event Tracing for Windows)** | Real-time event stream instead of polling; much more complex to implement, requires kernel-level tracing, and needs admin privileges even for basic events |
| **WMI/CIM (`Win32_NetworkConnection`)** | Slow, doesn't reliably expose all TCP states, limited UDP support |
| **Raw sockets / packet capture (pcap)** | Overkill — shows packets, not connection state; requires Npcap/WinPcap driver |

### 6.2 Different UI Frameworks

| Alternative | Why Not |
|-------------|---------|
| **WinForms** | Simpler but no XAML data binding, no `DataTrigger` for row coloring, manual grid management |
| **MAUI** | Cross-platform but `iphlpapi.dll` is Windows-only; MAUI desktop is less mature than WPF |
| **Avalonia** | Cross-platform XAML; viable alternative but smaller ecosystem and less WPF compatibility |
| **Blazor Hybrid / WebView** | Adds browser runtime overhead; overkill for a monitoring tool |
| **Terminal.Gui (TUI)** | Would give a rich terminal UI but loses mouse interaction, color flexibility, and familiar desktop feel |

### 6.3 Different Architecture

| Alternative | Why Not |
|-------------|---------|
| **MVVM with INotifyPropertyChanged** | ConnectionViewModel could implement INPC and update properties in-place instead of creating new instances. This would reduce allocations further but adds significant complexity for a read-heavy monitoring app where the entire dataset changes each tick |
| **Reactive Extensions (Rx)** | Could model the poll as an `IObservable<IReadOnlyList<ConnectionEntry>>` stream. Elegant but adds a dependency and learning curve for a simple timer→poll→render pipeline |
| **Background service + SignalR** | Run the poller as a Windows Service, push updates via SignalR to a web dashboard. Better for multi-user/remote scenarios but far more infrastructure for a local tool |
| **Plugin architecture** | Allow custom columns, filters, or data sources via MEF/plugins. Would future-proof extensibility but over-engineering for the current scope |
| **SQLite for connection history** | Store all poll snapshots for trend analysis and historical queries. Useful for forensics but adds storage management complexity |

### 6.4 Performance Alternatives

| Alternative | Why Not |
|-------------|---------|
| **Object pooling for ConnectionEntry** | Reuse entry objects from a pool instead of allocating new ones. Would reduce GC further but entries are small and short-lived; the current ~80MB footprint is acceptable |
| **Span/stackalloc for marshal** | Use `Span<byte>` and stack allocation instead of `Marshal.AllocHGlobal`. Would avoid heap allocation in IpHelper but the buffer can be 100KB+, too large for the stack |
| **Virtualized DataGrid with deferred loading** | Only create ViewModels for visible rows. WPF's built-in virtualization (`VirtualizingPanel.IsVirtualizing="True"`) already handles this; we just need to provide the data |

---

*This document reflects the state of the codebase at v1.2.0.*
