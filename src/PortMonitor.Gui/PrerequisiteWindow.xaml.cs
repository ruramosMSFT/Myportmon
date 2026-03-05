using PortMonitor.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PortMonitor.Gui;

public partial class PrerequisiteWindow : Window
{
    private IReadOnlyList<PrerequisiteResult> _results = [];

    public PrerequisiteWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RunCheck();
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    private void RunCheck()
    {
        _results = PrerequisiteChecker.CheckAll();
        RenderResults(_results);

        bool wingetOk   = _results.Any(r => r.Name == "winget" && r.IsOk);
        bool hasFixable = _results.Any(r => !r.IsOk && r.CanAutoFix);
        BtnInstall.IsEnabled = wingetOk && hasFixable;
    }

    private void RenderResults(IReadOnlyList<PrerequisiteResult> results)
    {
        ResultsPanel.Children.Clear();

        foreach (var r in results)
        {
            bool isSoft = !r.IsOk && r.Name == "Administrator";

            var row = new Border
            {
                Background      = new SolidColorBrush(r.IsOk  ? Color.FromRgb(0, 30, 0) :
                                                       isSoft  ? Color.FromRgb(30, 28, 0) :
                                                                 Color.FromRgb(35, 0, 0)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(12, 8, 12, 8),
                Margin          = new Thickness(0, 0, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Icon
            var icon = new TextBlock
            {
                Text       = r.IsOk ? "✓" : isSoft ? "!" : "✗",
                FontSize   = 14,
                FontWeight = FontWeights.Bold,
                Foreground = r.IsOk  ? Brushes.LimeGreen :
                             isSoft  ? Brushes.Yellow :
                                       Brushes.OrangeRed,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            // Name
            var name = new TextBlock
            {
                Text              = r.Name,
                FontFamily        = new FontFamily("Consolas"),
                FontSize          = 12,
                FontWeight        = FontWeights.SemiBold,
                Foreground        = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);

            // Message (+ install hint)
            string fullMsg = r.Message;
            if (!r.IsOk && r.CanAutoFix && r.WingetId is not null)
                fullMsg += $"\n  ↳ winget install {r.WingetId}";

            var msg = new TextBlock
            {
                Text              = fullMsg,
                FontFamily        = new FontFamily("Consolas"),
                FontSize          = 11,
                Foreground        = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                TextWrapping      = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(msg, 2);

            grid.Children.Add(icon);
            grid.Children.Add(name);
            grid.Children.Add(msg);
            row.Child = grid;
            ResultsPanel.Children.Add(row);
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        BtnInstall.IsEnabled = false;

        // Show a progress row
        var progressBlock = new TextBlock
        {
            FontFamily   = new FontFamily("Consolas"),
            FontSize     = 11,
            Foreground   = Brushes.Cyan,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(12, 8, 12, 4)
        };
        ResultsPanel.Children.Add(progressBlock);

        await Task.Run(() =>
        {
            PrerequisiteChecker.InstallMissing(_results, msg =>
            {
                Dispatcher.Invoke(() =>
                {
                    progressBlock.Text += msg + "\n";
                });
            });
        });

        // Re-run check after installation
        RunCheck();
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => RunCheck();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
