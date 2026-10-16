# Publishes a release to Gitee, the download and update source that works from mainland China:
# a Gitee release with the portable zip attached, the README, and last the signed latest.json
# (+ .sig) on the default branch, so the manifest never points at a file that is not there yet.
# Older releases lose their attachments beyond -KeepAttachments, because a free Gitee repository
# holds at most 1 GB of attachments.
#
# Needs the token saved by scripts\save-gitee-token.ps1 and the signing key from
# `dotnet run scripts/update-manifest.cs -- keygen`.
param(
    [string]$Tag = '',
    [string]$Zip = '',
    [string]$Notes = '',
    [int]$KeepAttachments = 3
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if (-not $Tag) { $Tag = 'v' + [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props')), '<Version>([^<]+)</Version>').Groups[1].Value }
$updater = [IO.File]::ReadAllText((Join-Path $root 'src\Listener.App\Updater.cs'))
$repository = [regex]::Match($updater, 'GiteeRepository\s*=\s*"([^"]+)"').Groups[1].Value
$github = [regex]::Match($updater, 'const string Repository\s*=\s*"([^"]+)"').Groups[1].Value
$name = "XingshangAssistant-$Tag-win-x64.zip"
if (-not $Zip) { $Zip = Join-Path $root "artifacts\release-$Tag\$name" }
if (-not $Notes) { $Notes = Join-Path $root "docs\releases\$Tag.md" }
foreach ($file in @($Zip, $Notes)) { if (-not (Test-Path -LiteralPath $file)) { throw "找不到 $file" } }
$noteLines = @(Get-Content -LiteralPath $Notes -Encoding UTF8)
$title = $noteLines[0] -replace '^#\s*', ''
$body = (($noteLines | Select-Object -Skip 1) -join "`n").Trim()

$tokenFile = Join-Path $env:USERPROFILE '.aqtw-release\gitee-token.txt'
if (-not (Test-Path -LiteralPath $tokenFile)) { throw '先运行 scripts\save-gitee-token.ps1 保存 Gitee 令牌。' }
$secure = Get-Content -LiteralPath $tokenFile | ConvertTo-SecureString
$token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
$api = 'https://gitee.com/api/v5'
function Address($path) { $api + $path + $(if ($path.Contains('?')) { '&' } else { '?' }) + 'access_token=' + [Uri]::EscapeDataString($token) }
function Call($method, $path, $fields) {
    if ($fields) {
        $json = $fields | ConvertTo-Json -Depth 5 -Compress
        return Invoke-RestMethod -UseBasicParsing -Method $method -Uri (Address $path) -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json))
    }
    return Invoke-RestMethod -UseBasicParsing -Method $method -Uri (Address $path)
}
function Find($path) {
    try { $found = Call GET $path $null; if ($found -and $found.id -ne $null -or $found.sha) { return $found }; return $null }
    catch { if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return $null }; throw }
}
# What an anonymous visitor gets: the total size behind the public download link.
function PublicSize($url) {
    $headers = curl.exe -sL -r 0-0 -D - -o NUL --max-time 90 -A 'AqtwListener-Updater' $url
    $range = @($headers | Where-Object { $_ -match '^Content-Range:\s*bytes\s+\d+-\d+/(\d+)' }) | Select-Object -Last 1
    if ($range -and $range -match '/(\d+)\s*$') { return [int64]$Matches[1] }
    return -1
}
function PublicBytes($url) {
    $out = Join-Path $env:TEMP ('aqtw-gitee-' + [guid]::NewGuid().ToString('n'))
    try { curl.exe -sL --fail --max-time 60 -o $out $url | Out-Null; if ($LASTEXITCODE -eq 0 -and (Test-Path $out)) { return [IO.File]::ReadAllBytes($out) }; return $null }
    finally { Remove-Item -LiteralPath $out -ErrorAction SilentlyContinue }
}
function PutFile($path, [byte[]]$bytes, $message) {
    $existing = Find "/repos/$repository/contents/$path`?ref=$branch"
    $fields = @{ content = [Convert]::ToBase64String($bytes); message = $message; branch = $branch }
    if ($existing -and $existing.sha) { $fields.sha = $existing.sha; Call PUT "/repos/$repository/contents/$path" $fields | Out-Null }
    else { Call POST "/repos/$repository/contents/$path" $fields | Out-Null }
}

