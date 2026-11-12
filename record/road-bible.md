# The Road Bible

The Road Bible of Walkers. The planning thread writes it from the owner's vision as `record/rulings.md`
holds it, and writes every change to it (THE CAST and THE RECORD, in `record/COMMANDMENTS.txt`). Rule 4
says how each part of it may change.

How to read it:

- A mark such as (R9) names the entry of `record/rulings.md` that a sentence rests on. Where two entries
  disagree, the later ruling stands (R15).
- The mark (1.1.1) is on a sentence that rests on no ruling: it describes the mod as version 1.1.1 was
  designed and ships. The owner's merge of this Road Bible is what makes such a sentence stand.
- A haunt STAGE and an escalation LEVEL are two different scales, both 1 to 5. This book always writes
  the word with the number.

This book starts from version 1.1.1 of the mod, which is on `main` and public.

## Part 1. The concept

### What Walkers is

Walkers is a mod for the game Vintage Story, made for game version 1.22.7. It adds one creature, the
Skinwalker, and the haunting it carries out. It is a single code mod that the server and every client
install (R10). It is public, under the MIT licence (R14), and it must stay safe in multiplayer and cheap
in compute and size (R14).

The creature, called the walker in this book:

1. **It is real.** One creature that every player sees, can hurt and can kill. No effect that one player
   alone sees stands in for it (R10).
2. **It has two bodies.** The guise is a doe of the deer type that lives where its victim is, with
   something wrong about her eyes (R10, R11). The true form is taller than a man and thin, furs over a
   dark woody body, red eyes in a plain bark face (R10, R13, R14). A shift swaps one body for the other.
3. **It wants one player, alone.** Each walker haunts one player at a time, its victim, chosen by the
   server (1.1.1). Nobody can opt out (R11).
4. **A haunt climbs five STAGES,** from a distant watcher at STAGE 1 to the hunt at STAGE 5. Company
   holds a haunt at STAGE 3: the walker watches and mimics, and does not hunt (1.1.1). Only at LEVEL 5
   does company stop holding it (R12).
5. **A gaze holds it still.** While a player watches it, it does not move, and that is the player's
   first defence (1.1.1). The hold begins after half a second of unbroken watching and ends after a
   second and a half unwatched (R12). With the settings as version 1.1.1 ships them, it moves under a
   gaze in four cases:
   - The doe is shy of people. Looked at from within 20 blocks, she holds eye contact for 3 seconds
     and then bolts for cover. Anyone within 6 blocks cuts that short: she bolts at once (R12, R13).
   - From LEVEL 2, its victim's unbroken stare of 2 seconds from within 20 blocks no longer holds the
     true form. For 20 seconds it is free, and in a world whose creatures are aggressive it hunts the
     starer (R12, 1.1.1).
   - In the hunt, the gaze does not hold it within 5 blocks of its victim, where it dashes and strikes
     (1.1.1).
   - A player's blow frees it for 3 seconds. Outside the hunt, a true form that a player hit strikes
     back in reach and otherwise walks off, not held, for 20 seconds. In the hunt, the blow frees it
     for those 3 seconds only (R12, 1.1.1).

   A walker that is burning or swimming is not held either (1.1.1).

   The rule itself stays, and the retuned fight (feature 9) may change these four cases and their
   numbers (R34). The prototype may try changes to the four cases behind its switch, and the workshop
   of step 2 settles with the owner what stays (R34). The brief of feature 9 holds what the workshop
   settles, and this point is brought in line with it at the book's next change.
6. **It plays fair.** Every scare has a tell (1.1.1). It never kills in one blow (1.1.1). What it costs
   a player is small, counted and capped, and it respects land claims (R11, R12). A padlock, a door bar,
   reinforcement or a block behind the door keeps it out at every LEVEL (R12).
7. **Only silver and fire end it.** Killed with silver or by fire, that walker is gone for good (R10).
   This is a banish: a silver killing blow, death while burning, or the body burned in time (1.1.1). Any
   other death is an improper death: the walker returns, two seasons later as version 1.1.1 ships it
   (R10). Its furs mark whoever carries them, and a returning walker goes to the most-marked carrier
   (R11, R12).
