# shellcheck shell=bash
# Shared setup for the scripts in this folder. Source it, do not run it.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/versions.env
source "$repo_root/scripts/versions.env"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# The game folder the mod compiles against and the smoke test boots. Defaults to the server that
# scripts/setup.sh unpacks; set VINTAGE_STORY to point at another install.
export VINTAGE_STORY="${VINTAGE_STORY:-$repo_root/.cache/vintagestory/server}"

# Prefer a dotnet that scripts/setup.sh installed, so an older system dotnet does not shadow it.
dotnet_install_dir="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
if [[ -x "$dotnet_install_dir/dotnet" && ":$PATH:" != *":$dotnet_install_dir:"* ]]; then
  export DOTNET_ROOT="$dotnet_install_dir"
  export PATH="$dotnet_install_dir:$PATH"
fi

log() { printf '[%s] %s\n' "$(basename "$0" .sh)" "$*"; }
fail() { printf '[%s] ERROR: %s\n' "$(basename "$0" .sh)" "$*" >&2; exit 1; }
