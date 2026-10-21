#!/usr/bin/env python3
"""Headless soak test of the walkers mod on the 1.22.7 dedicated server.

Run it through tests/soak/soak.sh, which builds nothing and only points this script at the server and
the zip. It boots a fresh world with stand-in victims (vanilla player bots), runs haunts on them for
in-game days on a sped-up calendar, restarts the server twice, kills a walker with no ritual, skips
time to its return, kills the victims, and samples tick time, memory and the director's state the whole way.

Everything lands in .cache/soak/: the server data and logs, samples.csv, events.log (every command
and its reply) and, when dotnet-trace is installed, a CPU profile of a stretch of night play.
tests/soak/analyze.py turns that folder into a report.

This is a measuring tool, not a pass/fail gate: it stops on a server crash and otherwise records.
"""
import csv
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

VS = Path(os.environ["VINTAGE_STORY"])
ZIP = Path(sys.argv[1])
OUT = Path(sys.argv[2])
# Real minutes of play per phase, and the calendar speed (1 = one game minute per real second,
# 24 real minutes a day; the game's default is 0.5).
PHASE_MINUTES = float(os.environ.get("SOAK_PHASE_MINUTES", "12"))
CALENDAR_SPEED = float(os.environ.get("SOAK_CALENDAR_SPEED", "2"))
SAMPLE_SECONDS = float(os.environ.get("SOAK_SAMPLE_SECONDS", "20"))
PROFILE_SECONDS = int(os.environ.get("SOAK_PROFILE_SECONDS", "90"))
PORT = os.environ.get("SOAK_PORT", "42440")

OVERRIDES = {
    "Upnp": False,
    "AdvertiseServer": False,
    "ServerLanguage": "en",
    # Game time stands still on an empty server unless this is on; stand-ins are not players.
    "PassTimeWhenEmpty": True,
    "WorldConfig": {"Seed": "20261020"},
}

events = None
samples_writer = None
samples_file = None


def stamp():
    return time.strftime("%H:%M:%S")


def note(text):
    line = f"[{stamp()}] {text}"
    print(line, flush=True)
    events.write(line + "\n")
    events.flush()


class Server:
    def __init__(self, data, logs, label):
        self.label = label
        self.stdout_path = OUT / f"stdout-{label}.log"
        self.lock = threading.Lock()
        args = ["dotnet", "VintagestoryServer.dll", "--dataPath", str(data), "--ip", "127.0.0.1",
                "--port", PORT, "--withconfig", json.dumps(OVERRIDES), "--logPath", str(logs)]
        self.out = open(self.stdout_path, "w")
        self.proc = subprocess.Popen(args, cwd=VS, stdin=subprocess.PIPE, stdout=self.out,
                                     stderr=subprocess.STDOUT, text=True)
        started = time.time()
        while "Dedicated Server now running" not in self.text():
            if self.proc.poll() is not None:
                raise SystemExit(f"server exited during boot ({label})")
            if time.time() - started > 300:
                raise SystemExit("server not up after 300 s")
            time.sleep(1)
        note(f"server {label} up after {time.time() - started:.0f} s, pid {self.proc.pid}")

    def text(self):
        return self.stdout_path.read_text(errors="replace")

    def cmd(self, command, wait=2.0, quiet=False):
        """Types one console command and returns the reply: the log text after its echo."""
        with self.lock:
            if self.proc.poll() is not None:
                raise SystemExit("the server is gone")
            before = len(self.text())
            self.proc.stdin.write(command + "\n")
            self.proc.stdin.flush()
            time.sleep(wait)
            reply = self.text()[before:]
        reply = reply.split("Handling Console Command", 1)[-1]
        reply = reply.split("\n", 1)[1] if "\n" in reply else ""
        # Only up to the next command's echo (the sampler may have typed one meanwhile).
        reply = reply.split("Handling Console Command", 1)[0]
        reply = re.sub(r"<[^>]+>", "", reply).strip()
        if not quiet:
            note(f"> {command}\n{indent(reply)}")
        return reply

    def stop(self):
        with self.lock:
            self.proc.stdin.write("/stop\n")
            self.proc.stdin.flush()
        try:
            code = self.proc.wait(timeout=120)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            code = "killed after 120 s"
        self.out.close()
        clean = "Stopped the server!" in self.stdout_path.read_text(errors="replace")
        note(f"server {self.label} stopped, exit {code}, clean stop line: {clean}")


def indent(text):
    return "\n".join("    " + line for line in text.splitlines())


def rss_mb(pid):
    try:
        for line in Path(f"/proc/{pid}/status").read_text().splitlines():
            if line.startswith("VmRSS:"):
                return int(line.split()[1]) / 1024
    except OSError:
        pass
    return None


STATS = re.compile(r"Managed/Total: ([\d.]+)Mb / ([\d.]+) ?Mb.*?Average Tick Time: ([\d.]+) ms.*?Ticks/s: ([\d.]+)"
                   r".*?Last 10 ticks \(ms\): ([\d, ]+).*?Loaded chunks: (\d+).*?Loaded entities: (\d+)", re.S)


