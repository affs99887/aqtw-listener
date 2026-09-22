using System.Windows.Interop;

namespace Listener.App;

internal static class WindowPlacement
{
    public static Rect WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
    }
    public static void UseContentHeight(Window window)
    {
        window.SizeToContent = SizeToContent.Height;
        window.MaxHeight = SystemParameters.WorkArea.Height;
        void Fit()
        {
            if (!window.IsLoaded) return;
            var area = WorkArea(window); window.MaxHeight = Math.Min(window.MaxHeight, area.Height);
            if (double.IsFinite(window.Top)) window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.ActualHeight));
            if (double.IsFinite(window.Left)) window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.ActualWidth));
        }
        window.Loaded += (_, _) => { window.MaxHeight = WorkArea(window).Height; Fit(); };
        window.SizeChanged += (_, _) => Fit();
        window.DpiChanged += (_, _) => { window.MaxHeight = WorkArea(window).Height; Fit(); };
    }
}
