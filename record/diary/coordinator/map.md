# Map of the coordinator

Written 2026-11-12 17:42Z, changed 17:48Z, and again at 17:54Z and 18:28Z. The rules (record/COMMANDMENTS.txt), the Road Bible (record/road-bible.md) and the settled briefs outrank this map. It points to them and adds only my own plan beneath. Where it differs from them, they stand and the map is wrong.

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

1. Done: the owner merged PR #105 (part 2 of the Road Bible after R45) on 2026-11-12 at 18:24:36Z, merge commit fa9cdcf. The prototype, PR #75, may merge now (rule 1, R43).
2. After PR #105 merges: I tell the proto thread that the prototype's terms changed, so that it updates its map, and tell the other threads as news. The planning thread updates its own map too. When each of the two maps is on main I wake review under 6(f).
3. The owner plays the prototype (zip of PR #75 at c1920a7) and merges PR #75. What the owner says after playing goes to the record thread for player-diary.md, with the build named; I send each entry to the thread that owns what it is about.
4. After PR #75 merges: the proto thread opens the PR that removes the prototype (owner's merge), and the owner deletes the section `proto` from ModConfig/walkers.json by hand (G40).
5. Then I start builders as the Road Bible calls for them, each by its role name and pointed at the rules, and tell each that a record thread is at hand. A shipped feature gets its brief when its owning thread is about to change it (Road Bible part 3, R6): director the brief of feature 1 before feature 8, commands of feature 5 before feature 7, creature of features 2 and 3 before feature 9. I start each when the step of its feature is at hand. For creature that is earlier than its step: I start it as soon as the owner has played the prototype, for the briefs of features 2 and 3 as they stand and then the brief of feature 9, which the Road Bible lets begin then; rule 7 holds back only the code. harass changes no shipped feature on the road and I do not start it while step 3 waits whole (R27). At their start I hand G47 to director, G48 to creature, G50 to commands and director, and point director and creature to the proto thread's entry 2026-11-12T071700Z.md, which holds the numbers the owner played. Step 1 starts no code until the prototype is merged (rule 7). I start director, commands and creature only once the planning thread's small change to part 3 after PR #105 (G40, G42, what the prototype tried) is on main, as planning asked at 17:52Z.
6. I start an inquisitor before the first verdict on a patch or the first step gate is asked for; until then the owner decides its gates and I have each decision written into rulings.md.
7. Each brief or change to a book goes the way rule 4 sets for its kind. When its author tells me the PR is ready with green checks, I wake review: under 6(a) for the Road Bible or a brief, under 6(e) for the rules. I relay the report. A change to the steps and features that review certifies may auto-merge; a PR that waits for the owner goes to the owner once its description cites the report.
8. When a step is reported done: a zip of main through the record thread to the owner, with the step's "what to try" lists and anything known to be broken. The zip holds no gate (rule 3): the checks here load the project, so the test of loading is the latest run of the full checks on main. I wake the inquisitor, and after its decision review under 6(b).
9. Open with the owner, asked and not yet answered: the two ruleset settings of SETUP ("Do not allow bypassing" on, "Require branches to be up to date" off); an own GitHub account for the threads or the step-gate list; whether the 13 old branches may be deleted.

At every start I do rule 5's reading before I act, and each time I come to rest my diary entry ends with "Next:" and what I read up to.