8. **It holds a grudge.** Each walker has an escalation LEVEL: 1 Interloping, 2 Grudge, 3 Rage, 4 Hate,
   5 Wrath. The LEVEL rises with each improper death, and with each in-game month of active haunting
   since the first improper death. Damage never raises it, and only a banish ends it (R12). A returning
   walker is angrier, not stronger (R11). Low LEVELS blend in with the animals; the middle ones isolate
   and attack the cursed player with the harassment kit, so that players never get used to one scary
   monster (R12).
9. **It can always be dealt with.** Players get tools and traps, not an off switch: fire, silver, light,
   company, a barred door. The threat is never insurmountable (R11). Every rule is a setting for the
   server's admin (R13, 1.1.1). A player may turn off only a few comforts of their own: their name in
   whispers, the screen effects, the titles and the scare volume (1.1.1).
10. **Players learn it from Ellesby's papers.** The lore is myth meets creepypasta: scattered documents
    by several unreliable people, each partly wrong about what works (R10). The mod's page keeps the
    return secret (R13, R14) and does not say which kills work (1.1.1).

### What this road adds

The owner's direction is R9. In one line: kill it wrong and it comes back sooner; keep killing it wrong
and it changes. The owner's order of work, in the owner's words: "First we adjust and tweak what is
already there. Then and only then we move on to expanding/adding to the concept" (R27, and R24 before
it). The first two points below adjust what is already there. The third adds to the concept.

- **It comes back sooner.** An improperly killed walker returns faster. "Faster" is the owner's word
  (R9). What it is measured against the owner has left open, as the first question of the brief of
  feature 8 (R37). The owner's reason, in the owner's words: "we need to totally retune how fast a
  walker comes back normally. 54 in-game days turns out to be Waaaay too long for players to notice a
  difference and under default rules is something like 36+ hours of play which is quite alot after
  both some play-testing and player feedback" (R37). Two measures are on record. The owner's word
  "Faster" answered a card whose first option read "Faster, as I told players", with the line "Step 3
  retunes the timing so an improper kill brings it back sooner than a new walker comes, and angrier."
  (R9). And as version 1.1.1 ships, the walker returns two seasons after it died (R10), which is 54
  in-game days with the game's months of 9 days, and in half that time at LEVEL 5 (R12). Meanwhile
  another walker may come for the same player after about a week: a rest of 5 days, then a 35% chance
  each day (1.1.1). A return that falls due waits for its target's rest, as any haunt does, and is
  then served before the day's roll for a new walker. So a return reaches no player sooner than 5 days
  after that player's last haunt ended, unless the admin sends it by command (1.1.1). No thread takes
  either measure for the ruling: the brief of feature 8 puts the question to the owner, and the
  measure the prototype takes is a first guess (part 2). Of the climb up the escalation LEVELS the
  owner said that it "will likely need to be shorter to line up with shortened respawn-time" (R9).
  Whether it is shortened, and by how much, is a question of the same brief.
- **It fights back.** The walker's stats and its intelligence are retuned. The owner settles them in a
  workshop (R9).
- **It changes.** When players kill a walker improperly more than twice in a row, its return skips the
  rest of the escalation LEVELS: it comes back as a Super Predator, one of two preset creatures picked at
  random, which fill opposite niches (R9). Only improper kills count; a banish does not (R22).
  - The **Goblin**, or "Mimic": a smaller, more compact creature with no attack at all and a far larger
    harassment kit. In the owner's words it "walks in and eats a pantry full of food and then hides, or
    something that intentionally breaks clay-molds, moves particular blocks or simply fills in a
    basement with salt water" (R9).
  - The **Hunter**: the inverse. In the owner's words: "All gas, no breaks, barrels through a
    cobblestone wall like the koolaid man with heavy armor and a large axe with killer reach" (R9).

