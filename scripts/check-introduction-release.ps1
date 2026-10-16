param(
    [string]$Repository = 'affs99887/aqtw-listener',
    [string]$Url = 'https://affs99887.github.io/aqtw-listener/'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$raw = gh api "repos/$Repository/releases/latest"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the latest GitHub Release.' }
$release = $raw | ConvertFrom-Json
$assets = @($release.assets | Where-Object { $_.name -match 'win-x64\.zip$' -and $_.state -eq 'uploaded' })
if ($release.draft -or $assets.Count -ne 1) { throw 'Expected one published Windows x64 portable archive.' }
$asset = $assets[0]
$download = $asset.browser_download_url
$hash = $asset.digest -replace '^sha256:', ''
$site = $Url.TrimEnd('/')
$updater = [IO.File]::ReadAllText((Join-Path $root 'src\Listener.App\Updater.cs'))
$gitee = [regex]::Match($updater, 'GiteeRepository\s*=\s*"([^"]+)"').Groups[1].Value
$giteeZip = "https://gitee.com/$gitee/releases/download/$($release.tag_name)/$($asset.name)"

# Total size an anonymous visitor would get behind a public link, read from a one-byte range request.
function PublicSize($link) {
    $headers = curl.exe -sL -r 0-0 -D - -o NUL --max-time 90 -A 'AqtwListener-Updater' $link
    $range = @($headers | Where-Object { $_ -match '^Content-Range:\s*bytes\s+\d+-\d+/(\d+)' }) | Select-Object -Last 1
    if ($range -and $range -match '/(\d+)\s*$') { return [int64]$Matches[1] }
    return -1
}

$separator = if ($Url.Contains('?')) { '&' } else { '?' }
$response = Invoke-WebRequest -UseBasicParsing -Uri ($Url + $separator + 'release-check=' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
$html = $response.Content
$hrefs = @([regex]::Matches($html, 'href="([^"]+)"') | ForEach-Object { [Net.WebUtility]::HtmlDecode($_.Groups[1].Value) })
$githubLinks = @($hrefs | Where-Object { $_ -match '^https://github\.com/[^/]+/[^/]+/releases/download/.+\.zip(?:[?#].*)?$' })
if ($githubLinks.Count -lt 1 -or @($githubLinks | Where-Object { $_ -ne $download }).Count) { throw 'Introduction page GitHub download links do not match the latest release.' }
$giteeLinks = @($hrefs | Where-Object { $_ -match '^https://gitee\.com/.+\.zip(?:[?#].*)?$' })
if ($giteeLinks.Count -lt 1 -or @($giteeLinks | Where-Object { $_ -ne $giteeZip }).Count) { throw "Introduction page Gitee download links do not match $giteeZip." }
if (-not $html.Contains($release.tag_name)) { throw 'Introduction page version is stale.' }

# Every place the archive is offered must serve the release's own bytes.
$sources = [ordered]@{ Gitee = $giteeZip; Site = "$site/download/$($asset.name)" }
foreach ($key in @($sources.Keys)) {
    $size = PublicSize $sources[$key]
    if ($size -ne [int64]$asset.size) { throw "$key archive is $size bytes, the release has $($asset.size): $($sources[$key])" }
}

# The signed update information the assistant reads, on Gitee and on the GitHub Pages copy.
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli-home'; $env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$manifests = @("https://gitee.com/$gitee/raw/master/latest.json", "$site/update/latest.json")
foreach ($manifest in $manifests) {
    $folder = Join-Path $env:TEMP ('aqtw-manifest-check-' + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Force $folder | Out-Null
    try {
        $file = Join-Path $folder 'latest.json'
        curl.exe -sL --fail --max-time 60 -o $file $manifest | Out-Null
        curl.exe -sL --fail --max-time 60 -o ($file + '.sig') ($manifest + '.sig') | Out-Null
        if (-not (Test-Path $file) -or -not (Test-Path ($file + '.sig'))) { throw "Cannot read $manifest and its signature." }
        $checked = (& $dotnet run (Join-Path $root 'scripts\update-manifest.cs') -- verify $file 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "Signature check failed for ${manifest}: $checked" }
        $result = $checked | ConvertFrom-Json
        if ($result.tag -ne $release.tag_name -or $result.sha256 -ne $hash -or [int64]$result.size -ne [int64]$asset.size -or $result.urls[0] -ne $giteeZip) {
            throw "$manifest describes $($result.tag) $($result.sha256), the release is $($release.tag_name) $hash."
        }
    } finally { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue }
}

$readmePath = Join-Path $root 'README.md'
if ([IO.File]::ReadAllText($readmePath) -match '来源|鸣谢|致谢|(?i:acknowledg|attribution|credits)') { throw 'README contains disallowed attribution or acknowledgements.' }
[pscustomobject]@{ Passed=$true; Version=$release.tag_name; Download=$download; GiteeDownload=$giteeZip; GitHubButtons=$githubLinks.Count; GiteeButtons=$giteeLinks.Count; SignedManifests=$manifests.Count; Page=$Url }
