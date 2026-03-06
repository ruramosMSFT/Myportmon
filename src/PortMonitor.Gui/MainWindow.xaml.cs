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
    private int                       _intervalSeconds = 2;

    // ── Grid data source ──────────────────────────────────────────────────────
    private readonly ObservableCollection<ConnectionViewModel> _rows = [];

    // ── Filter / sort state ───────────────────────────────────────────────────
    private string           _filter       = string.Empty;
    private readonly HashSet<string> _stateFilters = [];
    private bool             _initialized;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindow()
    {
        try
        {
            InitializeComponent();

            ConnectionGrid.ItemsSource = _rows;

            // Admin status
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
            StatusAdmin.Text       = isAdmin ? "✓ Administrator" : "⚠ Not elevated — some PIDs show [N/A]";
            StatusAdmin.Foreground = isAdmin
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

            Loaded += (_, _) =>
            {
                _timer.Start();
                Refresh();
                _ = FetchPublicIpAsync();
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

    private void OnTimerTick(object? sender, EventArgs e) => Refresh();

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

        // Persist snapshot settings
        AppSettings.Current.SetString("SnapshotPath",   dlg.SnapshotPath);
        AppSettings.Current.SetString("SnapshotFormat", dlg.SnapshotFormat);
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
            SetInterval(2);
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
}
