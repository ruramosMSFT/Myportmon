<img src="https://r2cdn.perplexity.ai/pplx-full-logo-primary-dark%402x.png" style="height:64px;margin-right:32px"/>

# i am a network engineer, and need to create an app or program similar to [Portmon - Sysinternals | Microsoft Learn](https://learn.microsoft.com/en-us/sysinternals/downloads/portmon) that is capable of reading in real time the port usage of a windows OS. like an interactive netstat. for it i wanna use github copilot to create such app. create me a prompt that i can use in copilot or write me a MD file i can run .

Here is a comprehensive GitHub Copilot prompt packaged as a ready-to-use Markdown file you can drop into your repo and open in VS Code with Copilot Chat:

***

```markdown
# 🖥️ Interactive Port Monitor — Copilot Prompt

## Project Goal

Build a **real-time, interactive Windows port monitor** — a modern alternative to
Sysinternals Portmon / TCPView — that continuously polls active TCP/UDP connections
and displays them in a live, filterable terminal UI.

---

## Tech Stack Constraints

- **Language:** C# (.NET 8+) **or** PowerShell 7+ (ask me which before starting)
- **UI mode:** Terminal/console interactive (no WinForms/WPF)
- **Data source:** `Get-NetTCPConnection` / `Get-NetUDPEndpoint` (PowerShell) or
  `System.Net.NetworkInformation.IPGlobalProperties` (C#)
- **No third-party kernel drivers required**
- Admin elevation reminder should be shown at startup

---

## Features to Implement

### Core
- [ ] Poll all active TCP + UDP connections every **N seconds** (configurable, default 2s)
- [ ] Display columns: `Protocol | Local Address | Local Port | Remote Address | Remote Port | State | PID | Process Name`
- [ ] Resolve process name from PID using `Get-Process` or `System.Diagnostics.Process`
- [ ] Color-code by connection state:
  - `LISTEN` → Yellow
  - `ESTABLISHED` → Green
  - `TIME_WAIT` / `CLOSE_WAIT` → Red
  - `UDP` → Cyan

### Diff / Delta Detection
- [ ] Highlight **newly opened** connections since last poll (mark with `[NEW]`)
- [ ] Highlight **closed** connections that disappeared (mark with `[CLOSED]`, fade after 2 cycles)
- [ ] Optional: log new/closed events to a file (`portmon.log`)

### Filtering (interactive keypresses)
- [ ] Press `F` → enter filter string (matches any column: IP, port, process name)
- [ ] Press `S` → cycle sort column (Port / PID / State / Process)
- [ ] Press `L` → toggle LISTEN-only view
- [ ] Press `E` → toggle ESTABLISHED-only view
- [ ] Press `R` → reset all filters
- [ ] Press `Q` → quit

### Display
- [ ] Clear and redraw terminal on each poll cycle (no scroll spam)
- [ ] Show header with: current time, refresh interval, active filter, connection count
- [ ] Paginate if connections exceed terminal height (Up/Down arrow keys)

---

## Code Structure (suggested)

```

PortMonitor/
├── Program.cs (or portmonitor.ps1)
├── Models/
│   └── ConnectionEntry.cs       \# Data model per connection
├── Services/
│   ├── ConnectionPoller.cs      \# Data collection loop
│   └── DiffEngine.cs            \# New/closed delta detection
├── UI/
│   └── ConsoleRenderer.cs       \# Terminal drawing logic
└── README.md

```

---

## Implementation Notes

### For C# (.NET 8)

Use `IPGlobalProperties.GetActiveTcpConnections()` and
`IPGlobalProperties.GetActiveTcpListeners()` for TCP.
For UDP use `GetActiveUdpListeners()`.
PID resolution requires P/Invoke to `GetExtendedTcpTable` (iphlpapi.dll) since
.NET does not expose PIDs natively — provide a helper class for this.

```csharp
// P/Invoke signature hint
[DllImport("iphlpapi.dll", SetLastError = true)]
static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen,
    bool sort, int ipVersion, TCP_TABLE_CLASS tblClass, uint reserved = 0);
```


### For PowerShell 7+

```powershell
Get-NetTCPConnection | Select-Object LocalAddress, LocalPort, RemoteAddress,
    RemotePort, State, OwningProcess,
    @{Name="ProcessName"; Expression={(Get-Process -Id $_.OwningProcess -EA SilentlyContinue).ProcessName}}
