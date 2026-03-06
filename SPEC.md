# PortMonitor — Full Project Specification

> Use this document to fully replicate the project from scratch.  
> Stack: **.NET 8 · WPF · WinForms color-picker · xUnit · iphlpapi.dll P/Invoke**

---

## 1. Solution Layout

```
Myportmon/
├── PortMonitor.slnx             ← solution file (Visual Studio 2022+)
├── .gitignore
└── src/
    ├── PortMonitor/             ← core library (models, services, console UI)
    ├── PortMonitor.Cli/         ← interactive console front-end
    ├── PortMonitor.Gui/         ← WPF GUI front-end
    └── PortMonitor.Tests/       ← xUnit unit tests
```

### Create the solution

```powershell
dotnet new sln -n PortMonitor
dotnet new classlib  -n PortMonitor       -f net8.0-windows -o src/PortMonitor
dotnet new console   -n PortMonitor.Cli   -f net8.0-windows -o src/PortMonitor.Cli
dotnet new wpf       -n PortMonitor.Gui   -f net8.0-windows -o src/PortMonitor.Gui
dotnet new xunit     -n PortMonitor.Tests -f net8.0-windows -o src/PortMonitor.Tests

dotnet sln add src/PortMonitor/PortMonitor.csproj
dotnet sln add src/PortMonitor.Cli/PortMonitor.Cli.csproj
dotnet sln add src/PortMonitor.Gui/PortMonitor.Gui.csproj
dotnet sln add src/PortMonitor.Tests/PortMonitor.Tests.csproj
```

---

## 2. Project Files

### `src/PortMonitor/PortMonitor.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>PortMonitor</RootNamespace>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

### `src/PortMonitor.Cli/PortMonitor.Cli.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>PortMonitorCli</AssemblyName>
    <RootNamespace>PortMonitor.Cli</RootNamespace>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\PortMonitor\PortMonitor.csproj" />
  </ItemGroup>
</Project>
```

### `src/PortMonitor.Gui/PortMonitor.Gui.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>  <!-- for System.Windows.Forms.ColorDialog -->
    <AssemblyName>PortMonitorGui</AssemblyName>
    <RootNamespace>PortMonitor.Gui</RootNamespace>
    <Version>1.0.0</Version>
    <StartupObject>PortMonitor.Gui.App</StartupObject>
    <ApplicationIcon>app.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
    <Resource Include="app.ico" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\PortMonitor\PortMonitor.csproj" />
  </ItemGroup>
</Project>
```

> **Icon notes:** `app.ico` is a multi-size ICO (256/48/32/16 px) embedded as a WPF `Resource`. The `<ApplicationIcon>` embeds it in the EXE header (Explorer icon); the XAML `Icon="pack://application:,,,/app.ico"` loads it from the assembly resource at runtime (titlebar + taskbar). A bare file-path `Icon="app.ico"` would crash in single-file publish because no loose file exists next to the EXE.

### `src/PortMonitor.Tests/PortMonitor.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\PortMonitor\PortMonitor.csproj" />
  </ItemGroup>
</Project>
```

---

## 3. Core Library — `src/PortMonitor/`

### `Models/ConnectionEntry.cs`

```csharp
namespace PortMonitor.Models;

public enum ConnectionState
{
    Unknown, Closed, Listen, SynSent, SynReceived, Established,
    FinWait1, FinWait2, CloseWait, Closing, LastAck, TimeWait,
    DeleteTcb, Udp
}

public class ConnectionEntry
{
    public string          Protocol      { get; init; } = string.Empty;
    public string          LocalAddress  { get; init; } = string.Empty;
    public int             LocalPort     { get; init; }
    public string          RemoteAddress { get; init; } = string.Empty;
    public int             RemotePort    { get; init; }
    public ConnectionState State         { get; init; }
    public int             Pid           { get; init; }
    public string          ProcessName   { get; set;  } = string.Empty;

    // Delta tracking
    public bool IsNew        { get; set; }
    public bool IsClosed     { get; set; }
    public int  ClosedCycles { get; set; }

