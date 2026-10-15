using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Listener.App;

internal static class BrandAssets
{
    public const string AppName = "行商助手";
    private static readonly Uri IconUri = new("pack://application:,,,/AqtwListener;component/Assets/listener.ico");
    private static readonly Dictionary<int, BitmapImage> marks = [];
    public static Image CreateMark(double size) => new MarkImage(size);
    public static BitmapImage WindowIcon { get; } = LoadImage("listener-app.png");
    public static Drawing.Icon TrayIcon { get; } = LoadIcon();
    private static BitmapImage LoadImage(string name, int pixelWidth = 0)
    {
        var image = new BitmapImage(); image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/AqtwListener;component/Assets/{name}");
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (pixelWidth > 0) image.DecodePixelWidth = pixelWidth;
        image.EndInit();
        image.Freeze(); return image;
    }
    private sealed class MarkImage : Image
    {
        public MarkImage(double size)
        {
            Width = Height = size; Stretch = Stretch.Uniform;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            Loaded += (_, _) => RefreshSource(VisualTreeHelper.GetDpi(this));
            RefreshSource(VisualTreeHelper.GetDpi(this));
        }
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi); RefreshSource(newDpi);
        }
        private void RefreshSource(DpiScale dpi)
        {
            // Decode the approved source once at the physical display size, avoiding
            // a large bitmap being reduced again by the default render-time scaler.
            var pixels = Math.Max(1, (int)Math.Round(Width * dpi.DpiScaleX));
            if (!marks.TryGetValue(pixels, out var image)) marks[pixels] = image = LoadImage("listener-mark.png", pixels);
            Source = image;
        }
    }
    private static Drawing.Icon LoadIcon()
    {
        using var stream = Application.GetResourceStream(IconUri).Stream;
        using var icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        return (Drawing.Icon)icon.Clone();
    }
    public static Forms.ContextMenuStrip TrayMenu() => new()
    {
        Renderer = new Forms.ToolStripProfessionalRenderer(new TrayColors()),
        Font = new Drawing.Font("Microsoft YaHei UI", 9f),
        ForeColor = Drawing.Color.FromArgb(230, 236, 231), BackColor = Drawing.Color.FromArgb(29, 29, 29),
        ShowImageMargin = false, Padding = new Forms.Padding(4),
    };
    private sealed class TrayColors : Forms.ProfessionalColorTable
    {
        public TrayColors() { UseSystemColors = false; }
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(29, 29, 29);
        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(53, 62, 62);
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(42, 48, 48);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(127, 151, 149);
        public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(53, 62, 62);
        public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(29, 29, 29);
    }
    public static void StyleWindow(Window window)
    {
        window.Icon = WindowIcon;
        window.UseLayoutRounding = true;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var dark = 1; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            var background = 0x00121211; DwmSetWindowAttribute(handle, 35, ref background, sizeof(int));
            var text = 0x00E7ECE6; DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        };
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
