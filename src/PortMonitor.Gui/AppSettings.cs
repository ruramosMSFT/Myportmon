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
    private Dictionary<string, bool> _flags;
    private Dictionary<string, string> _strings;

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
            ["GridLineBrush"]        = "#FF2D2D30",
            ["BgMenuHoverBrush"]    = "#FF3E3E42",
            ["FgMenuHoverBrush"]    = "#FFFFFFFF",
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
        _flags = new Dictionary<string, bool>
        {
            ["DnsEnabled"] = true,
        };
        _strings = new Dictionary<string, string>
        {
            ["SnapshotPath"]   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            ["SnapshotFormat"] = "csv",   // "csv" or "text"
            ["NetshTracePath"] = @"C:\Temp",
        };
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public string GetHex(string key) =>
        _colors.TryGetValue(key, out var v) ? v : "#FF808080";

    public void SetHex(string key, string hex) => _colors[key] = hex;

    public bool GetFlag(string key) =>
        _flags.TryGetValue(key, out var v) && v;

    public void SetFlag(string key, bool value) => _flags[key] = value;

    public string GetString(string key) =>
        _strings.TryGetValue(key, out var v) ? v : string.Empty;

    public void SetString(string key, string value) => _strings[key] = value;

    public AppSettings Clone()
    {
        var clone = new AppSettings();
        clone._colors  = new Dictionary<string, string>(_colors);
        clone._flags   = new Dictionary<string, bool>(_flags);
        clone._strings = new Dictionary<string, string>(_strings);
        return clone;
    }

    public static void CopyFrom(AppSettings source)
    {
        Current._colors  = new Dictionary<string, string>(source._colors);
        Current._flags   = new Dictionary<string, bool>(source._flags);
        Current._strings = new Dictionary<string, string>(source._strings);
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
            var payload = new Dictionary<string, object>
            {
                ["colors"]  = _colors,
                ["flags"]   = _flags,
                ["strings"] = _strings
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(
                payload, new JsonSerializerOptions { WriteIndented = true }));
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

                // Try new format: { "colors": {...}, "flags": {...} }
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("colors", out var colorsEl))
                {
                    var s = new AppSettings();
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(colorsEl.GetRawText());
                    if (dict != null)
                        foreach (var (k, v) in dict)
                            if (s._colors.ContainsKey(k)) s._colors[k] = v;

                    if (doc.RootElement.TryGetProperty("flags", out var flagsEl))
                    {
                        var flags = JsonSerializer.Deserialize<Dictionary<string, bool>>(flagsEl.GetRawText());
                        if (flags != null)
                            foreach (var (k, v) in flags)
                                if (s._flags.ContainsKey(k)) s._flags[k] = v;
                    }

                    if (doc.RootElement.TryGetProperty("strings", out var stringsEl))
                    {
                        var strs = JsonSerializer.Deserialize<Dictionary<string, string>>(stringsEl.GetRawText());
                        if (strs != null)
                            foreach (var (k, v) in strs)
                                if (s._strings.ContainsKey(k)) s._strings[k] = v;
                    }

                    return s;
                }

                // Legacy format: flat { "BgPrimaryBrush": "#FF...", ... }
                var legacy = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (legacy != null)
                {
                    var s = new AppSettings();
                    foreach (var (k, v) in legacy)
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
            // Always replace (not mutate) — frozen brushes can't be mutated,
            // and DynamicResource picks up the replacement automatically.
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
        catch { }
    }
}
