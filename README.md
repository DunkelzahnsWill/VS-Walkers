# Walkers

A code mod for Vintage Story 1.22.7. Its first creature is the Skinwalker: something that looks like
an ordinary animal until it doesn't. *Something's moving out there.*

Status: prototype, version `0.1.0-dev.1`. The mod builds, packs and loads cleanly on a dedicated
server. The Skinwalker has a deer guise and a placeholder true form that spawn alone at night in
forests (or with `/walkers spawn`), and the stalker AI drives both: it picks the nearest player, climbs
the five-stage ladder, freezes while watched and hunts at stage 5. The director, form shifts, kill
ritual and harassment are still to come.

| Field | Value |
| --- | --- |
| Mod id and asset domain | `walkers` |
| Type and side | Code, Universal (required on client and server) |
| Game | 1.22.7, .NET 10 (`net10.0`) |
| Based on | the official `VintageStory.Mod.Templates` 1.22.0 |

## Build and test in the cloud (or any Linux x64 machine)

```bash
scripts/setup.sh        # .NET 10 SDK if missing, and the 1.22.7 dedicated server (MD5 and SHA-256 checked) in .cache/
./build.sh              # validate JSON and modinfo.json, build, pack Releases/walkers_<version>.zip
scripts/smoke-test.sh   # boot a headless server with that zip; fails on any error in the log
```

Setup takes about a minute the first time (a 54 MB download) and skips finished steps afterwards.
The build takes about 10 seconds, the smoke test about two minutes (it boots the server twice).

- **Game DLLs.** The mod compiles against the DLLs of the game folder named by the `VINTAGE_STORY`
  environment variable. When it is not set, `Directory.Build.props` points it at the server that
  `scripts/setup.sh` unpacks, so plain `dotnet build Walkers/Walkers.csproj` also works. The DLLs
  and the server are never committed (`.cache/` is ignored).
- **Pinned versions.** The game version, download URL and both checksums live in
  `scripts/versions.env`. The setup refuses to unpack a download whose checksums differ.
- **Build steps** (`CakeBuild/Program.cs`, run with `./build.sh --target=<step>`):
  `ValidateJson` parses every asset JSON and fails on syntax errors, a key repeated in a lang file,
  and a lang key defined in two lang files (compared the way the game merges them, domain prefix and
  `worldconfig-<lang>.json` included). A key repeated in any other asset is only a warning, because
  vanilla files copied as a starting point do it; the game uses the last value. `ValidateModInfo` checks the modid, version format, type,
  side, game dependency, and that the only asset folders are `walkers` and `game`. `Build` publishes
  the DLL and `Package` writes the zip. Add `--configuration=Debug` for a debug build.
- **Smoke test.** It copies the zip into a fresh server data folder, boots the server with a fixed
  world seed, types any console commands from `tests/smoke/*.commands`, then sends `/stop`. It fails
  unless the server stops cleanly, `walkers` is in the loaded mod list, every pattern in
  `tests/smoke/*.expect` matches a line of `server-main.log`, and the logs hold no `[Error]` or
  `[Fatal]` line, no `[Warning]` line that mentions the mod, and no crash log. When there are
  `tests/smoke/*.restart.commands`, it then boots the server a second time on the same world, types
  those in and stops it again, and every `tests/smoke/*.restart.expect` must match that boot's log
  (`data/Logs-restart/`), so a feature can check what it saved. The world and logs stay in
  `.cache/smoke/` afterwards.
- **CI.** `.github/workflows/build.yml` runs the same three scripts on every pull request and every
  commit on `main` (a new push to a pull request cancels its older run). Every run that gets as far
  as a zip keeps it as an artifact named `walkers_<version>.zip`, also when the smoke test fails, and
  every run keeps the server logs of both boots (artifact `smoke-test-logs`).

## Play-testing on your PC

The cloud covers building and the server side. Your PC is needed for anything a player sees or hears:
the model, animations, eye glow, sounds, client-side effects, and multiplayer tests with real clients.

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (only needed to build
   on the PC; skip it if you use CI zips).
2. Point `VINTAGE_STORY` at your game folder. With the Windows installer's default location, run this
   in PowerShell once, then restart your terminal and editor:
   `[Environment]::SetEnvironmentVariable("VINTAGE_STORY", "$env:APPDATA\Vintagestory", "User")`