$owner, $repoName = $repository.Split('/')
$me = Call GET '/user' $null
if ($me.login -ne $owner) { throw "Updater.GiteeRepository 是 $repository，但令牌属于 $($me.login)；先把 Updater.cs 里的 GiteeRepository 改成 $($me.login)/$repoName。" }
$repo = Find "/repos/$repository"
if (-not $repo) {
    $repo = Call POST '/user/repos' @{ name = $repoName; description = '暗区突围行商助手：国内下载与更新'; private = $false; has_issues = $false; has_wiki = $false; auto_init = $true }
    Write-Host "已创建 Gitee 仓库 $repository"
}
if ($repo.private) { throw "Gitee 仓库 $repository 是私有的，未登录的用户下载不了。请在仓库设置里改为开源（可能需要等 Gitee 审核）。" }
$branch = $repo.default_branch
if ($branch -ne 'master') { throw "默认分支是 $branch，而 Updater.ManifestUrls 读的是 master，两边要一致。" }

$page = "https://gitee.com/$repository/releases/tag/$Tag"
$urls = @(
    "https://gitee.com/$repository/releases/download/$Tag/$name",
    "https://affs99887.github.io/aqtw-listener/download/$name",
    "https://github.com/$github/releases/download/$Tag/$name"
)
$manifestDir = Join-Path $root "artifacts\release-$Tag\update"
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli-home'; $env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$arguments = @('run', 'scripts/update-manifest.cs', '--', 'write', '--zip', $Zip, '--tag', $Tag, '--name', $title, '--notes', $Notes, '--page', $page, '--out', $manifestDir)
foreach ($url in $urls) { $arguments += @('--url', $url) }
& (Join-Path $root '.tools\dotnet\dotnet.exe') @arguments | Out-Null
if ($LASTEXITCODE -ne 0) { throw '生成或签名 latest.json 失败' }
$manifest = [IO.File]::ReadAllBytes((Join-Path $manifestDir 'latest.json'))
$signature = [IO.File]::ReadAllBytes((Join-Path $manifestDir 'latest.json.sig'))

$release = Find "/repos/$repository/releases/tags/$Tag"
if (-not $release) {
    $release = Call POST "/repos/$repository/releases" @{ tag_name = $Tag; name = $title; body = $body; prerelease = $false; target_commitish = $branch }
    Write-Host "已创建发行版 $Tag"
}
foreach ($file in @(Call GET "/repos/$repository/releases/$($release.id)/attach_files" $null)) {
    if ($file -and $file.name -eq $name) { Call DELETE "/repos/$repository/releases/$($release.id)/attach_files/$($file.id)" $null | Out-Null }
}
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromMinutes(30)
$form = New-Object System.Net.Http.MultipartFormDataContent
$stream = [IO.File]::OpenRead($Zip)
try {
    $content = New-Object System.Net.Http.StreamContent($stream)
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/zip')
    $form.Add($content, 'file', $name)
    $response = $client.PostAsync((Address "/repos/$repository/releases/$($release.id)/attach_files"), $form).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) { throw ('上传附件失败：' + [int]$response.StatusCode + ' ' + $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()) }
} finally { $stream.Dispose(); $client.Dispose() }
$size = (Get-Item -LiteralPath $Zip).Length
$public = PublicSize $urls[0]
if ($public -ne $size) { throw "未登录下载附件得到的大小是 $public，应为 $size。" }
Write-Host "附件可公开下载：$($urls[0])"

PutFile 'README.md' ([IO.File]::ReadAllBytes((Join-Path $root 'README.md'))) "同步 README（$Tag）"
PutFile 'latest.json.sig' $signature "更新信息签名 $Tag"
PutFile 'latest.json' $manifest "更新信息 $Tag"
$raw = "https://gitee.com/$repository/raw/$branch/latest.json"
$matched = $false
for ($attempt = 0; $attempt -lt 8 -and -not $matched; $attempt++) {
    if ($attempt) { Start-Sleep -Seconds 10 }
    $remote = PublicBytes $raw; $remoteSig = PublicBytes ($raw + '.sig')
    $matched = $remote -and $remoteSig -and ([Convert]::ToBase64String($remote) -eq [Convert]::ToBase64String($manifest)) -and ([Convert]::ToBase64String($remoteSig) -eq [Convert]::ToBase64String($signature))
}
if (-not $matched) { throw "未登录读取 $raw 与本地 latest.json 不一致" }

$removed = 0
$all = @(Call GET "/repos/$repository/releases?per_page=100&page=1" $null) | Where-Object { $_ } | Sort-Object { [datetime]$_.created_at } -Descending
foreach ($old in ($all | Select-Object -Skip $KeepAttachments)) {
    foreach ($file in @(Call GET "/repos/$repository/releases/$($old.id)/attach_files" $null)) {
        if ($file) { Call DELETE "/repos/$repository/releases/$($old.id)/attach_files/$($file.id)" $null | Out-Null; $removed++ }
    }
}
$token = $null
[pscustomobject]@{ Passed = $true; Repository = $repository; Tag = $Tag; Download = $urls[0]; Size = $size; Manifest = $raw; ManifestFolder = $manifestDir; RemovedOldAttachments = $removed }
