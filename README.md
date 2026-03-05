# PortMonitor

Real-time interactive Windows port monitor — a modern CLI alternative to Sysinternals Portmon / TCPView.

## Download

**[PortMonitorGui.exe v1.2.0](https://github.com/ruramosMSFT/Myportmon/releases/tag/v1.2.0)** — self-contained Windows x64 executable (~155 MB). No .NET install required. Double-click to run.

> Run as Administrator for full PID→process name resolution.

## Features

- Polls all active **TCP + UDP connections** every N seconds (configurable 1/2/5/10s)
- Columns: `Tag | Proto | Local Address | L.Port | Remote Address | R.Port | State | PID | Process | Remote Host`
- **Remote Host** — async reverse DNS lookup (FQDN) with persistent cache, toggleable via Settings
- **Color-coded by state**: LISTEN (yellow), ESTABLISHED (green), TIME_WAIT/CLOSE_WAIT (red), UDP (cyan)
- **Delta detection**: marks `[NEW]` connections and `[CLS]` closed connections (fade-out after 2 cycles)
- **State filter buttons**: clickable toggles for NEW, CLOSED, LISTEN, ESTABLISHED, TIME_WAIT, UDP
- **Interactive text filtering** by IP, port, process name, hostname, or state
- **Settings dialog** — refresh interval, DNS toggle, colors, prerequisites
- **Customizable dark theme** — 24 color keys, JSON-persisted to `%AppData%\PortMonitor\settings.json`
- **Status bar**: connection count, last refresh, admin status, CPU%, memory, public IP
- **Door-themed app icon** (multi-size ICO: 256/48/32/16px)
- Optional file logging of new/closed events (CLI)

## Requirements

- Windows 10/11
- .NET 8 SDK (for building) or self-contained EXE (no runtime needed)
- Run as Administrator for full PID→process name resolution

## Build

```bash
dotnet build PortMonitor.slnx
```

## Publish (create .exe)

### GUI app (WPF — no console window)

**Self-contained** (~155 MB, no .NET install required):

```bash
dotnet publish src/PortMonitor.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-gui
```

Output: `publish-gui\PortMonitorGui.exe` — double-click to run.

### CLI terminal app

**Self-contained**:

```bash
dotnet publish src/PortMonitor.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-cli
```

Output: `publish-cli\PortMonitorCli.exe` — run from a console/PowerShell window.

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

### CLI Options

| Flag | Default | Description |
|------|---------|-------------|
| `--interval <n>` | `2` | Refresh interval in seconds (1–300) |
| `--log <path>` | _(none)_ | Append new/closed events to a log file |
| `--check` | | Run interactive prerequisite check, install missing components via winget, then exit |
| `--help` / `-h` | | Print usage and exit |

### CLI Keyboard Shortcuts

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
│   │   ├── ConnectionPoller.cs        Polling + process/DNS resolution
│   │   ├── DiffEngine.cs              New/closed delta detection
│   │   └── PrerequisiteChecker.cs     OS/runtime/winget checks + auto-install
│   └── UI/
│       ├── AppState.cs                Interactive UI state (filter, sort, page)
│       └── ConsoleRenderer.cs         Terminal drawing logic
├── PortMonitor.Cli/                   Terminal app (console window)
│   └── Program.cs                     Entry point, CLI args, main loop, key input
├── PortMonitor.Gui/                   WPF GUI app (no console window)
│   ├── App.xaml / App.xaml.cs         Dark theme resources + global styles
│   ├── MainWindow.xaml/.cs            Main window with DataGrid + toolbar
│   ├── SettingsPanel.xaml/.cs         Settings dialog (interval, DNS, actions)
│   ├── SettingsWindow.xaml/.cs        Color customization dialog
│   ├── PrerequisiteWindow.xaml/.cs    Prerequisite check dialog
│   ├── AppSettings.cs                 Persistent colors + flags (JSON)
│   ├── GlobalUsings.cs                WinForms/WPF type aliases
│   ├── app.ico                        Door-themed multi-size icon
│   └── ViewModels/
│       └── ConnectionViewModel.cs     WPF data-binding model
└── PortMonitor.Tests/
    └── DiffEngineTests.cs             Unit tests for DiffEngine (9 tests)
```

## Repositories

- **Public**: https://github.com/ruramosMSFT/Myportmon
- **Enterprise**: https://github.com/ruramos_microsoft/Myportmon

## Notes

- PID resolution uses `GetExtendedTcpTable` / `GetExtendedUdpTable` via P/Invoke to `iphlpapi.dll` — the same approach used by TCPView — because .NET's `IPGlobalProperties` does not expose PIDs natively.
- Process names that cannot be resolved (Access Denied) appear as `[N/A]`.
- DNS resolution is async (non-blocking) with a bounded cache (4096 entries). Results appear on the next poll cycle.
- The `ConnectionEntry.Key` is computed once and cached to avoid repeated string allocations.
