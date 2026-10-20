# ModDB listing

The text for the mod's page on <https://mods.vintagestory.at>. The first three paragraphs are the
lore bible's listing, unchanged; the rest is for players who want to know what they are installing
and for server owners.

Rules for this page (owner's decisions, 20 Oct 2026):

- It never mentions the return. Players find out in play.
- It keeps the name "Skinwalker" and the lore bible's fiction rule: nothing from real peoples,
  places, beliefs or ceremonies, and no internet "skinwalker rules".
- It does not say which kills work. The papers in game argue about it; that is the puzzle.
- Configuration is for server owners only; players cannot opt out.

## Fields

| Field | Value |
| --- | --- |
| Name | Walkers: Skinwalker |
| Mod id | `walkers` |
| Summary (83 of 100 characters) | A doe that won't run. A thing in furs that learns your sounds. The Forest Saw That. |
| Side | Both (required on server and clients) |
| Game version tag | 1.22.7 |
| Tags | Creatures, Horror, Lore, Server-side config |
| Source | https://github.com/DunkelzahnsWill/VS-Walkers |
| License | see `LICENSE` in the source |
| File | `walkers_<version>.zip`; notes: that version's `CHANGELOG.md` section (on the first ModDB upload, every section so far) |

## Description

There is a doe at the edge of your trees. She does not run. Every time you look up she is a little
nearer, and you never see her walk.

Walkers adds the Skinwalker, a thing of the forest edge that learns one person at a time: your
footsteps, your door, your name. It plays them back to you from the dark. It wants you on your own.
And when it has learned enough, it comes for you.

The papers of a lost hamlet called Ellesby will start turning up on your doorstep, and every one of
their writers was partly wrong about what works. Read them against each other, work out which of
them was right, and finish it properly, because something's moving out there.

### What you will meet

- **A doe that is not a doe.** She wears the deer of your own country, and something about her eyes
  is wrong. She never moves while you are looking.
- **Its true form.** Taller than a man, thin as reeds, furs over dark wood, red eyes.
- **A haunting, not a boss fight.** Sounds that are your own, knocks, calls from the wrong animal,
  whispers, torches gone dark, doors that were shut, a scrape at the wall when you try to sleep.
  Most nights nothing touches you. It wants you alone; company and firelight matter.
- **It can be ended.** Not easily, and not with just anything.
- **It holds a grudge.** Let it linger and things get worse: crops, animals, doors. Bar your
  doors.
- **Ellesby's papers.** Journals, books and parchments from people who met it before you, handed out
  as your haunt goes on. Painted hides and a child's drawings to hang on your walls.

### Multiplayer and server owners

- It haunts one player at a time, chosen by the server: lone, rested, out in the forest. Groups,
  caves, offline and idle players are left alone. Nobody can opt out, but each player can turn off
  their name in whispers, the screen effects and the titles, and lower the scare volume, with
  `/walkers me`.
- Every rule and act is set server-side in `ModConfig/walkers.json` and can be changed live with
  `/walkers config`. Clients get the server's settings.
- Land claims are respected, and a padlock or a bar on a door always holds.
- Cheap to run: three walkers at once cost about 0.2 ms of server time per second in our tests.
- No Harmony patches; everything lives in its own `walkers` domain.

### Compatibility

Made and tested for Vintage Story **1.22.7** on a dedicated server. It is a code mod: the server and
every client need the same version. Other 1.22 versions may load it but are untested. Mods that
change vanilla deer, doors or torches should work alongside it, but have not been tested together
yet; reports are welcome.

Not related to Vanilla Fantasy Overhaul - Lore Mobs (vfolm), whose retextured shivers are a different
creature.

### Reporting a problem

Open an issue at https://github.com/DunkelzahnsWill/VS-Walkers/issues with your `server-main.log`
(or `client-main.log`) and the mod version.
