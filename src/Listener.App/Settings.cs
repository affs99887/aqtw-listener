namespace Listener.App;

public sealed class Settings
{
    public string DeviceId { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Hotkey { get; set; } = "Ctrl+Alt+L";
    public double Left { get; set; } = 32;
    public double Top { get; set; } = 110;
    public double Width { get; set; } = 470;
    public double Height { get; set; } = 720;
    public double Opacity { get; set; } = .94;
    public bool ShowNames { get; set; } = false;
    public bool HardwareAcceleration { get; set; } = false;
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "local-data", "settings.json");
    public static Settings Load() => File.Exists(FilePath) ? JsonFile.Read<Settings>(FilePath) : new();
    public void Save() => JsonFile.Write(FilePath, this);
}
