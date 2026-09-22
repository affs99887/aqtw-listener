namespace Listener.App;

internal readonly record struct OverlayBounds(double LeftPixels, double TopPixels, double Width, double MaximumHeight);

internal static class OverlayPlacement
{
    // A ceiling on available content, never a fixed window height.
    public static OverlayBounds TopCenter(Rect viewportPixels, double scaleX, double scaleY, double requestedWidth)
    {
        var width = Math.Min(Math.Clamp(requestedWidth, 300, 900), viewportPixels.Width * .4 / scaleX);
        var gap = Math.Min(16 * scaleY, viewportPixels.Height / 12);
        return new(viewportPixels.Left + (viewportPixels.Width - width * scaleX) / 2,
            viewportPixels.Top + gap, width, Math.Max(1, (viewportPixels.Height / 3 - gap) / scaleY));
    }
}
