using System.Windows;

namespace PortMonitor.Gui;

public partial class SettingsPanel : Window
{
    // ── Results (read by MainWindow after dialog closes) ─────────────────────
    public int    IntervalSeconds { get; private set; } = 2;
    public bool   DnsEnabled      { get; private set; } = true;
    public bool   DidReset        { get; private set; }
    public bool   OpenColors      { get; private set; }
    public bool   OpenPrereqs     { get; private set; }

    public SettingsPanel(int currentInterval, bool currentDns)
    {
        InitializeComponent();

        // Restore current values
        IntervalSeconds = currentInterval;
        DnsEnabled      = currentDns;

        switch (currentInterval)
        {
            case 1:  Rb1s.IsChecked  = true; break;
            case 5:  Rb5s.IsChecked  = true; break;
            case 10: Rb10s.IsChecked = true; break;
            default: Rb2s.IsChecked  = true; break;
        }
        ChkDns.IsChecked = currentDns;

        // Wire radio changes
        Rb1s.Checked  += (_, _) => IntervalSeconds = 1;
        Rb2s.Checked  += (_, _) => IntervalSeconds = 2;
        Rb5s.Checked  += (_, _) => IntervalSeconds = 5;
        Rb10s.Checked += (_, _) => IntervalSeconds = 10;

        ChkDns.Checked   += (_, _) => DnsEnabled = true;
        ChkDns.Unchecked += (_, _) => DnsEnabled = false;
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
