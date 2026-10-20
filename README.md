# Walkers

A code mod for Vintage Story 1.22.7. Its first creature is the Skinwalker: something that looks like
an ordinary animal until it doesn't. *Something's moving out there.*

Version `1.0.0`, for Vintage Story 1.22.7. The Skinwalker wears the guise of a doe of the local
deer type and shifts to its true form out of sight. The director chooses one lone player at a time
and places it; the stalker AI climbs a five-stage ladder from a distant watcher to the hunt; the
harassment kit works through the night without touching the victim; the forms system shifts it and
judges how it dies; and over a five-level escalation ladder, from Interloping to Wrath, a wronged
walker grows worse. The lore bible's texts arrive as pages, and every rule is a server setting.
What changed in each version is in [CHANGELOG.md](CHANGELOG.md).

| Field | Value |
| --- | --- |
| Mod id and asset domain | `walkers` |
| Type and side | Code, Universal (required on client and server) |
| Game | 1.22.7, .NET 10 (`net10.0`) |
| Based on | the official `VintageStory.Mod.Templates` 1.22.0 |
| License | [MIT](LICENSE) |

## Installing

Players and server owners: put `walkers_<version>.zip`, still zipped, into the `Mods` folder of the
game (`%APPDATA%\VintagestoryData\Mods` on Windows) and of the server's data folder. The mod is
required on both sides, and a client must have exactly the server's version; the game offers to
download it from ModDB on joining. The server writes `ModConfig/walkers.json` on its first start;
see [Config and commands](#config-and-commands).

**Compatibility.** Built against and tested on 1.22.7: every change boots a headless 1.22.7
dedicated server (the smoke test), and a 45-minute soak with three haunts showed zero errors and
about 0.2 ms of server time per second. Other 1.22 versions may load it but are untested; a new game
version gets a new mod version (see [Moving to a new game version](#moving-to-a-new-game-version)).
It uses no Harmony patches and adds rather than replaces: vanilla deer, doors and torches are left as
they are, and the mod acts on them only through the game's own block and entity calls. It has not yet
been tested together with other mods.


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

Feature folders: `Commands` (config and commands), `Director` (director, spawns and the return),
`Stalker` (stalker AI), `Forms` (forms and kill ritual) and `Harassment` (harassment kit).

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
| `/walkers me [choice] [value]` | every player | Shows or sets the player's own choices: `noName`, `noScreenEffects`, `noTitles`, and `scareVolume` (0 to 1). These change only how scares reach the player; nobody can opt out of being haunted |

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

## The director

### For server owners

The director decides who is haunted, when, and where the walker appears; walkers never spawn on
their own. Every 10 seconds it looks at the online players. Each player in survival or guest mode who is past
`director.newPlayerGraceDays`, on the surface, near forest and in deer country rolls
`director.hauntChancePerDay` once an in-game day; a walker then haunts them for at most
`director.hauntMaxDays`. Nobody can opt out; counterplay is fire, silver and a barred door.

- **Deer country.** The guise wears a deer type that lives where its victim is, picked from the
  climate (`forms.guisePickMarginC`, `forms.guisePickMarginRain`), then the wild deer nearby
  (`forms.guiseCensusRadius`). Where no deer type lives, in hot dry land, nobody is haunted and a
  running haunt pauses; `forms.guiseNoFit` set to a type, such as `pampas`, allows haunts there anyway.
- **Pauses.** The haunt pauses while its victim is offline, idle, underground, away from the forest,
  out of the doe's country, in another game mode or in a temporal storm, and the walker withdraws out
  of sight.
- **The return.** A walker killed without the ritual goes into the ledger and comes back
  `return.afterSeasons` later, for the player most marked by walker furs, else its last victim. It
  comes back angrier, not stronger: its escalation level, 1 (Interloping) to 5 (Wrath), is 1 plus
  its improper deaths plus one per `escalation.monthsPerLevel` months of haunting since, and drives
  the harassment kit's acts. At Wrath it returns in half the time, and groups no longer hold it
  back: it may appear near the victim's company (never in anyone's sight) and haunt a victim who is
  near another victim.
- **Unnatural furs.** Skinning the body gives furs that mark whoever carries them in their own
  inventories after `furs.markThresholdHours` of play. Burning the furs or waiting clears the mark;
  `furs.markTarget` off makes them trophies only.

Everything is saved in the world's save game.

| Command | What it does |
| --- | --- |
| `/walkers director status [walker or player]` | Every walker and online player as the director sees them, or one in detail |
| `/walkers director haunt <player> [stage]` | Starts a haunt on that player now, or moves their haunt to a stage (1 to 5) |
| `/walkers director end <walker or player> [rest\|ledger\|banish]` | Ends a haunt (rest), puts the walker in the ledger as if it died, or banishes it |
| `/walkers director return <walker> [now]` | When a walker in the ledger returns; with `now`, at once |
| `/walkers director ledger` | Every walker death, how it died and what came of it |
| `/walkers director tick` | Runs one director update now |
| `/walkers director standin add [position]`, `remove <name>`, `list` | Debug stand-in victims for servers with nobody online; needs `debug.standInVictims` |
| `/walkers furs [status\|clear] [player]` | Who carries walker furs and how marked they are; clears every mark or one player's |

### For feature code

- Get the director with `api.GetWalkersDirector()` (`Walkers/Director/IWalkerDirector.cs`); it is
  null on clients and when the director is not loaded, so a feature still runs on its own.
- A walker's memory is one `TreeAttribute` under `walkers` in every body's `entity.Attributes`: the
  same instance as the director's saved copy, so a value set on a body survives the body. Write only
  your own subtree (`walkers/ai`, `walkers/harass`, `walkers/form`); the top-level keys
  (`WalkerMemory`: id, victim, stage, pace, returns, escalation, guise, withdraw) are the director's.
- A shift creates the new body, calls `AttachBody(walkerId, newBody)` before `SpawnEntity`, then
  retires the old body with `Die(EnumDespawnReason.Expire)`.
- Report to the director instead of keeping your own state: `ReportAct`, `ReportHarm`, `EndHaunt`,
  `Banish`, `Release`, `ClearFurMarks` for burned furs, and `AddRestore` for every block a walker
  changes, with a handler for your kind from `RegisterRestoreHandler`. The stage ladder plugs in with
  `RegisterLadder`.
- Furs carry no walker's identity: any `walkers:walkerfur` in a player's own inventories marks them
  for every walker, while some walker that is not banished has had its furs taken
  (`Release(..., fursTaken: true)`).
- Smoke tests: `tests/smoke/director.*` haunts a stand-in, places the walker, puts it in the ledger,
  and `director.restart.*` checks after a restart that the ledger came back and the return works.

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
| `walkers-doorway` | true | Stage 5: opens an unlocked door the victim is behind (where `harass.respectClaims` allows), holds the doorway, never walks in; off with `harass.doorway.enabled` |

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
  director's to remove. The one exception is the enraged walk-in below.
- **Enraged walk-in.** While `walkers/harass/entryUntilMs` lies ahead, the hunt follows the victim in
  through the door at `entryX/Y/Z`, no farther than `enraged.maxIndoorBlocks` from it. It leaves on a
  landed hit, at the timeout, when the victim lies down, with company, when it burns or breaks off,
  when it goes too far in, or after 5 s without progress inside. On leaving it sets `entryUntilMs` to 0
  and walks back out through the door before anything else. A body still inside 50 s after the
  entry began sets `walkers/withdraw` and vanishes (`Die(Expire)`).
- **Freeze pose.** A true form plays its `freeze` animation while the gaze holds it.
- **Escalation level.** `walkers/escalation` (1 to 5 across returns; without it, returns + 1) changes
  the true form. From 2, a victim's stare of 2 s no longer freezes it: it hunts the starer for 20 s.
  From 3, a victim alone for 30 s at night is hunted too, at any stage (doors stay a stage-5 act).
  At 5 it no longer breaks off for company, and the ladder's group cap stops holding it back. All of
  it only where creatureHostility is aggressive.
- **Restore list.** A door opened by `walkers-doorway` is entered as kind `walkers-doorway`, with its
  own undo handler. The task also closes the door itself when it lets go of it (the hold ends, the
  hunt ends, or the body leaves), and tries again each second for ten minutes while someone stands in
  the doorway.
- **Strikes.** `walkers-lunge` checks its reach with a physical line, so glass blocks a strike even
  though the gaze sees through it.

## Forms and the kill ritual

The forms system (`Walkers/Forms/`) runs on the server. It owns the two bodies, the shift between
them, the rules of a walker's death and the items that come of it.

- **The guise** wears a deer type that lives where its victim is (see the director's deer country).
  With `forms.guiseShrugsDamage` on, nothing a player does kills it: it flinches with the hurt
  animation and sound and is whole again soon after. From stage 3, a guise a player hurts or sets
  alight stands up as the true form `forms.hurtShiftDelayMs` later.
- **The shift.** A new body takes over the walker's memory (`AttachBody`, then the old body is
  retired), at least `forms.shiftCooldownSeconds` apart: to the true form at night as the stalker AI
  allows, back to the guise once a broken-off true form is out of sight. At escalation level 1 a
  walker may shift at night only on about one night in five (`forms.interlopingNightShiftChance`). Once a
  haunt, it may shift in plain sight while its victim watches from within `forms.revealDistance`.
- **The ritual.** A true form killed with silver (`ritual.silverItems`, or any item with the
  `walkersBane` attribute; silver hits do `ritual.silverDamageMultiplier` damage) or by fire is
  banished for good: it leaves ash and bark, its eyes and a page, and the killer gets
  `ritual.banishStabilityRecovery` temporal stability back. Any other death leaves the body for
  `ritual.huskBurnWindowSeconds`: burning it banishes the walker; skinning it gives 2 to
  `ritual.huskFurCount` unnatural furs, and the walker is released to the director, as it is when
  the body is left alone. A lit torch sets it alight (`ritual.torchIgniteChance`).
- **Items.** `walkers:walkerfur` (unnatural fur; burning it reports to the director, which clears the
  dropper's mark), `walkers:walkereyes` (trophy), `walkers:spearhead-silver` (smithed on the
  vanilla anvil from one silver ingot; vanilla has no silver spear head) and `walkers:spear-silver`,
  weaker than copper on purpose. Every other silver weapon is vanilla's own; the mod adds no molds.

| Command | What it does |
| --- | --- |
| `/walkers forms shift [entity id]` | Shifts every loaded walker, or the one with that entity id, into its other body at once |
| `/walkers forms test` | Spawns walkers at the world spawn, runs the shift, the guise's death and the silver and fire kills on them, and removes them (the smoke test runs it) |

For feature code: Forms writes only `walkers/form` (`FormMemory.cs` lists the keys) and reads
`walkers/stage` and `walkers/ai`. It reports deaths to the director with `Banish` or `Release`, and
raises `FormsModSystem.FursBurned` when furs burn. The guise refuses a death through the director's
`EntityWalker.RefuseDeath` hook, so a console kill on a doe stands it up as the true form; admins use
`/walkers despawn`.

## Harassment kit

The harassment kit (`Walkers/Harassment/`) runs on the server. For every walker the stalker AI tracks
with a victim, it draws non-combat acts from a nightly budget (`harass.actsPerNight`, by stage), at
least `harass.minGapSeconds` apart plus up to two minutes, with one quiet stretch a night and never the
same act twice in a row. It holds back for 10 seconds after the victim is hurt, during a temporal
storm, while the walker withdraws, and from stage 4 while another player is within 24 blocks.

| Act | Stages | What it does |
| --- | --- | --- |
| `echoes` | 3-5 | Plays back one of the victim's own recent block sounds behind them, or footsteps on their ground |
| `knocks` | 3-5, level 2+ | Raps on the outer face of the nearest door (one rap, two in stage 4, three at dusk in stage 5) and leaves a page |
| `wrongCalls` | 2-5 | A deer, small-deer or elk call from 20-40 blocks off, at a lowered pitch; sometimes another answers |
| `whispers` | 3-5 | A line from the lore bible's whisper table; W5 uses the player's name only when their prefs allow it |
| `douseTorches` | 3-5 | A lit torch out of the victim's sight goes out and stays out until a player relights it (the level's `torchesPerNight`); from level 4 it is eaten |
| `doors` | 4-5 | Shuts an open door or gate, or swings a closed one open out of sight; closed again after `harass.doors.maxOpenHours` |
| `chestLids` | 4-5 | A chest lid out of sight lifts and drops; never one a player has open |
| `sleepDisturb` | 4-5 | A scrape at the wall wakes the victim and leaves them too tired to sleep again at once |
| `breakIn` | 5, enraged | On a night the level's `breakInChance` comes up, tears the nearest closed single door off its hinges near the awake victim (alone, below level 5), within the level's wooden and iron-or-heavy door budgets; pops lids and douses torches within `harass.breakIn.reach`, then opens the way in for the stalker AI. A padlock, bar, reinforcement, anything behind the door, a gate or double door, a spent budget or a foreign claim keeps it out; then it scrapes at the boards |

Beside the drawn acts: the stability drain (toward `harass.stabilityDrain.targets` per stage, never
below `floor`, faster while watched, handed back while paused, the warp of `harass.mundaneWarp` in
mundane mode), livestock panic near the walker, the dawn offering at the victim's door once a haunt,
the two titles ("Something's moving out there.", "The Forest Saw That." for whoever hits the guise),
screen shake, and the Pages act, which hands out the lore journals' pages on the lore bible's triggers.
Every act has its own `harass.<act>.enabled` switch and a nightly cap. Doors, trapdoors, gates, chests
and torches are touched only within 16 blocks of the victim, never under a padlock, and only where
`harass.respectClaims` allows (`victimAccess`: unclaimed land or a claim the victim owns, shares
through its group, or was granted access to). `harass.checkClaims` (on by default) switches every claim
check, the break-in, eaten crops and the stalker AI's doorway included, off at once; padlocks and bars
still hold.

**The escalation ladder** (creature bible, section 7; the owner's ladder). The level is the
director's `walkers/escalation`, 1 to 5 (else 1 + `walkers/returns`; always 1 while
`escalation.enabled` is off), and `escalation.levels.level1` to `level5` set what the kit may do at
each, per haunt unless named per night:

| Level | Kit defaults |
| --- | --- |
| 1 Interloping | 8 crops, 1 torch a night, no knocks |
| 2 Grudge | knocks, 12 crops, 3 torches a night, 1 dead animal |
| 3 Rage | enraged; 3 dead animals; break-in on 1 hunt night in 4, 2 wooden and 1 iron or heavy door |
| 4 Hate | break-in on 9 hunt nights in 10, 4 wooden and 2 other doors, torches eaten |
| 5 Wrath | a break-in every hunt night, company no longer keeps it out |

A walker is enraged from `enraged.fromLevel` (3), or after a hit if `enraged.onProvoked` is on (off by
default), and never while creatureHostility is off. Enraged, its dusk knocks end in a long scrape and
an iron-and-stone whisper, and it may break in (`enraged.entriesPerNight`); the stalker AI then comes
in for at most `enraged.maxIndoorSeconds` and `maxIndoorBlocks`. A torn door drops whole as an item
and is hung back at dawn if it still lies there. The dead animals are the rotting herd (does 16 to 24
blocks from the door, rot only, gone after `escalation.herd.decayHours`), one of which may be the
victim's own farm animal (`escalation.herd.killLivestock`, off); eaten crops are lines of ripe crops
set back, only on the victim's own or shared claims by default. Stalking, stare and isolation aggro
are the stalker AI's, shifting is Forms', and the halved return at level 5 is the director's.

| Command | What it does |
| --- | --- |
| `/walkers harass status [radius]` | Each haunting walker's budget, tonight's counts, what holds it back, and the last five acts |
| `/walkers harass act <code> [radius]` | Runs one act now, under its own rules (switch, stage, time of day, cap) |
| `/walkers harass selftest` | Every act against a playerbot stand-in in a small test house above the world spawn (the smoke test runs it) |

For feature code:

- **The walker's memory.** The kit writes only `walkers/harass` (`HarassMemory.cs` lists the keys);
  `drainTaken` is the total it drained, which the ladder reads. It reads `walkers/ai` (provoked,
  sightings, lastPlayerHitMs, hauntBegan), `walkers/form/revealed` and `walkers/escalation`. For the
  stalker AI's walk-in it keeps `enraged` and the entry (`entryX`, `entryY`, `entryZ`, `entryUntilMs`,
  `entryTorn`); the hunt sets `entryUntilMs` to 0 when it leaves.
