# Changelog

Every release of Walkers, newest first. The version in `Walkers/modinfo.json` changes with every
release, because a joining client must have exactly the server's version of the mod.

This file is shown to players (it is pasted into the ModDB file notes), so it says what a player
meets, not how every rule works. Server owners find the details in the README.

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
- **Killing it.** Possible, but only properly. Ordinary weapons only bring it down for a while.
  The old papers disagree about what works; some of them are right.
- **Escalation.** A walker that is wronged, or left to haunt for long, grows worse over five levels,
  from Interloping to Wrath: a few ripe crops gone, torches out, knocks at night, dead animals near
  the door, and in the end a door torn off its hinges. It grows angrier, not stronger.

### The haunting

Echoes of your own sounds, knocks, wrong animal calls, whispers, doused torches, doors and chest lids
that move out of sight, a scrape at the wall when you try to sleep, livestock that panics, an
offering at dawn, a slow drain on temporal stability (or, in mundane mode, a quieter unease), and
titles: *Something's moving out there.* and *The Forest Saw That.*

### The lore

The scattered papers of a lost hamlet called Ellesby: 24 journal chapters, two books and seven
parchments, handed out as the haunt goes on, from unreliable narrators who were each
partly wrong. Painted hides and a child's drawings as wall blocks, and a chalk watching mark players
can draw themselves (decoration only).

### Items

Unnatural furs and the walker's eyes as trophies. Silver-only clay molds for a cast silver spear head,
knife blade and arrowhead. Cast silver is meant to be weaker than copper; the plated silver falx stays
the strong weapon.

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
  claim the victim owns or shares. A padlock, a bar or anything behind a door keeps the break-in out.
- Tested headless on a 1.22.7 dedicated server: three walkers haunting three victims cost about
  0.2 ms of the server's time per second, with no memory growth over restarts.
- Admin commands under `/walkers` (status, spawn, despawn, config, director, stalker, forms, harass,
  furs), with self-tests the build runs on every change.