```

Use `[Console]::SetCursorPosition(0,0)` for in-place terminal redraw.
Use `$Host.UI.RawUI.ReadKey("NoEcho,AvailableInput")` for non-blocking key input.

---

## Copilot Instructions

1. **Start** by asking me: C\# or PowerShell?
2. Scaffold the full project structure with empty classes/functions first.
3. Implement `ConnectionPoller` first, then `DiffEngine`, then `ConsoleRenderer`.
4. Add keyboard input handling last.
5. Include XML doc comments on all public methods.
6. Add a `--help` flag that prints usage.
7. Add a `--interval <seconds>` CLI argument.
8. Add a `--log <path>` CLI argument to enable file logging.
9. Write unit tests for `DiffEngine` (new/closed detection logic).

---

## Example Output (target)

```
╔══════════════════════════════════════════════════════════════════════════════╗
║  PortMonitor v1.0  |  11:24:03  |  Refresh: 2s  |  Filter: none  |  42 conn ║
╚══════════════════════════════════════════════════════════════════════════════╝
 Proto  Local Addr       L.Port  Remote Addr      R.Port  State        PID   Process
─────────────────────────────────────────────────────────────────────────────────
[NEW]  TCP   0.0.0.0          443    0.0.0.0              0  LISTEN       1234  svchost
       TCP   192.168.1.10    52341  142.250.1.1        443  ESTABLISHED  5678  chrome
[CLS]  TCP   192.168.1.10    51020  10.0.0.1           443  CLOSED          -  -
       UDP   0.0.0.0           5353  *                    *  UDP          1234  svchost

[F]ilter  [S]ort  [L]isten-only  [E]stablished-only  [R]eset  [Q]uit
```


---

## Acceptance Criteria

- Runs on Windows 10/11, no installation required beyond .NET runtime or PS7
- Does not require kernel drivers
- Refreshes without flickering (cursor repositioning, not `cls`)
- Correctly attributes PIDs to process names
- Detects and highlights new/closed connections across poll cycles
- Handles `Access Denied` on process name resolution gracefully (show `[N/A]`)

```

***

## How to Use This

1. **Save** this file as `COPILOT_PROMPT.md` at the root of your repo[^1][^2]
2. Open **GitHub Copilot Chat** in VS Code and type:
```

@workspace Please read COPILOT_PROMPT.md and implement this step by step

```
3. Or use **Copilot Edits** (`Ctrl+Shift+I`) and paste the prompt directly

The key APIs powering this are `Get-NetTCPConnection` / `Get-NetUDPEndpoint` in PowerShell  and `GetExtendedTcpTable` via P/Invoke in C# for PID resolution, since .NET's `IPGlobalProperties` doesn't expose PIDs natively. The Sysinternals TCPView tool uses the same `iphlpapi.dll` approach under the hood.[^3][^4][^1]

Which language do you prefer — **C# (.NET 8)** or **PowerShell 7+**? That will determine the scaffolding Copilot should generate first.
<span style="display:none">[^10][^11][^12][^13][^14][^5][^6][^7][^8][^9]</span>

<div align="center">⁂</div>

[^1]: https://adamtheautomator.com/netstat-port/
[^2]: https://isc.sans.edu/diary/30532
[^3]: https://stackoverflow.com/questions/79439488/how-to-collect-network-stats-using-powershell-or-class-from-remote-machine-in-c
[^4]: https://learn.microsoft.com/en-us/sysinternals/downloads/tcpview
[^5]: https://bartpasmans.tech/how-to-monitor-windows-events-in-real-time-using-powershell/
[^6]: https://www.eltima.com/portmon-alternative.html
[^7]: https://isc.sans.edu/diary/Netstat+but+Better+and+in+PowerShell/30532/
[^8]: https://github.com/mololab/portry
[^9]: https://www.youtube.com/watch?v=hrJUqMXzJos
[^10]: https://www.com-port-monitoring.com
[^11]: https://www.reddit.com/r/PowerShell/comments/mmrqt7/how_to_find_listening_ports_with_netstat_and/
[^12]: https://colinfinck.de/posts/the-enlyze-portsniffer-monitor-serial-parallel-port-traffic-on-modern-windows/
[^13]: https://www.reddit.com/r/sysadmin/comments/1h87jiz/recommendations_for_a_good_serial_port_monitor_on/
[^14]: https://apps.microsoft.com/related/9nkdkfkrgm05?hl=en-US```

