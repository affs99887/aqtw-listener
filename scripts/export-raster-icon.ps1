param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-source.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener.ico'),
    [string]$MarkDestination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-mark.png'),
    [string]$AppDestination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-app.png'),
    [string]$PreviewDirectory = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

# Preserve the approved PNG byte-for-byte. Only ICO frames are resampled;
# the source's transparent padding, outline and opaque visor are retained.
$sourcePath = [IO.Path]::GetFullPath($Source)
$bitmap = [Windows.Media.Imaging.BitmapImage]::new()
$bitmap.BeginInit()
$bitmap.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$bitmap.UriSource = [Uri]::new($sourcePath)
$bitmap.EndInit()
$bitmap.Freeze()
$rgba = [Windows.Media.Imaging.FormatConvertedBitmap]::new($bitmap, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
$pixels = [byte[]]::new($rgba.PixelWidth * $rgba.PixelHeight * 4)
$rgba.CopyPixels($pixels, $rgba.PixelWidth * 4, 0)
$corners = @($pixels[3], $pixels[($rgba.PixelWidth - 1) * 4 + 3],
    $pixels[($rgba.PixelHeight - 1) * $rgba.PixelWidth * 4 + 3], $pixels[$pixels.Length - 1])
if (@($corners | Where-Object { $_ -ne 0 }).Count) { throw 'The selected logo must have transparent corners.' }
foreach ($path in @($MarkDestination, $AppDestination)) {
    $target = [IO.Path]::GetFullPath($path)
    if ($target -ne $sourcePath) { Copy-Item -LiteralPath $sourcePath -Destination $target -Force }
}
if ($PreviewDirectory) { New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null }

function Render-Frame([int]$Size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::HighQuality)
    $drawing = $visual.RenderOpen()
    try {
        $scale = [Math]::Min($Size / $bitmap.PixelWidth, $Size / $bitmap.PixelHeight)
        $width = $bitmap.PixelWidth * $scale; $height = $bitmap.PixelHeight * $scale
        $drawing.DrawImage($bitmap, [Windows.Rect]::new(($Size - $width) / 2, ($Size - $height) / 2, $width, $height))
    } finally { $drawing.Close() }
    $frame = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $frame.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($frame))
    $memory = [IO.MemoryStream]::new()
    try { $encoder.Save($memory); return ,($memory.ToArray()) }
    finally { $memory.Dispose() }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bytes = Render-Frame $size
    $frames.Add($bytes)
    if ($PreviewDirectory) { [IO.File]::WriteAllBytes((Join-Path $PreviewDirectory "icon-$size.png"), $bytes) }
}
$stream = [IO.File]::Create([IO.Path]::GetFullPath($Destination))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($bytes in $frames) { $writer.Write($bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
[pscustomobject]@{ Source = $sourcePath; Width = $bitmap.PixelWidth; Height = $bitmap.PixelHeight;
    SourceSha256 = (Get-FileHash -LiteralPath $sourcePath).Hash; TransparentCorners = $corners; IconSizes = $sizes }
