#!/usr/bin/env python3
"""Turns a tests/soak/soak.sh run folder (.cache/soak) into a Markdown report on stdout.

Usage: python3 -I tests/soak/analyze.py .cache/soak
"""
import csv
import json
import re
import statistics
import sys
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

run = Path(sys.argv[1])
data = run / "data"
LOG_TIME = re.compile(r"^(\d\d\.\d\d\.\d{4} \d\d:\d\d:\d\d) \[([^\]]+)\] (.*)")
MONTHS = "January February March April May June July August September October November December".split()


def log_lines(name):
    """(time, level, text) for every line of that log in every boot, in order; continuation lines join the line above."""
    out = []
    for logs in sorted(data.glob("Logs-*")):
        path = logs / name
        if not path.exists():
            continue
        for raw in path.read_text(errors="replace").splitlines():
            m = LOG_TIME.match(raw)
            if m:
                out.append([datetime.strptime(m.group(1), "%d.%m.%Y %H:%M:%S"), m.group(2), m.group(3), logs.name])
            elif out:
                out[-1][2] += "\n" + raw
    return out


def pct(values, q):
    values = sorted(values)
    return values[min(len(values) - 1, int(q * len(values)))] if values else float("nan")


def game_days(text):
    """'1. May, Year 0, 08:17' -> days since the start of year 0 (12 months of 9 days, the default)."""
    m = re.match(r"(\d+)\. (\w+), Year (\d+), (\d+):(\d+)", text or "")
    if not m:
        return None
    day, month, year, hour, minute = int(m.group(1)), MONTHS.index(m.group(2)) if m.group(2) in MONTHS else 0, int(m.group(3)), int(m.group(4)), int(m.group(5))
    return (year * 12 + month) * 9 + (day - 1) + (hour + minute / 60) / 24


rows = list(csv.DictReader(open(run / "samples.csv")))
print("# Walkers soak report\n")

# ---- Ticks and memory per phase
print("## Server tick and memory, per phase\n")
print("| Phase | Samples | Game time span | Avg tick ms (median / max) | Slowest of last 10 ticks, p95 / max ms | Managed MB first → last (max) | RSS MB first → last (max) | Entities |")
print("| --- | --- | --- | --- | --- | --- | --- | --- |")
phases = []
for row in rows:
    if not phases or phases[-1][0] != row["phase"]:
        phases.append((row["phase"], []))
    phases[-1][1].append(row)
for phase, items in phases:
    num = lambda k: [float(r[k]) for r in items if r[k] not in ("", None)]
    avg, mx, man, rss, ent = num("avg_tick_ms"), num("max_last10_ms"), num("managed_mb"), num("rss_mb"), num("entities")
    span = f"{items[0]['game_time']} → {items[-1]['game_time']}"
    print(f"| {phase} | {len(items)} | {span} | {statistics.median(avg) if avg else '-'} / {max(avg) if avg else '-'} | "
          f"{pct(mx, 0.95) if mx else '-'} / {max(mx) if mx else '-'} | {man[0] if man else '-'} → {man[-1] if man else '-'} ({max(man) if man else '-'}) | "
          f"{rss[0] if rss else '-'} → {rss[-1] if rss else '-'} ({max(rss) if rss else '-'}) | {int(min(ent)) if ent else '-'}–{int(max(ent)) if ent else '-'} |")
print()