def sample(server, phase):
    stats = server.cmd("/stats", wait=1.0, quiet=True)
    time_reply = server.cmd("/time", wait=0.6, quiet=True)
    director = server.cmd("/walkers director status", wait=1.0, quiet=True)
    stalker = server.cmd("/walkers stalker status", wait=1.0, quiet=True)
    m = STATS.search(stats)
    ticks = [int(t) for t in m.group(5).replace(" ", "").split(",") if t] if m else []
    clock = re.search(r"Server time: (.*)", time_reply)
    row = {
        "t": time.time(), "phase": phase,
        "game_time": clock.group(1).strip() if clock else "",
        "managed_mb": m.group(1) if m else "", "total_mb": m.group(2) if m else "",
        "rss_mb": f"{rss_mb(server.proc.pid) or 0:.0f}",
        "avg_tick_ms": m.group(3) if m else "", "ticks_per_s": m.group(4) if m else "",
        "max_last10_ms": max(ticks) if ticks else "",
        "chunks": m.group(6) if m else "", "entities": m.group(7) if m else "",
        "director": " | ".join(l.strip() for l in director.splitlines()),
        "stalker": " | ".join(l.strip() for l in stalker.splitlines()),
    }
    samples_writer.writerow(row)
    samples_file.flush()
    return row


def play(server, phase, minutes):
    """Lets the world run, sampling every SAMPLE_SECONDS."""
    note(f"--- {phase}: {minutes:.1f} real minutes of play")
    end = time.time() + minutes * 60
    while time.time() < end:
        row = sample(server, phase)
        note(f"sample {phase}: {row['game_time']}; tick avg {row['avg_tick_ms']} ms, max of last 10 {row['max_last10_ms']} ms; "
             f"managed {row['managed_mb']} MB, rss {row['rss_mb']} MB; entities {row['entities']}")
        time.sleep(max(0.0, min(SAMPLE_SECONDS, end - time.time())))


def profile(server, seconds):
    tool = shutil.which("dotnet-trace") or str(Path.home() / ".dotnet/tools/dotnet-trace")
    if not Path(tool).exists():
        note("dotnet-trace not installed; no CPU profile")
        return
    trace = OUT / "night.nettrace"
    note(f"profiling {seconds} s with dotnet-trace")
    # The default profile samples every thread's stack once a millisecond.
    subprocess.run([tool, "collect", "-p", str(server.proc.pid), "--format", "Speedscope",
                    "--duration", time.strftime("00:%H:%M:%S", time.gmtime(seconds)), "-o", str(trace)],
                   stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)


def add_standins(server, wanted):
    """Stand-ins on loaded ground, at least 100 blocks apart (director.victimSeparation is 64)."""
    spots = [(0, 0), (120, 0), (0, -120), (-120, 0), (0, 120), (120, 120), (-120, -120), (120, -120), (-120, 120)]
    placed = []
    for x, z in spots:
        if len(placed) == wanted:
            break
        for y in (110, 95, 125, 80, 140):
            reply = server.cmd(f"/walkers director standin add {x} {y} {z}")
            got = re.search(r"Stand-in (standin-\d+) placed at (-?\d+), (-?\d+), (-?\d+)", reply)
            if got:
                placed.append((got.group(1), int(got.group(2)), int(got.group(3)), int(got.group(4))))
                break
            if "not loaded" in reply:
                break
    return placed


def walker_bodies(server):
    """{walker id: body entity id} from /walkers director status."""
    reply = server.cmd("/walkers director status")
    return {int(w): int(e) for w, e in re.findall(r"Walker (\d+): .*? #(\d+) at", reply)}


