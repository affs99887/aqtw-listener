param(
    [string]$Repository = 'affs99887/aqtw-listener',
    [string]$Url = 'https://affs99887.github.io/aqtw-listener/'
)
$ErrorActionPreference = 'Stop'
$raw = gh api "repos/$Repository/releases/latest"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the latest GitHub Release.' }
$release = $raw | ConvertFrom-Json
$assets = @($release.assets | Where-Object { $_.name -match 'win-x64\.zip$' -and $_.state -eq 'uploaded' })
if ($release.draft -or $assets.Count -ne 1) { throw 'Expected one published Windows x64 portable archive.' }
$download = $assets[0].browser_download_url
$separator = if ($Url.Contains('?')) { '&' } else { '?' }
$response = Invoke-WebRequest -UseBasicParsing -Uri ($Url + $separator + 'release-check=' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
$html = $response.Content
$hrefs = @([regex]::Matches($html, 'href="([^"]+)"') | ForEach-Object { [Net.WebUtility]::HtmlDecode($_.Groups[1].Value) })
$downloads = @($hrefs | Where-Object { $_ -match '^https://github\.com/[^/]+/[^/]+/releases/download/.+\.zip(?:[?#].*)?$' })
if ($downloads.Count -lt 2 -or @($downloads | Where-Object { $_ -ne $download }).Count) { throw 'Introduction page download buttons do not match the latest release.' }
# Mirrors only prefix the same release URL. Keep this list aligned with Updater.DownloadMirrors.
$mirrors = @('https://ghfast.top/', 'https://gh-proxy.com/')
$mirrored = @($hrefs | Where-Object { $_ -match '^https://[^/]+/https://github\.com/.+\.zip(?:[?#].*)?$' })
if ($mirrored.Count -lt 1 -or @($mirrored | Where-Object { $link = $_; -not @($mirrors | Where-Object { $link -eq $_ + $download }) }).Count) { throw 'Introduction page mirror links do not match the latest release.' }
if (-not $html.Contains($release.tag_name)) { throw 'Introduction page version is stale.' }
$readmePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'README.md'
if ([IO.File]::ReadAllText($readmePath) -match '来源|鸣谢|致谢|(?i:acknowledg|attribution|credits)') { throw 'README contains disallowed attribution or acknowledgements.' }
[pscustomobject]@{ Passed=$true; Version=$release.tag_name; Download=$download; DownloadButtons=$downloads.Count; MirrorButtons=$mirrored.Count; Page=$Url }
