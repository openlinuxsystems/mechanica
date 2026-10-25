#!/usr/bin/env bash
# Installs the .NET SDK that global.json pins into ~/.dotnet and, on GitHub Actions, puts it on PATH.
# CI uses this instead of actions/setup-dotnet: since 2026-10-25 the aka.ms links that setup-dotnet's bundled
# installer follows (and every --channel install) point at dotnetorigin.blob.core.windows.net, which refuses
# downloads with HTTP 409. The current official installer fetches an exact version from builds.dotnet.microsoft.com.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
dir="$HOME/.dotnet"
installer="${RUNNER_TEMP:-/tmp}/dotnet-install.sh"

curl -fsSL --retry 3 https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o "$installer"
bash "$installer" --jsonfile "$root/global.json" --install-dir "$dir" --no-path

if [ -n "${GITHUB_PATH:-}" ]; then
  echo "$dir" >> "$GITHUB_PATH"
  echo "DOTNET_ROOT=$dir" >> "$GITHUB_ENV"
fi
"$dir/dotnet" --version
