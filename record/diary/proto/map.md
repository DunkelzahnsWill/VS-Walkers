# proto: map

The Road Bible (`record/road-bible.md`) outranks this map. This map points to it and adds only my plan.

**My features.** None (THE CAST). I build the prototype (part 2), and own what part 4 gives me: my own
folder (`Walkers/Proto/`, `Walkers/Config/WalkersConfig.Proto.cs`, every `proto` folder and `proto-*`
file under `Walkers/assets/walkers/`, `tests/smoke/proto.*`), the checks (`.github/`), the build and its
tools, `CLAUDE.md`, `README.md`, `CHANGELOG.md`, `docs/` and `Walkers/modinfo.json`.

**What I need, and from whom.**
- The owner: the play of the prototype and its merge (rule 4). The change to part 2 that the answer (a)
  to R40 called for is merged (PR #105, R45), so the order of R43 no longer holds it. The measure of the faster return is a
  first guess (part 2, R37), and the retune may try changes to the four gaze cases behind the switch
  (part 2, R34).
- The coordinator and the record thread: the entries of rulings.md as they are recorded.
- `director` (feature 1) and `creature` (features 2 and 3): nothing before the prototype merges. After
  it, what the prototype put in their folders is theirs (part 2).

**Who needs something from me.**
- The owner: the one early zip (rule 3), and the prototype's PR with every number and every change to
  shipped code listed (part 2, points 4 and 5).
- `director` and `creature`: the prototype's numbers on record (my entry 2026-11-12T071700Z.md);
  features 8 and 9 start again from 1.1.1 (R45).
- `director` (feature 8) and `commands` (features 5 and 7): `Walkers/Proto/` gone before they build.
  It names eleven settings of features 1, 2 and 3 and calls into feature 5's loader and commands, so
  while it is on `main` their PRs could break code that only I may repair (G40).
- Every thread: the checks as SETUP wants them (part 2); `CLAUDE.md` and `README.md` in line with the
  rules before step 1 (part 4, R8); `Walkers/Proto/` removed before step 1 is reported done (part 2).

**My plan.**
1. The checks: the workflow runs on every PR and in full on `main`; a PR that changes only `record/`
   passes without the build and the server test.
2. `Walkers/Proto/`: the switch, off by default, and the admin commands (`/walkers proto ...`) that reach
   both showings of part 2, point 2 in a single-player world within ten minutes.
3. The fight and 4. the faster return: done in PR #75 as first guesses, every number in my own files
   (`Walkers/Config/WalkersConfig.Proto.cs`) and laid over the live settings by `Walkers/Proto/`, with no
   change to shipped code. The owner answered R40 with (a) (R45), so they stay there, as part 2
   says since PR #105, and go with the folder (step 7).
5. One PR with both, its numbers and its changes to shipped code listed, the "what to try" list opening
   with Survival mode and aggressive creatures; then the early zip to the owner.
6. Before step 1 (part 4): `CLAUDE.md` and `README.md` brought in line with the rules (R8). Done:
   PR #91, merged by the owner.
7. The removal: a PR that removes `Walkers/Proto/`, `Walkers/Config/WalkersConfig.Proto.cs`, the `proto`
   lang folder and `tests/smoke/proto.*`, opened as soon as the owner has merged the prototype and before
   any builder's first code of step 1. The book's "before step 1 is reported done" (part 2) is the last
   day for it, not the plan.
8. The `proto` section the prototype leaves in a settings file: once the folder is gone, the loader
   counts it as an unknown key, and a file with an unknown key gets no new settings added. Only the owner
   has loaded the prototype (the early zip goes to the owner alone), so the removal PR and the word of
   what is known to be broken that goes with step 1's zip tell the owner to delete that section from
   `ModConfig/walkers.json` by hand. The loader is feature 5's, and I change nothing in it.
