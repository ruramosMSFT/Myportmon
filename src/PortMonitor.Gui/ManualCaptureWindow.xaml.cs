using System.Windows;

namespace PortMonitor.Gui;

public partial class ManualCaptureWindow : Window
{
    // ── Results (read by MainWindow after dialog closes) ─────────────────────
    public bool    RequestStart { get; private set; }
    public bool    RequestStop  { get; private set; }
    public string? FilterIp    { get; private set; }
    public string? FilterPort  { get; private set; }

    public ManualCaptureWindow(bool captureRunning)
    {
        InitializeComponent();

        if (captureRunning)
        {
            TxtStatus.Text = "🔴 A capture is currently running.";
            BtnStartCapture.IsEnabled = false;
        }
        else
        {
            TxtStatus.Text = "No capture running.";
            BtnStopCapture.IsEnabled = false;
        }
    }

    private void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        string ip   = TxtIp.Text.Trim();
        string port = TxtPort.Text.Trim();

        // Validate port if provided
        if (!string.IsNullOrEmpty(port))
        {
            if (!int.TryParse(port, out int p) || p < 1 || p > 65535)
            {
                TxtStatus.Text = "⚠ Port must be a number between 1 and 65535.";
                return;
            }
        }

        FilterIp   = string.IsNullOrEmpty(ip)   ? null : ip;
        FilterPort = string.IsNullOrEmpty(port)  ? null : port;
        RequestStart = true;
        DialogResult = true;
    }

    private void StopCapture_Click(object sender, RoutedEventArgs e)
    {
        RequestStop = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
