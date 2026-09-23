namespace Listener.App;

internal readonly record struct OverlayBounds(double LeftPixels, double TopPixels, double Width, double MaximumHeight);

internal static class OverlayPlacement
{
    // A ceiling on available content, never a fixed window height.
    public static OverlayBounds TopCenter(Rect viewportPixels, double scaleX, double scaleY, double requestedWidth)
    {
        var width = Math.Min(Math.Clamp(double.IsFinite(requestedWidth) ? requestedWidth : 470, 280, 1600), viewportPixels.Width * .94 / scaleX);
        var gap = Math.Min(12 * scaleY, viewportPixels.Height / 16);
        return new(viewportPixels.Left + (viewportPixels.Width - width * scaleX) / 2,
            viewportPixels.Top + gap, width, Math.Max(1, (viewportPixels.Height - gap - 8 * scaleY) / scaleY));
    }
}
