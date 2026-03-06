using PortMonitor.Gui.ViewModels;
using PortMonitor.Models;
using PortMonitor.Services;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.Windows.Threading;

namespace PortMonitor.Gui;

public partial class MainWindow : Window
{
    // ── Services ─────────────────────────────────────────────────────────────
    private readonly ConnectionPoller _poller  = new();
    private readonly DiffEngine       _diff    = new();
    private static readonly HttpClient _http   = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Process _self = Process.GetCurrentProcess();
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck;

    // ── Timer ─────────────────────────────────────────────────────────────────
    private readonly DispatcherTimer  _timer   = new();
    private int                       _intervalSeconds = 5;

    // ── Grid data source ──────────────────────────────────────────────────────
    private readonly ObservableCollection<ConnectionViewModel> _rows = [];

    // ── Filter / sort state ───────────────────────────────────────────────────
    private string           _filter       = string.Empty;
    private readonly HashSet<string> _stateFilters = [];
    private bool             _initialized;
    private bool             _isAdmin;

    // ── Packet capture (pktmon) ────────────────────────────────────────────────
    private bool _captureRunning;
    private string? _activeTraceFileName;
    private string? _activeTracePath;
    private static readonly string _captureLogPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "pktmon.log");

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindow()
    {
        try
        {
            InitializeComponent();

            ConnectionGrid.ItemsSource = _rows;

            // Admin status
            _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
            StatusAdmin.Text       = _isAdmin ? "✓ Administrator" : "⚠ Not elevated — some PIDs show [N/A]";
            StatusAdmin.Foreground = _isAdmin
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.Yellow;

            // Timer setup — start refresh after window is visible
            _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
            _timer.Tick    += OnTimerTick;
            _initialized    = true;   // guard: allow Refresh() to run

            _lastCpuTime  = _self.TotalProcessorTime;
            _lastCpuCheck = DateTime.UtcNow;

            // Restore DNS toggle from persisted settings
            bool dnsOn = AppSettings.Current.GetFlag("DnsEnabled");
            _poller.DnsEnabled   = dnsOn;
            ColRemoteHost.Visibility = dnsOn ? Visibility.Visible : Visibility.Collapsed;

            // Restore refresh interval from persisted settings
            if (int.TryParse(AppSettings.Current.GetString("RefreshInterval"), out int savedInterval) && savedInterval >= 1)
                _intervalSeconds = savedInterval;
            _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);

            Loaded += (_, _) =>
            {
                _timer.Start();
                Refresh();
                _ = FetchPublicIpAsync();
                _ = RefreshCaptureStatusAsync();
            };

            Closed += (_, _) =>
            {
                _timer.Stop();
                _self.Dispose();
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error:\n\n{ex}", "PortMonitor", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    // ── Poll & render ─────────────────────────────────────────────────────────

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Refresh();

        if (DateTime.Now.Second % Math.Max(2, _intervalSeconds) == 0)
            _ = RefreshCaptureStatusAsync();
    }

    private void Refresh()
    {
        if (!_initialized) return;   // called before InitializeComponent() finishes

        IReadOnlyList<ConnectionEntry> polled;
        IReadOnlyList<ConnectionEntry> merged;

        try
        {
            polled = _poller.Poll();
            merged = _diff.Apply(polled);
        }
        catch (Exception ex)
        {
            StatusRefresh.Text = $"Poll error: {ex.Message}";
            return;   // skip this cycle — timer will retry next tick
        }

        // Apply state filter
        var filtered = merged.AsEnumerable();
        if (_stateFilters.Count > 0)
        {
            filtered = filtered.Where(e =>
                (_stateFilters.Contains("New")         && e.IsNew)                                                        ||
                (_stateFilters.Contains("Closed")      && e.IsClosed)                                                     ||
                (_stateFilters.Contains("Listen")      && e.StateDisplay == "LISTEN")                                     ||
                (_stateFilters.Contains("Established") && e.StateDisplay == "ESTABLISHED")                                ||
                (_stateFilters.Contains("TimeWait")    && (e.StateDisplay == "TIME_WAIT" || e.StateDisplay == "CLOSE_WAIT")) ||
                (_stateFilters.Contains("Udp")         && e.Protocol == "UDP")
            );
        }

        // Apply text filter
        if (_filter.Length > 0)
        {
            filtered = filtered.Where(e =>
                e.LocalAddress.Contains(_filter,  StringComparison.OrdinalIgnoreCase) ||
                e.RemoteAddress.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                e.LocalPort.ToString().Contains(_filter)                              ||
                e.RemotePort.ToString().Contains(_filter)                             ||
                e.ProcessName.Contains(_filter,   StringComparison.OrdinalIgnoreCase) ||
                e.StateDisplay.Contains(_filter,  StringComparison.OrdinalIgnoreCase) ||
                e.Pid.ToString().Contains(_filter)                             ||
                e.RemoteHost.Contains(_filter,    StringComparison.OrdinalIgnoreCase));
        }

        // Build filtered+sorted list
        var sorted = filtered.OrderBy(e => e.LocalPort).ToList();

        // In-place update: reuse existing ViewModels where possible to reduce GC pressure
        int i = 0;
        for (; i < sorted.Count; i++)
        {
            var vm = new ConnectionViewModel(sorted[i]);
            if (i < _rows.Count)
                _rows[i] = vm;
            else
                _rows.Add(vm);
        }
        // Remove excess rows
        while (_rows.Count > sorted.Count)
            _rows.RemoveAt(_rows.Count - 1);

        // Status bar
        StatusConnections.Text = $"Connections: {_rows.Count}";
        StatusRefresh.Text     = $"Last refresh: {DateTime.Now:HH:mm:ss}";
        StatusFilter.Text      = _filter.Length > 0 ? $"Filter: {_filter}" : string.Empty;
        UpdateResourceStats();
    }

    // ── Toolbar event handlers ────────────────────────────────────────────────

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = FilterBox.Text.Trim();
        Refresh();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Text = string.Empty;   // triggers TextChanged → Refresh
    }

    private void StateFilter_Click(object sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender;
        var key = (string)btn.Tag;
        if (btn.IsChecked == true)
            _stateFilters.Add(key);
        else
            _stateFilters.Remove(key);
        Refresh();
    }

    private void SetInterval(int seconds)
    {
        _intervalSeconds = seconds;
        _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();

        var dlg = new SettingsPanel(_intervalSeconds, _poller.DnsEnabled) { Owner = this };
        dlg.ShowDialog();

        // Apply interval
        SetInterval(dlg.IntervalSeconds);

        // Apply DNS toggle
        if (dlg.DnsEnabled != _poller.DnsEnabled)
        {
            _poller.DnsEnabled       = dlg.DnsEnabled;
            ColRemoteHost.Visibility = dlg.DnsEnabled ? Visibility.Visible : Visibility.Collapsed;
            AppSettings.Current.SetFlag("DnsEnabled", dlg.DnsEnabled);
        }

        // Persist snapshot + capture + interval settings
        AppSettings.Current.SetString("SnapshotPath",   dlg.SnapshotPath);
        AppSettings.Current.SetString("SnapshotFormat", dlg.SnapshotFormat);
        AppSettings.Current.SetString("CapturePath", dlg.NetshTracePath);
        AppSettings.Current.SetString("RefreshInterval", dlg.IntervalSeconds.ToString());
        AppSettings.Current.Save();

        // Handle action buttons
        if (dlg.DidReset)
        {
            FilterBox.Text = string.Empty;
            _stateFilters.Clear();
            BtnNew.IsChecked      = false;
            BtnClosed.IsChecked   = false;
            BtnListen.IsChecked   = false;
            BtnEstab.IsChecked    = false;
            BtnTimeWait.IsChecked = false;
            BtnUdp.IsChecked      = false;
            SetInterval(5);
            _diff.Reset();
        }

        if (dlg.OpenColors)
        {
            var colorDlg = new SettingsWindow { Owner = this };
            colorDlg.ShowDialog();
        }

        if (dlg.OpenPrereqs)
        {
            var prereqDlg = new PrerequisiteWindow { Owner = this };
            prereqDlg.ShowDialog();
        }

        _timer.Start();
        Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Text              = string.Empty;
        _stateFilters.Clear();
        BtnNew.IsChecked            = false;
        BtnClosed.IsChecked         = false;
        BtnListen.IsChecked         = false;
        BtnEstab.IsChecked          = false;
        BtnTimeWait.IsChecked       = false;
        BtnUdp.IsChecked            = false;
        SetInterval(2);
        _diff.Reset();
        Refresh();
    }

    // ── Resource stats ────────────────────────────────────────────────────────

    private void UpdateResourceStats()
    {
        try
        {
            _self.Refresh();
            var now     = DateTime.UtcNow;
            var cpuUsed = _self.TotalProcessorTime - _lastCpuTime;
            var elapsed = now - _lastCpuCheck;

            double cpuPct = elapsed.TotalMilliseconds > 0
                ? cpuUsed.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100.0
                : 0;

            _lastCpuTime  = _self.TotalProcessorTime;
            _lastCpuCheck = now;

            StatusCpu.Text = $"{cpuPct:0.0}%";

            double memMb = _self.WorkingSet64 / (1024.0 * 1024.0);
            StatusMem.Text = $"{memMb:0.0} MB";
        }
        catch { /* non-critical */ }
    }

    // ── Public IP ─────────────────────────────────────────────────────────────

    private async Task FetchPublicIpAsync()
    {
        try
        {
            using var response = await _http.GetAsync("https://api.ipify.org",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // Guard: reject responses larger than 64 bytes (a valid IP is max ~45 chars)
            if (response.Content.Headers.ContentLength > 64)
            {
                StatusPublicIp.Text = "invalid";
                return;
            }

            string ip = (await response.Content.ReadAsStringAsync()).Trim();
            if (ip.Length > 45) ip = ip[..45];   // IPv6 max is 45 chars
            StatusPublicIp.Text = ip;
        }
        catch
        {
            StatusPublicIp.Text = "unavailable";
        }
    }

    private void Prereqs_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        var dlg = new PrerequisiteWindow { Owner = this };
        dlg.ShowDialog();
        _timer.Start();
    }

    private void Colors_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        var dlg = new SettingsWindow { Owner = this };
        dlg.ShowDialog();
        _timer.Start();
    }

    // ── Snapshot export ───────────────────────────────────────────────────────

    private void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            StatusRefresh.Text = "Snapshot: no data to export";
            return;
        }

        string folder = AppSettings.Current.GetString("SnapshotPath");
        string format = AppSettings.Current.GetString("SnapshotFormat");

        if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
        {
            StatusRefresh.Text = "Snapshot: invalid export folder — configure in Settings";
            return;
        }

        string ext  = format == "text" ? "txt" : "csv";
        string name = $"PortMonitor_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        string path = System.IO.Path.Combine(folder, name);

        try
        {
            using var sw = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8);

            if (format == "csv")
            {
                sw.WriteLine("Tag,Protocol,LocalAddress,LocalPort,RemoteAddress,RemotePort,State,PID,Process,RemoteHost");
                foreach (var r in _rows)
                {
                    sw.WriteLine($"{Esc(r.Tag)},{Esc(r.Protocol)},{Esc(r.LocalAddress)},{r.LocalPort}," +
                                 $"{Esc(r.RemoteAddress)},{Esc(r.RemotePortDisplay)},{Esc(r.StateDisplay)}," +
                                 $"{Esc(r.Pid)},{Esc(r.ProcessName)},{Esc(r.RemoteHost)}");
                }
            }
            else
            {
                sw.WriteLine($"PortMonitor Snapshot — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sw.WriteLine($"Connections: {_rows.Count}");
                sw.WriteLine(new string('-', 120));
                sw.WriteLine($"{"Tag",-6} {"Proto",-5} {"Local Address",-16} {"L.Port",-7} {"Remote Address",-16} " +
                             $"{"R.Port",-7} {"State",-14} {"PID",-7} {"Process",-18} {"Remote Host"}");
                sw.WriteLine(new string('-', 120));
                foreach (var r in _rows)
                {
                    sw.WriteLine($"{r.Tag,-6} {r.Protocol,-5} {r.LocalAddress,-16} {r.LocalPort,-7} " +
                                 $"{r.RemoteAddress,-16} {r.RemotePortDisplay,-7} {r.StateDisplay,-14} " +
                                 $"{r.Pid,-7} {r.ProcessName,-18} {r.RemoteHost}");
                }
            }

            StatusRefresh.Text = $"Snapshot saved: {name}";
        }
        catch (Exception ex)
        {
            StatusRefresh.Text = $"Snapshot error: {ex.Message}";
        }
    }

    private static string Esc(string v) =>
        v.Contains(',') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

    // ── Packet capture (pktmon) ────────────────────────────────────────────────

    private async void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin)
        {
            MessageBox.Show("Packet capture requires Administrator elevation.\nRestart the app as Administrator.",
                "PortMonitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_captureRunning)
        {
            MessageBox.Show("A capture is already running.\nStop it first before starting a new one.",
                "PortMonitor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = ConnectionGrid.SelectedItem as ConnectionViewModel;
        if (selected == null)
        {
            StatusCapture.Text = "Capture: select a connection row first";
            return;
        }

        // Parse port from selected row
        int port = 0;
        if (int.TryParse(selected.RemotePortDisplay, out int rp) && rp > 0)
            port = rp;

        await StartPktmonCaptureAsync(selected.RemoteAddress, port > 0 ? port.ToString() : null);
    }

    private void ManualCapture_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin)
        {
            MessageBox.Show("Packet capture requires Administrator elevation.\nRestart the app as Administrator.",
                "PortMonitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new ManualCaptureWindow(_captureRunning) { Owner = this };
        dlg.ShowDialog();

        if (dlg.RequestStart)
            _ = StartPktmonCaptureAsync(dlg.FilterIp, dlg.FilterPort);
        else if (dlg.RequestStop)
            StopCapture_Click(sender, e);
    }

    private async Task StartPktmonCaptureAsync(string? ip, string? port)
    {
        if (_captureRunning)
        {
            MessageBox.Show("A capture is already running.\nStop it first before starting a new one.",
                "PortMonitor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string traceFolder = AppSettings.Current.GetString("CapturePath");
        if (string.IsNullOrWhiteSpace(traceFolder))
        {
            StatusCapture.Text = "Capture: trace folder not set — configure in Settings";
            return;
        }

        try { System.IO.Directory.CreateDirectory(traceFolder); }
        catch
        {
            StatusCapture.Text = $"Capture: cannot create folder {traceFolder}";
            return;
        }

        // Build trace file name
        string label = string.IsNullOrWhiteSpace(ip) ? "all" : ip!;
        if (!string.IsNullOrWhiteSpace(port)) label += $"_{port}";
        string fileName = $"capture_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.etl";
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        string tracePath = System.IO.Path.Combine(traceFolder, fileName);

        // Step 1: Remove any existing filters
        CaptureLog("CLEAN  pktmon filter remove");
        await RunCommandAsync("pktmon", "filter remove", 10000);

        // Step 2: Add filters (IP and/or port)
        bool hasIp   = !string.IsNullOrWhiteSpace(ip);
        bool hasPort = !string.IsNullOrWhiteSpace(port);

        if (hasIp || hasPort)
        {
            string filterFlags = "";
            if (hasIp)   filterFlags += $" -i {ip}";
            if (hasPort) filterFlags += $" -p {port}";

            string filterArgs = $"filter add PortMonCapture{filterFlags}";
            CaptureLog($"FILTER pktmon {filterArgs}");
            var filterResult = await RunCommandAsync("pktmon", filterArgs, 10000);
            CaptureLog($"RESULT success={filterResult.Success}  output={filterResult.Message}");
            if (!filterResult.Success)
            {
                UpdateCaptureStatusVisual($"Capture filter error: {filterResult.Message}", System.Windows.Media.Brushes.Yellow);
                return;
            }
        }
        else
        {
            CaptureLog("FILTER none — capturing all traffic");
        }

        // Step 3: Start capture — capture full packets at NIC level
        string startArgs = $"start --capture --comp nics --pkt-size 0 --file-name \"{tracePath}\"";
        CaptureLog($"START  pktmon {startArgs}");
        var startResult = await RunCommandAsync("pktmon", startArgs, 15000);
        CaptureLog($"RESULT success={startResult.Success}  output={startResult.Message}");

        if (!startResult.Success)
        {
            UpdateCaptureStatusVisual($"Capture error: {startResult.Message}", System.Windows.Media.Brushes.Yellow);
            return;
        }

        _captureRunning = true;
        _activeTraceFileName = fileName;
        _activeTracePath = tracePath;
        UpdateCaptureStatusVisual($"🔴 Capturing → {fileName}", System.Windows.Media.Brushes.OrangeRed);
        await RefreshCaptureStatusAsync();
    }

    private async void StopCapture_Click(object sender, RoutedEventArgs e)
    {
        UpdateCaptureStatusVisual("Capture: stopping...", System.Windows.Media.Brushes.Yellow);

        string? etlPath = _activeTracePath;

        CaptureLog("STOP   pktmon stop");
        var result = await RunCommandAsync("pktmon", "stop", 15000);
        CaptureLog($"RESULT success={result.Success}  output={result.Message}");

        // Clean up filters
        CaptureLog("CLEAN  pktmon filter remove");
        await RunCommandAsync("pktmon", "filter remove", 10000);

        _captureRunning = false;
        _activeTraceFileName = null;
        _activeTracePath = null;

        if (!result.Success)
        {
            UpdateCaptureStatusVisual($"Capture: {result.Message}", (System.Windows.Media.Brush)FindResource("FgPrimaryBrush"));
            return;
        }

        // Convert .etl to .pcap
        if (!string.IsNullOrEmpty(etlPath) && System.IO.File.Exists(etlPath))
        {
            string pcapPath = System.IO.Path.ChangeExtension(etlPath, ".pcap");
            string convertArgs = $"etl2pcap \"{etlPath}\" --out \"{pcapPath}\"";
            CaptureLog($"CONVERT pktmon {convertArgs}");
            UpdateCaptureStatusVisual("Converting to pcap...", System.Windows.Media.Brushes.Yellow);

            var convertResult = await RunCommandAsync("pktmon", convertArgs, 30000);
            CaptureLog($"RESULT success={convertResult.Success}  output={convertResult.Message}");

            if (convertResult.Success)
            {
                string pcapName = System.IO.Path.GetFileName(pcapPath);
                UpdateCaptureStatusVisual($"✓ Capture saved: {pcapName}", System.Windows.Media.Brushes.LimeGreen);
            }
            else
            {
                UpdateCaptureStatusVisual($"✓ Stopped (pcap convert failed: {convertResult.Message})", System.Windows.Media.Brushes.Yellow);
            }
        }
        else
        {
            UpdateCaptureStatusVisual("✓ Capture stopped", System.Windows.Media.Brushes.LimeGreen);
        }
    }

    private async Task RefreshCaptureStatusAsync()
    {
        try
        {
            bool running = await IsCaptureRunningAsync();
            _captureRunning = running;

            if (!running)
            {
                _activeTraceFileName = null;
                if (StatusCapture.Text.StartsWith("🔴 Capturing", StringComparison.Ordinal))
                    UpdateCaptureStatusVisual("Capture: idle", (System.Windows.Media.Brush)FindResource("FgPrimaryBrush"));
            }
            else if (!StatusCapture.Text.StartsWith("🔴 Capturing", StringComparison.Ordinal))
            {
                UpdateCaptureStatusVisual($"🔴 Capturing{(_activeTraceFileName is null ? string.Empty : $" → {_activeTraceFileName}")}", System.Windows.Media.Brushes.OrangeRed);
            }
        }
        catch { }
    }

    private void UpdateCaptureStatusVisual(string text, System.Windows.Media.Brush brush)
    {
        StatusCapture.Text = text;
        StatusCapture.Foreground = brush;
    }

    private async Task<bool> IsCaptureRunningAsync()
    {
        var result = await RunCommandAsync("pktmon", "status", 10000);
        if (!result.Success)
            return false;

        // pktmon status prints "Logger:  Active" when a capture is running
        return result.Message.Contains("Active", StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureLog(string entry)
    {
        try
        {
            System.IO.File.AppendAllText(_captureLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {entry}{Environment.NewLine}");
        }
        catch { /* non-critical */ }
    }

    private static async Task<(bool Success, string Message)> RunCommandAsync(string exe, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
            Task waitTask = proc.WaitForExitAsync();
            Task completed = await Task.WhenAny(waitTask, Task.Delay(timeoutMs));

            if (completed != waitTask)
            {
                try { proc.Kill(true); } catch { }
                return (false, "command timed out");
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            string message = string.IsNullOrWhiteSpace(stderr)
                ? stdout.Trim()
                : $"{stdout}\n{stderr}".Trim();

            if (proc.ExitCode != 0)
                return (false, string.IsNullOrWhiteSpace(message) ? $"exit code {proc.ExitCode}" : message);

            return (true, message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
