using System.Windows;

namespace PortMonitor.Gui;

public partial class SettingsPanel : Window
{
    // ── Results (read by MainWindow after dialog closes) ─────────────────────
    public int    IntervalSeconds  { get; private set; } = 5;
    public bool   DnsEnabled       { get; private set; } = true;
    public string SnapshotPath     { get; private set; } = string.Empty;
    public string SnapshotFormat   { get; private set; } = "csv";
    public string NetshTracePath   { get; private set; } = string.Empty;
    public bool   DidReset         { get; private set; }
    public bool   OpenColors       { get; private set; }
    public bool   OpenPrereqs      { get; private set; }

    public SettingsPanel(int currentInterval, bool currentDns)
    {
        InitializeComponent();

        // Restore current values
        IntervalSeconds = currentInterval;
        DnsEnabled      = currentDns;
        SnapshotPath    = AppSettings.Current.GetString("SnapshotPath");
        SnapshotFormat  = AppSettings.Current.GetString("SnapshotFormat");
        NetshTracePath  = AppSettings.Current.GetString("CapturePath");

        switch (currentInterval)
        {
            case 5:  Rb5s.IsChecked  = true; break;
            case 10: Rb10s.IsChecked = true; break;
            default:
                RbCustom.IsChecked = true;
                TxtCustomInterval.Text = currentInterval.ToString();
                break;
        }
        ChkDns.IsChecked       = currentDns;
        TxtSnapshotPath.Text   = SnapshotPath;
        TxtNetshPath.Text      = NetshTracePath;
        RbCsv.IsChecked        = SnapshotFormat == "csv";
        RbText.IsChecked       = SnapshotFormat == "text";

        // Wire changes
        Rb5s.Checked  += (_, _) => IntervalSeconds = 5;
        Rb10s.Checked += (_, _) => IntervalSeconds = 10;
        RbCustom.Checked += (_, _) => ApplyCustomInterval();
        TxtCustomInterval.TextChanged += (_, _) => { if (RbCustom.IsChecked == true) ApplyCustomInterval(); };

        ChkDns.Checked   += (_, _) => DnsEnabled = true;
        ChkDns.Unchecked += (_, _) => DnsEnabled = false;

        RbCsv.Checked  += (_, _) => SnapshotFormat = "csv";
        RbText.Checked += (_, _) => SnapshotFormat = "text";

        TxtSnapshotPath.TextChanged += (_, _) => SnapshotPath = TxtSnapshotPath.Text.Trim();
        TxtNetshPath.TextChanged    += (_, _) => NetshTracePath = TxtNetshPath.Text.Trim();
    }

    private void ApplyCustomInterval()
    {
        if (int.TryParse(TxtCustomInterval.Text.Trim(), out int val) && val >= 1)
            IntervalSeconds = val;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath        = SnapshotPath,
            Description         = "Select snapshot export folder",
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtSnapshotPath.Text = dlg.SelectedPath;
    }

    private void BrowseNetshFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath        = NetshTracePath,
            Description         = "Select capture output folder",
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtNetshPath.Text = dlg.SelectedPath;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        DidReset = true;
        DialogResult = true;
    }

    private void Colors_Click(object sender, RoutedEventArgs e)
    {
        OpenColors = true;
        DialogResult = true;
    }

    private void Prereqs_Click(object sender, RoutedEventArgs e)
    {
        OpenPrereqs = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
