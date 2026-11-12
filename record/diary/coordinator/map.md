# Map of the coordinator

Written 2026-11-12 17:45Z. The rules (record/COMMANDMENTS.txt), the Road Bible (record/road-bible.md) and the settled briefs outrank this map. It points to them and adds only my own plan beneath. Where it differs from them, they stand and the map is wrong.

## Where I fit

- Features: none. I own no feature and no folder outside record/diary/coordinator/. My office is the cast's second place: I monitor progress, relay between the threads and the owner in the sender's own words, keep rulings.md and player-diary.md, run rule 1 stops, send findings to their owning threads and the owner, and wake the review thread and the inquisitor.
- I cannot reach the repo. The record thread is my hands there (role "record"): it writes rulings.md, player-diary.md, this map and my diary, builds the zips rule 3 has me build, and sends me each new entry of rulings.md, player-diary.md and findings.md word for word.

## What I need, and from whom

- From the owner: rulings, merges, play reports with the build named.
- From the record thread: the writing above, and byte-exact copies of the record on main for my reading.
- From the planning thread: every change to the Road Bible and to the rules, and word when such a PR is ready, cites review's report and is assigned to the owner.
- From the proto thread: the prototype (PR #75) and its zip, its map kept in line with the prototype's terms, and after the prototype is merged, its removal from the mod.
- From the review thread: its reports and findings.
- From each builder, once started: its brief, word when a PR is ready, and its questions for the owner collected in the brief.
- From the inquisitor, once started: its signed decisions.

## Who needs what from me

- Every thread: each entry of rulings.md and player-diary.md that concerns it; each finding it owns; word when the rules or the Road Bible change; word whether a record thread is at hand.
- The review thread: the wakes of rule 6 (a) to (g).
- The inquisitor: a wake when a verdict on a patch is asked for or a step is reported done.
- The owner: questions and stops, findings, what waits for the owner's hand, and each step's zip with its "what to try" lists.

## My plan (mine to rewrite)

1. Now: PR #105 (part 2 of the Road Bible after R45) waits for the owner's merge. It merges before PR #75 (rule 1, R43). If PR #75 merges first, I tell review at once.
2. After PR #105 merges: I tell the proto thread that the prototype's terms changed, so that it updates its map, and tell the other threads as news; when proto's map is on main I wake review under 6(f).
3. The owner plays the prototype (zip of PR #75 at c1920a7) and merges PR #75. What the owner says after playing goes to the record thread for player-diary.md, with the build named; I send each entry to the thread that owns what it is about.
4. After PR #75 merges: the proto thread opens the PR that removes the prototype (owner's merge), and the owner deletes the section `proto` from ModConfig/walkers.json by hand (G40).
5. Then I start the builders the Road Bible names, each by its role name and pointed at the rules, and tell each that a record thread is at hand. Each first writes the brief of its shipped feature as it stands. At their start I hand G47 to director, G48 to creature, G50 to commands and director, and point director and creature to the proto thread's entry 2026-11-12T071700Z.md, which holds the numbers the owner played. Step 1 starts no code until the prototype is merged (rule 7).
6. I start an inquisitor before the first verdict on a patch or the first step gate is asked for; until then the owner decides its gates and I have each decision written into rulings.md.
7. Each brief or change to a book: when its author tells me the PR is ready with green checks, I wake review under 6(a), relay the report, and the PR goes to the owner once its description cites the report.
8. When a step is reported done: zip of main through the record thread to the owner, the owner's word that it loads into rulings.md, then the inquisitor, then review under 6(b).
9. Open with the owner, asked and not yet answered: the two ruleset settings of SETUP ("Do not allow bypassing" on, "Require branches to be up to date" off); an own GitHub account for the threads or the step-gate list; whether the 13 old branches may be deleted.

At every start I do rule 5's reading before I act, and each time I come to rest my diary entry ends with "Next:" and what I read up to.
