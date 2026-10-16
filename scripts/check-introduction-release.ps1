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
if ($downloads.Count -lt 1 -or @($downloads | Where-Object { $_ -ne $download }).Count) { throw 'Introduction page download buttons do not match the latest release.' }
# The page also serves the same archive itself, so a download does not have to reach GitHub.
$assetName = [regex]::Escape([IO.Path]::GetFileName($download))
$hosted = @($hrefs | Where-Object { $_ -match ('^(?:https://[^/]+/aqtw-listener/)?download/' + $assetName + '$') })
if ($hosted.Count -lt 1) { throw 'Introduction page does not offer the archive from the site itself.' }
foreach ($link in $hosted) {
    $file = if ($link -match '^https://') { $link } else { ([Uri]$Url).GetLeftPart([UriPartial]::Path).TrimEnd('/') + '/' + $link }
    $head = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $file
    if ([int64]$head.Headers['Content-Length'] -ne [int64]$assets[0].size) { throw "Hosted archive size differs from the release: $file" }
}
# A volunteer mirror may still be offered. It must prefix the same release URL.
$mirrors = @('https://ghfast.top/', 'https://gh-proxy.com/')
$mirrored = @($hrefs | Where-Object { $_ -match '^https://[^/]+/https://github\.com/.+\.zip(?:[?#].*)?$' })
if (@($mirrored | Where-Object { $link = $_; -not @($mirrors | Where-Object { $link -eq $_ + $download }) }).Count) { throw 'Introduction page mirror links do not match the latest release.' }
if (-not $html.Contains($release.tag_name)) { throw 'Introduction page version is stale.' }
$readmePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'README.md'
if ([IO.File]::ReadAllText($readmePath) -match '来源|鸣谢|致谢|(?i:acknowledg|attribution|credits)') { throw 'README contains disallowed attribution or acknowledgements.' }
[pscustomobject]@{ Passed=$true; Version=$release.tag_name; Download=$download; DownloadButtons=$downloads.Count; HostedButtons=$hosted.Count; MirrorButtons=$mirrored.Count; Page=$Url }
