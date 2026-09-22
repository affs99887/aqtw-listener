$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$go = Join-Path $root '.tools\go\bin\go.exe'
if (-not (Test-Path $go)) { $go = (Get-Command go -ErrorAction Stop).Source }
$env:GOCACHE = Join-Path $root '.tools\go-cache'
$env:GOPATH = Join-Path $root '.tools\go-work'
$env:GOTOOLCHAIN = 'local'
$env:CGO_ENABLED = '0'
New-Item (Join-Path $root 'data\engine') -ItemType Directory -Force | Out-Null
Push-Location (Join-Path $root 'src\Listener.Engine')
try {
    & $go build -trimpath -ldflags '-s -w' -o (Join-Path $root 'data\engine\Listener.Engine.exe') ./cmd/listener-engine
    if ($LASTEXITCODE -ne 0) { throw 'Engine build failed' }
} finally { Pop-Location }
