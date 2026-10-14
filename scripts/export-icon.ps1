param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-app.svg'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener.ico'),
    [string]$MarkSource = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-mark.svg'),
    [string]$MarkDestination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-mark.png'),
    [string]$AppDestination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-app.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

# The selected logo uses only filled SVG paths. Keep these vector sources editable,
# and render every ICO frame at its own size instead of reducing a concept image.
function Read-LogoSvg([string]$Path) {
    [xml]$svg = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $viewBox = @($svg.DocumentElement.GetAttribute('viewBox') -split '[ ,]+' |
        ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) })
    if ($viewBox.Count -ne 4 -or $viewBox[2] -le 0 -or $viewBox[3] -le 0) {
        throw "Invalid SVG viewBox: $Path"
    }
    $shapes = @($svg.SelectNodes("//*[local-name()='path']") | ForEach-Object {
        $geometry = [System.Windows.Media.Geometry]::Parse($_.GetAttribute('d'))
        $brush = [System.Windows.Media.SolidColorBrush]::new(
            [System.Windows.Media.ColorConverter]::ConvertFromString($_.GetAttribute('fill')))
        $geometry.Freeze(); $brush.Freeze()
        [PSCustomObject]@{ Geometry = $geometry; Brush = $brush }
    })
    if ($shapes.Count -eq 0) { throw "SVG contains no logo paths: $Path" }
    return [PSCustomObject]@{ ViewBox = $viewBox; Shapes = $shapes }
}

function Render-LogoPng($Artwork, [int]$Size) {
    $supersample = if ($Size -gt 256) { 2 } else { 4 }
    $pixels = $Size * $supersample
    $scale = [Math]::Min($pixels / $Artwork.ViewBox[2], $pixels / $Artwork.ViewBox[3])
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $drawing.PushTransform([System.Windows.Media.MatrixTransform]::new(
            $scale, 0, 0, $scale, -$Artwork.ViewBox[0] * $scale, -$Artwork.ViewBox[1] * $scale))
        foreach ($shape in $Artwork.Shapes) { $drawing.DrawGeometry($shape.Brush, $null, $shape.Geometry) }
        $drawing.Pop()
    } finally { $drawing.Close() }
    $large = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $pixels, $pixels, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $large.Render($visual); $large.Freeze()
    $smallVisual = [System.Windows.Media.DrawingVisual]::new()
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode(
        $smallVisual, [System.Windows.Media.BitmapScalingMode]::HighQuality)
    $smallDrawing = $smallVisual.RenderOpen()
    try { $smallDrawing.DrawImage($large, [System.Windows.Rect]::new(0, 0, $Size, $Size)) }
    finally { $smallDrawing.Close() }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($smallVisual); $bitmap.Freeze()
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new()
    try { $encoder.Save($memory); return ,($memory.ToArray()) }
    finally { $memory.Dispose() }
}

$appArtwork = Read-LogoSvg $Source
$markArtwork = Read-LogoSvg $MarkSource
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($MarkDestination), (Render-LogoPng $markArtwork 1254))
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($AppDestination), (Render-LogoPng $appArtwork 1254))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $frames.Add((Render-LogoPng $appArtwork $size)) }
$stream = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Destination))
$writer = [System.IO.BinaryWriter]::new($stream)
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
Write-Host "Exported Shidian mark, app PNG and icon frames: $($sizes -join ', ')"
