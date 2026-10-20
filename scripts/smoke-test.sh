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
#   - the server logs hold no [Error] or [Fatal] line, no [Warning] line that mentions the mod, and
#     no crash log
# Console commands in tests/smoke/*.commands are typed in after boot, before /stop.
# When any tests/smoke/*.restart.commands exist, the server then boots a second time on the same
# world, types those in, and stops again; tests/smoke/*.restart.expect must match that boot's log
# (data/Logs-restart/), so a feature can check what it saved. Every check above holds for both boots.
# The world and its logs stay in .cache/smoke/ for inspection; CI uploads them.
#
# Environment: SMOKE_BOOT_TIMEOUT (seconds, default 300), SMOKE_PORT (default 42420).
set -euo pipefail
# A write to the console after the server died must fail, not kill this script.
trap '' PIPE
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

boot_timeout="${SMOKE_BOOT_TIMEOUT:-300}"
port="${SMOKE_PORT:-42420}"
# A fixed seed keeps the smoke world the same from run to run. English, because the boot check
# below and the expect files match English log lines.
server_overrides='{ "Upnp": false, "AdvertiseServer": false, "ServerLanguage": "en", "WorldConfig": { "Seed": "20261020" } }'

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

# Types one line into the server console. Fails when the server is gone.
send() {
  kill -0 "$server_pid" 2>/dev/null && printf '%s\n' "$1" >&3 2>/dev/null
}

# Boots the server on the smoke world and waits until it runs.
# Arguments: the file for its console output, and extra server arguments.
boot() {
  local stdout="$1"
  shift
  # The server reads console commands from stdin, here a FIFO held open on fd 3.
  (cd "$VINTAGE_STORY" && exec dotnet VintagestoryServer.dll --dataPath "$data_dir" --ip 127.0.0.1 --port "$port" \
    --withconfig "$server_overrides" "$@") < "$run_dir/console" > "$stdout" 2>&1 &
  server_pid=$!
  exec 3> "$run_dir/console"

  local started=$SECONDS
  until grep -q "Dedicated Server now running" "$stdout"; do
    if ! kill -0 "$server_pid" 2>/dev/null; then
      tail -n 40 "$stdout" >&2
      fail "The server exited during boot"
    fi
    if (( SECONDS - started > boot_timeout )); then
      tail -n 40 "$stdout" >&2
      fail "The server was not up after ${boot_timeout}s"
    fi
    sleep 1
  done
  log "Server up after $(( SECONDS - started ))s"
}

