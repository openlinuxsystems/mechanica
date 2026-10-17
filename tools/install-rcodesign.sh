#!/usr/bin/env bash
# Downloads the pinned rcodesign (ad-hoc signs the macOS app from Linux) into .tools/ and prints its path.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
source "$root/tools/versions.env"
name="apple-codesign-${APPLE_CODESIGN_VERSION}-x86_64-unknown-linux-musl"
bin="$root/.tools/$name/rcodesign"
if [[ ! -x "$bin" ]]; then
  mkdir -p "$root/.tools"
  curl -fsSL "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign%2F${APPLE_CODESIGN_VERSION}/${name}.tar.gz" \
    | tar xz -C "$root/.tools"
fi
echo "$bin"
