using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PortMonitor.Gui;

/// <summary>
/// Persistent color settings for the application.
/// Stored as resource-key → hex-color in %AppData%\PortMonitor\settings.json.
/// </summary>
public class AppSettings
{
    // ── Singleton current instance ────────────────────────────────────────────
    public static AppSettings Current { get; private set; } = new();

    // ── Internal store ────────────────────────────────────────────────────────
    private Dictionary<string, string> _colors;

    public AppSettings()
    {
        _colors = new Dictionary<string, string>
        {
            // App palette
            ["BgPrimaryBrush"]      = "#FF1E1E1E",
            ["BgSecondaryBrush"]    = "#FF252526",
            ["BgControlBrush"]      = "#FF2D2D30",
            ["BgControlHoverBrush"] = "#FF3E3E42",
            ["BgAccentBrush"]       = "#FF007ACC",
            ["BorderBrush"]         = "#FF3F3F46",
            ["FgPrimaryBrush"]      = "#FFD4D4D4",
            ["FgMutedBrush"]        = "#FF9D9D9D",
            // Connection state backgrounds
            ["ColorNew"]            = "#FF1A3A1A",
            ["ColorClosed"]         = "#FF2A2A2A",
            ["ColorListen"]         = "#FF2B2B00",
            ["ColorEstablished"]    = "#FF002800",
            ["ColorTimeWait"]       = "#FF2E0000",
            ["ColorUdp"]            = "#FF002535",
            // Connection state foregrounds
            ["FgNew"]               = "#FF90FF90",
            ["FgClosed"]            = "#FF707070",
            ["FgListen"]            = "#FFFFD700",
            ["FgEstablished"]       = "#FF00DD00",
            ["FgTimeWait"]          = "#FFFF7070",
            ["FgUdp"]               = "#FF00DDDD",
            ["FgDefault"]           = "#FFD4D4D4",
        };
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public string GetHex(string key) =>
        _colors.TryGetValue(key, out var v) ? v : "#FF808080";

    public void SetHex(string key, string hex) => _colors[key] = hex;

    public AppSettings Clone()
    {
        var clone = new AppSettings();
        clone._colors = new Dictionary<string, string>(_colors);
        return clone;
    }

    public static void CopyFrom(AppSettings source)
    {
        Current._colors = new Dictionary<string, string>(source._colors);
    }

    /// <summary>Pushes all stored colors into WPF Application.Resources.</summary>
    public void Apply()
    {
        foreach (var (key, hex) in _colors)
            ApplyOne(key, hex);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PortMonitor", "settings.json");

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(
                _colors, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }

    public static void LoadAndApply()
    {
        Current = Load();
        Current.Apply();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    var s = new AppSettings();
                    foreach (var (k, v) in dict)
                        if (s._colors.ContainsKey(k)) s._colors[k] = v;
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    private static void ApplyOne(string key, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (Application.Current.Resources[key] is SolidColorBrush brush)
                brush.Color = color;
            else
                Application.Current.Resources[key] = new SolidColorBrush(color);
        }
        catch { }
    }
}
