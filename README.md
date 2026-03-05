# PortMonitor

Real-time interactive Windows port monitor — a modern CLI alternative to Sysinternals Portmon / TCPView.

## Features

- Polls all active **TCP + UDP connections** every N seconds (configurable)
- Columns: `Protocol | Local Address | Local Port | Remote Address | Remote Port | State | PID | Process Name`
- **Color-coded by state**: LISTEN (yellow), ESTABLISHED (green), TIME_WAIT/CLOSE_WAIT (red), UDP (cyan)
- **Delta detection**: marks `[NEW]` connections and `[CLS]` connections that disappeared (fade-out after 2 cycles)
- **Interactive filtering** by IP, port, process name, or state
- **No flicker** — uses cursor repositioning, not `cls`
- Optional file logging of new/closed events

## Requirements

- Windows 10/11
- .NET 8 SDK or runtime
- Run as Administrator for full PID→process name resolution

## Build

```bash
dotnet build PortMonitor.slnx
```

## Publish (create .exe)

### GUI app (WPF — no console window)

**Self-contained** (~70 MB, no .NET install required):

```bash
dotnet publish src/PortMonitor.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-gui
```

Output: `publish-gui\PortMonitorGui.exe` — double-click to run.

### CLI terminal app

**Self-contained**:

```bash
dotnet publish src/PortMonitor.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-cli
```

Output: `publish-cli\PortMonitorCli.exe` — run from a console/PowerShell window.

**Framework-dependent** (requires .NET 8, much smaller):

```bash
dotnet publish src/PortMonitor.Cli -c Release -r win-x64 -o publish-cli
```

## Run

```bash
# GUI (WPF window — no console)
dotnet run --project src/PortMonitor.Gui

# CLI (terminal, interactive)
dotnet run --project src/PortMonitor.Cli -- [options]

# or after publishing:
PortMonitorGui.exe          # GUI app
PortMonitorCli.exe [opts]   # terminal app
```

### Options

| Flag | Default | Description |
|------|---------|-------------|
| `--interval <n>` | `2` | Refresh interval in seconds (min: 1) |
| `--log <path>` | _(none)_ | Append new/closed events to a log file |
| `--check` | | Run interactive prerequisite check, install missing components via winget, then exit |
| `--help` / `-h` | | Print usage and exit |

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `F` | Enter filter string (matches any column) |
| `S` | Cycle sort: Port → PID → State → Process |
| `L` | Toggle LISTEN-only view |
| `E` | Toggle ESTABLISHED-only view |
| `R` | Reset all filters and sort |
| `Q` | Quit |
| `↑` / `↓` | Scroll pages |

## Project Structure

```
src/
├── PortMonitor/                       Core library (models, services, console UI)
│   ├── Models/
│   │   └── ConnectionEntry.cs         Data model per connection
│   ├── Services/
│   │   ├── IpHelper.cs                P/Invoke → iphlpapi.dll (PID resolution)
│   │   ├── ConnectionPoller.cs        Data collection loop
│   │   ├── DiffEngine.cs              New/closed delta detection
│   │   └── PrerequisiteChecker.cs     OS/runtime/winget checks + auto-install
│   └── UI/
│       ├── AppState.cs                Interactive UI state (filter, sort, page)
│       └── ConsoleRenderer.cs         Terminal drawing logic
├── PortMonitor.Cli/                   Terminal app (console window)
│   └── Program.cs                     Entry point, CLI args, main loop, key input
├── PortMonitor.Gui/                   WPF GUI app (no console window)
│   ├── App.xaml / App.xaml.cs         Dark theme resources
│   ├── MainWindow.xaml/.cs            Main window with DataGrid + toolbar
│   ├── PrerequisiteWindow.xaml/.cs    Prerequisite check dialog
│   └── ViewModels/
│       └── ConnectionViewModel.cs     WPF data-binding model
└── PortMonitor.Tests/
    └── DiffEngineTests.cs             Unit tests for DiffEngine (9 tests)
```

## Notes

- PID resolution uses `GetExtendedTcpTable` / `GetExtendedUdpTable` via P/Invoke to `iphlpapi.dll` — the same approach used by TCPView — because .NET's `IPGlobalProperties` does not expose PIDs natively.
- Process names that cannot be resolved (Access Denied) appear as `[N/A]`.
