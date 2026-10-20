#!/usr/bin/env bash
# Builds the release zip for the version in Walkers/modinfo.json, checks it, and writes the release
# notes next to it. It publishes nothing: the owner creates the GitHub release and uploads to ModDB.
#
# Usage: scripts/release.sh [--allow-dirty]
#
# Steps:
#   1. refuses a dev, pre or rc version, a version without its own CHANGELOG.md section, and (unless
#      --allow-dirty) uncommitted changes, so the zip matches a commit that can be tagged
#   2. scripts/setup.sh and ./build.sh, which validate, build and pack Releases/walkers_<version>.zip
#   3. adds LICENSE and CHANGELOG.md to the zip root (the game ignores them)
#   4. scripts/smoke-test.sh on that finished zip
#   5. writes Releases/walkers_<version>.zip.sha256 and Releases/walkers_<version>-notes.md (the
#      version's CHANGELOG section, for the GitHub release and the ModDB file notes)
set -euo pipefail
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

allow_dirty=0
for arg in "$@"; do
  case "$arg" in
    --allow-dirty) allow_dirty=1 ;;
    *) fail "Unknown argument: $arg (usage: scripts/release.sh [--allow-dirty])" ;;
  esac
done

cd "$repo_root"
read -r modid version game < <(python3 -I -c '
import json, sys
m = json.load(open(sys.argv[1]))
print(m["modid"], m["version"], m["dependencies"]["game"])' Walkers/modinfo.json)

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
  || fail "modinfo.json has version $version; a release needs a plain x.y.z version"
[[ "$game" == "$VS_VERSION" ]] \
  || fail "modinfo.json targets game $game but scripts/versions.env tests against $VS_VERSION"
grep -q "^## ${version//./\\.} " CHANGELOG.md \
  || fail "CHANGELOG.md has no \"## $version (...)\" section"
if (( ! allow_dirty )) && [[ -n "$(git status --porcelain)" ]]; then
  fail "Uncommitted changes; commit them first or pass --allow-dirty for a trial run"
fi
commit="$(git rev-parse --short HEAD)"
log "Releasing $modid $version for game $game from $commit"

scripts/setup.sh
./build.sh

zip_path="$repo_root/Releases/${modid}_${version}.zip"
[[ -f "$zip_path" ]] || fail "The build did not write $zip_path"
zip -q -j "$zip_path" LICENSE CHANGELOG.md

scripts/smoke-test.sh "$zip_path"

(cd Releases && sha256sum "$(basename "$zip_path")" > "$(basename "$zip_path").sha256")
notes="$repo_root/Releases/${modid}_${version}-notes.md"
python3 -I - "$version" CHANGELOG.md > "$notes" <<'PY'
import re, sys
version, path = sys.argv[1], sys.argv[2]
text = open(path, encoding="utf-8").read()
match = re.search(rf"^## {re.escape(version)} .*?$(.*?)(?=^## |\Z)", text, re.M | re.S)
print(match.group(1).strip())
PY

log "Done: $zip_path ($(du -h "$zip_path" | cut -f1), sha256 $(cut -c1-16 "$zip_path.sha256")...)"
log "Notes: $notes"
log "Next, by the owner: tag v$version on $commit, create the GitHub release with the zip and the notes, upload the zip to ModDB"
