# Walkers: notes for Claude sessions

**The standing rules are The Commandments, `record/COMMANDMENTS.txt`. Read them in full before anything
else.** Where this file or `README.md` seems to say otherwise, the Commandments win (R8 in
`record/rulings.md`), and the doubt goes to the coordinator under rule 1. This file only points the way.

- **Your role and what you own** come from the Road Bible, `record/road-bible.md`: part 3 gives each
  feature its owning thread and folders, part 4 every shared file. Outside `record/`, change only what you
  own there or have been handed under the rules (THE CAST).
- **The record** lives in `record/`. Before you start work, read what rule 5 lists: the rules, the Road
  Bible, your briefs, your map and diary from your last "Next:" entry, and the new entries of
  `rulings.md`, `player-diary.md` and `findings.md`. Keep your own folder, `record/diary/<role>/`
  (rule 5).
- **Every change reaches `main` through a pull request; never push to `main`.** How each kind of PR may
  merge is rule 4, and that list is the authority. Threads never merge by hand and never publish. In
  short: the working files of the record (diary entries, `rulings.md`, `player-diary.md`,
  `findings.md`) auto-merge on green checks; a patch auto-merges only on the inquisitor's "in"; a change
  to the steps and features of the Road Bible, or a commanded change to it or to a brief, may auto-merge
  once the review thread has certified it; everything else waits for the owner's merge. For anything
  outside the record's working files, green checks alone never make a PR mergeable.
- **No code for a feature before its brief is settled** (rule 2), experiments on `spike/<name>` aside.
  One unmerged feature per thread at a time (rule 3).
- **Before opening or updating a pull request**, run `scripts/setup.sh`, `./build.sh` and
  `scripts/smoke-test.sh`, and say in the PR what passed. CI runs the same three scripts. Rule 4 says
  what else a PR's description must hold.
- **Never commit** game DLLs, the server, or anything under `.cache/` or `Releases/`.

Code conventions (the concept, part 1 of the Road Bible, and `README.md`, "Working in parallel"):

- Prefix every registered code with `walkers` (`WalkersModSystem.ModId`). No Harmony. Patches to the
  game's files only add.
- A feature's folders are the ones part 3 of the Road Bible lists for it, and nothing else. Some
  features live in the folders of another (features 7 to 10 in those of the shipped features, for
  instance), so make no new folder or file name for a feature unless part 3 or part 4 has it. The
  pattern the shipped features follow is code in `Walkers/<Feature>/` with its own ModSystem, settings in
  `Walkers/Config/WalkersConfig.<Feature>.cs`, text in `assets/walkers/lang/<feature>/en.json`, patches in
  `assets/walkers/patches/<feature>-*.json` and checks in `tests/smoke/<feature>.*`.
- Chat commands hang off the one shared root: `api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)`
  plus your own `.BeginSubCommand("<feature>")`; never `Create` the root.
- Target game 1.22.7. Use legacy AI task codes in JSON; the `-r` refactored tasks have known bugs.