    public string Key          => $"{Protocol}|{LocalAddress}|{LocalPort}|{RemoteAddress}|{RemotePort}";
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
```

### `Services/IpHelper.cs`

P/Invoke wrapper around `iphlpapi.dll`.  Uses `GetExtendedTcpTable` (class `TcpTableOwnerPidAll`) and `GetExtendedUdpTable` (class `UdpTableOwnerPid`) to enumerate all IPv4 TCP connections and UDP endpoints with their owning PID.

Key points:
- Both structs are `[StructLayout(LayoutKind.Sequential)]`.
- Port bytes must be swapped: `NetworkToHostPort(uint dwPort)` = `((dwPort & 0xFF) << 8) | ((dwPort >> 8) & 0xFF)`.
- `MapTcpState(uint)` maps iphlpapi state codes 1–12 to `ConnectionState`.
- `GetTcpConnections()` returns `IEnumerable<(uint State, string LocalAddr, int LocalPort, string RemoteAddr, int RemotePort, int Pid)>`.
- `GetUdpListeners()` returns `IEnumerable<(string LocalAddr, int LocalPort, int Pid)>`.

```csharp
using System.Net;
using System.Runtime.InteropServices;
using PortMonitor.Models;

namespace PortMonitor.Services;

internal static class IpHelper
{
    private enum TcpTableClass
    {
        TcpTableBasicListener, TcpTableBasicConnections, TcpTableBasicAll,
        TcpTableOwnerPidListener, TcpTableOwnerPidConnections, TcpTableOwnerPidAll,
        TcpTableOwnerModuleListener, TcpTableOwnerModuleConnections, TcpTableOwnerModuleAll
    }

    private enum UdpTableClass { UdpTableBasic, UdpTableOwnerPid, UdpTableOwnerModule }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState, dwLocalAddr, dwLocalPort, dwRemoteAddr, dwRemotePort, dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint dwLocalAddr, dwLocalPort, dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr p, ref int len, bool sort,
        int af, TcpTableClass cls, uint reserved = 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr p, ref int len, bool sort,
        int af, UdpTableClass cls, uint reserved = 0);

    private const int AfInet = 2;

