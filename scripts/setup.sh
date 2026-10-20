#!/usr/bin/env bash
# Prepares a Linux x64 machine (a cloud session or CI) to build and test Walkers:
#   1. the .NET 10 SDK, unless one is already installed
#   2. the Vintage Story dedicated server from scripts/versions.env, checked against both pinned
#      checksums, unpacked into .cache/vintagestory/server
# The server folder supplies the game DLLs the mod compiles against and the headless server the
# smoke test boots. Nothing downloaded here is committed. Safe to re-run: finished steps are skipped.
#
# Usage: scripts/setup.sh [--no-sdk] [--no-server]
set -euo pipefail
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

want_sdk=1
want_server=1
for arg in "$@"; do
  case "$arg" in
    --no-sdk) want_sdk=0 ;;
    --no-server) want_server=0 ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) fail "Unknown option: $arg (try --help)" ;;
  esac
done

install_sdk() {
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL}\."; then
    log ".NET SDK $(dotnet --version) found at $(command -v dotnet)"
    return
  fi
  log "Installing the .NET $DOTNET_CHANNEL SDK into $dotnet_install_dir"
  local tmp
  tmp="$(mktemp -d)"
  curl -fsSL --retry 3 -o "$tmp/dotnet-install.sh" https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh
  bash "$tmp/dotnet-install.sh" --channel "$DOTNET_CHANNEL" --install-dir "$dotnet_install_dir" --no-path
  rm -rf "$tmp"
  log "Installed .NET SDK $("$dotnet_install_dir/dotnet" --version). The scripts in this repo find it on their own;"
  log "for your shell: export DOTNET_ROOT=\"$dotnet_install_dir\" PATH=\"$dotnet_install_dir:\$PATH\""
}

checksums_ok() {
  echo "$VS_SERVER_MD5  $1" | md5sum --check --status - &&
    echo "$VS_SERVER_SHA256  $1" | sha256sum --check --status -
}

install_server() {
  local server_dir="$repo_root/.cache/vintagestory/server"
  local download_dir="$repo_root/.cache/vintagestory/downloads"
  local stamp="$VS_VERSION $VS_SERVER_SHA256"
  local tarball="$download_dir/${VS_SERVER_URL##*/}"

  if [[ -f "$server_dir/VintagestoryAPI.dll" && "$(cat "$server_dir/.walkers-stamp" 2>/dev/null)" == "$stamp" ]]; then
    log "Vintage Story server $VS_VERSION already unpacked in $server_dir"
    return
  fi

  mkdir -p "$download_dir"
  if [[ -f "$tarball" ]] && checksums_ok "$tarball"; then
    log "Using the cached download $tarball"
  else
    log "Downloading $VS_SERVER_URL"
    curl -fsSL --retry 3 -o "$tarball.part" "$VS_SERVER_URL"
    if ! checksums_ok "$tarball.part"; then
      rm -f "$tarball.part"
      fail "Checksum mismatch for $VS_SERVER_URL (expected MD5 $VS_SERVER_MD5 and SHA-256 $VS_SERVER_SHA256). Nothing was unpacked."
    fi
    mv "$tarball.part" "$tarball"
  fi
  log "MD5 and SHA-256 match the pins in scripts/versions.env"

  local staging="$server_dir.staging"
  rm -rf "$staging"
  mkdir -p "$staging"
  tar -xzf "$tarball" -C "$staging"
  [[ -f "$staging/VintagestoryAPI.dll" ]] || fail "Unexpected archive layout: no VintagestoryAPI.dll at the top level"
  echo "$stamp" > "$staging/.walkers-stamp"
  rm -rf "$server_dir"
  mv "$staging" "$server_dir"
  log "Vintage Story server $VS_VERSION unpacked in $server_dir"
}

(( want_sdk )) && install_sdk
(( want_server )) && install_server
log "Ready. Next: ./build.sh, then scripts/smoke-test.sh"
