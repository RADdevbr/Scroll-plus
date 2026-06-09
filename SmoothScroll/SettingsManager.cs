using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmoothScroll;

/// <summary>Scroll smoothing model.</summary>
public enum ScrollMode
{
    /// <summary>Velocity that decays exponentially each frame (friction + steps).</summary>
    Inertia,

    /// <summary>Eases the remaining distance toward a target each frame (web-like ease-out).</summary>
    Momentum,
}

/// <summary>
/// Strongly-typed, serializable application settings. A single instance of this
/// object is shared (by reference) between the UI and the <see cref="ScrollEngine"/>,
/// so edits made in the settings form take effect immediately.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Master on/off switch (mirrors the tray toggle).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which smoothing model to use. Defaults to the web-like Momentum feel.</summary>
    public ScrollMode Mode { get; set; } = ScrollMode.Momentum;

    /// <summary>Multiplier applied to the raw wheel delta. Range 0.5x .. 3.0x.</summary>
    public double Sensitivity { get; set; } = 1.0;

    /// <summary>
    /// Momentum mode: fraction of the remaining distance eased out each frame.
    /// Lower = longer, smoother glide; higher = snappier. Range 0.08 .. 0.40.
    /// </summary>
    public double EaseFactor { get; set; } = 0.18;

    /// <summary>Per-frame exponential decay factor (inertia). Range 0.80 .. 0.98.</summary>
    public double Friction { get; set; } = 0.90;

    /// <summary>
    /// How finely the accumulated velocity is subdivided per frame. Higher values
    /// emit more, smaller messages per scroll event. Range 4 .. 20.
    /// </summary>
    public int StepsPerEvent { get; set; } = 8;

    /// <summary>Frame cadence in milliseconds (~8ms == ~120fps).</summary>
    public int FrameIntervalMs { get; set; } = 8;

    /// <summary>When true, smoothing is applied only to windows in <see cref="TargetWindows"/>.</summary>
    public bool TargetOnly { get; set; } = true;

    /// <summary>
    /// Process names or window-title fragments to match (case-insensitive).
    /// Defaults cover RadiAnt DICOM Viewer and Weasis.
    /// </summary>
    public List<string> TargetWindows { get; set; } = new() { "RadiAnt", "Weasis" };

    public AppSettings Clone() => new()
    {
        Enabled = Enabled,
        Mode = Mode,
        Sensitivity = Sensitivity,
        EaseFactor = EaseFactor,
        Friction = Friction,
        StepsPerEvent = StepsPerEvent,
        FrameIntervalMs = FrameIntervalMs,
        TargetOnly = TargetOnly,
        TargetWindows = new List<string>(TargetWindows),
    };
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under
/// %AppData%\SmoothScroll\settings.json. No external dependencies; uses
/// System.Text.Json from the base SDK.
/// </summary>
public sealed class SettingsManager
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }, // serialize ScrollMode as text
    };

    public SettingsManager()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmoothScroll");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public string FilePath => _filePath;

    /// <summary>Reads settings from disk, falling back to defaults on any error.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable file -> start clean rather than crashing.
            return new AppSettings();
        }
    }

    /// <summary>Persists settings to disk. Swallows IO errors (best-effort).</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Best-effort persistence; a transient IO failure should not crash the app.
        }
    }
}
