# Changelog

Every release of Walkers, newest first. The version in `Walkers/modinfo.json` changes with every
release, because a joining client must have exactly the server's version of the mod.

This file is shown to players (it is pasted into the ModDB file notes), so it says what a player
meets, not how every rule works. Server owners find the details in the README.

## 1.1.1 (for Vintage Story 1.22.7)

### The creature

- **She holds your gaze longer.** The doe turns to face you and holds your eyes about three seconds
  before she bolts. Only someone within a few steps, or coming at her unseen, makes her run at once.
- Hurt her badly enough, and the doe shows what she is from the first night of a haunt, not only later.

### Fixes

- A badly hurt doe that fled under low branches no longer stays a doe for good: what hides in it comes
  out once there is room.

### For server owners

- New settings: `stalk.panicDistance` (6) and `forms.hurtShiftMinStage` (1, every stage).
- `stalk.eyeContactSeconds` now defaults to 3. A server whose `walkers.json` already holds 1.5 keeps
  it; set it with `/walkers config stalk.eyeContactSeconds 3`.

## 1.1.0 (for Vintage Story 1.22.7)

### The creature

- **The doe meets your eyes.** Look at her and she holds your gaze a moment, then bolts for the trees
  like any startled deer. Come too close, or hit her, and she runs at once.
- **Harder to unmask.** A stray arrow or spear no longer brings out what hides in the doe: she runs
  like a deer until she is badly hurt.
- **The true form defends itself.** Strike it and it strikes back if you are within reach; otherwise
  it walks away.
- **Its stillness lingers.** It takes a moment of watching to hold it still, and it stays still a
  moment after you look away.
- Its reactions to being stared at now reach about 20 blocks; farther off it simply holds still.

### For server owners

- New settings: `gaze.freezeAfterSeconds`, `gaze.freezeReleaseSeconds`, `gaze.stareRange` (20),
  `gaze.stareAggroSeconds`, `stalk.eyeContactSeconds`, `stalk.guiseFleeSpeed`, `stalk.fleeSeconds`,
  `stalk.vanishApproach` and `forms.hurtShiftHealthFraction` (0.5). A server's existing
  `walkers.json` gains them with their defaults.

## 1.0.2 (for Vintage Story 1.22.7)

### Fixes

- Fixed a game crash on the client when a title, a whisper or the banishing line appeared on screen.

## 1.0.1 (for Vintage Story 1.22.7)

### Items

- Removed the mod's clay molds and cast heads. Weapons are made the vanilla way, and a new spear head
  is smithed on the vanilla anvil from one ingot; a stick makes it a spear.

### For server owners

- A world that ran 1.0.0 and holds the old molds or cast heads shows them as unknown blocks and items
  (the game's usual missing-mapping prompt). Nothing else in a 1.0.0 world changes.

## 1.0.0 (for Vintage Story 1.22.7)

The first release, and the first Walker: the Skinwalker.

### The creature

- **The guise.** A doe of whatever deer type lives where you are, with one thing wrong about the
  eyes. She does not run. She is nearer every time you look up, and she never moves while you watch.
  Blows and burns do not kill her; she flinches, and mends.
- **The true form.** Taller and broader than a man, thin and reedy, furs over a dark wooden body, red
  eyes. It shifts between the two out of sight, and once, if you are close enough, in front of you.
- **The haunt.** It learns one person at a time, over days: it keeps its distance, creeps closer,
  stares, follows, and when it has learned enough, it hunts. It wants you alone; company, firelight
  and a barred door all matter. A haunt ends when its victim dies.
- **Killing it.** Possible, but only properly. The old papers disagree about what works; some of
  them are right.
- **It gets worse.** The longer it haunts, the worse it gets, over five levels from Interloping to
  Wrath: a few ripe crops gone, torches out, knocks at night, dead animals near the door. It grows
  angrier, not stronger, and a door is not always enough.

### The haunting

Echoes of your own sounds, knocks, wrong animal calls, whispers, doused torches, doors and chest lids
that move out of sight, a scrape at the wall when you try to sleep, livestock that panics, an
offering at dawn, a slow drain on temporal stability (or, in mundane mode, a quieter unease), and
titles: *Something's moving out there.* and *The Forest Saw That.*

### The lore

The scattered papers of a lost hamlet called Ellesby: 27 journal chapters, two books and seven
parchments, handed out as the haunt goes on, from unreliable narrators who were each
partly wrong. Painted hides and a child's drawings as wall blocks, and a chalk watching mark players
can draw themselves (decoration only).

### Items

Unnatural furs and the walker's eyes as trophies. Silver-only clay molds for a cast spear head, knife
blade and arrowhead.

### For server owners

- Code mod, required on server and clients. No Harmony, every code under the `walkers` domain.
- Everything is server-side and set in `ModConfig/walkers.json` (well over a hundred settings, every
  act and rule with its own switch), changed live with `/walkers config` or `/walkers reload`. Clients get
  the server's copy; players cannot opt out of being haunted. Each player may only turn down how
  scares reach them (`/walkers me`: their name in whispers, screen effects, titles, scare volume).
- Walkers never spawn on their own; the director picks a lone, rested player who has been on the
  server a few days, in forest, in deer country, and places the walker out of sight. Haunts pause
  for offline, idle and underground players and during temporal storms.
- Claims are respected: doors, chests, torches and crops are touched only in unclaimed land or a
  claim the victim owns or shares, and a player or group the owner granted access counts as sharing.
  `harass.checkClaims` (on by default) turns every claim check off for servers that want it. A
  padlock, a door bar, reinforcement or anything behind the door always keeps the break-in out.
- Tested headless on a 1.22.7 dedicated server: three walkers haunting three victims cost about
  0.2 ms of the server's time per second, with no memory growth over restarts.
- Admin commands under `/walkers` (status, spawn, despawn, config, director, stalker, forms, harass,
  furs), with self-tests the build runs on every change.
