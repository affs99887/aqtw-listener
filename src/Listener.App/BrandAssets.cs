using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Listener.App;

internal static class BrandAssets
{
    private static readonly Uri IconUri = new("pack://application:,,,/AqtwListener;component/Assets/listener.ico");
    public static BitmapImage Mark { get; } = LoadMark();
    public static Drawing.Icon TrayIcon { get; } = LoadIcon();
    private static BitmapImage LoadMark()
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/AqtwListener;component/Assets/listener-mark.png"));
        image.Freeze(); return image;
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
        ForeColor = Drawing.Color.FromArgb(228, 232, 229), BackColor = Drawing.Color.FromArgb(26, 30, 32),
        ShowImageMargin = false, Padding = new Forms.Padding(4),
    };
    private sealed class TrayColors : Forms.ProfessionalColorTable
    {
        public TrayColors() { UseSystemColors = false; }
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(26, 30, 32);
        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(59, 66, 69);
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(47, 56, 56);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(110, 129, 120);
        public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(59, 66, 69);
        public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(26, 30, 32);
    }
    public static void StyleWindow(Window window)
    {
        window.Icon = Mark;
        window.UseLayoutRounding = true;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var dark = 1; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            var background = 0x00141210; DwmSetWindowAttribute(handle, 35, ref background, sizeof(int));
            var text = 0x00E5E8E4; DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        };
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