- **Restore list.** Doors it opens go on the director's list as kind `walkers-harass-door` with their
  own undo handler, and torn doors as `walkers-harass-torndoor`, hung back at dawn. Doused torches
  are not tracked: they stay out until a player relights them. Without a director it keeps its own
  list and undoes it the same way.
- **Pages.** Driven by the director's `WalkerReleased` and `WalkerBanished` events and its ledger, so
  no other feature calls the kit. Every page is checked against its journal's chapter count before it
  is handed out; the painting journals are never handed out as pages.

## Folder map

```
.github/workflows/build.yml   CI: setup, build, smoke test
CakeBuild/                    build steps (validation, build, package)
scripts/versions.env          pinned game version, download URL and checksums
scripts/setup.sh              SDK and game server setup
scripts/smoke-test.sh         headless server test
scripts/release.sh            release zip, checksum and notes (publishes nothing)
CHANGELOG.md                  every release, player-facing
LICENSE                       MIT
docs/moddb.md                 the ModDB page's text
tests/smoke/                  per-feature smoke expectations and console commands
Walkers/                      the mod project
  modinfo.json
  Core/                       mod identity and startup log line
  Config/                     the shared config: walkers.json, its checks, player choices
  Commands/                   the /walkers command
  Stalker/                    the stalker AI: ladder, gaze, brain, AI tasks, /walkers stalker
  Director/                   the director: haunts, placement, ledger, return, escalation, furs
  Forms/                      the shift, the kill ritual, the body and its items, /walkers forms
  Harassment/                 the harassment kit: acts, scheduler, pages, /walkers harass
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

## Releasing

1. On `main`, set `"version"` in `Walkers/modinfo.json` (plain `x.y.z`) and add its section at the top
   of `CHANGELOG.md`. The changelog is pasted into ModDB, so it never mentions the return.
2. Run `scripts/release.sh`. It refuses a dev version, a missing changelog section or uncommitted
   changes, then runs setup and the build, adds `LICENSE` and `CHANGELOG.md` to the zip, smoke-tests
   that finished zip, and writes `Releases/walkers_<version>.zip.sha256` and
   `Releases/walkers_<version>-notes.md` (the changelog section). `--allow-dirty` makes a trial run.
3. The owner publishes: tag `v<version>` on that commit, create the GitHub release with the zip and
   the notes, then upload the same zip on ModDB with the notes and the game version tag. The page's
   text is in [docs/moddb.md](docs/moddb.md).