# Types every line of the given command files into the console, one a second.
run_commands() {
  local commands line
  for commands in "$@"; do
    while IFS= read -r line || [[ -n "$line" ]]; do
      [[ -z "${line// }" || "$line" == \#* ]] && continue
      log "> $line"
      send "$line" || { log "The server is gone; skipping the remaining commands"; return 0; }
      sleep 1
    done < "$commands"
  done
}

# Sends /stop and waits for the server to exit; records a problem when it did not stop cleanly.
# Argument: the file with its console output.
stop() {
  local stdout="$1" exit_code=0
  sleep 2
  send /stop || log "The server is gone before /stop"
  local stop_started=$SECONDS
  while kill -0 "$server_pid" 2>/dev/null; do
    if (( SECONDS - stop_started > 120 )); then
      fail "The server did not stop within 120s of /stop"
    fi
    sleep 1
  done
  wait "$server_pid" || exit_code=$?
  server_pid=""
  exec 3>&-
  if (( exit_code != 0 )); then
    problems+=("The server exited with code $exit_code (${stdout##*/})")
    tail -n 40 "$stdout" >&2
  fi
  grep -q "Stopped the server!" "$stdout" || problems+=("The server did not report a clean stop (${stdout##*/})")
}

problems=()
shopt -s nullglob
first_commands=()
restart_commands=()
for commands in "$repo_root"/tests/smoke/*.commands; do
  if [[ "$commands" == *.restart.commands ]]; then
    restart_commands+=("$commands")
  else
    first_commands+=("$commands")
  fi
done
shopt -u nullglob

boot "$run_dir/stdout.log"
run_commands "${first_commands[@]}"
stop "$run_dir/stdout.log"

restart_log_dir="$data_dir/Logs-restart"
if (( ${#restart_commands[@]} > 0 )); then
  log "Booting again on the same world for tests/smoke/*.restart.commands"
  boot "$run_dir/stdout-restart.log" --logPath "$restart_log_dir"
  run_commands "${restart_commands[@]}"
  stop "$run_dir/stdout-restart.log"
fi

main_log="$data_dir/Logs/server-main.log"
debug_log="$data_dir/Logs/server-debug.log"
[[ -f "$main_log" ]] || fail "No server log at $main_log"
restart_main_log="$restart_log_dir/server-main.log"
restart_debug_log="$restart_log_dir/server-debug.log"
if (( ${#restart_commands[@]} > 0 )); then
  [[ -f "$restart_main_log" ]] || fail "No server log at $restart_main_log"
fi

grep -Eq "Mods, sorted by dependency:.*\b${modid}\b" "$main_log" || problems+=("$modid is not in the loaded mod list")

shopt -s nullglob
for crash in "$data_dir"/Logs/*crash*.log "$restart_log_dir"/*crash*.log; do
  [[ -s "$crash" ]] && problems+=("Crash log ${crash##*/}:"$'\n'"$(head -n 20 "$crash")")
done
for expect in "$repo_root"/tests/smoke/*.expect; do
  expect_name="${expect##*/}"
  expect_log="$main_log"
  if [[ "$expect" == *.restart.expect ]]; then
    expect_log="$restart_main_log"
    if (( ${#restart_commands[@]} == 0 )); then
      problems+=("$expect_name: there is no tests/smoke/*.restart.commands, so the server did not boot a second time")
      continue
    fi
  fi
  while IFS= read -r pattern || [[ -n "$pattern" ]]; do
    [[ -z "${pattern// }" || "$pattern" == \#* ]] && continue
    grep -Eq -- "$pattern" "$expect_log" || problems+=("$expect_name: no log line matches: $pattern")
  done < "$expect"
done
shopt -u nullglob

# Log lines with the checkout path cut out, so a folder named like the mod does not count as a mention.
# The game prints paths under $HOME as ~/..., so cut that form too.
repo_tilde="$repo_root"
[[ "$repo_root" == "$HOME"/* ]] && repo_tilde="~${repo_root#"$HOME"}"
log_lines() { cat "$@" 2>/dev/null | sed -e "s|$repo_root|<repo>|g" -e "s|$repo_tilde|<repo>|g"; }

errors="$(log_lines "$main_log" "$debug_log" "$restart_main_log" "$restart_debug_log" | grep -E '\[(Server )?(Error|Fatal)\]' | sort -u || true)"
[[ -z "$errors" ]] || problems+=("Errors in the server log:"$'\n'"$errors")
warnings="$(log_lines "$main_log" "$debug_log" "$restart_main_log" "$restart_debug_log" | grep -E '\[(Server )?Warning\]' | grep -i -- "$modid" | sort -u || true)"
[[ -z "$warnings" ]] || problems+=("Warnings about $modid in the server log:"$'\n'"$warnings")

echo "---- $modid lines in server-main.log ----"
log_lines "$main_log" | grep -i -- "$modid" || true
if [[ -f "$restart_main_log" ]]; then
  echo "---- $modid lines in Logs-restart/server-main.log ----"
  log_lines "$restart_main_log" | grep -i -- "$modid" || true
fi
echo "-----------------------------------------"
if (( ${#problems[@]} > 0 )); then
  printf '[smoke-test] FAIL: %s\n' "${problems[@]}" >&2
  log "Logs: $data_dir/Logs"
  exit 1
fi
log "PASS: $modid loaded, no errors, clean stop. Logs: $data_dir/Logs"
