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
            BtnDns.IsChecked     = dnsOn;
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

    private void IntervalCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        _intervalSeconds = IntervalCombo.SelectedIndex switch
        {
            0 => 1,
            1 => 2,
            2 => 5,
            3 => 10,
            _ => 2
        };
        _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
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
        IntervalCombo.SelectedIndex = 1;
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

    private void DnsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = BtnDns.IsChecked == true;
        _poller.DnsEnabled       = enabled;
        ColRemoteHost.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        // Persist
        AppSettings.Current.SetFlag("DnsEnabled", enabled);
        AppSettings.Current.Save();

        // Immediate refresh so the column appears/disappears right away
        Refresh();
    }
}