def main():
    global events, samples_writer, samples_file
    with socket.socket() as probe:
        if probe.connect_ex(("127.0.0.1", int(PORT))) == 0:
            raise SystemExit(f"port {PORT} is in use: another server (or soak run) is still up")
    if OUT.exists():
        shutil.rmtree(OUT)
    data = OUT / "data"
    (data / "Mods").mkdir(parents=True)
    shutil.copy(ZIP, data / "Mods")
    events = open(OUT / "events.log", "w")
    samples_file = open(OUT / "samples.csv", "w", newline="")
    samples_writer = csv.DictWriter(samples_file, fieldnames=[
        "t", "phase", "game_time", "managed_mb", "total_mb", "rss_mb", "avg_tick_ms", "ticks_per_s",
        "max_last10_ms", "chunks", "entities", "director", "stalker"])
    samples_writer.writeheader()

    # ---- Boot 1: set up, three haunts, a day and a night of play, a profile at night.
    s = Server(data, data / "Logs-1", "1")
    sample(s, "idle")
    for command in [
        "/walkers config debug.standInVictims true",
        "/walkers config director.minForestDensity 0",
        "/walkers config director.maxActiveWalkers 3",
        "/walkers config director.hauntChancePerDay 0",  # only the haunts typed here
        f"/time calendarspeedmul {CALENDAR_SPEED}",
        "/time set 17:00",
    ]:
        s.cmd(command)
    placed = add_standins(s, 3)
    note(f"stand-ins: {placed}")
    stages = [4, 5, 2]
    for (uid, *_), stage in zip(placed, stages):
        s.cmd(f"/walkers director haunt {uid} {stage}")
    for _ in range(3):
        s.cmd("/walkers director tick")
    s.cmd("/walkers director status")
    play(s, "day1-evening", PHASE_MINUTES / 2)
    profile(s, PROFILE_SECONDS)
    play(s, "day1-night", PHASE_MINUTES / 2)
    s.cmd("/walkers harass status")
    s.cmd("/walkers stalker status")
    before = s.cmd("/walkers director status", wait=4)
    s.stop()

    # ---- Boot 2: everything came back from the save; another stretch; a victim dies; a walker dies badly.
    s = Server(data, data / "Logs-2", "2")
    after = s.cmd("/walkers director status", wait=4)
    note("director status before restart:\n" + indent(before) + "\nafter restart:\n" + indent(after))
    s.cmd("/walkers director standin list")
    s.cmd("/walkers director ledger")
    play(s, "day2", PHASE_MINUTES)

    # Any walker with a body in the world dies badly; a body that has withdrawn comes back within minutes.
    killed = None
    for _ in range(6):
        bodies = walker_bodies(s)
        if bodies:
            killed, body = sorted(bodies.items())[-1]
            break
        play(s, "day2-wait", 0.5)
    if killed is None:
        note("FAIL: no walker had a body in the world to kill")
    else:
        note(f"killing walker {killed}'s body #{body} with no ritual")
        s.cmd(f"/entity cmd e[id={body}] kill")
        # A doe refuses the death and stands up as the true form (Forms): kill that body too.
        shifted = walker_bodies(s).get(killed)
        if shifted is not None and shifted != body:
            note(f"the guise refused it and stood up as #{shifted}; killing that body")
            s.cmd(f"/entity cmd e[id={shifted}] kill")
        s.cmd("/walkers director tick")
        s.cmd(f"/walkers director status {killed}")
        # The husk's burn window (ritual.huskBurnWindowSeconds, 120 s) and the director's 60 s grace.
        play(s, "burn-window", 3.5)
        s.cmd("/walkers director tick")
        s.cmd(f"/walkers director status {killed}")
    s.cmd("/walkers director ledger")
    s.stop()

    # ---- Boot 3: the ledger survived; skip two seasons; the walker returns for its last victim.
    s = Server(data, data / "Logs-3", "3")
    s.cmd("/walkers director ledger")
    s.cmd("/walkers director status")
    s.cmd("/time")
    s.cmd("/time add 3 months", wait=4)
    s.cmd("/walkers director tick")
    s.cmd("/walkers director ledger")
    s.cmd("/time add 4 months", wait=4)
    s.cmd("/time")
    for _ in range(3):
        s.cmd("/walkers director tick")
    s.cmd("/walkers director status")
    s.cmd("/walkers director ledger")
    play(s, "after-return", 3)
    # Escalation (the owner's five levels): 1 on the first haunt, +1 per improper death, +1 per
    # escalation.monthsPerLevel of live haunting after one. Resting adds nothing; a second improper death
    # and return adds one; ten days (over a 9-day month) inside the 12-day haunt adds one more.
    w = killed or 2
    s.cmd(f"/walkers director status {w}", wait=3)
    s.cmd("/time add 10 days", wait=4)
    s.cmd("/walkers director tick")
    s.cmd(f"/walkers director status {w}", wait=3)
    s.cmd(f"/walkers director end {w} ledger")
    s.cmd(f"/walkers director return {w} now")
    s.cmd("/walkers director tick")
    s.cmd(f"/walkers director status {w}", wait=3)
    s.cmd("/time add 10 days", wait=4)
    s.cmd("/walkers director tick")
    s.cmd(f"/walkers director status {w}", wait=3)
    s.cmd("/walkers furs")
    s.cmd("/walkers harass status", wait=3)
    s.cmd("/time set 21:00", wait=3)
    play(s, "enraged-night", 3)
    s.cmd("/walkers harass status", wait=3)
    # Every victim dies at once (the console's entity selector cannot pick one bot by position): each
    # haunt, the returned one included, must end with VictimDied.
    s.cmd("/entity cmd e[type=playerbot] kill", wait=3)
    s.cmd("/walkers director tick")
    s.cmd("/walkers director status", wait=3)
    s.cmd("/walkers stalker status", wait=3)
    play(s, "victims-dead", 1)
    # Config and commands, on a busy server.
    for command in [
        "/walkers status",
        "/walkers config gaze",
        "/walkers config stalk",
        "/walkers config forms.hurtShiftHealthFraction",
        "/walkers config harass.doorway.enabled false",
        "/walkers config harass.minGapSeconds 5",
        "/walkers reload",
        "/walkers config harass.minGapSeconds 120",
        "/walkers config harass.doorway.enabled true",
        "/walkers me",
        "/walkers harass status",
        "/walkers despawn",
        "/walkers director status",
    ]:
        s.cmd(command)
    sample(s, "end")
    s.stop()
    note("soak done")


if __name__ == "__main__":
    main()