3. Get a mod zip: download the `walkers_<version>.zip` artifact from the pull request's GitHub Actions
   run (Checks, then the run, then Artifacts), or build it with `.\build.ps1`. If PowerShell refuses
   to run scripts, use `dotnet run --project CakeBuild/CakeBuild.csproj` from the repository folder.
4. Copy `walkers_<version>.zip`, still zipped, into `%APPDATA%\VintagestoryData\Mods`, start the game
   and check that the Mod Manager lists Walkers. For a multiplayer test, the server and every client
   need the same zip.
5. For quick iteration, the template's launch profiles (`.vscode/launch.json`, and
   `Walkers/Properties/launchSettings.json` for Visual Studio and Rider) start the game with the
   repository's build and asset folders, so JSON edits show without packing a zip.

When something goes wrong, `%APPDATA%\VintagestoryData\Logs\client-main.log` is the file to share.

## Working in parallel

Each piece of work happens on its own branch with a pull request to `main`, and CI must be green
before merging. The layout lets branches stay out of each other's files:

| What | Where it goes |
| --- | --- |
| C# code | its own folder, `Walkers/<Feature>/` |
| Startup | its own `ModSystem` in that folder, e.g. `Walkers/Stalker/StalkerModSystem.cs`. The game finds every ModSystem on its own, so nothing needs registering in a shared file |
| Settings | its own section in `Walkers/Config/WalkersConfig.<Feature>.cs`, a `partial` of the shared config class (example in `WalkersConfig.cs`) |
| Text | `Walkers/assets/walkers/lang/<feature>/en.json`. The game merges every `en.json` under `lang/`, and the build fails if two files define the same key |
| JSON patches | `Walkers/assets/walkers/patches/<feature>-*.json` |
| Smoke checks | `tests/smoke/<feature>.expect` and, if needed, `tests/smoke/<feature>.commands`; checks after a restart in `tests/smoke/<feature>.restart.commands` and `.restart.expect` |

Feature folders: `Commands` (config and commands) and `Director` (director, spawns and the return) so
far; planned are `Stalker` (stalker AI), `Forms` (forms and kill ritual) and `Harassment` (harassment kit).

The shared surfaces, and who changes them:

- **The config** (`Walkers/Config/`) is the one piece of code every feature uses; see
  [Config and commands](#config-and-commands) below. The config and commands work owns
  `WalkersConfig.cs`, `WalkersConfigJson.cs`, `WalkersConfigSystem.cs` and `WalkersPlayerPrefs.cs`.
  Each feature owns its own `WalkersConfig.<Feature>.cs`; the director, stalker AI, forms and
  harassment ones start as drafts of the creature bible's settings, and their features change them freely.
- **The `/walkers` chat command** is one root shared by every feature. Get it with
  `api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)` and add only your own
  `.BeginSubCommand("<feature>")`; never `Create` the root, or the second feature to start fails once
  both are on `main`. The config and commands work sets the root's description and privilege.
  Network channels are named `walkers-<feature>`.
- **The creature's entity JSON** (`assets/walkers/entities/`) belongs to the JSON prototype work. A
  code branch that needs a change there keeps it small and says so in its pull request.
- **`modinfo.json` and `Walkers/Walkers.csproj`** rarely change. The project already references every
  game assembly the plan needs (API, VSEssentials, VSSurvivalMod, Newtonsoft.Json, protobuf-net). The
  version changes only at release.
- **`lang/en.json`** holds only general keys such as the mod name.

Conventions that keep the mod compatible with others: everything under the `walkers` domain, every
registered code prefixed with the mod id (AI tasks `walkers-<name>`, behaviors and commands likewise,
using `WalkersModSystem.ModId`), no Harmony, additive patches only, and server-only C# unless a
client effect truly needs client code.

## Config and commands

### For server owners

Every setting lives in `ModConfig/walkers.json` in the server's data folder (on Windows,
`%APPDATA%\VintagestoryData\ModConfig\walkers.json`). The server writes it with the defaults on its
first start with the mod, and adds settings that later versions bring. Change it either way:

- edit the file and type `/walkers reload`, or
- set one value with `/walkers config <setting> <value>`, which saves it to the file and applies it.

Changes apply at once, without a restart. Nothing in the file can stop the server: a value it cannot
read keeps its default, a number out of range is clamped to the nearest allowed value, and an
unknown key is ignored. Each case appears as a warning in the server log and under
`/walkers status`. While the file has such a problem, or comments, the server never rewrites it, so
nothing you typed is lost; fix it and reload. A file that cannot be read at all leaves the settings
as they were (on startup, the defaults) until it is fixed.