What a Super Predator keeps from the ten points: it is one real creature (point 1), silver and fire end
it (point 7), and it can always be dealt with (point 9). What it does respects land claims, is counted
and capped, and each kind of it is a setting the server's admin can switch off (point 6).

Where a Super Predator departs from the ten points, and only so far:

- The Goblin's harm lasts and is far larger than the walker's (R9).
- The Hunter is stronger than the walker, and plain walls do not keep it out (R9). Reinforced blocks
  still hold against it, and so do locked or barred doors. The owner ruled this "tentatively" (R23), so
  it may yet change, and nothing is built on it before step 3.

Which of the walker's other habits a Super Predator keeps, such as stopping under a gaze, is settled in
its brief.

### What Walkers is not

- **Not a trick of the mind.** No effect that one player alone sees stands in for the creature. That
  idea is kept for another concept (R10).
- **Not a monster that grows.** A returning walker comes back angrier, not stronger (R11). Only a Super
  Predator is stronger, and it is a different creature with an answer of its own.
- **Not a way to grief.** It respects land claims, and access that a player was granted counts as
  sharing (R11, R12). The server's admin can switch the claim checks off (R12).
- **No tools made only for it.** Silver works as it does in the game. The one addition is the silver
  spear, weaker than a copper one (R12, R14).
- **No wards.** The chalk watching mark is decoration only (R12).
- **No tidying up after it at the server's cost.** A torch it puts out stays out until a player relights
  it (R12, R14).
- **No structures in the world, for now.** No world generation for now: the lore sites stay texts and
  paintings, and the mod needs no new world (R13).
- **Nothing from real peoples.** The name "Skinwalker" stays, and the mod makes no statement about any
  real people, their beliefs or their history (R10, R13). Its fiction takes nothing from real peoples,
  places, beliefs or ceremonies, and uses no internet "skinwalker rules" (1.1.1).
- **No patching of the game's code.** No Harmony. Every code the mod registers with the game begins
  with `walkers`, and its patches to the game's files only add (1.1.1).

## Part 2. The prototype's terms

The prototype is a new, small build on top of version 1.1.1 (R5). The proto thread builds it and sends
the owner the one early zip (rule 3). It is not a feature and has no brief (rule 2).

**What the first build must show** (R24):

1. It loads on game version 1.22.7. With nothing of the prototype switched on, a world plays and saves
   exactly as it does under version 1.1.1. Everything the prototype adds is off until the server's admin
   switches it on.
2. Switched on, both of these can be reached by admin commands in a single-player world, by a player in
   Survival mode and with the world's creatures set to aggressive, in ten minutes all told and without
   waiting for in-game days to pass:
   - the faster return: an improperly killed walker that comes back sooner. What "sooner" is measured
     against the owner has left to the brief of feature 8 (R37, part 1), so the measure the prototype
     takes is a first guess (point 4);
   - a retuned fight: a walker whose stats and wits have had a first retune, met by a player who hunts
     it. The retune may try changes to the four cases of point 5 of the concept, behind the
     prototype's switch (R34).

   Both conditions matter. For a player in Creative or Spectator mode the haunt pauses, the gaze does
   not count and the lunge never strikes. Where the world's creatures are not aggressive, a walker
   does not hunt outside STAGE 5, and its lunge strikes only a player who has hurt it, or nobody where
   creatures are switched off (1.1.1).
3. It shows no Super Predator (R24).
4. Its numbers are the proto thread's first guesses, and its PR lists every one, with any measure it
   took as a first guess. It is not asked to show balance, lore, or anything that must survive a
   restart.
5. Its PR lists what to try, in the order of point 2, and what is known to be rough. The list opens
   with the two conditions of point 2: Survival mode, and a world whose creatures are aggressive.

**The checks.** As part of the prototype the proto thread brings the checks in line with SETUP: the
workflow still runs on every PR and in full on `main`, and a PR that changes only `record/` passes
without the build and the server test.

