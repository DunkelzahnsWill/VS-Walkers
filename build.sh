#!/usr/bin/env bash
# Validates the JSON assets and modinfo.json, builds the mod and packages Releases/walkers_<version>.zip.
# The steps live in CakeBuild/Program.cs (the official mod template's Cake build, extended).
#
# Usage: ./build.sh [--target=ValidateJson|Build|Package] [--configuration=Debug|Release] [--skipJsonValidation=true]
set -euo pipefail
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/scripts/lib.sh"
cd "$repo_root"
exec dotnet run --project ./CakeBuild/CakeBuild.csproj -- "$@"
