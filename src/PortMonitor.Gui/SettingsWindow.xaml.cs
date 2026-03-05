using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PortMonitor.Gui;

public partial class SettingsWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────
    private readonly AppSettings _original;   // to revert on Cancel
    private readonly AppSettings _working;    // edited copy

    // Maps resource key → (swatch rectangle, hex textbox) for reset
    private readonly Dictionary<string, (Rectangle Swatch, TextBox Box)> _rows = [];

    // ── Color entry definitions ───────────────────────────────────────────────
    private static readonly (string Label, string Key)[] AppColorEntries =
    [
        ("Main Background",    "BgPrimaryBrush"),
        ("Toolbar Background", "BgSecondaryBrush"),
        ("Control Background", "BgControlBrush"),
        ("Control Hover",      "BgControlHoverBrush"),
        ("Accent / Pressed",   "BgAccentBrush"),
        ("Border",             "BorderBrush"),
        ("Primary Text",       "FgPrimaryBrush"),
        ("Muted Text",         "FgMutedBrush"),
    ];

    private static readonly (string Label, string Key)[] StateColorEntries =
    [
        ("New — Background",         "ColorNew"),
        ("New — Text",               "FgNew"),
        ("Closed — Background",      "ColorClosed"),
        ("Closed — Text",            "FgClosed"),
        ("Listen — Background",      "ColorListen"),
        ("Listen — Text",            "FgListen"),
        ("Established — Background", "ColorEstablished"),
        ("Established — Text",       "FgEstablished"),
        ("Time Wait — Background",   "ColorTimeWait"),
        ("Time Wait — Text",         "FgTimeWait"),
        ("UDP — Background",         "ColorUdp"),
        ("UDP — Text",               "FgUdp"),
        ("Default Text",             "FgDefault"),
    ];

    // ── Constructor ───────────────────────────────────────────────────────────
    public SettingsWindow()
    {
        InitializeComponent();

        _original = AppSettings.Current.Clone();
        _working  = AppSettings.Current.Clone();

        BuildSection("App Colors",              AppColorEntries);
        BuildSection("Connection State Colors", StateColorEntries);
    }

    // ── Build UI ──────────────────────────────────────────────────────────────

    private void BuildSection(string title, (string Label, string Key)[] entries)
    {
        // Section header
        ColorPanel.Children.Add(new TextBlock
        {
            Text       = title,
            FontSize   = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Margin     = new Thickness(0, 14, 0, 4)
        });

        // Divider
        ColorPanel.Children.Add(new Separator
        {
            Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin     = new Thickness(0, 0, 0, 6)
        });

        foreach (var (label, key) in entries)
            ColorPanel.Children.Add(BuildRow(label, key));
    }

    private UIElement BuildRow(string label, string key)
    {
        var hex   = _working.GetHex(key);
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = false };

        // Label
        var lbl = new TextBlock
        {
            Text                = label,
            Width               = 195,
            VerticalAlignment   = VerticalAlignment.Center,
            Foreground          = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
            FontFamily          = new FontFamily("Consolas"),
            FontSize            = 12
        };
        DockPanel.SetDock(lbl, Dock.Left);

        // Color swatch (clickable)
        var swatch = new Rectangle
        {
            Width           = 36,
            Height          = 22,
            Margin          = new Thickness(0, 0, 8, 0),
            Cursor          = Cursors.Hand,
            Fill            = HexToBrush(hex),
            Stroke          = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            StrokeThickness = 1,
            ToolTip         = "Click to open color picker"
        };
        DockPanel.SetDock(swatch, Dock.Left);

        // Hex text input
        var box = new TextBox
        {
            Text              = hex,
            Width             = 105,
            Background        = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground        = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
            BorderBrush       = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            CaretBrush        = Brushes.White,
            FontFamily        = new FontFamily("Consolas"),
            FontSize          = 12,
            Padding           = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip           = "Enter hex color (#AARRGGBB or #RRGGBB)"
        };
        DockPanel.SetDock(box, Dock.Left);

        // Wire up events
        swatch.MouseLeftButtonUp += (_, _) => PickColor(key, swatch, box);
        box.TextChanged += (_, _) =>
        {
            try
            {
                var color  = (Color)ColorConverter.ConvertFromString(box.Text);
                swatch.Fill = new SolidColorBrush(color);
                _working.SetHex(key, box.Text);
            }
            catch { /* invalid hex while typing — ignore */ }
        };

        _rows[key] = (swatch, box);

        panel.Children.Add(lbl);
        panel.Children.Add(swatch);
        panel.Children.Add(box);
        return panel;
    }

    // ── Color picker ──────────────────────────────────────────────────────────

    private static void PickColor(string key, Rectangle swatch, TextBox box)
    {
        var current = (SolidColorBrush)swatch.Fill;
        var c       = current.Color;

        using var dlg = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen      = true,
            Color         = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B)
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var p   = dlg.Color;
            // Triggers TextChanged → updates swatch fill + _working
            box.Text = $"#FF{p.R:X2}{p.G:X2}{p.B:X2}";
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _working.Apply();
        _working.Save();
        AppSettings.CopyFrom(_working);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _original.Apply();   // revert any in-memory resource changes
        Close();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        foreach (var (key, (_, box)) in _rows)
            box.Text = defaults.GetHex(key);   // triggers TextChanged → swatch + _working
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SolidColorBrush HexToBrush(string hex)
    {
        try   { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Colors.Gray); }
    }
}