**The proto thread's own folder** is `Walkers/Proto/`, with the files that go with it (part 4). The
switch and the admin commands of the prototype live there. So do the numbers it tries for the return
and the fight, and the code that puts them in the place of the features' settings while the switch is
on (R45). Nothing outside it may depend on anything inside it, so that the folder can be removed on its
own. The proto thread removes it before step 1 is reported done. The numbers are removed with it, so
the numbers that features 8 and 9 start from are those of version 1.1.1 (R45). What the prototype
tried stays in the record: the entry that its PR carries in the proto thread's diary holds every
number that the PR lists (point 4).

**Where the rest goes.** A change to shipped code that the prototype needs for the return goes in
feature 1's folders, and one that it needs for the fight in the folders of features 2 and 3. Once the
prototype is merged, each of these belongs to that feature's owner (THE CAST), who builds feature 8 or
feature 9 on it or replaces it, as that feature's brief says. The proto thread keeps its changes to
shipped code small and lists every one in its PR. The prototype does not wait for the briefs of
features 1, 2 and 3 as they stand (part 3).

## Part 3. The steps and features

Each feature has a number that is never changed and never reused, a short name, an owning thread and
its folders. Its brief is `record/briefs/<number>-<name>.md`, the number written with two digits:
`record/briefs/08-faster-return.md`.

### The builder threads

| Role name | Its features |
| --- | --- |
| `director` | 1, 8, 10 |
| `creature` | 2, 3, 9, 12 |
| `harass` | 4, 6, 11 |
| `commands` | 5, 7 |

The proto thread (`proto`) and the janitor (`janitor`) own no features (THE CAST). What `proto` and
`commands` own beside features is in part 4.

### Already shipped: the features of version 1.1.1

These six features are complete. The PRs that completed them were merged before these rules, and version
1.1.1 (commit `dedf7e3` on `main`) holds them. They are listed under no step, so no step's gate counts
them. The owner has accepted every PR merged before these rules, #1 to #48 (R38).

A shipped feature gets its brief when its owning thread is about to change it. Before that thread's own
first change in the feature's folders, it writes the feature's brief as the feature stands (R6; THE
RECORD has each brief written by its owning thread). The brief describes the feature as version 1.1.1
ships it, at commit `dedf7e3`. No other thread writes one of these briefs, and none waits for one. The
prototype does not wait: it needs no brief (rule 2), and its PR lists every change it makes to shipped
code (part 2). What the prototype has put into a shipped feature's folders is left out of that feature's
brief as it stands, and falls to the brief of feature 8 or of feature 9.

When a later feature changes what one of these briefs says, the later feature's brief PR changes the
earlier brief too, so that every settled brief stays true as written. So the brief of a shipped
feature as it stands is settled before the brief of the later feature that changes it: the brief of
feature 1 before that of feature 8, the briefs of features 2 and 3 before that of feature 9, and the
brief of feature 5 before that of feature 7. Features 7 to 10 live in the folders of shipped features,
so those folders come to hold more than one feature. A patch in such folders must leave the brief of
each of those features true (rule 7).

In the tables, `assets/` stands for `Walkers/assets/walkers/`.