# ---- CPU profile
profile = next(iter(run.glob("*.speedscope.json")), None)
if profile:
    doc = json.loads(profile.read_text())
    frames = [f["name"] for f in doc["shared"]["frames"]]
    walker_frame = [("Walkers." in n) for n in frames]
    total_cpu = 0.0
    walkers_total = 0.0
    by_method = Counter()
    by_thread = []
    for prof in doc["profiles"]:
        thread_walkers = 0.0
        thread_total = 0.0
        if prof["type"] == "evented":
            stack, last = [], prof["startValue"]
            for ev in prof["events"]:
                at = ev["at"]
                dt = at - last
                if stack:
                    thread_total += dt
                    # Leaf-most Walkers frame owns the time; any Walkers frame on the stack counts it once.
                    ws = [f for f in stack if walker_frame[f]]
                    if ws:
                        thread_walkers += dt
                        by_method[frames[ws[0]]] += dt
                last = at
                if ev["type"] == "O":
                    stack.append(ev["frame"])
                elif stack:
                    stack.pop()
        else:
            for sample, weight in zip(prof["samples"], prof["weights"]):
                thread_total += weight
                ws = [f for f in sample if walker_frame[f]]
                if ws:
                    thread_walkers += weight
                    by_method[frames[ws[0]]] += weight
        total_cpu += thread_total
        walkers_total += thread_walkers
        by_thread.append((prof["name"], thread_total, thread_walkers))
    duration = max((p.get("endValue", 0) - p.get("startValue", 0)) for p in doc["profiles"]) or 1
    unit = doc["profiles"][0].get("unit", "milliseconds")
    print(f"## CPU profile at night ({profile.name}, {duration / 1000:.0f} s of {unit})\n")
    print(f"Time with a `Walkers.*` frame on the stack: **{walkers_total:.0f} ms over {duration / 1000:.0f} s, "
          f"{walkers_total / duration * 1000:.2f} ms per real second**, summed over all threads "
          f"(dotnet-trace samples each thread's stack, waits included, so this is an upper bound on CPU).\n")
    print("| Thread | Sampled ms | With a Walkers frame, ms |")
    print("| --- | --- | --- |")
    for name, tot, wk in sorted(by_thread, key=lambda t: -t[2])[:6]:
        print(f"| {name} | {tot:.0f} | {wk:.0f} |")
    print("\n| Outermost Walkers method | ms |")
    print("| --- | --- |")
    for name, ms in by_method.most_common(15):
        print(f"| `{name[:110]}` | {ms:.0f} |")
    print()

# ---- Harassment acts
debug = log_lines("server-debug.log")
samples_at = [(datetime.fromtimestamp(float(r["t"])), game_days(r["game_time"])) for r in rows if game_days(r["game_time"]) is not None]


def game_at(when):
    best = min(samples_at, key=lambda s: abs((s[0] - when).total_seconds()), default=None)
    if not best:
        return None
    # Game time moves on between samples; extrapolate from the nearest sample at the calendar's pace.
    return best[1]


ACT = re.compile(r"Harassment: (\S+) on (\S+) \(stage (\d)\): (.*)")
SKIP = re.compile(r"Harassment: (\S+) skipped for (\S+): (.*)")
acts = defaultdict(list)
skips = Counter()
for when, level, text, boot in debug:
    m = ACT.search(text)
    if m:
        acts[m.group(2)].append((when, m.group(1), int(m.group(3)), m.group(4), boot))
    m = SKIP.search(text)
    if m:
        skips[(m.group(1), m.group(3).split(":")[0][:60])] += 1
print("## Harassment acts\n")
if not acts:
    print("No act lines in server-debug.log.\n")
for victim, items in sorted(acts.items()):
    counts = Counter(code for _, code, *_ in items)
    # harass.minGapSeconds spaces the drawn acts; titles, pages and the dawn offering come on their own triggers.
    drawn = [i for i in items if i[1] not in ("titles", "pages", "offerings")]
    gaps = [(b[0] - a[0]).total_seconds() for a, b in zip(drawn, drawn[1:]) if a[4] == b[4]]
    nights = Counter()
    for when, code, stage, detail, boot in items:
        g = game_at(when)
        if g is not None:
            # A night runs from noon to noon, keyed by the evening's day.
            nights[int(g - 0.5)] += 1
    print(f"**{victim}**: {len(items)} acts, stages {sorted(set(s for _, _, s, *_ in items))}; "
          f"shortest gap between two drawn acts {min(gaps) if gaps else '-'} s real; per night (noon to noon) {dict(nights)}")
    print("  " + ", ".join(f"{c} {n}" for c, n in counts.most_common()))
    print()
if skips:
    print("Skips (act, reason): " + "; ".join(f"{a} '{r}' ×{n}" for (a, r), n in skips.most_common(20)) + "\n")

# ---- Director story
main = log_lines("server-main.log")
print("## Director and walker events (server-main.log, [walkers] lines)\n")
print("```")
for when, level, text, boot in main:
    if "[walkers]" in text and not text.startswith("[walkers] Reloaded") and "Handling Console" not in text:
        print(f"{boot} {when:%H:%M:%S} {level}: {text.splitlines()[0][:220]}")
print("```\n")

# ---- Problems
print("## Errors and warnings\n")
problems = []
for name in ("server-main.log", "server-debug.log"):
    for when, level, text, boot in log_lines(name):
        if re.search(r"Error|Fatal", level) or (re.search(r"Warning", level) and re.search(r"walkers", text, re.I)):
            problems.append(f"{boot}/{name} {when:%H:%M:%S} [{level}] {text[:600]}")
crashes = [p for logs in data.glob("Logs-*") for p in logs.glob("*crash*")]
print("```")
for line in sorted(set(problems)) or ["none"]:
    print(line)
for c in crashes:
    print(f"CRASH LOG {c}")
print("```")
