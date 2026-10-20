# Walkers: notes for Claude sessions

Read README.md first; it is the source of truth for building, testing and the parallel-work layout.

- Before opening or updating a pull request, run `scripts/setup.sh`, `./build.sh` and
  `scripts/smoke-test.sh`, and say in the PR what passed. CI runs the same three scripts.
- Stay inside your feature's files (README, "Working in parallel"): your own `Walkers/<Feature>/`
  folder and ModSystem, your own `WalkersConfig.<Feature>.cs`, `lang/<feature>/en.json`,
  `patches/<feature>-*.json` and `tests/smoke/<feature>.*`. Changes to shared files go in their own,
  clearly described commit.
- Never commit game DLLs, the server, or anything under `.cache/` or `Releases/`.
- Prefix every registered code with `walkers` (`WalkersModSystem.ModId`). No Harmony.
- Target game 1.22.7. Use legacy AI task codes in JSON; the `-r` refactored tasks have known bugs.