| No. | Name | What it is | Owner | Folders |
| --- | --- | --- | --- | --- |
| 1 | `haunt` | The haunt: who is haunted and when, where the walker is placed, the rest between haunts, the return, the escalation LEVEL and the fur mark | `director` | `Walkers/Director/`; `Walkers/Config/WalkersConfig.Director.cs`; `assets/lang/director/`; `tests/smoke/director.*` |
| 2 | `stalker` | The walker in the wild: its two bodies as the game loads them, and how it moves, watches, climbs the STAGES and attacks | `creature` | `Walkers/Stalker/`; `Walkers/Config/WalkersConfig.Stalker.cs`; `assets/entities/skinwalker-animal.json`; `assets/entities/skinwalker-true.json`; `assets/shapes/entity/skinwalker/`; `assets/textures/entity/skinwalker/`; `assets/lang/stalker/`; `assets/lang/creature/`; `tests/smoke/stalker.*`; `tests/smoke/creature.*` |
| 3 | `forms` | The forms and the kill ritual: the shift, how a walker dies, its body, and the items that come of it (the furs, the eyes, the silver spear) | `creature` | `Walkers/Forms/`; `Walkers/Config/WalkersConfig.Forms.cs`; `assets/itemtypes/forms-*`; `assets/recipes/grid/forms-*`; `assets/recipes/smithing/forms-*`; `assets/shapes/item/`; `assets/textures/item/`; `assets/lang/forms/`; `tests/smoke/forms.*` |
| 4 | `kit` | The harassment kit: the night acts, what each escalation LEVEL allows, and the break-in | `harass` | `Walkers/Harassment/`; `Walkers/Config/WalkersConfig.Harassment.cs`; `assets/lang/harass/`; `tests/smoke/harass.*` |
| 5 | `config` | Config and commands: the settings file, the `/walkers` command and each player's own choices | `commands` | `Walkers/Commands/`; in `Walkers/Config/`: `WalkersConfig.cs`, `WalkersConfigJson.cs`, `WalkersConfigSystem.cs`, `WalkersPlayerPrefs.cs`; `assets/lang/commands/`; `tests/smoke/commands.*`; `tests/smoke/config.expect` |
| 6 | `papers` | Ellesby's papers: the lore texts, the painted hides, the child's drawings and the chalk watching mark | `harass` | `assets/blocktypes/lore/`; `assets/config/lore/`; `assets/lang/lore/`; `assets/patches/lore-*`; `assets/recipes/grid/lore-*`; `assets/shapes/block/decor/`; `assets/textures/block/decor/`; `Walkers/assets/game/textures/block/cloth/tapestry/` |

### Step 1. It comes back sooner

Done, the owner can kill a walker improperly and see that same walker return sooner. Sooner is measured
as the brief of feature 8 settles it (R37). The owner tells the walker that returned from any other by
its number and its count of returns, which the admin's `/walkers director status` shows (1.1.1). All
of this holds on an installation whose settings file an older version of the mod wrote as on a fresh
one, and in a world saved under version 1.1.1 as in a new one.

A settings file that still holds the prototype's section `proto` does not count as one that an older
version of the mod wrote, here or in the row of feature 7. Once the prototype's folder is removed
(part 2), that section is a setting the mod does not know, and as version 1.1.1 loads a settings file,
one with a setting it does not know gets no new setting written into it (1.1.1). Such a file counts
again once the section is deleted from it by hand.

| No. | Name | What it is | Owner | Folders |
| --- | --- | --- | --- | --- |
| 7 | `settings-update` | An installation whose settings file was written by an older version of the mod gets the defaults that a new version changes. The file is the one `ModConfig/walkers.json` of the server's installation, and every world played from that installation reads it (1.1.1). Under version 1.1.1 a setting the file lacks takes its default, and every value the file holds is kept: a new setting arrives, and a changed default of a setting already in the file never does (1.1.1). The file does not mark which of its values an admin chose and which are untouched defaults (1.1.1), so which saved values follow a changed default is the first question of this feature's brief | `commands` | those of feature 5 |
| 8 | `faster-return` | An improperly killed walker returns sooner (R9). What "sooner" is measured against is the first question of its brief (R37, part 1). The thread brings to its brief what the prototype tried for the return, as the proto thread's diary holds it (part 2). Its brief also asks the owner whether the climb up the escalation LEVELS is shortened with it: the owner's words are that it "will likely need to be shorter" (R9). Its brief also says what becomes of a walker that already waits to return in a world saved under version 1.1.1: such a walker carries the return day it was given when it died (1.1.1). It needs feature 7 only if it changes the default of a setting that settings files already hold | `director` | those of feature 1 |

### Step 2. It fights back

Done, the owner can hunt a walker and find its stats and its wits as the workshop settled them.

The brief of feature 9 may be begun as soon as the owner has played the prototype. Rule 7 holds back
only this step's code, until step 1 has passed its gate.

