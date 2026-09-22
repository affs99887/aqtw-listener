namespace Listener.App;

public enum OverlayPositionMode { GameTopCenter, Manual }

public sealed class Settings
{
    public const string GameProcessName = "UAGame";
    public string DeviceId { get; set; } = "";
    public string Hotkey { get; set; } = "Ctrl+Alt+L";
    public string InteractionHotkey { get; set; } = "Ctrl+Alt+C";
    public bool PositionLocked { get; set; } = true;
    public string? MonitorName { get; set; }
    public double MonitorOffsetX { get; set; }
    public double MonitorOffsetY { get; set; }
    public double FontScale { get; set; } = 1;
    public bool AutomaticRecognition { get; set; } = true;
    public double Left { get; set; } = 32;
    public double Top { get; set; } = 110;
    public OverlayPositionMode? PositionMode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public OverlayPositionMode EffectivePositionMode => PositionMode ??
        (Left == 32 && Top == 110 ? OverlayPositionMode.GameTopCenter : OverlayPositionMode.Manual);
    public double Width { get; set; } = 470;
    public double ThumbnailSize { get; set; } = 88;
    public double Opacity { get; set; } = .94;
    public bool ShowNames { get; set; } = false;
    public bool HardwareAcceleration { get; set; } = false;
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "local-data", "settings.json");
    public static Settings Load() => File.Exists(FilePath) ? JsonFile.Read<Settings>(FilePath) : new();
    public void Save() => JsonFile.Write(FilePath, this);
}
