param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-source.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener.ico'),
    [string]$MarkDestination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-mark.png'),
    [string]$AppDestination = (Join-Path $PSScriptRoot '..\src\Listener.App\Assets\listener-app.png'),
    [string]$PreviewDirectory = ''
)
# Raster packaging only: use the exact approved transparent IND70 logo.
& (Join-Path $PSScriptRoot 'export-raster-icon.ps1') @PSBoundParameters
