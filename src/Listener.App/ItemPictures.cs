using System.Windows.Media.Imaging;

namespace Listener.App;

// Item pictures for every surface (overlay cards, history, catalogue, the 展示物品 page), always
// scaled uniformly. A picture without the game's rarity background (情报站 renders) sits on that
// colour drawn over the item's grid footprint, like the screenshot thumbnails, so every card
// shows rarity and size the same way.
internal static class ItemPictures
{
    private static readonly Dictionary<(string Path, int Width), BitmapImage> images = [];
    private static readonly Dictionary<(string Path, int Width), BitmapSource> visibleAreas = [];

    public static UIElement Create(ItemDefinition item, string libraryRoot, double margin, int decodeWidth, double labelSize)
    {
        if (item.Thumbnail is null) return Theme.Label("暂无图片", labelSize, Theme.Muted);
        try
        {
            var path = Path.Combine(libraryRoot, item.Thumbnail);
            var key = (path, decodeWidth);
            if (!images.TryGetValue(key, out var image))
            {
                image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = decodeWidth; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); images[key] = image;
            }
            var picture = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(margin) };
            if (item.ThumbnailBackground is not { } colour) return picture;
            // Fit the item's visible outline, not the render's transparent canvas, to the footprint;
            // the file itself is left as it is.
            picture.Source = VisibleArea(key, image);
            const double cell = 100;
            picture.Margin = new Thickness(cell * .06);
            var footprint = new Border { Width = item.GridWidth * cell, Height = item.GridHeight * cell, Background = Theme.Brush(colour), Child = picture };
            return new Viewbox { Child = footprint, Stretch = Stretch.Uniform, Margin = new Thickness(margin) };
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
        { return Theme.Label("暂无图片", labelSize, Theme.Muted); }
    }

    // The opaque part of a transparent render (alpha above 8, with a 3% border), cut at display time.
    private static BitmapSource VisibleArea((string Path, int Width) key, BitmapSource image)
    {
        if (visibleAreas.TryGetValue(key, out var cached)) return cached;
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = pixels.PixelWidth, height = pixels.PixelHeight, stride = width * 4;
        var data = new byte[stride * height];
        pixels.CopyPixels(data, stride, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (data[y * stride + x * 4 + 3] > 8)
                { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        BitmapSource result = image;
        if (right >= left)
        {
            var pad = (int)Math.Round(.03 * Math.Max(right - left + 1, bottom - top + 1));
            left = Math.Max(0, left - pad); top = Math.Max(0, top - pad);
            right = Math.Min(width - 1, right + pad); bottom = Math.Min(height - 1, bottom + pad);
            result = new CroppedBitmap(pixels, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
            result.Freeze();
        }
        return visibleAreas[key] = result;
    }
}
