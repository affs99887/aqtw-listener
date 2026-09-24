#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Build the cross-platform projects: the Core recognition library, the offline
# CLI, and the core test harness (each restores its dependencies implicitly).
# The WPF desktop app (src/Listener.App, tests/Listener.App.Tests) targets
# net10.0-windows and is Windows-only, so it is intentionally not built here.
dotnet build -c Release src/Listener.Cli/Listener.Cli.csproj
dotnet build -c Release tests/Listener.Tests/Listener.Tests.csproj
