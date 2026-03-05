using PortMonitor.Gui.ViewModels;
using PortMonitor.Models;
using PortMonitor.Services;
using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PortMonitor.Gui;

public partial class MainWindow : Window
{
    // ── Services ─────────────────────────────────────────────────────────────
    private readonly ConnectionPoller _poller  = new();
    private readonly DiffEngine       _diff    = new();

    // ── Timer ─────────────────────────────────────────────────────────────────
    private readonly DispatcherTimer  _timer   = new();
    private int                       _intervalSeconds = 2;

    // ── Grid data source ──────────────────────────────────────────────────────
    private readonly ObservableCollection<ConnectionViewModel> _rows = [];

    // ── Filter / sort state ───────────────────────────────────────────────────
    private string    _filter      = string.Empty;
    private SortMode  _sort        = SortMode.Port;
    private bool      _initialized;

    private enum SortMode { Port, Pid, State, Process }

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

            Loaded += (_, _) =>
            {
                _timer.Start();
                Refresh();
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
            MessageBox.Show($"Error polling connections:\n\n{ex}", "PortMonitor",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Apply text filter
        var filtered = merged.AsEnumerable();
        if (_filter.Length > 0)
        {
            filtered = filtered.Where(e =>
                e.LocalAddress.Contains(_filter,  StringComparison.OrdinalIgnoreCase) ||
                e.RemoteAddress.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                e.LocalPort.ToString().Contains(_filter)                              ||
                e.RemotePort.ToString().Contains(_filter)                             ||
                e.ProcessName.Contains(_filter,   StringComparison.OrdinalIgnoreCase) ||
                e.StateDisplay.Contains(_filter,  StringComparison.OrdinalIgnoreCase) ||
                e.Pid.ToString().Contains(_filter));
        }

        // Apply sort
        var sorted = _sort switch
        {
            SortMode.Pid     => filtered.OrderBy(e => e.Pid),
            SortMode.State   => filtered.OrderBy(e => e.StateDisplay),
            SortMode.Process => filtered.OrderBy(e => e.ProcessName),
            _                => filtered.OrderBy(e => e.LocalPort)
        };

        // Rebuild collection — efficient for typical sizes (<500 rows)
        _rows.Clear();
        foreach (var entry in sorted)
            _rows.Add(new ConnectionViewModel(entry));

        // Status bar
        StatusConnections.Text = $"Connections: {_rows.Count}";
        StatusRefresh.Text     = $"Last refresh: {DateTime.Now:HH:mm:ss}";
        StatusFilter.Text      = _filter.Length > 0 ? $"Filter: {_filter}" : string.Empty;
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

    private void SortCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        _sort = (SortCombo.SelectedIndex) switch
        {
            0 => SortMode.Port,
            1 => SortMode.Pid,
            2 => SortMode.State,
            3 => SortMode.Process,
            _ => SortMode.Port
        };
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
        _sort                       = SortMode.Port;
        SortCombo.SelectedIndex     = 0;
        IntervalCombo.SelectedIndex = 1;
        _diff.Reset();
        Refresh();
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
}
