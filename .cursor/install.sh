#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

DOTNET_DIR=/usr/local/dotnet

# Install the .NET 10 SDK if it is not already available. dotnet resolves its
# runtime relative to the real binary, so a symlink on PATH is sufficient and no
# shell-profile changes are required.
if ! command -v dotnet >/dev/null 2>&1 && [ ! -x "$DOTNET_DIR/dotnet" ]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  sudo mkdir -p "$DOTNET_DIR"
  sudo bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR"
fi
if [ ! -x /usr/local/bin/dotnet ] && [ -x "$DOTNET_DIR/dotnet" ]; then
  sudo ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet
fi

dotnet --info | head -5

# Build the cross-platform projects: the Core recognition library, the offline
# CLI, and the core test harness (each restores its dependencies implicitly).
# The WPF desktop app (src/Listener.App, tests/Listener.App.Tests) targets
# net10.0-windows and is Windows-only, so it is intentionally not built here.
dotnet build -c Release src/Listener.Cli/Listener.Cli.csproj
dotnet build -c Release tests/Listener.Tests/Listener.Tests.csproj