| Command | Who | What it does |
| --- | --- | --- |
| `/walkers status` | admins | Version, master switch, config problems, every loaded walker with its position |
| `/walkers spawn <form> [player]` | admins | Spawns a walker 8 blocks in front of you or the named player; from the console, at the world spawn. Forms: `animal` (a random deer type), `animal-<deertype>`, `true` |
| `/walkers despawn [radius]` | admins | Removes every loaded walker, or those within the radius of you. A removal is not a death: no drops, and it does not count as a kill |
| `/walkers reload` | admins | Reads the file again and lists what changed |
| `/walkers config [setting] [value]` | admins | Without arguments lists the sections; with a section or setting shows its values, default, range and meaning; with a value sets it |
| `/walkers me [choice] [value]` | every player | Shows or sets the player's own choices: `noName`, `noScreenEffects`, `noTitles`, `scareVolume` (0 to 1), and `noHaunt` where `director.allowPlayerOptOut` is on |

Admin commands need the `controlserver` privilege. Settings use the paths the file shows, such as
`harass.doors.enabled`; case does not matter. Times ending in Seconds, Minutes or Ms are real time;
Hours, Days, Nights and Seasons are in-game time. The settings beyond `enabled` and `mundaneMode` are
drafts from the creature bible until their features land.

### For feature code

- **Read** the config once in your system's `Start` and keep it:
  `var config = api.GetWalkersConfig();`. Read values at the moment you use them
  (`config.Harass.Doors.Enabled`); a reload changes them in place, in the same objects. If you build
  something from the values (a lookup table, a timer), rebuild it on `WalkersConfigSystem.Changed`.
  The server has the file's values; a client gets the server's copy a moment after joining and has
  the defaults before that.
- **Add a setting** to your own `Walkers/Config/WalkersConfig.<Feature>.cs`: a property with a
  default, a `[Description]` and, for numbers, a `[Range]`. A new top-level section also takes a
  `[JsonProperty(Order = …)]` (director 10s, stalker AI 20s, forms 30s, return 40s, harassment 50s,
  new features 60 and up). The rules are in the comment on `WalkersConfig`; the file, the checks,
  `/walkers config` and the client copy pick a new setting up on their own.
- **A player's choices**: `player.GetWalkersPrefs()` before acting on that player, for example skip
  the drain when `NoScreenEffects` is set.
- **Your own subcommand**: `api.ChatCommands.GetOrCreate(WalkersModSystem.ModId).BeginSubCommand("ledger")…EndSubCommand()`
  in your own system. It inherits the admin privilege. To add a line to `/walkers status`, subscribe
  to `WalkersCommandsSystem.Status`.
