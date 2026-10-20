#!/usr/bin/env bash
# Headless soak test: several in-game days of haunts on stand-in victims, three boots on one world,
# tick time, memory and a CPU profile. See tests/soak/soak.py for the scenario. Takes about 45 minutes.
#
# Usage: tests/soak/soak.sh [path/to/walkers_<version>.zip]
#   Output in .cache/soak/; then python3 -I tests/soak/analyze.py .cache/soak for the report.
# Environment: SOAK_PHASE_MINUTES (default 12), SOAK_CALENDAR_SPEED (default 2), SOAK_PORT (default 42440).
# For the CPU profile: dotnet tool install -g dotnet-trace
set -euo pipefail
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/../../scripts/lib.sh"
zip="${1:-$(ls "$repo_root"/Releases/walkers_*.zip | head -n 1)}"
[[ -f "$zip" ]] || fail "No zip; run ./build.sh or pass its path"
[[ -f "$VINTAGE_STORY/VintagestoryServer.dll" ]] || fail "No server in $VINTAGE_STORY; run scripts/setup.sh"
exec python3 -I "$repo_root/tests/soak/soak.py" "$zip" "$repo_root/.cache/soak"
