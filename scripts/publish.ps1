param([string]$Output = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
# Keep one directly runnable release in a stable folder. Do not clear local-data:
# it contains the user's settings, recordings, drafts and personal library.
if (-not $Output) { $Output = Join-Path $root 'portable' }
if (-not [IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $root $Output }
$Output = [IO.Path]::GetFullPath($Output)
$workspacePrefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
if (-not $Output.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must be a subdirectory of this workspace.'
}
$libraryOutput = [IO.Path]::GetFullPath((Join-Path $Output 'library'))
if (-not $libraryOutput.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $libraryOutput -eq [IO.Path]::GetFullPath((Join-Path $root 'data/library'))) {
    throw 'Invalid publish library destination.'
}
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet publish src\Listener.App -c Release -r win-x64 --self-contained true --no-restore -o $Output -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
& $dotnet publish src\Listener.Cli -c Release -r win-x64 --self-contained true --no-restore -o $Output -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed' }
# MSBuild copies new content but leaves removed files behind. Replace only the
# bundled library, including cleanup of pre-0.3.3 putdown samples and indices,
# and drop the retired Go engine folder. local-data is never cleared by this script.
if (Test-Path -LiteralPath $libraryOutput) { Remove-Item -LiteralPath $libraryOutput -Recurse -Force }
# The project used to ship the whole docs tree (design files, screenshots). Drop whatever
# the new build no longer carries, so an updated portable folder does not keep the old bulk.
$docsOutput = Join-Path $Output 'docs'
if (Test-Path -LiteralPath $docsOutput) {
    Get-ChildItem -LiteralPath $docsOutput -Recurse -File |
        Where-Object { $_.Name -notin 'COVERAGE.md', 'TEST-REPORT.md' } |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $docsOutput -Recurse -Directory |
        Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Recurse -File -ErrorAction SilentlyContinue) } |
        Remove-Item -Force
}
$engineOutput = Join-Path $Output 'engine'
if (Test-Path -LiteralPath $engineOutput) { Remove-Item -LiteralPath $engineOutput -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $root 'data/library') -Destination $libraryOutput -Recurse
Copy-Item README.md (Join-Path $Output '使用说明.md') -Force
Copy-Item LICENSE (Join-Path $Output 'LICENSE') -Force
# The apphost still loads AqtwListener.dll; its public launcher uses the product name.
Move-Item -LiteralPath (Join-Path $Output 'AqtwListener.exe') -Destination (Join-Path $Output '行商助手.exe') -Force
Write-Host "已更新行商助手便携程序：$(Join-Path $Output '行商助手.exe')"
