$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
# Keep one directly runnable release in a stable folder. Do not clear local-data:
# it contains the user's settings, recordings, drafts and personal library.
$Output = Join-Path $root 'portable'
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet publish src\Listener.App -c Release -r win-x64 --self-contained true -o $Output -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
& $dotnet publish src\Listener.Cli -c Release -r win-x64 --self-contained true -o $Output -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed' }
Copy-Item README.md (Join-Path $Output '使用说明.md') -Force
Copy-Item LICENSE (Join-Path $Output 'LICENSE') -Force
Write-Host "已更新便携程序：$(Join-Path $Output 'AqtwListener.exe')"
