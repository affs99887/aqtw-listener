$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$staging = Join-Path $root 'artifacts\source'
New-Item $staging -ItemType Directory -Force | Out-Null
$files = & rg --files --hidden -g '!artifacts/**' -g '!.tools/**' -g '!**/bin/**' -g '!**/obj/**' -g '!local-data/**' -g '!.git/**'
foreach ($relative in $files) {
    $target = Join-Path $staging $relative
    New-Item (Split-Path $target -Parent) -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target -Force
}
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath 'artifacts\AqtwListener-0.1.0-source.zip' -Force