    public static IEnumerable<(uint State, string LocalAddr, int LocalPort,
                                string RemoteAddr, int RemotePort, int Pid)> GetTcpConnections()
    {
        int sz = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref sz, true, AfInet, TcpTableClass.TcpTableOwnerPidAll);
        IntPtr buf = Marshal.AllocHGlobal(sz);
        try
        {
            if (GetExtendedTcpTable(buf, ref sz, true, AfInet, TcpTableClass.TcpTableOwnerPidAll) != 0)
                yield break;
            int n = Marshal.ReadInt32(buf);
            IntPtr p = buf + 4;
            int rowSz = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (int i = 0; i < n; i++, p += rowSz)
            {
                var r = Marshal.PtrToStructure<MibTcpRowOwnerPid>(p);
                yield return (r.dwState,
                    new IPAddress(r.dwLocalAddr).ToString(),  NetworkToHostPort(r.dwLocalPort),
                    new IPAddress(r.dwRemoteAddr).ToString(), NetworkToHostPort(r.dwRemotePort),
                    (int)r.dwOwningPid);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static IEnumerable<(string LocalAddr, int LocalPort, int Pid)> GetUdpListeners()
    {
        int sz = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref sz, true, AfInet, UdpTableClass.UdpTableOwnerPid);
        IntPtr buf = Marshal.AllocHGlobal(sz);
        try
        {
            if (GetExtendedUdpTable(buf, ref sz, true, AfInet, UdpTableClass.UdpTableOwnerPid) != 0)
                yield break;
            int n = Marshal.ReadInt32(buf);
            IntPtr p = buf + 4;
            int rowSz = Marshal.SizeOf<MibUdpRowOwnerPid>();
            for (int i = 0; i < n; i++, p += rowSz)
            {
                var r = Marshal.PtrToStructure<MibUdpRowOwnerPid>(p);
                yield return (new IPAddress(r.dwLocalAddr).ToString(),
                    NetworkToHostPort(r.dwLocalPort), (int)r.dwOwningPid);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static ConnectionState MapTcpState(uint s) => s switch
    {
        1  => ConnectionState.Closed,       2  => ConnectionState.Listen,
        3  => ConnectionState.SynSent,      4  => ConnectionState.SynReceived,
        5  => ConnectionState.Established,  6  => ConnectionState.FinWait1,
        7  => ConnectionState.FinWait2,     8  => ConnectionState.CloseWait,
        9  => ConnectionState.Closing,      10 => ConnectionState.LastAck,
        11 => ConnectionState.TimeWait,     12 => ConnectionState.DeleteTcb,
        _  => ConnectionState.Unknown
    };

    private static int NetworkToHostPort(uint dw) =>
        (int)(((dw & 0xFF) << 8) | ((dw >> 8) & 0xFF));
}
```

### `Services/ConnectionPoller.cs`

Calls `IpHelper.GetTcpConnections()` and `IpHelper.GetUdpListeners()`, resolves each PID to a process name (with a 5-second cache), deduplicates by `entry.Key`, and returns `IReadOnlyList<ConnectionEntry>`.

- PID 0 → `"Idle"`, PID 4 → `"System"`.
- Falls back to `"[N/A]"` when `Process.GetProcessById` throws.
- `_processCache` is invalidated after 5 seconds to reduce overhead.

### `Services/DiffEngine.cs`

Compares consecutive poll snapshots to produce delta annotations.

- `_active` — dict of last live snapshot.
- `_closed` — fade-out queue: entries stay for 2 additional render cycles (`ClosedCycles` 1→2, then dropped).
- `Apply(current)` algorithm:
  1. Mark entries NOT in `_active` as `IsNew = true`.
  2. Advance fade-out entries by one cycle; drop those at `ClosedCycles >= 2`.
  3. Detect newly closed (in `_active` but not in `current`); add with `IsClosed = true, ClosedCycles = 1`.
  4. Update `_active = currentMap` and `_closed = nextClosed`.
- `Reset()` clears both dicts.

### `Services/PrerequisiteChecker.cs`

Checks four requirements:

| Check | Hard/Soft | Auto-fix via winget |
|-------|-----------|---------------------|
| Windows 10 build ≥ 10240 | Hard | — |
| .NET 8 Runtime | Hard | `Microsoft.DotNet.Runtime.8` |
| Administrator elevation | Soft (yellow) | — |
| winget available | Hard (enabler) | — |

Public API:
- `CheckAll()` → `IReadOnlyList<PrerequisiteResult>`
- `SilentCheck()` → list of failing results only
- `RunInteractive()` → full console UI with Y/N prompt to install
- `InstallMissing(results, progress?)` → runs `winget install --id … --silent …`

### `UI/AppState.cs`

Mutable state bag shared between the render loop and the key-input task (CLI only):

```csharp
public enum SortColumn { Port, Pid, State, Process }
public enum ViewMode   { All, ListenOnly, EstablishedOnly }

public class AppState
{
    public string Filter          { get; set; } = string.Empty;
    public SortColumn Sort        { get; set; } = SortColumn.Port;
    public ViewMode   View        { get; set; } = ViewMode.All;
    public int        Page        { get; set; } = 0;
    public int        PageSize    { get; set; } = 20;
    public bool       IsEnteringFilter { get; set; }
    public string     FilterInput { get; set; } = string.Empty;
    public int        MaxPage     { get; set; } = 0;

    public void CycleSortColumn() { /* Port→Pid→State→Process→Port */ }
    public void ScrollUp()        => Page = Math.Max(0, Page - 1);
    public void ScrollDown()      => Page = Math.Min(MaxPage, Page + 1);
    public void ResetFilters()    { Filter = ""; View = ViewMode.All; Sort = SortColumn.Port; Page = 0; }
}
```

### `UI/ConsoleRenderer.cs`

Renders the full terminal UI using `Console.SetCursorPosition(0,0)` (no flicker).

- **Header**: box-drawing border with timestamp, filter, view mode, connection count, sort, page.
- **Column headers + rows**: fixed-width columns — Tag(6), Proto(5), LocalAddr(16), LPort(7), RemoteAddr(16), RPort(7), State(14), PID(7), Process(18). Long values are truncated with `…`.
- **Row colors**: `IsNew` → White; `IsClosed` → DarkGray; else by state: LISTEN=Yellow, ESTABLISHED=Green, TIME_WAIT/CLOSE_WAIT=Red, UDP=Cyan, SYN_*=Magenta, default=Gray.
- **Footer**: key hints or active filter input with cursor.
- **Log**: appends `NEW`/`CLOSED` events to a text file if `--log` was passed.
- Uses `WriteFullLine` to pad every line to terminal width (overwrites stale characters).

---

## 4. CLI Front-end — `src/PortMonitor.Cli/Program.cs`

Top-level statements. Key flow:

1. Handle `--help` / `--check` flags.
2. Parse `--interval <n>` and `--log <path>`.
3. `PrerequisiteChecker.SilentCheck()` — warn on issues, pause on hard failures.
4. Hide cursor, `Console.Clear()`.
5. Create `ConnectionPoller`, `DiffEngine`, `AppState`, `ConsoleRenderer`.
6. **Key-input task** (background `Task.Run`): reads `ConsoleKey` non-blocking:
   - `Q` → cancel; `F` → enter filter mode; `S` → cycle sort; `L` → toggle Listen-only; `E` → toggle Established-only; `R` → reset + `diff.Reset()`; `↑↓` → scroll.
   - Filter-entry mode: printable chars append to `FilterInput`; Enter commits; Esc cancels.
7. **Main loop**: `Poll() → diff.Apply() → renderer.Render()` then `await Task.Delay(interval * 1000, cts.Token)`.

---

## 5. WPF GUI — `src/PortMonitor.Gui/`

### Architecture

```
App.xaml / App.xaml.cs        ← application startup, global exception handlers,
                                  loads AppSettings on startup
GlobalUsings.cs                ← resolves WinForms vs WPF type ambiguities
AppSettings.cs                 ← persistent colors + flags (JSON)
MainWindow.xaml / .cs          ← main window
ViewModels/ConnectionViewModel ← wraps ConnectionEntry for DataGrid binding
SettingsPanel.xaml / .cs       ← settings dialog (interval, DNS, actions)
SettingsWindow.xaml / .cs      ← color customization dialog
PrerequisiteWindow.xaml / .cs  ← prerequisite checker dialog
```

### `GlobalUsings.cs`

Required because `UseWindowsForms=true` creates type conflicts:

```csharp
global using Application    = System.Windows.Application;
global using Color          = System.Windows.Media.Color;
global using Colors         = System.Windows.Media.Colors;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Brushes        = System.Windows.Media.Brushes;
global using FontFamily     = System.Windows.Media.FontFamily;
global using Cursors        = System.Windows.Input.Cursors;
global using MessageBox     = System.Windows.MessageBox;
global using Rectangle      = System.Windows.Shapes.Rectangle;
global using TextBox        = System.Windows.Controls.TextBox;
```

### `App.xaml.cs`

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    DispatcherUnhandledException += (_, ex) => { /* show MessageBox, mark handled */ };
    AppDomain.CurrentDomain.UnhandledException += (_, ex) => { /* show MessageBox */ };
    base.OnStartup(e);
    AppSettings.LoadAndApply();   // apply persisted colors before first render
}
```

### `AppSettings.cs`

Singleton persistent color store. Saved to `%AppData%\PortMonitor\settings.json` as `{ "BrushKey": "#AARRGGBB", … }`.

**22 keys and their defaults:**

| Key | Default | Purpose |
|-----|---------|---------|
| `BgPrimaryBrush` | `#FF1E1E1E` | Main background |
| `BgSecondaryBrush` | `#FF252526` | Toolbar / strip backgrounds |
| `BgControlBrush` | `#FF2D2D30` | ComboBox / button backgrounds |
| `BgControlHoverBrush` | `#FF3E3E42` | Control hover |
| `BgAccentBrush` | `#FF007ACC` | Accent / pressed |
| `BorderBrush` | `#FF3F3F46` | Borders |
| `FgPrimaryBrush` | `#FFD4D4D4` | Primary text |
| `FgMutedBrush` | `#FF9D9D9D` | Muted text |
| `GridLineBrush` | `#FF2D2D30` | DataGrid horizontal lines |
| `ColorNew` | `#FF1A3A1A` | NEW row background |
| `ColorClosed` | `#FF2A2A2A` | CLOSED row background |
| `ColorListen` | `#FF2B2B00` | LISTEN row background |
| `ColorEstablished` | `#FF002800` | ESTABLISHED row background |
| `ColorTimeWait` | `#FF2E0000` | TIME_WAIT row background |
| `ColorUdp` | `#FF002535` | UDP row background |
| `FgNew` | `#FF90FF90` | NEW row foreground |
| `FgClosed` | `#FF707070` | CLOSED row foreground |
| `FgListen` | `#FFFFD700` | LISTEN row foreground |
| `FgEstablished` | `#FF00DD00` | ESTABLISHED row foreground |
| `FgTimeWait` | `#FFFF7070` | TIME_WAIT row foreground |
| `FgUdp` | `#FF00DDDD` | UDP row foreground |
| `FgDefault` | `#FFD4D4D4` | DataGrid row default foreground |

Key methods:
- `ApplyOne(key, hex)` → always does `Application.Current.Resources[key] = new SolidColorBrush(color)` (replaces, never mutates frozen brushes).
- `Apply()` → calls `ApplyOne` for all entries.
- `LoadAndApply()` → deserializes from JSON (merges with defaults), then calls `Apply()`.
- `Save()` → serializes `_colors` to JSON (creates directory if needed).
- `Clone()` / `CopyFrom(source)` → used by the Settings dialog for Apply/Cancel/Reset.

### `App.xaml` — Resource Dictionary

**Critical design decisions:**
- All custom brush references in styles use `{DynamicResource …}` (not `StaticResource`) so color changes propagate immediately.
- Brush declarations appear at the top of `Application.Resources`.

**Brush declarations (22 total):**
```xaml
<SolidColorBrush x:Key="BgPrimaryBrush"      Color="#FF1E1E1E" />
<SolidColorBrush x:Key="BgSecondaryBrush"    Color="#FF252526" />
<SolidColorBrush x:Key="BgControlBrush"      Color="#FF2D2D30" />
<SolidColorBrush x:Key="BgControlHoverBrush" Color="#FF3E3E42" />
<SolidColorBrush x:Key="BgAccentBrush"       Color="#FF007ACC" />
<SolidColorBrush x:Key="BorderBrush"         Color="#FF3F3F46" />
<SolidColorBrush x:Key="FgPrimaryBrush"      Color="#FFD4D4D4" />
<SolidColorBrush x:Key="FgMutedBrush"        Color="#FF9D9D9D" />
<SolidColorBrush x:Key="GridLineBrush"        Color="#FF2D2D30" />
<SolidColorBrush x:Key="ColorNew"         Color="#FF1A3A1A" />
... (ColorClosed, ColorListen, ColorEstablished, ColorTimeWait, ColorUdp)
<SolidColorBrush x:Key="FgNew"         Color="#FF90FF90" />
... (FgClosed, FgListen, FgEstablished, FgTimeWait, FgUdp)
<SolidColorBrush x:Key="FgDefault"     Color="#FFD4D4D4" />
```

**Styles defined in App.xaml:**
- `TextBlock` — Consolas 12, FgPrimaryBrush
- `TextBox` — BgControlBrush, FgPrimaryBrush, BorderBrush, hover trigger
- `Button` — BgControlBrush, FgPrimaryBrush, hover/pressed triggers
- `ComboBox` — **full ControlTemplate** bypassing Windows dark mode: gray backgrounds (`#B8B8B8`/`#D8D8D8`), black bold text (`#111111`), toggle button, popup, ComboBoxItem template with TextElement attached properties
- `ToggleButton` — BgControlBrush, IsChecked→BgAccentBrush+White, hover trigger
- `StateFilterButton` (x:Key style) — custom ControlTemplate with `CornerRadius="3"`, `Opacity="0.45"` normally, `1.0` when checked + white border
- `DataGrid` — BgPrimaryBrush, GridLineBrush, no border, Consolas 12, IsReadOnly, AutoGenerateColumns=False
- `DataGridColumnHeader` — BgSecondaryBrush, FgPrimaryBrush, BorderBrush bottom/right, bold
- `DataGridRow` — BgPrimaryBrush, FgDefault; triggers for IsClosed/StateDisplay LISTEN/ESTABLISHED/TIME_WAIT/NEW/UDP→ColorXxx+FgXxx
- `DataGridCell` — transparent background, no border
- `StatusBar` — BgSecondaryBrush, FgMutedBrush
- `StatusBarItem` — FgMutedBrush

### `MainWindow.xaml` layout

```
DockPanel
├── Border (DockPanel.Dock="Top")          ← Toolbar
│   └── WrapPanel
│       ├── "Filter:" TextBlock
│       ├── FilterBox TextBox (200px)
│       ├── "✕" clear Button
│       ├── Separator
│       ├── "Refresh:" TextBlock
│       ├── IntervalCombo (1s/2s/5s/10s)
│       ├── Separator
│       ├── "↺ Reset" Button
│       ├── "⚙ Prereqs" Button
│       └── "🎨 Colors" Button
├── StatusBar (DockPanel.Dock="Bottom")    ← Status bar
│   ├── StatusConnections
│   ├── StatusRefresh
│   ├── StatusAdmin
│   ├── StatusFilter
│   └── (DockPanel.Dock="Right") StackPanel:
│       ├── StatusCpu         ("CPU: 0.3%")
│       ├── StatusMem         ("Mem: 85.2 MB")
│       └── StatusPublicIp    ("IP: x.x.x.x")
├── Border (DockPanel.Dock="Bottom")       ← State filter strip
│   └── WrapPanel
│       ├── "State Filter:" label
│       ├── BtnNew     ToggleButton (■ NEW,         Tag="New")
│       ├── BtnClosed  ToggleButton (■ CLOSED,      Tag="Closed")
│       ├── BtnListen  ToggleButton (■ LISTEN,      Tag="Listen")
│       ├── BtnEstab   ToggleButton (■ ESTABLISHED,  Tag="Established")
│       ├── BtnTimeWait ToggleButton (■ TIME_WAIT,   Tag="TimeWait")
│       └── BtnUdp     ToggleButton (■ UDP,          Tag="Udp")
└── DataGrid (ConnectionGrid)              ← Main data grid
    └── Columns: Tag, Proto, Local Address, L.Port,
                 Remote Address, R.Port, State, PID, Process,
                 Remote Host (ColRemoteHost, toggleable)
```

Each state filter button uses `Style="{StaticResource StateFilterButton}"` with `Background="{DynamicResource ColorXxx}"` and `Foreground="{DynamicResource FgXxx}"`. All six share one `Click="StateFilter_Click"` handler; the `Tag` property identifies which state.

The `<Window>` element includes `Icon="pack://application:,,,/app.ico"` to display the door-themed icon in the titlebar and taskbar.

### `MainWindow.xaml.cs` — Code-behind

**Fields:**
```csharp
private readonly ConnectionPoller _poller  = new();
private readonly DiffEngine       _diff    = new();
private static readonly HttpClient _http   = new() { Timeout = TimeSpan.FromSeconds(10) };
private readonly Process _self = Process.GetCurrentProcess();
private TimeSpan _lastCpuTime;
private DateTime _lastCpuCheck;
private readonly DispatcherTimer  _timer   = new();
private int                       _intervalSeconds = 2;
private readonly ObservableCollection<ConnectionViewModel> _rows = [];
private string           _filter       = string.Empty;
private readonly HashSet<string> _stateFilters = [];
private bool             _initialized;
```

**Constructor guard pattern** — `_initialized = true` is set after `InitializeComponent()` completes; `Refresh()` returns early if not initialized. This prevents crashes from `SelectionChanged` events fired during XAML initialization. The `Loaded` event starts the timer, calls the first `Refresh()`, and kicks off `FetchPublicIpAsync()`.

**`Refresh()` pipeline:**
1. `_poller.Poll()` → `_diff.Apply()` → `merged`
2. If `_stateFilters.Count > 0` → filter by OR of active state keys
3. If `_filter.Length > 0` → text filter across all columns
4. `OrderBy(e => e.LocalPort)` → rebuild `_rows`
5. Update status bar labels
6. Call `UpdateResourceStats()` to refresh CPU% and memory

**`UpdateResourceStats()`**: Uses `Process.Refresh()` to read current `TotalProcessorTime` and `WorkingSet64`. CPU% is calculated as delta CPU time ÷ (wall-clock elapsed × core count) × 100. Memory is shown as `WorkingSet64` in MB.

**`FetchPublicIpAsync()`**: Called once on `Loaded`. Hits `https://api.ipify.org` via `HttpClient` (10 s timeout). Sets `StatusPublicIp.Text` to the IP string or `"unavailable"` on failure.

**Handlers:** `FilterBox_TextChanged`, `ClearFilter_Click`, `StateFilter_Click` (adds/removes from `_stateFilters`), `Settings_Click` (opens SettingsPanel dialog, applies interval/DNS/snapshot/reset/colors/prereqs on close), `Snapshot_Click` (exports current `_rows` to CSV or text file per AppSettings), `Reset_Click`, `Prereqs_Click`, `Colors_Click`.

### `SettingsPanel.xaml / .cs`

Settings dialog window with four sections:

**Refresh Interval** — radio buttons: 1s / 2s / 5s / 10s (restored from current value).

**Options** — checkboxes:
- 🌐 DNS Resolution — toggles `ConnectionPoller.DnsEnabled` and `ColRemoteHost` column visibility. Persisted to `AppSettings` flags.

**Snapshot Export** — configurable folder (text box + Browse button using `FolderBrowserDialog`) and format (CSV / Text radio buttons). Defaults to Desktop + CSV. Persisted to `AppSettings` strings (`SnapshotPath`, `SnapshotFormat`).

**Actions** — buttons:
- ↺ Reset Filters — sets `DidReset = true`, closes dialog
- 🎨 Colors... — sets `OpenColors = true`, closes dialog
- ⚙ Prerequisites... — sets `OpenPrereqs = true`, closes dialog

All results read by MainWindow after `ShowDialog()` returns.

### `ViewModels/ConnectionViewModel.cs`

```csharp
public sealed class ConnectionViewModel
{
    public string Tag              { get; }   // "[NEW]", "[CLS]", or ""
    public string Protocol         { get; }
    public string LocalAddress     { get; }
    public int    LocalPort        { get; }
    public string RemoteAddress    { get; }
    public string RemotePortDisplay { get; } // "0" → "*" for UDP
    public string StateDisplay     { get; }
    public string Pid              { get; }  // "-" when Pid == 0
    public string ProcessName      { get; }
    public string RemoteHost       { get; }  // reverse DNS FQDN
    public bool   IsNew            { get; }  // used by DataTrigger
    public bool   IsClosed         { get; }  // used by DataTrigger
}
```

### `SettingsWindow.xaml.cs`

Color settings dialog with two sections:

**App Colors (8 entries):**
Main Background, Toolbar Background, Control Background, Control Hover, Accent/Pressed, Border, Primary Text, Muted Text, Grid Lines.

**Connection State Colors (13 entries):**
New (bg+fg), Closed (bg+fg), Listen (bg+fg), Established (bg+fg), Time Wait (bg+fg), UDP (bg+fg), Default Text.

Each row has: label, colored Rectangle swatch (click to open `System.Windows.Forms.ColorDialog`), hex TextBox. Changes preview live via `AppSettings.Current.Apply()`. **Apply** persists (calls `Save()`). **Cancel** restores original. **Reset** restores built-in defaults.

### `PrerequisiteWindow.xaml.cs`

Dialog that calls `PrerequisiteChecker.CheckAll()` and displays results in a `ScrollViewer`. Shows ✓/✗ icons, colored text (green/red/yellow). Has a "Fix Missing" button that calls `PrerequisiteChecker.InstallMissing()` with progress messages appended to a log TextBox. Close button re-enables the parent window's timer.

---

## 6. Unit Tests — `src/PortMonitor.Tests/DiffEngineTests.cs`

Tests for `DiffEngine.Apply()`:

| Test | What it verifies |
|------|-----------------|
| `FirstPoll_AllEntriesMarkedNew` | All entries on first call get `IsNew=true` |
| `SecondPoll_SameEntries_NotNew` | Same entries on second call have `IsNew=false` |
| `EntryDisappears_MarkedClosed` | Entry missing from second poll gets `IsClosed=true` |
| `ClosedEntry_FadesAfterTwoCycles` | Entry disappears after `ClosedCycles >= 2` |
| `EntryReappears_NotClosed` | Entry re-appearing is marked new, not closed |
| `Reset_ClearsPreviousState` | After `Reset()` all entries are new again |
| `DuplicateKeys_Deduplicated` | Duplicate keys from OS don't cause double rows |
| `MultipleStates_FilteredCorrectly` | Mixed-state list handled correctly |

---

## 7. Publish

### Self-contained single-file executable (recommended)

```powershell
dotnet publish src/PortMonitor.Gui/PortMonitor.Gui.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o src/PortMonitor.Gui/publish-gui
```

Output: `publish-gui/PortMonitorGui.exe` (~154 MB, no .NET install required).

> **Important:** `IncludeNativeLibrariesForSelfExtract=true` is required. Without it the WPF native DLLs are not bundled and the app crashes on startup with `DllNotFoundException`.

### CLI — self-contained

```powershell
dotnet publish src/PortMonitor.Cli/PortMonitor.Cli.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o publish-cli
```

---

## 8. Known Implementation Notes

| Issue | Solution |
|-------|----------|
| WPF ComboBox unreadable in Windows dark mode | Full `ControlTemplate` bypassing system colors; gray `#B8B8B8`/`#D8D8D8` backgrounds, black bold text `#111111` |
| Colors saved but not updating live | Use `DynamicResource` (not `StaticResource`) everywhere; `ApplyOne` replaces the resource entry rather than mutating a frozen brush |
| `SortCombo_Changed` / `IntervalCombo_Changed` fires during `InitializeComponent` | Guard with `if (!_initialized) return` at the top of all event handlers |
| WinForms + WPF type conflicts (`Color`, `MessageBox`, `TextBox`, etc.) | `GlobalUsings.cs` with explicit `global using` aliases |
| `PortMonitorGui.exe` locked during publish | Stop the process first: `Stop-Process -Name PortMonitorGui -ErrorAction SilentlyContinue` |
| App icon crashes single-file EXE | `Icon="app.ico"` resolves from filesystem; use `Icon="pack://application:,,,/app.ico"` + `<Resource Include="app.ico" />` in csproj to load from embedded resource |
| Synchronous DNS blocks UI and leaks ~1 GB | Use async `Dns.GetHostEntryAsync()` fire-and-forget with `ConcurrentDictionary` cache (bounded at 4096). Results appear next poll cycle |
| `ConnectionEntry.Key` allocates string on every access | Cache as a field via `ComputeKey()` called once after init-property assignment |
| `Process.GetProcesses()` leaks native handles | Dispose each `Process` in `finally` block; use `using var proc` for `GetProcessById` |