- **Replies**: wrap your handler in `WalkersCommandsSystem.SafeReply(...)`, so a brace in a reply
  (a JSON error, a player's argument) reaches the caller instead of breaking the server's formatting.
- **Smoke tests**: `tests/smoke/config.expect` proves the file is written with every default in
  range; `tests/smoke/commands.*` runs each command from the console. All `.commands` files share one
  server run, in file-name order, so a test that changes a setting restores it at the end.

## Stalker AI

The stalker AI (`Walkers/Stalker/`) runs on the server. It keeps a brain for every loaded walker body
(`WalkerBrain`): its victim, a gaze check twice a second, a mode, the freeze and the slip-away. The
brain picks the mode; the AI tasks in the entity JSON act on it.

| Task | Form | What it does |
| --- | --- | --- |
| `walkers-stare` | both | Slot 1: turns the body toward the victim in small steps while it stands still |
| `walkers-creep` | both | Keeps the stage's distance band, moves a little closer after every look, never into bright block light; stage 4 at night, the true form stands far off in the open |
| `walkers-hunt` | true | Stage 5: closes in while unwatched, runs within 16 blocks, holds at doors and while the victim sleeps |
| `walkers-lunge` | true | From within `gaze.lungeRange`: a dash and one hit, capped at `forms.maxHitFraction` of the target's health |
| `walkers-retreat` | both | Into cover after a break-off, a withdraw or a hit on the guise; to water while burning |
| `walkers-doorway` | true | Stage 5: opens an unlocked door the victim is behind, holds the doorway, never walks in |

Without a director, a body picks the nearest counting player within 64 blocks and climbs the ladder
on its own every 10 seconds (a guise starts at stage 1, a true form at 5). Admin commands:

| Command | What it does |
| --- | --- |
| `/walkers stalker status [radius]` | Each walker's mode, stage, victim, gaze, freeze, slip-away mark and ladder clocks |
| `/walkers stalker stage <1-5> [radius]` | Sets the stage; its clocks start again |
| `/walkers stalker victim <player or entity id> [radius]` | Sets the victim of walkers the director did not place; an entity id makes a stand-in |
| `/walkers stalker selftest` | Ladder, view cone, gaze and slip-away checks with a playerbot stand-in (the smoke test runs it) |

For feature code:

- **The walker's memory.** The ladder is the only writer of the top-level `walkers/stage` during a
  haunt. Everything else the stalker AI keeps is in `walkers/ai`; other features read it and never
  write it. `StalkerMemory.cs` lists every key. The ones meant for others: `watched`,
  `victimWatching`, `watchers`, `lastSeenMs` (the last gaze check), `breakOff` (why a true form broke
  off: health, fire, company, dawn, drivenoff, stoodup, withdraw, huntover; empty when it has not),
  `mode`, `silhouettesTonight`, `provoked`, `lastPlayerHitMs` and `lastAttacker`.
- **The gaze in code.** `WalkerGaze.IsWatched(body, out watchers)` reads the last check;
  `WalkerGaze.CheckNow(body, out watchers)` runs one. `StalkerModSystem.BrainOf(body)` gives the brain
  (mode, victim, frozen), or null for a body the AI does not track.
- **The ladder.** `StalkerLadder` implements `IWalkerLadder`; it registers with the director at the
  RunGame phase. `OnHarm` gives its stage bonus once per haunt, so a hit reported by both forms and the
  brain counts once. Stages 3 and 4 need `ladder.stageMinActs` acts (`IWalkerDirector.ReportAct`).
  The low-stability pace leaves out the walker's own drain if the harassment kit writes the total it
  drained as a double at `walkers/harass/drainTaken`.
- **Forms.** Shift a guise to the true form in stage 4 at night only while `stalk.silhouettes` is on
  and `walkers/ai/silhouettesTonight` is below `stalk.silhouettesPerNight`; past that the true form
  breaks off with `withdraw`. Shift a true form with a non-empty `breakOff` back once nobody sees it.
- **Removal.** The stalker AI removes a body itself only to slip away (`Die(Expire)`, logged at debug
  level), never while it burns or within 20 seconds of a player's hit. Withdrawing bodies are the
  director's to remove.
- **Restore list.** A door opened by `walkers-doorway` is entered as kind `walkers-doorway`, with its
  own undo handler. The task also closes the door itself when it lets go of it (the hold ends, the
  hunt ends, or the body leaves), and tries again each second for ten minutes while someone stands in
  the doorway.
- **Strikes.** `walkers-lunge` checks its reach with a physical line, so glass blocks a strike even
  though the gaze sees through it.

## Folder map

```
.github/workflows/build.yml   CI: setup, build, smoke test
CakeBuild/                    build steps (validation, build, package)
scripts/versions.env          pinned game version, download URL and checksums
scripts/setup.sh              SDK and game server setup
scripts/smoke-test.sh         headless server test
tests/smoke/                  per-feature smoke expectations and console commands
Walkers/                      the mod project
  modinfo.json
  Core/                       mod identity and startup log line
  Config/                     the shared config: walkers.json, its checks, player choices
  Commands/                   the /walkers command
  Stalker/                    the stalker AI: ladder, gaze, brain, AI tasks, /walkers stalker
  Director/                   the director's interface (IWalkerDirector.cs)
  assets/walkers/             entities, shapes, textures, sounds, itemtypes, patches, lang, config/lore
```

Empty asset folders hold a `.gitkeep`, which the package step leaves out of the zip. A zip mod reads
world-config options only from `worldconfig.json` at the zip root, so that file goes in `Walkers/`
next to `modinfo.json`; the build copies it to the dev output and the package step to the zip root.
A mod with a `worldconfig.json` also needs `assets/game/lang/worldconfig-en.json`: without that
folder the server logs an error and the smoke test fails.

## Moving to a new game version

1. Update the three `VS_SERVER` lines and `VS_VERSION` in `scripts/versions.env` (MD5 from
   <https://api.vintagestory.at/stable.json>, SHA-256 from `sha256sum` on the download).
2. Update `"game"` in `Walkers/modinfo.json`.
3. Run `scripts/setup.sh`, `./build.sh` and `scripts/smoke-test.sh`.

Never reuse a version number: a joining client must have exactly the server's version of the mod.
