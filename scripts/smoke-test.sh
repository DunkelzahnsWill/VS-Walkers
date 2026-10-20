#!/usr/bin/env bash
# Boots the Vintage Story dedicated server headless with the packaged mod and checks it loaded cleanly.
#
# Usage: scripts/smoke-test.sh [path/to/walkers_<version>.zip]
#   Without an argument it tests the zip in Releases/ (run ./build.sh first).
#
# Passes when:
#   - the server reaches "Dedicated Server now running", then stops cleanly on /stop
#   - the mod is in the server's loaded mod list
#   - every pattern in tests/smoke/*.expect is found in Logs/server-main.log
#   - the server logs hold no [Error] or [Fatal] line, and no [Warning] line that mentions the mod
# Console commands in tests/smoke/*.commands are typed in after boot, before /stop.
# The world and its logs stay in .cache/smoke/ for inspection; CI uploads them.
#
# Environment: SMOKE_BOOT_TIMEOUT (seconds, default 300), SMOKE_PORT (default 42420).
set -euo pipefail
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

boot_timeout="${SMOKE_BOOT_TIMEOUT:-300}"
port="${SMOKE_PORT:-42420}"
# A fixed seed keeps the smoke world the same from run to run.
server_overrides='{ "Upnp": false, "AdvertiseServer": false, "WorldConfig": { "Seed": "20261020" } }'

modid="$(python3 -I -c 'import json, sys; print(json.load(open(sys.argv[1]))["modid"])' "$repo_root/Walkers/modinfo.json")"
zip="${1:-}"
if [[ -z "$zip" ]]; then
  shopt -s nullglob
  zips=("$repo_root"/Releases/"${modid}"_*.zip)
  shopt -u nullglob
  (( ${#zips[@]} == 1 )) || fail "Expected one Releases/${modid}_*.zip, found ${#zips[@]}. Run ./build.sh or pass the zip path."
  zip="${zips[0]}"
  if [[ -n "$(find "$repo_root/Walkers" \( -name bin -o -name obj \) -prune -o -type f -newer "$zip" -print -quit)" ]]; then
    log "Note: $(basename "$zip") is older than files in Walkers/; run ./build.sh to test your latest changes"
  fi
fi
[[ -f "$zip" ]] || fail "No such file: $zip"
[[ -f "$VINTAGE_STORY/VintagestoryServer.dll" ]] || fail "No server in $VINTAGE_STORY. Run scripts/setup.sh first."

run_dir="$repo_root/.cache/smoke"
data_dir="$run_dir/data"
rm -rf "$run_dir"
mkdir -p "$data_dir/Mods"
cp "$zip" "$data_dir/Mods/"
mkfifo "$run_dir/console"
log "Testing $(basename "$zip") on Vintage Story server $VS_VERSION ($VINTAGE_STORY)"

server_pid=""
cleanup() {
  if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

# The server reads console commands from stdin, here a FIFO held open on fd 3.
(cd "$VINTAGE_STORY" && exec dotnet VintagestoryServer.dll --dataPath "$data_dir" --ip 127.0.0.1 --port "$port" \
  --withconfig "$server_overrides") < "$run_dir/console" > "$run_dir/stdout.log" 2>&1 &
server_pid=$!
exec 3> "$run_dir/console"

started=$SECONDS
until grep -q "Dedicated Server now running" "$run_dir/stdout.log"; do
  if ! kill -0 "$server_pid" 2>/dev/null; then
    tail -n 40 "$run_dir/stdout.log" >&2
    fail "The server exited during boot"
  fi
  if (( SECONDS - started > boot_timeout )); then
    tail -n 40 "$run_dir/stdout.log" >&2
    fail "The server was not up after ${boot_timeout}s"
  fi
  sleep 1
done
log "Server up after $(( SECONDS - started ))s"

shopt -s nullglob
for commands in "$repo_root"/tests/smoke/*.commands; do
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "${line// }" || "$line" == \#* ]] && continue
    log "> $line"
    printf '%s\n' "$line" >&3
    sleep 1
  done < "$commands"
done
shopt -u nullglob
sleep 2

printf '/stop\n' >&3
stop_started=$SECONDS
while kill -0 "$server_pid" 2>/dev/null; do
  if (( SECONDS - stop_started > 120 )); then
    fail "The server did not stop within 120s of /stop"
  fi
  sleep 1
done
exit_code=0
wait "$server_pid" || exit_code=$?
server_pid=""
exec 3>&-

main_log="$data_dir/Logs/server-main.log"
debug_log="$data_dir/Logs/server-debug.log"
[[ -f "$main_log" ]] || fail "No server log at $main_log"
problems=()

(( exit_code == 0 )) || problems+=("The server exited with code $exit_code")
grep -q "Stopped the server!" "$run_dir/stdout.log" || problems+=("The server did not report a clean stop")
grep -Eq "Mods, sorted by dependency:.*\b${modid}\b" "$main_log" || problems+=("$modid is not in the loaded mod list")

shopt -s nullglob
for expect in "$repo_root"/tests/smoke/*.expect; do
  expect_name="${expect##*/}"
  while IFS= read -r pattern || [[ -n "$pattern" ]]; do
    [[ -z "${pattern// }" || "$pattern" == \#* ]] && continue
    grep -Eq -- "$pattern" "$main_log" || problems+=("$expect_name: no log line matches: $pattern")
  done < "$expect"
done
shopt -u nullglob

# Log lines with the checkout path cut out, so a folder named like the mod does not count as a mention.
log_lines() { cat "$@" 2>/dev/null | sed "s|$repo_root|<repo>|g"; }

errors="$(log_lines "$main_log" "$debug_log" | grep -E '\[(Server )?(Error|Fatal)\]' | sort -u || true)"
[[ -z "$errors" ]] || problems+=("Errors in the server log:"$'\n'"$errors")
warnings="$(log_lines "$main_log" "$debug_log" | grep -E '\[(Server )?Warning\]' | grep -i -- "$modid" | sort -u || true)"
[[ -z "$warnings" ]] || problems+=("Warnings about $modid in the server log:"$'\n'"$warnings")

echo "---- $modid lines in server-main.log ----"
log_lines "$main_log" | grep -i -- "$modid" || true
echo "-----------------------------------------"
if (( ${#problems[@]} > 0 )); then
  printf '[smoke-test] FAIL: %s\n' "${problems[@]}" >&2
  log "Logs: $data_dir/Logs"
  exit 1
fi
log "PASS: $modid loaded, no errors, clean stop. Logs: $data_dir/Logs"