| No. | Name | What it is | Owner | Folders |
| --- | --- | --- | --- | --- |
| 9 | `fight` | The walker's stats and intelligence, retuned (R9). Its brief is the workshop the owner asked for: the thread brings the numbers as they stand, the settings and the numbers fixed in the code alike; what the prototype tried for the fight, as the proto thread's diary holds it (part 2); what the owner said after playing the prototype; and its proposals. The owner settles them through the brief's questions (rule 2) and in play. The retuned fight may change the four cases of point 5 of the concept and their numbers, and the workshop settles with the owner what stays (R34) | `creature` | those of features 2 and 3 |

### Step 3. It changes

Done, the owner can kill a walker improperly three times running and meet what it comes back as.

This step waits whole (R27): no brief and no experiment for features 10 to 12 is begun before step 2
has passed its gate.

| No. | Name | What it is | Owner | Folders |
| --- | --- | --- | --- | --- |
| 10 | `mutation` | The kill streak and the roll: when players have killed a walker improperly more than twice in a row, its return skips the rest of the escalation LEVELS and it comes back as a Super Predator, picked at random between the two (R9, R22) | `director` | those of feature 1 |
| 11 | `goblin` | The Goblin, or "Mimic": the small Super Predator that never attacks and harasses far beyond the walker's kit (R9). Its brief settles its name | `harass` | `Walkers/Goblin/`; `Walkers/Config/WalkersConfig.Goblin.cs`; under `assets/`, every folder named `goblin` and every file named `goblin-*`; `tests/smoke/goblin.*` |
| 12 | `hunter` | The Hunter: the armoured Super Predator that comes straight through plain walls (R9, R23) | `creature` | `Walkers/Hunter/`; `Walkers/Config/WalkersConfig.Hunter.cs`; under `assets/`, every folder named `hunter` and every file named `hunter-*`; `tests/smoke/hunter.*` |

## Part 4. The shared list

Every file or folder outside `record/` that sits in no single feature's folders, with the one thread
that owns it. As in part 3, `assets/` stands for `Walkers/assets/walkers/`.

| What | Where | Owner | In which steps |
| --- | --- | --- | --- |
| The proto thread's own folder | `Walkers/Proto/`; `Walkers/Config/WalkersConfig.Proto.cs`; under `assets/`, every folder named `proto` and every file named `proto-*`; `tests/smoke/proto.*` | `proto` | The prototype. Removed before step 1 is reported done (part 2) |
| The checks | `.github/` | `proto` | The prototype (part 2), then as needed |
| The build and its tools | `build.sh`; `build.ps1`; `CakeBuild/`; `scripts/`; `tests/soak/`; `global.json`; `Directory.Build.props`; `Directory.Build.targets`; `Walkers.sln`; `Walkers/Walkers.csproj`; `Walkers/Properties/`; `.editorconfig`; `.gitattributes`; `.gitignore`; `.vscode/`; every `.gitkeep` | `proto` | Any |
| The instructions for threads | `CLAUDE.md` | `proto` | Before step 1: brought in line with these rules (R8) |
| What players and server owners read | `README.md`; `CHANGELOG.md`; `docs/` | `proto` | `README.md` before step 1, with `CLAUDE.md` (R8). All three in a release PR |
| The mod's own card | `Walkers/modinfo.json` | `proto` | A release PR (the version number) |
| The code every feature starts from | `Walkers/Core/`; `assets/lang/en.json`; `tests/smoke/core.expect` | `commands` | Any |
| The licence | `LICENSE` | No thread. The owner alone changes it | None |

A new file belongs to the feature or the row whose folder or name it falls under. A path that falls
under none is added to this list before anyone creates it.

**A release.** When the owner calls for one, the proto thread opens one release PR: the version number,
the changelog, the mod page's text and the README. It waits for the owner's merge. The owner tags and
publishes; threads never publish (rule 4). The mod's page keeps the return secret (point 10 of the
concept), and step 1 is the return. Before it opens the release PR that carries step 1, the proto thread
asks the owner, through the coordinator, whether the page and the changelog may speak of the return.
