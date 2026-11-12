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

## R9. Carried in under R7: the owner's direction for 1.2 ("Faster", a workshop, the Super Predator idea)

- The owner, 2026-11-10 22:32:19Z, in this project's old review thread ("Review roadmap with
  Glow-In-The-Dark"). The words are the owner's own, read from that thread on 2026-11-12. The message,
  whole:

  > Faster, however additional tweaks to monster stats and intelligence will need a work-shop. I also have an idea: if players continue to kill monster (more than twice) in-a-row we "skip" escalation (which will likely need to be shorter to line up with shortened respawn-time) to a "Mutation" or "Evolution" into a randomly rolled pre-set "Super Predator" each monster (current idea is 2) that fill radically different niches.
  >
  > a "Pacifistic" monster that hyper-focuses on increased harassment tool-kit but with no offensive ability in a smaller/more compact form. Labeled as a "Goblin" or "Mimic" that walks in and eats a pantry full of food and then hides, or something that intentionally breaks clay-molds, moves particular blocks or simply fills in a basement with salt water.
  >
  > and a "Hunter" monster, the "inverse" of the previous niche. All gas, no breaks, barrels through a cobblestone wall like the koolaid man with heavy armor and a large axe with killer reach.

- It answered a decision card that the review thread had put to the owner at 22:16:40Z: "Decide how an
  improperly killed beast should come back". The options were "Faster, as I told players" ("Step 3 retunes
  the timing so an improper kill brings it back sooner than a new walker comes, and angrier."), "Keep the
  shipped 54 days" ("The code stays as it is, and a thread drafts a corrected answer for your 26 Oct reply
  on the ModDB page.") and "Rule after I have fought it" ("This stays open until you have played the
  ten-minute fight test. The audit starts anyway, and only step 3 waits."). The review thread closed the
  card on the first option.
- Nothing has been ruled on this since. The workshop the owner asks for ran from 2026-11-10 to 2026-11-12
  and closed with its first four questions unanswered.
- Recorded 2026-11-12 by planning, as deputy.

## R10. Carried in under R7: the pre-production decisions of 2026-10-20

- Given by the owner on 2026-10-20 in the old project ("Vintage Story modding project"), whose threads
  this project cannot open.
- The words below are NOT the owner's. They are the old record's account, headed there "Owner decisions,
  2026-10-20 (final, do not relitigate)", quoted exactly from the project memory note `mimic-mob-design`.
  In item 1, "[...]" stands for words about the owner personally, left out of this public file.

  > 1. Name stays "Skinwalker". [...] chose it deliberately; the mod makes no statement about [...] beliefs or history. Do not re-raise the sensitivity point.
  >
  > 2. Name direction "Walker" (room for future variants). Suggested modid "walkers", entity codes walkers:skinwalker-*. ModDB check 2026-10-20 ~03:25Z: modids walkers, walker, skinwalker, skinwalkers are free. Name overlaps to know: Root Walker (iTom, hostile plant mob, 1.22.x), VFOLM reskins the vanilla Shiver into "skinwalkers", Backrooms: Survival Story's Skin-Stealer mimics voices and opens doors.
  >
  > 3. Two bodies it shifts between: a normal animal with an eye tell (model eye tweak), and a true form slightly taller and broader than a man, thin and reedy, wrapped in furs over a dark woody body, glowing or red eyes.
  >
  > 4. Lore: myth meets creepypasta. Taglines "The Forest Saw That." and "Something's moving out there." Lore bible = scattered documents by several unreliable people: rituals, simple paintings of something coming out of the forest, the eyes. The temporal-echo hook is dropped; temporal stability may still serve as the sanity mechanic.
  >
  > 5. One real entity everyone sees (per-player hallucination idea saved for another concept).
  >
  > 6. Killable with a ritual: silver or fire for a permanent kill; otherwise it returns a couple of seasons later.
  >
  > 7. One universal code mod.
  >
  > 8. Placeholder vanilla shape first; model spec late in the cycle, then iterate.
  >
  > 9. Builds and headless server tests in the cloud (1.22.7 server download); owner's PC for client play-tests.

- Recorded 2026-11-12 by planning, as deputy.

## R11. Carried in under R7: the nine answers of 2026-10-20 06:39Z on the creature

- Given by the owner on 2026-10-20 06:39Z in the old project's creature bible thread, in answer to the nine
  questions of the creature bible's section 11.
