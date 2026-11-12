# Rulings

Entries R1, R2 and so on: questions raised, the owner's rulings and commands in the owner's own words, and
anything threads agree between themselves (THE RECORD, in `record/COMMANDMENTS.txt`). Kept by the
coordinator. An entry written by a deputy says so.

Quotes are exact. In a quoted question, "you" is the owner and "I" is the thread that asked.

## R1. Question under rule 1: what is the prototype for a mod that already loads?

- Raised 2026-11-12 by planning. Its reasoning is question 1 in
  `record/diary/planning/2026-11-12T030409Z.md`.
- A reading of these rules (rules 3 and 7 speak of "the prototype, the first build of the project that
  loads", and Walkers 1.1.1 already loads), so a stop for everyone. Planning was the only thread besides
  the coordinator, and it stopped.
- Put to the owner in the planning thread, 2026-11-12 03:05:45Z, in these words:

  > 1. **Prototype.** 1.1.1 already loads. (a) The proto thread builds a new, small 1.2 prototype and sends
  > you the early zip, or (b) 1.1.1 counts as the prototype. *I recommend (a).*

- Answered by R5.
- Recorded 2026-11-12 by planning, as deputy.

## R2. Question under rule 1: do the shipped features need briefs?

- Raised 2026-11-12 by planning. Its reasoning is question 2 in
  `record/diary/planning/2026-11-12T030409Z.md`.
- A reading of these rules (the features of 1.1.1 were built before rule 2, and rule 7 passes a patch only
  if it "leaves that feature's brief true as written"), so a stop for everyone, as in R1.
- Put to the owner in the planning thread, 2026-11-12 03:05:45Z, in these words:

  > 2. **Briefs for shipped features.** (a) Each builder writes a brief of its feature as it stands before
  > touching it, or (b) every change to shipped code waits for your merge. *I recommend (a).*

- Answered by R6.
- Recorded 2026-11-12 by planning, as deputy.

## R3. Question under rule 1: do the owner's earlier rulings stand?

- Raised 2026-11-12 by planning. Its reasoning is question 3 in
  `record/diary/planning/2026-11-12T030409Z.md`.
- A reading of these rules (the Road Bible is written from the owner's vision as rulings.md holds it, and
  the owner's rulings of 2026-10 and 2026-11 were given before this file existed), so a stop for everyone,
  as in R1.
- Put to the owner in the planning thread, 2026-11-12 03:05:45Z, in these words:

  > 3. **Your earlier rulings** (settled design, "Faster", the Super Predator idea). (a) They stand and the
  > coordinator copies them into rulings.md in your words, or (b) we start fresh. *I recommend (a).*

- Answered by R7.
- Recorded 2026-11-12 by planning, as deputy.

## R4. Question under rule 1: which instructions win until CLAUDE.md is changed?

- Raised 2026-11-12 by planning. Its reasoning is question 4 in
  `record/diary/planning/2026-11-12T030409Z.md`.
- Two sets of instructions that disagree (`CLAUDE.md` lets a small fix auto-merge on green checks; rule 4
  lets a patch auto-merge only on the inquisitor's "in"). Which one binds is a reading of these rules, so a
  stop for everyone, as in R1.
- Put to the owner in the planning thread, 2026-11-12 03:05:45Z, in these words:

  > 4. **CLAUDE.md** still says small fixes may auto-merge on green CI, which v2 doesn't allow. (a) v2 wins
  > from the merge, a thread brings CLAUDE.md and README in line later, and you replace the old rule 6 in
  > the project instructions with a pointer to `record/COMMANDMENTS.txt`, or (b) CLAUDE.md is changed
  > first. *I recommend (a).*

- Answered by R8.
- Recorded 2026-11-12 by planning, as deputy.

## R5. The owner's ruling on R1: the prototype is a new build

- The owner, in the planning thread, 2026-11-12 03:07:45Z. The message, whole:

  ```text
  1.) A
  2.) A
  3.) A, to be fair this was plan-work/design work.
  4.) A
  ```

- The line that answers R1 is "1.) A". "A" is option (a) as quoted in R1.
- Closes the stop of R1.
- Recorded 2026-11-12 by planning, as deputy: the owner answered in the planning thread and did not say
  the coordinator would record it.

## R6. The owner's ruling on R2: shipped features get briefs

- The owner, in the same message as R5 (quoted whole there). The line that answers R2 is "2.) A". "A" is
  option (a) as quoted in R2.
- Closes the stop of R2.
- Recorded 2026-11-12 by planning, as deputy, as in R5.

## R7. The owner's ruling on R3: the earlier rulings stand

- The owner, in the same message as R5 (quoted whole there). The line that answers R3 is
  "3.) A, to be fair this was plan-work/design work." "A" is option (a) as quoted in R3.
- Closes the stop of R3.
- Not yet done under this ruling: the earlier rulings are not in this file. Each entry that carries one
  in will cite R7.
- Recorded 2026-11-12 by planning, as deputy, as in R5.

## R8. The owner's ruling on R4: these rules win over CLAUDE.md

- The owner, in the same message as R5 (quoted whole there). The line that answers R4 is "4.) A". "A" is
  option (a) as quoted in R4.
- Closes the stop of R4.
- Not yet done under this ruling: the project instructions still carry the old rule 6 (the owner's to
  replace), and `CLAUDE.md` and `README.md` are unchanged.
- Recorded 2026-11-12 by planning, as deputy, as in R5.