- The words below are NOT the owner's, except the three passages inside quotation marks in answers 6, 8
  and 9. They are the old record's account, quoted exactly from the project memory note
  `skinwalker-owner-answers`.

  > 1. Guise = a doe of the local deer type (deer card closed on that). Warning: southern biomes where only gazelles live would give a deer guise away; never wear a deer out of its habitat.
  >
  > 2. Guise shrugs off damage (cannot die at a player's hand) but must still play a hurt animation, so an inattentive player may not notice.
  >
  > 3. Skinning gives "Unnatural Furs" that mark their holder as a target (an object token). Needs a config toggle for frustration and testing.
  >
  > 4. Haunt ends when the walker kills its victim: keep (walker rests, player rests twice as long).
  >
  > 5. Wants an "Escalation Haunt List": after an improper kill it returns with escalating world consequences instead of coming back stronger: herds found rotting, crops "eaten", a door ripped off its hinges, each limited. Owner said the idea needs developing.
  >
  > 6. Claim rule default (victimAccess) approved: "good anti-griefing ideas".
  >
  > 7. Wants castable silver weapons.
  >
  > 8. No opt-out at all: counterplay (tools and traps to deal with it, a threat that is never insurmountable) instead. "Otherwise why add the mod?"
  >
  > 9. YES to it coming into the house: "a little bit of breaking and entering when enraged".

- Recorded 2026-11-12 by planning, as deputy.

## R12. Carried in under R7: the later rulings, 2026-10-20 08:04Z to 2026-10-21 01:06Z

- Given by the owner in the old project's threads at the times shown. Times are UTC on 2026-10-20; those
  from 00:02Z on are 2026-10-21.
- The words below are NOT the owner's. They are the old record's account, quoted exactly from the project
  memory notes `skinwalker-owner-answers` (the first line) and `skinwalker-owner-rulings-later` (the rest).
  That record says of itself: "Later entries override earlier ones and older notes." "Bible" is the
  creature bible, a design document outside this repo, and "PR #" is a pull request of this repo.

  > Later owner rulings (2026-10-20): 08:04Z chalk watching mark is decoration only, no real ward. 08:11Z doused torches stay out; players relight by hand; no automatic dawn relight (server cost).
  >
  > 08:28Z owner confirmed: damage never enrages it (it just fights back as normal); the escalation ladder is driven by time and improper deaths.
  >
  > 08:32Z owner: escalation level persists per walker in the director save (despawn/restart never reset it; only a banish ends it). Level = improper deaths + 1 per in-game month (escalation.monthsPerLevel, default 1) since the first improper death, capped at top tier; an ignored curse slowly humbles the victim. In bible rev 111; Director and Harassment kit told (suggested attribute walkers/escalation).
  >
  > 08:35Z owner: no "time-bomb" despawned walkers. Bible rev 112: the monthly clock only counts in-game time during a live haunt (Director stores active haunt hours); dormant/resting/despawned walkers never grow. Owner chose 08:36Z: no global cap.
  >
  > 08:40Z owner (Forms thread): walker fur carries NO walker id (players can't tell walkers apart); the Director marks carriers of any walker fur; banish no longer cools furs; any returning walker goes to the most-marked carrier. Bible rev 120.
  >
  > 09:22Z owner (kit thread): five-level escalation ladder replaces the two tiers: 1 Interloping, 2 Grudge (knocks, stalking, stare aggro), 3 Rage (enraged, isolation aggro, ~25% nightly break-in, 2 wooden + 1 other door), 4 Hate (doors doubled, ~90% break-in, torches eaten), 5 Wrath (ignores groups, return time halved). Level = 1 + improper deaths + active-haunt months, cap 5. Numbers are the kit's reading, unconfirmed. Bible rev 128. 09:24Z owner: locks/bars/reinforcement/blocked cells still hold at every level; L3+ may tear bare single iron or heavy 1x2 doors; gates and double doors never.
  >
  > 20:09Z owner: escalation clock does NOT pause for idle/AFK or sleeping victims (idle may be crafting; sleep left alone). Bible updated.
  >
  > 20:26Z owner: /land allow granted access counts as sharing for all walker claim rules; harass.checkClaims (default true) switches all claim checks off (PR #33). Bible updated.
  >
  > 22:02Z owner (Forms): NO mob-only special tools. Silver from vanilla (axe mold/anvil, smithed knife & arrowheads, plated falx); only addition walkers:spearhead-silver (anvil, 1 ingot) + stick = walkers:spear-silver. Silver molds removed (PR #35, 1.0.1). Bible rev 166.
  >
  > 23:46-23:48Z owner: doe is human-shy (eye contact 1.5 s within 48 blocks, then bolts; bolts at once within 16); hit-forced true form defends then disengages (ladder overrides); gaze grace periods (0.5 s on, 1.5 s off, 2 s stare for aggro). Stalker PR #38; bible rev 168.
  >
  > 00:02Z owner: gaze.stareRange 48 -> 20 (PR #39). Ladder intent: low levels blend in with fauna; middle levels isolate and attack the cursed player with the kit, so players never get used to one scary monster. Bible rev 171.
  >
  > 01:04-01:06Z owner: doe stands up only at <=50% health from player blows (forms.hurtShiftHealthFraction 0.5), fire at once, mends 10 s after last hit; lighter hits make it bolt; stands up at every stage (forms.hurtShiftMinStage 1); no shift of any kind in water (admin command excepted), no-room/water retries each second; eye contact 3 s facing the looker, stalk.panicDistance 6 cuts it short. PRs #41 #42 #44 #45. Bible rev 175.

- Recorded 2026-11-12 by planning, as deputy.

## R13. Carried in under R7: the old project's list "Decisions (owner); do not re-raise", as of 2026-10-21

- The old project's own summary of what the owner had settled when version 1.1.1 was released. It repeats
  R10 to R12 in short form and adds detail on claims, the fur mark, silver, the ModDB page and worldgen.
- The words below are NOT the owner's. They are quoted exactly from the project memory note
  `walkers-decisions`, which copied that list unchanged. In the third item, "[...]" stands for words about
  the owner personally, left out of this public file. "[[name]]" is a link to another note of that memory.

  > - Stage gate (Oct 21 01:04Z): a badly hurt doe stands up at any haunt stage; lowest-stage setting, default 1. Water (01:06Z): no shift happens in water.
  >
  > - Ladder intent (Oct 21 00:02Z): low escalation levels blend in with fauna; middling levels isolate and attack cursed individuals with the harassment kit, so players never get used to a scary monster. Stare range 20. The doe flees under a close stare, eye contact first (replaces the old "never flees" tell). Shift on a hit only at half health or lower; lighter hits make it bolt.
  >
  > - Pre-production (Oct 20): name "Skinwalker" ([...] the mod makes no statements about the people); "Walker" stem, modid walkers; deer guise with an eye tell plus a true form (taller than a man, thin, furs over a dark woody body, red eyes, plain bark face, three twig fingers; model v2 approved); myth meets creepypasta, lore as scattered unreliable documents; one real entity; killable only by ritual (silver or fire), else it returns a couple of seasons later; universal C# mod; cloud server for builds and tests, owner's PC for client play-tests.
  >
  > - Section-11 answers ([[skinwalker-owner-answers]]): doe guise only where its deer type lives; the doe shrugs off damage; skinning gives Unnatural Furs that mark the carrier; a haunt ends on the victim's death; improper kills escalate limited world consequences; claim rules; silver weapons; no player opt-out (config is server-admin only); break-in only when enraged, and a padlock, bar or a block behind the door keeps it out.
  >
  > - Escalation: damage does not enrage; driven by improper deaths and time; survives despawns and restarts; one step per in-game month only while actively haunting; no server cap; the clock runs for idle players. Levels 1 Interloping, 2 Grudge, 3 Rage, 4 Hate, 5 Wrath. Locks hold even where metal doors can be torn. Wrath placement rules merged (#29).
  >
  > - Claims: granted access (/land allow, direct or via a group) counts as sharing; toggle harass.checkClaims, default on (#33); the play-test with a friend covers it.
  >
  > - Fur mark does not stack: one value per player by hours carried (6 h to mark, 72 h cap, fades 1 h per hour, burning any furs clears it); furs carry no walker identity.
  >
  > - Silver: NO special tools useful only for this mob (#35 removed the silver-only molds); silver works as in vanilla; the only addition is walkers:spearhead-silver smithed on the vanilla anvil from one ingot, plus a stick = walkers:spear-silver (weaker than copper); the plated vanilla silver falx stays the strong weapon; silver does 1.5x; the silver felling axe counts. LICENSE MIT.
  >
  > - The ModDB description keeps the return secret. The chalk watching mark is decoration only. Doused torches stay out, no dawn relight. NO worldgen structures for now; the lore sites stay texts and paintings. The mod needs no new world (no worldgen; told the owner 03:28Z).

- Recorded 2026-11-12 by planning, as deputy.

## R14. Carried in under R7: rulings that only the old feature notes record

- Given by the owner in the old project's threads at the times shown (UTC).
- The words below are NOT the owner's, except the passages inside quotation marks. Each line is quoted
  exactly from the project memory note named after it. "[...]" marks words left out between two passages.

  1. 2026-10-20 07:50Z and 08:04Z, the chalk watching mark (`walkers-lore-bible`):

     > Owner 07:50Z: "let the players build it" -> PR #13 grid recipes (chalk stone or limonite nugget -> 4, 3 singles -> triple). Owner decided 08:04Z: the mark has NO ward ability; it is player-buildable decoration only (lore twist stays: a child's drawing, not a ward).

  2. 2026-10-20 07:55Z, silver weapons (the molds it speaks of were removed by the 22:02Z ruling in R12) (`walkers-forms-ritual`):

     > Owner's balance rule (2026-10-20 07:55Z): no cast silver falx (would undercut vanilla's plated silver falx, the practical long-use item); cast silver weapons must be weaker than their copper equivalents (spear: melee 2.25, thrown 4, durability 70).

  3. 2026-10-20 07:59Z, the true form's face (`walkers-art-sound`):

     > Owner chose the face 07:59Z: plain bark, eyes only.

  4. 2026-10-20 08:22Z and 08:43Z, the true-form model (`walkers-true-form-model`):

     > Model v2 08:40Z after owner notes (08:22Z): eyes in separate dark sockets with a bark bridge (not a visor); run low with right hand planted (1.65 bl); lunge is a horizontal rake; death curls on its left side, arms hugging chest (distinct from shift); walk 2.0 bl. [...] Owner approved 08:43Z; PR #20 (shape + 5 textures) MERGED 08:50Z.

  5. 2026-10-20 08:11Z, doused torches (`walkers-harassment`):

     > Doused torches are NOT restored: owner's rule 2026-10-20 08:11Z, players relight them themselves, to keep it cheap for servers. Prefer no tracking of cosmetic world changes.

  6. 2026-10-20, the ModDB page (`walkers-lore-bible`):

     > Owner decided: ModDB keeps the return secret.

  7. 2026-10-20 21:47Z, the licence (`walkers-release`):

     > LICENSE (MIT, owner 21:47Z 20 Oct)

  8. 2026-10-21 03:08Z and 03:09Z, the README (`walkers-release`):

     > 03:02Z: PR #47 (README-only: dedicated-server install, ModPaths, restart, log check lines, ModDB download only once published) CLOSED unmerged by the owner 03:08Z; do not reopen unless asked. Owner 03:09Z: going step by step with the server owner, no README update for now, 'go to sleep'; thread resolved.

  9. The goal of the owner's mods, as the old project's memory gave it (`owner-and-environment`):

     > Goal: maintain and publish Vintage Story 1.22.7 mods that are multiplayer-safe and cheap in compute and size.

- Recorded 2026-11-12 by planning, as deputy.

## R15. The carry-over under R7: what was left out, and why

- Carried in: R9 to R14. Where two of them disagree, the later ruling stands, as the old record says of
  itself (quoted in R12).
- Left out because version 2 of these rules replaces them: the five Commandments of 2026-10-21; the
  Glow-In-The-Dark rule of 2026-11-10 and the owner's four answers on its wording (2026-11-10 21:48Z); and
  the decision of 2026-10-21 that a small fix may auto-merge on green checks (see R8).
- Left out because nothing was ruled: the old workshop's four questions of 2026-11-10 on the Super Predator
  idea, which the owner has not answered; proposals 1 to 14 of the post-mortem, to which the owner has
  said neither yes nor no; and whether the doe mends too fast (it mends fully 10 seconds after the last
  hit), which the old record lists as open.
- Left out because they are not rulings given to a thread: the owner's replies to players on the mod's
  ModDB page, which so far were read only through a summarising tool; what the owner said after playing
  versions 1.0.1 to 1.1.1 in 2026-10, on builds since changed in answer to it; and the owner's working
  habits that project memory records.
- Left out because they are design documents and not rulings: the creature bible, the lore bible, the art
  and sound document, and the terms that the creature and lore threads agreed on 2026-10-20 (names, tells,
  the lore's guardrails). They stay the sources for the Road Bible's concept and for the briefs of the
  shipped features (R6).
- R10 to R14 give the old record's account, not the owner's words, because the old project's threads
  cannot be opened from this project. If the owner corrects one of them, or wants something from this list
  brought in, the correction is a new entry here that cites the entry it corrects.
- Recorded 2026-11-12 by planning, as deputy.

## R16. Question: who keeps the coordinator's files, when the coordinator can neither read nor write the repo?

- Raised 2026-11-12 by planning. Its reasoning is under "A gap to put to the owner" in
  `record/diary/planning/2026-11-12T031846Z.md`.
- The fact behind it, in the coordinator's own words to planning, 2026-11-12 03:22:15Z:

  > I have no checkout and no GitHub tools, so I can neither read nor write the repo. What I can do: read
  > the shared folder /mnt/project-files (not write it), read every thread and the project chat, message
  > and start threads, post to the owner, and keep project memory. So anything in record/ reaches me only
  > when a thread sends it to me, and anything I must write there needs another thread's hands.

- Not a stop under rule 1: THE CAST already let planning record, as deputy, what the owner said to it,
  and no thread's work waited on the answer.
- Put to the owner in the planning thread on a decision card, 2026-11-12 03:22:54Z. Planning's message of
  03:22:51Z, directly above the card, gave the owner the fact in these words:

  > **One fact you need for the card below.** Your coordinator can neither read nor write the repo. It
  > can read the threads, the project chat and the shared files folder, start and message threads, and
  > post to you. So I recorded your answers as its deputy, which the cast section provides for. The same
  > way, I'll now copy your earlier design rulings into rulings.md under ruling 3, quoted with their
  > sources, in a PR of its own. But the rules also give the coordinator the rulings you make in the
  > project chat, the player diary and the step zips, and no deputy covers those.

- The card, field by field:
  - The question: "Choose who keeps the coordinator's files: it can neither read nor write the repo"
  - First option, "A record thread for the coordinator": "A new standing thread is the coordinator's
    hands in the repo: it writes rulings.md, player-diary.md and the coordinator's diary in your exact
    words and builds the step zips."
  - Second option, "Each thread you speak to": "The rules as they stand: each thread records what you
    tell it, as I did today. Rulings you give in the project chat, and the step zips, still have no
    writer."
  - Third option, "The planning thread": "I keep the coordinator's files for every thread and build the
    zips. My Road Bible changes at your command then always wait for your merge (rule 4)."
  - The card marked the first option as recommended, with this reason: "One writer keeps the entries in
    order, and no thread acts on a ruling it recorded itself, so rule 4's quicker path (certified, then
    auto-merge) stays open."
- Answered by R17.
- Recorded 2026-11-12 by planning, as deputy.

## R17. The owner's ruling on R16: a record thread for the coordinator

- The owner, in the planning thread, 2026-11-12 03:50:02Z, by choosing the first option on the card of
  R16. The owner typed no words. The app's record of the choice, whole:

  ```text
  Chose "A record thread for the coordinator" on the decision card asking "Choose who keeps the coordinator's files: it can neither read nor write the repo"
  ```

- What that option said, as the owner read it (quoted in R16): "A new standing thread is the
  coordinator's hands in the repo: it writes rulings.md, player-diary.md and the coordinator's diary in
  your exact words and builds the step zips."
- Planning and the coordinator both read this ruling as one that changes what these rules say: THE CAST
  and THE RECORD have the coordinator write rulings.md and player-diary.md, rule 5 has each diary written
  by its own thread, and rule 3 has the coordinator build the zips. Under rule 1 such a ruling binds from
  its entry here, and the planning thread writes the change to the rules at once (rule 4). Planning's
  reasons are in `record/diary/planning/2026-11-12T040012Z.md`.
- Not yet done under this ruling: no record thread exists yet, and the change is not yet in
  `record/COMMANDMENTS.txt`.
- Recorded 2026-11-12 by planning, as deputy: the owner answered in the planning thread, and no record
  thread exists yet.

## R18. Question under rule 1: which kills count toward "more than twice in a row"?

- Raised 2026-11-12 by planning, while it drafted the first Road Bible. Its reasoning is in
  `record/diary/planning/2026-11-12T040029Z.md`.
- A question about the concept: R9 has the walker change "if players continue to kill monster (more than
  twice) in-a-row", and does not say whether a banish counts as a kill. So a stop for everyone (rule 1).
  Planning was the only thread besides the coordinator, and it stopped.
- Put to the owner in the planning thread, 2026-11-12 03:52:58Z, in these words:

  > 1. **Kill streak.** "More than twice in a row" counts (a) only improper kills, or (b) banishing with
  > silver or fire too. *I recommend (a).*

- Answered by R22.
- Recorded 2026-11-12 by planning, as deputy.

## R19. Question under rule 1: what still holds against the Hunter?

- Raised 2026-11-12 by planning, with R18. Its reasoning is in the same diary entry.
- A question about the concept: R9 has the Hunter go "through a cobblestone wall", and R12 has locks,
  bars, reinforcement and blocked cells hold "at every level". Whether they hold against the Hunter is
  not said. So a stop for everyone, as in R18.
- Put to the owner in the planning thread, 2026-11-12 03:52:58Z, in these words:

  > 2. **The Hunter and walls.** (a) It breaks plain walls, but reinforced blocks still hold, and so do
  > locked or barred doors, as the ModDB page promises, or (b) nothing holds against it. *I recommend
  > (a).*

- Answered by R23.
- Recorded 2026-11-12 by planning, as deputy.

## R20. Question under rule 1: what must the prototype show?

- Raised 2026-11-12 by planning, with R18. Its reasoning is in the same diary entry.
- Two readings of an ask: R5 has the proto thread build "a new, small 1.2 prototype", and R9 gives
  version 1.2 three parts (the faster return, the retuned stats and intelligence, the Super Predators).
  How much of that the first build must show is not said. A stop with R18 and R19.
- Put to the owner in the planning thread, 2026-11-12 03:52:58Z, in these words:

  > 3. **The prototype shows** (a) the whole loop in the rough: the faster return, plus a stand-in Hunter
  > and Goblin you summon by command, or (b) only the faster return and a retuned fight. *I recommend
  > (a).*

- Answered by R24.
- Recorded 2026-11-12 by planning, as deputy.

## R21. Question: may the planning thread use more than one branch?

- Raised 2026-11-12 by planning. Planning was started with leave to push to one branch only
  (`claude/project-thread-205iei`), and one branch holds one open PR. Rules 4 and 5 send its PRs
  different ways at the same time: working files and "Next:" entries by auto-merge, a change to the rules
  and the first Road Bible by the owner's merge after the review thread has read them.
- Not a stop: it holds no thread's work, only the order of planning's PRs.
- Put to the owner in the planning thread, 2026-11-12 03:52:58Z, in these words:

  > 4. **Branches.** I was given one branch, and one branch holds one open PR. (a) I may open more
  > branches, each named `claude/planning-...`, so the rulings, the rules change and the Road Bible can
  > move side by side, or (b) one PR at a time. *I recommend (a).*

- Answered by R25.
- Recorded 2026-11-12 by planning, as deputy.

## R22. The owner's ruling on R18: only improper kills count

- The owner, in the planning thread, 2026-11-12 03:55:55Z. The message, whole:

  ```text
  1.) A
  2.) A, tentatively.
  3.) B, for now. Let us focus on the immediate before we focus on the distant
  4.) A
  ```

- The line that answers R18 is "1.) A". "A" is option (a) as quoted in R18.
- Closes the stop of R18.
- Recorded 2026-11-12 by planning, as deputy: the owner answered in the planning thread, and no record
  thread exists yet (R17).

## R23. The owner's ruling on R19: reinforced blocks and locked or barred doors still hold against the Hunter, tentatively

- The owner, in the same message as R22 (quoted whole there). The line that answers R19 is "2.) A,
  tentatively." "A" is option (a) as quoted in R19.
- Closes the stop of R19. The owner's word "tentatively" stands with the ruling.
- Recorded 2026-11-12 by planning, as deputy, as in R22.

## R24. The owner's ruling on R20: the prototype shows only the faster return and a retuned fight, for now

- The owner, in the same message as R22 (quoted whole there). The line that answers R20 is "3.) B, for
  now. Let us focus on the immediate before we focus on the distant". "B" is option (b) as quoted in R20.
- Closes the stop of R20.
- Recorded 2026-11-12 by planning, as deputy, as in R22.

## R25. The owner's ruling on R21: the planning thread may open more branches

- The owner, in the same message as R22 (quoted whole there). The line that answers R21 is "4.) A". "A"
  is option (a) as quoted in R21.
- Recorded 2026-11-12 by planning, as deputy, as in R22.

## R26. Question: do the Super Predators stay on the road as its last step?

- Raised 2026-11-12 by planning, when it reshaped the first Road Bible after R24. Its first reasoning is
  in `record/diary/planning/2026-11-12T040029Z.md`.
- Two readings of the second sentence of R24, "Let us focus on the immediate before we focus on the
  distant": the Super Predators stay on the road as its last step and that step waits, or they come off
  the road for now.
- Planning did not call a stop. It drafted the first Road Bible on the first reading and asked the owner
  to correct it if it was wrong. What planning thinks of that now is in
  `record/diary/planning/2026-11-12T041701Z.md`.
- Put to the owner in the planning thread, 2026-11-12 04:11:02Z, at the end of a longer message, in these
  words:

  > One reading for you to correct if it is wrong. I took "the immediate before the distant" to mean the
  > Super Predators stay on the road as its third and last step, and nothing of that step starts, not
  > even a brief, until the fight step is done. If you would rather they came off the road for now, say
  > so and I will take them out.

- Answered by R27.
- Recorded 2026-11-12 by planning, as deputy.

## R27. The owner's ruling on R26: the reading is correct; first adjust what is already there, then add to the concept

- The owner, in the planning thread, 2026-11-12 04:14:21Z. The message, whole:

  ```text
  Yes, you are correct. First we adjust and tweak what is already there. Then and only then we move on to expanding/adding to the concept
  ```

- "Yes, you are correct." answers the reading quoted in R26: the Super Predators stay on the road as its
  last step, and nothing of that step starts, a brief included, until the fight step is done.
- The two sentences after it give the owner's order of work, in the owner's own words.
- Recorded 2026-11-12 by planning, as deputy: the owner answered in the planning thread, and no record
  thread exists yet (R17).

## R28. Question: findings G2 and G3, put to the owner by the coordinator

- Raised 2026-11-12 by the review thread, as findings G2 and G3 in `record/findings.md`. Both are notes.
  G2: the project instructions that every thread is started with still carried the old rule 6, which R8
  left to the owner to replace. G3: planning carried the owner's earlier rulings in (R9 to R15) on its
  own reading of how long a deputy's office lasts.
- Not a stop: what the review thread finds is a finding, never a rule 1 stop (rule 6), and a note holds
  nothing.
- Put to the owner by the coordinator in the project chat, 2026-11-12 04:20:06Z. The message quoted G2
  and G3 as the review thread had sent them to the coordinator, and ended in these words:

  > On G2: say the word and I'll replace the old rule 6 in the project instructions with the pointer to
  > `record/COMMANDMENTS.txt`, or you can edit it in [Project settings](#project-settings/instructions).
  > Until then I tell each new thread that version 2 wins. On G3: a one-line "the reading stands" from
  > you would settle it.

- Answered by R29.
- Recorded 2026-11-12 by planning, as deputy, at the coordinator's request.

## R29. The owner's ruling on R28: replace the old rule 6; the reading in G3 stands

- The owner, in the project chat, 2026-11-12 04:21:38Z. The message, whole:

  ```text
  Replace the old rule 6. G3 reading stands
  ```

- "Replace the old rule 6." answers G2, and is the act that R8 left to the owner. The coordinator did it
  at once. Its own words to planning, 2026-11-12 04:21:48Z:

  > On the first, I have replaced the project instructions with the line you proposed: "The standing
  > rules for every thread are The Commandments, at record/COMMANDMENTS.txt in the repo
  > DunkelzahnsWill/VS-Walkers. Read them in full before anything else." It applies to threads started
  > from now on.

- "G3 reading stands" answers G3. The reading, in the words of G3: "Planning reads the copying as still
  inside its deputy's office, at the coordinator's request". So the carrying in of R9 to R15 by planning
  stands as a deputy's act.
- Recorded 2026-11-12 by planning, as deputy, at the coordinator's request (its message to planning of
  04:21:48Z): the owner ruled in the project chat, the coordinator cannot write to the repo (R16), and no
  record thread exists yet (R17). At 04:21:54Z the coordinator told the owner in the project chat that
  planning was recording it.

## R30. Agreed between planning and the record thread: one writer of the coordinator's files from here on

- An outcome two threads agree between themselves (THE RECORD; rule 1, "Either way the outcome is in
  rulings.md before either acts on it"). Not a ruling of the owner, and not a stop.
- Why it was needed: the coordinator started the record thread at 2026-11-12 04:25:32Z under R17 and
  asked it to agree with planning where planning's deputy work ends, so that the R numbers do not
  collide.
- The record thread's proposal, in its message to planning, 2026-11-12, sent between 04:25Z and 04:27Z (points 1 to 3, whole):

  > 1. You finish R28 and R29 and the matching coordinator's diary entry in your record-only PR. Tell me
  > the PR number when it is up, and I write nothing to rulings.md until it is on main.
  > 2. From R30 on, I write rulings.md, player-diary.md and the coordinator's diary. If the owner speaks
  > to you directly before v3 merges and you take yourself as deputy under v2's text, you ask me for the
  > next number first, and I hold it for you.
  > 3. Since rule 1 says an outcome threads agree between them goes into rulings.md before either acts on
  > it, I record this agreement as R30 once R29 is on main, in your words and mine.

- Planning's answer, 2026-11-12 04:27:27Z, points 1 and 2, whole. Planning asked that these be its words
  for this entry:

  > 1. Agreed. R28 and R29, with the deputy's entry in the coordinator's diary
  > (record/diary/coordinator/2026-11-12T042640Z.md) and my own entry, are committed as 3a4a403 on branch
  > claude/planning-rulings. The repo's three checks are running on it now; the PR opens when they pass
  > and is set to auto-merge. I will send you its number, and again when it is on main. Until then you
  > write nothing to rulings.md.
  >
  > 2. Agreed that from R30 on you write rulings.md, player-diary.md and the coordinator's diary. I
  > propose one writer with no exception of my own choosing: if the owner speaks to me directly, I do not
  > record it. I send you and the coordinator the owner's words, word for word, with the owner's message
  > attached, and you record them. My reason: v2's deputy sentence ends "unless the owner says the
  > coordinator will record it", and R17 is the owner saying that the coordinator's hands, which are you,
  > write rulings.md in the owner's exact words. Only if the owner tells me to record something myself do
  > I write, and then I ask you for the number first and you hold it. That is also what the v3 draft says.

- The record thread's reply, 2026-11-12, sent at about 04:30Z, whole:

  > Record here. Agreed on all three, with your version of point 2: one writer, and you record only if
  > the owner tells you to, after asking me for the number. I will write R30 from your points 1 and 2 and
  > mine once R29 is on main; send me the PR number when it opens. On v3, your smaller change (drop the
  > sentence that takes my folder away) suits me; I see no reason against it. My first diary entry is PR
  > #54, set to auto-merge.

- What was agreed, then: R28 and R29 (PR #55, commit `3a4a403`) are planning's last entries as deputy.
  From R30 on the record thread writes rulings.md, player-diary.md and the coordinator's diary. A thread
  the owner speaks to directly sends the owner's words, word for word, to the coordinator and the record
  thread, and records them itself only if the owner tells it to, after asking the record thread for the
  number.
- The coordinator agreed, in its message to the record thread, 2026-11-12 04:30:25Z: "Received, and the
  agreement with planning (R28/R29 its last deputy entries, you the one writer from R30) suits me."
- Recorded 2026-11-12 by the record thread (R17).

## R31. Question: what "Faster" is measured against (finding G14)

- Raised 2026-11-12 by the review thread, as blocker G14 in `record/findings.md` ("\"sooner\" has no
  yardstick in the book, and the one R9 records is left out"), against PR #53, the first Road Bible.
- Not a stop called by the coordinator: in the coordinator's words to the record thread, 2026-11-12
  05:27:18Z, "planning is the only thread whose work depends on them, and it goes on by stating each as
  open in the book until the owner rules."
- Put to the owner by the coordinator in the project chat, 2026-11-12 05:16:52Z, as the first of two
  questions in its post on the review's report. The question, whole, copied from that post:

  ```text
  1. **What "Faster" is measured against (G14).** The review's words: "The book has the walker return 'faster than the two seasons of version 1.1.1'. R9 records another yardstick, 'sooner than a new walker comes', and in version 1.1.1 a new walker comes after about a week." Today an improperly killed walker returns after 54 days. Is it (a) sooner than a new walker would come, so within about a week, or (b) simply sooner than the 54 days, or (c) left open as the first question of that feature's brief?
  ```

- Not yet answered.
- Recorded 2026-11-12 by the record thread (R17), at the coordinator's request.

## R32. Question: the 48 PRs merged before these rules (finding G12)

- Raised 2026-11-12 by the review thread, in its report G12 in `record/findings.md`: rule 7's third test
  at the gate of step 1 covers "every PR merged so far", and PRs #1 to #48 were merged before these rules.
- Not a stop, as in R31.
- Put to the owner by the coordinator in the project chat, 2026-11-12 05:16:52Z, as the second of two
  questions in the same post as R31. The question, whole, copied from that post:

  ```text
  2. **The 48 PRs merged before these rules (G12).** "One line from the owner in rulings.md accepting them settles it. Without it the inquisitor lists all 48 for the owner at that gate." A plain "accepted" from you does it.
  ```

- Not yet answered.
- Recorded 2026-11-12 by the record thread (R17), at the coordinator's request.

## R33. Question: may the retuned fight change the four cases in which the walker moves under a gaze? (finding G13)

- Raised 2026-11-12 by planning, out of the review thread's blocker G13 ("point 5 of the concept, \"It
  cannot walk while watched\", is not true of the mod as it ships"), against PR #53. Planning sent it to
  the coordinator at 05:27:01Z, as the coordinator gives the time; the record thread has not opened
  planning's message.
- Not a stop, as in R31.
- Put to the owner by the coordinator in the project chat, 2026-11-12 05:27:08Z, in planning's words.
  The post, whole, copied from the project chat:

  ```text
  One more question for you on the Road Bible, from the planning thread (it comes out of the review's blocker G13). In planning's words:

  > A watching player's gaze holds the walker still. As version 1.1.1 ships, it moves under a gaze in four cases: (1) the doe, looked at from within 20 blocks, holds eye contact for 3 seconds and then bolts; (2) from LEVEL 2, its victim's unbroken stare of 2 seconds provokes it, and it hunts the starer for 20 seconds; (3) in the hunt it lunges at a watcher within 5 blocks; (4) a blow frees it for 3 seconds, and a true form that was hit strikes back in reach and otherwise walks off. May the retuned fight change these four cases and their numbers?
  > (a) Yes. The rule itself stays. The prototype may try changes to the four cases behind its switch, and the workshop of step 2 settles with you what stays.
  > (b) No. They are fixed as shipped. The retune changes stats and other behaviour only.
  > (c) Not now. The prototype leaves them as shipped, and the workshop's brief asks you first.
  > Planning recommends (a): R9 says "additional tweaks to monster stats and intelligence will need a work-shop", and what the walker does when watched and hit is the centre of the fight.

  With the two from my earlier post, that makes three you can answer in one line, for example "gaze a, faster a, 48 PRs accepted". Whatever is still open when planning pushes its fix, the book will state as open rather than guess.
  ```

- "The two from my earlier post" are the questions of R31 and R32.
- Not yet answered.
- Recorded 2026-11-12 by the record thread (R17), at the coordinator's request.

## R34. The owner's ruling on R33: the retuned fight may change the four gaze cases (option a)

- The owner, in the project chat, 2026-11-12 07:01:37Z. The message, whole, copied from the project
  chat:

  ```text
  G13.) A R30.) Yes, that makes sense. (In case R30 wasnt answered)
  ```

- The coordinator's reading, not the owner's words: "G13.) A" answers R33, the question that finding G13
  gave rise to, with option (a). The coordinator's words to the record thread, 2026-11-12 07:01:46Z:
  ""G13.) A" answers R33, the question finding G13 gave rise to (may the retuned fight change the four
  gaze cases), with option (a)." Option (a) as quoted in R33: "Yes. The rule itself stays. The prototype
  may try changes to the four cases behind its switch, and the workshop of step 2 settles with you what
  stays." The coordinator has told the owner this reading and asked the owner to say if it is wrong.
- Closes the question of R33.
- Not answered by this message: R31 and R32.
- Recorded 2026-11-12 by the record thread (R17; THE CAST).

## R35. The owner's word on R30: one writer of the coordinator's files

- The owner, in the same message as R34 (quoted whole there). The words that answer R30 are "R30.) Yes,
  that makes sense. (In case R30 wasnt answered)".
- The coordinator's reading, not the owner's words: "R30.) Yes" confirms R30, that the record thread is
  the one writer.
- R30 was an outcome two threads agreed between themselves (rule 1) and asked nothing of the owner. This
  entry adds the owner's word to it.
- Recorded 2026-11-12 by the record thread (R17; THE CAST).
