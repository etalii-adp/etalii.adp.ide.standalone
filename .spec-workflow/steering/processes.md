# Processes

How work moves through this repository: where it happens, how it is committed, how it is checked, and what it leaves behind. Conventions about *code* — formatting, naming, logging, line endings — are not here; they live in `CLAUDE.md` and `src/.editorconfig`. This document is about the work, not the artefact.

**`CLAUDE.md` is the enforcement surface and this is the authority.** `CLAUDE.md` is loaded into every agent's context automatically and steering documents are not, so each process below keeps a compact imperative line there pointing at its section here. The rule travels; the reasoning lives here. That split has a cost worth naming: two files mention each process, so a change to one must check the other. The compact lines are deliberately imperative-only — they state what to do and never restate *why*, because rationale in two places is rationale that drifts.

**Almost every rule here was learned rather than designed.** The incident is kept with the rule on purpose, and for a sharper reason than "context helps": **a rule can only be obeyed or broken, while a reason can be checked against evidence and found to disagree with you.** That is not theoretical — an agent measured what looked like drift between two example trees, read the recorded reason, found it contradicted the measurement, followed it to the commit, and discovered the user had deliberately deleted the guard it was about to ask for. A bare rule would have been read straight past; there was nothing in it for a survey result to collide with. Reasoning is the only part of a document capable of contradicting a careful person who is wrong.

The second reason is the older one: an agent who knows only the rule is the one who carves an exception into it when the rule looks inconvenient, and several of these were bought at the cost of somebody's lost work.

## Chat naming

A session's title says **which role it holds and what it is currently doing**, in the form `<Role> N - <topic/specification>`. The team is one Scrum master, four Developers, two Testers and two Architects; [roles.md](roles.md) defines what each is responsible for.

- A title that already carries a role and number keeps both; only the topic part changes.
- A session with no number takes the next free one **within its role** — list the other sessions and take the highest existing number for that role plus one.
- When the session moves to a new topic, rename again: same role, same number, new topic.
- The Scrum master session is named `Scrum master`, without a number, and never renames — it is the single point of contact and its address must stay stable.

The role and number together are an identity other sessions address; the topic is how the board is read at a glance. A finished session renames itself `<Role> N - Idle`, which is what marks it available for new work.

**Why the role is in the title rather than only in the assignment.** The Scrum master assigns tasks to Developers, specifications and documentation to Architects, and testing to Testers — and it reads the board from these titles. A title that says only what a session is doing hides whether the right kind of session is doing it, and the mismatch surfaces at the end rather than at the start. A session asked for work outside its role says so rather than taking it.

## Where work happens

**Implementation work happens in a dedicated git worktree (`.claude/worktrees/<name>/`), never in the main checkout.** Several sessions use this repository at once, and the main checkout's index and working tree are shared between them. A `git add` there can stage another session's files.

**Specification documents are the exception, and they are not worktree work at all.** Requirements, designs and tasks documents — and every approval, snapshot and implementation log beside them under `.spec-workflow/` — are written and committed **on `develop` in the main checkout**, using `git -C C:\git\EtAlii.Adp` and an explicit pathspec. This holds for the whole document phase: drafting, revising after a rejection, and the bookkeeping that marks a task complete. Never create a worktree to write a spec, and never write one from inside a worktree held for other work.

The reason is that the dashboard reads `.spec-workflow/` from the main checkout only. A document written in a worktree is invisible to the person who has to approve it, and its approval card points at a path whose content does not exist where the card is read — so the spec silently cannot progress. Surveying and measuring for a spec may happen anywhere, including a throwaway worktree; the *document* lands on `develop`.

**One worktree per specification, unless its tasks are independently landable and several agents are working them in parallel — then each agent takes its own.** Never share one worktree between sessions: a shared working tree is a shared index, which is the contamination the first rule exists to prevent, and it is a worse failure than several worktrees for one spec. A tasks document claiming its tasks are independently landable is claiming exactly this, and should say so where a reader will meet it.

**Keep worktree directory names short.** A worktree adds roughly 36 characters plus the name to already-deep project paths, and Windows' 260-character `MAX_PATH` then silently breaks the build in a way that reports as an empty test suite rather than as a failure — see *Running the backend tests*.

**When running the app from a worktree**, change both the client's dev server port (`src/client/vite.config.ts`'s `server.port`) and the backend's `Client:DevServerUrl` (`src/backend/EtAlii.Adp.Backend.Service/appsettings.developer.json`) to a free pair before starting either server; the main checkout's own servers may hold the defaults (5174 client / 5080 backend). Revert both files before merging, so the merge never carries a stray port change into `develop`.

## Retiring a worktree

**Retire a worktree once its branch is merged into `develop` and its working tree is clean:** `bash C:/git/EtAlii.Adp/.github/tools/gate/retire.sh <name>`, which checks what a script can of the rules below and deletes the worktree itself rather than asking git to. One is created per piece of work, so without this they only accumulate — twenty-four had built up before anyone counted.

Four rules make that safe, and none is optional.

- **Never remove a worktree another session is still working in.** Merged and clean describe the *branch* and say nothing about whether somebody is sitting in the directory. List the live sessions first — their names match the worktree directory names — and leave those alone. Four live sessions were deregistered underneath their owners in one sweep, which is why this is first.
- **Never remove a worktree with uncommitted changes or unmerged commits.** Raise it for a decision. Housekeeping that destroys work is not housekeeping.
- **Removing a worktree does not remove its branch,** so a merged branch's commits stay reachable either way. Do not delete branches as part of this.
- **Removal often fails on Windows**, with `Filename too long` (deep `node_modules` paths) or `Permission denied` (a dev server, an IDE, or your own shell sitting inside the directory). Git still deregisters the worktree; only the directory deletion fails. **Never `--force`.** For `Permission denied`, move your shell out and retry. For `Filename too long`, delete the leftover directory by hand with a long-path-capable command — and look before deleting, with file tools and never with git (see below), because a leftover folder can still hold source.

**`retire.sh` is the remedy for the two paragraphs that follow.** It never asks git to delete the tree: it deletes it by absolute path, its `.git` file last, and then prunes the registration — so `Filename too long` cannot stop it wherever a build put the long paths, and until the tree is empty the directory is still a worktree of its own, never a husk. **Its first version got this wrong in the instructive way:** it deleted `node_modules` and then called `git worktree remove`, on the belief that the long paths lived there. They also live in dotnet's `obj/` (measured up to 265 characters under a worktree), and both real retirements after it landed left a husk. Its own test had passed, because the control reproduced only the cause its author had in mind; the fixture now plants a real module's `obj/` output with no `node_modules` at all, and the first version fails it. Before deleting anything it refuses a husk, anything that is not a registered worktree of its own, uncommitted changes, commits that have not landed on `develop` (by patch-id, so a re-landed commit is not lost work), any link whose target lies outside the worktree, and a shell standing inside it; it keeps the branch. A delete that stops early leaves a registered worktree with files missing — every one of them on `develop` — and says so. **It cannot see whether another session is working in the worktree** — the first rule above is still yours. What follows remains true for anyone retiring by hand.

**The removal above makes a husk even when it is done correctly.** When `git worktree remove` fails on a long path it has usually deregistered the worktree already, and it leaves the directory behind without its `.git` file. **A directory that git does not recognise as a worktree answers every git command on behalf of whichever repository contains it** — git finds its repository by walking up from the working directory, however the command arrived there — so from that moment until the directory is deleted, every git command run in it answers for the **main checkout**: `cd` into it and `git status` reports the *main checkout's* dirty files, `git add -A` would commit another session's work, and `git -C <dir> reset --hard` would reset the shared checkout. Nothing any of them prints looks wrong. A husk and a worktree look identical to git; **only `git rev-parse --show-toplevel` compared against the path itself tells them apart.**

**So a window between deregistration and deletion opens every time a removal fails, and no git command runs in it** — not by `cd`, not by `-C`, not to look before deleting; look with file tools. **The one exception is the question that detects it:** `git rev-parse --show-toplevel` compared against the path, which only reads and is exactly what the scratch-worktree guard asks. Anything else acts on the main checkout; even `git status` can write a refreshed index there. Developer 1 retired seven worktrees through that window in one day and ran no git command in the gap, so nothing happened. **Do not recreate a worktree at that path until the directory is gone.** `git worktree add` refuses the occupied path, and a chain that skips the creation because the directory exists, or carries on past the refusal, walks into the husk with its next command — which is exactly what a chain that retires a scratch tree and recreates it under the same name does (see the scratch-worktree guard under *Committing and merging*). The files themselves are safe: copy out anything you need, delete the directory, then `git worktree add` again. (`Filename too long` is git's own limit, not the OS one; it wants `core.longpaths=true`, which is the user's call to set.)

**Before a recursive delete, list the links in the directory and where they point, and stop on any target outside it.** `find <dir> -type l -printf '%p -> %l\n'` in Git Bash lists junctions with their targets. npm's workspace links under `src/node_modules` are junctions with absolute targets inside their own tree, so in a husk from a fresh removal they point at the husk's own files, which the delete removes anyway; **what can reach real source is a link whose target lies outside the directory being deleted.** Measured on this machine on 2026-09-10 against a junction to an outside folder: Git Bash `rm -rf` and Windows PowerShell 5.1 `Remove-Item -Recurse -Force` both removed the link without following it, and `Get-ChildItem -Recurse` did not descend into it. **That is today's builds, not a guarantee**, which is why the rule is a check rather than trust in the tool. Found by Developer 1, removing four husks that held 37 such links, every one pointing back inside its own husk.

## Committing and merging in the shared main checkout

Two distinct hazards, and the rule for one does not cover the other.

**Commit with an explicit pathspec: `git commit -F msg -- <paths>`.** `git add <path>` stages one file, but a bare `git commit` commits *the whole index*, including whatever another session has staged into it. This has swept another agent's files into an unrelated commit under the wrong message and the wrong identity at least four times. "Stage carefully" is not the fix, because care is not what fails — **in this repository a directory pathspec is a sweep by construction**, since the main checkout nearly always holds somebody's uncommitted work. Name files, never folders.

**And the rule is *name the files*; the CHECK is *read the `create mode` lines back*.** A pathspec over
a DIRECTORY commits the modified tracked files under it and **silently ignores the untracked ones**,
because a pathspec filters the index and an untracked file is not in it - while `git add -- <dir>` does
pick them up, which is what makes the asymmetry easy to miss. **On 2026-09-23 an approval request landed
and its request-time snapshot beside it did not**, leaving a card pointing at a snapshot that is not in
the repository: **invisible until somebody else checks out that commit**, because the file is present in
the working tree of the session that raised it. It was caught by reading the commit's own output - three
`create mode` lines where four were meant - and **a pathspec that silently covers two of three files
looks identical to one that covers all three** until then. Naming the files is what people already do;
reading the modes back is the part that fails loudly.

**Commit your own implementation log the moment the tool writes it.** The other half of the same failure, and the half that is easier to miss: an uncommitted file left in the shared checkout is what turns somebody else's careless pathspec into a wrong commit message. The sweeper gets blamed; whoever left the file there supplied it. This is already what *Specification bookkeeping* asks for — it was being read as covering the documents rather than the logs the tool writes for you.

**Never merge while anything is staged - yours or anyone else's.** `git merge` requires the index to match HEAD; when it does not, git stashes the working state and, on the failure path, does **not** restore it. That silently reverted or deleted **26 paths belonging to three other sessions**, reporting only `Index was not unstashed. Merge with strategy ort failed.` - a single line for two dozen files of other people's work. Reading `git status` first is necessary and not sufficient: it tells you what a merge would *write*, not that attempting one is unsafe.

**Merge through a scratch worktree rather than waiting for a clean index.** The mechanism is the point: `git merge` builds the merge commit's tree *from the index*, which is why a foreign staged file blocks it and why its failure path is what dangles other sessions' work. **A fast-forward builds no tree from the index and does not care.** So create a scratch worktree at the develop tip, `git merge --no-ff` your branch there, run the four gates *in it* - on the actually-merged tree - then in the main checkout `git merge --ff-only` the merged commit. **Create the scratch tree detached** (`git worktree add --detach`), which is what the guard below requires. The last step succeeds with foreign files staged, and leaves them staged and untouched. The resulting history is identical to merging in place, and the route never puts the main checkout in a state where git must save and restore anybody's work - so it **cannot** reproduce the incident rather than merely avoiding it. Waiting for a clean index is not a workable alternative: one staged file has sat in this checkout for over two hours.

**Run the shared gate rather than writing a chain:** `bash C:/git/EtAlii.Adp/.github/tools/gate/gate.sh mrg<N> <branch> <identity> "<message>"` - your own scratch name, all four arguments required, none defaulted. If it ends `RESULT=gates-green-ff-withheld`, run the `land.sh` line it prints, yourself, in the foreground; any other `RESULT=` names what stopped it. The gate is this section and the guard below, executed: it reads the guard from `develop`'s copy of this document at run time and refuses a husk, the main checkout and anyone's feature worktree; locks the scratch tree; runs the four gates with every status captured before any pipe; starts its verdict refused and asks the dotnet log for positive evidence; refuses gates that changed tracked files; and never moves `develop`. `land.sh` fast-forwards only when the main checkout is on `develop` and `develop` is still the base the gates merged onto. **Both refuse to run unless they are byte-identical to `develop`'s copy** - the guard text was always shared, but until this script every session enforced it with its own chain, so the enforcement protected one session at a time. And three hand-written chains had the same fail-open verdict in three different forms on one evening, each found and fixed separately: every new chain re-derives the verdict and can re-derive the hole, which is *A remedy that must be remembered is not a remedy* applied to the scripts themselves. The user ruled for one shared, committed, tested gate on 2026-09-10. **Change it like any other code:** its self-test (`gate.test.sh`, 106 cases, 116 on Git Bash, hermetic, against real dotnet logs) runs inside `dotnet test`, so a regression in the gate fails a gate - and after landing a change to it, run it once on `develop` itself, a no-op merge, because the one thing the hermetic test cannot show is a green run through real gates. The rest of this section is why the gate is shaped the way it is.

**Prove the scratch worktree is a worktree of its own, and not the main checkout, before the first git command touches it.** **Our own procedure makes husks:** every removal that fails on a long path leaves one - directory present, `.git` gone - until somebody deletes it by hand (see *Retiring a worktree*), so a chain that retires its scratch tree and recreates it under the same name meets one whenever that removal failed. Husks left by a pruner or by accident come on top of that. **A directory that git does not recognise as a worktree answers every git command on behalf of whichever repository contains it**, because git finds its repository by walking up from the working directory however it got there - `cd <dir> && git ...` and `git -C <dir> ...` alike - and here that repository is the **main checkout**. A husk and a worktree look identical to git; **only `--show-toplevel` compared against the path tells them apart.** A merge chain aimed at a husk therefore runs `config --worktree user.name`, `reset --hard develop` and `merge` on the shared checkout, and every one of them reports success. It would look like somebody's work vanishing. It is the shape catalogued under *Checking that a command answered your question*: every answer is well-formed, and none of them says which repository gave it.

**The existence test every chain used is the bug.** `if [ ! -d "$SCRATCH" ]; then create; fi` passes a husk, because a husk *is* a directory. The ingredients coexisted on the day this was written: four husks under `.claude/worktrees/`, each resolving to `C:/git/EtAlii.Adp`, and scratch trees disappearing to a pruner. **Put this before the first git command that uses the path - `config --worktree` and `checkout --` run before any `reset`:**

```sh
canon() { p=$(cygpath -m "$1" 2>/dev/null) || p=$1; printf '%s' "$p" | tr '[:upper:]' '[:lower:]' | sed 's:/*$::'; }
MAIN_TOP=$(canon "$(git -C "$MAIN" rev-parse --show-toplevel)")
if [ ! -e "$MRG" ]; then
  git -C "$MAIN" worktree prune
  git -C "$MAIN" worktree add --detach "$MRG" develop > /dev/null 2>&1 || exit 3
fi
MRG_TOP=$(git -C "$MRG" rev-parse --show-toplevel 2>/dev/null || true)
if [ -z "$MRG_TOP" ] || [ "$(canon "$MRG_TOP")" != "$(canon "$MRG")" ]; then
  echo "ABORT: $MRG resolves to '${MRG_TOP:-nothing}', not to itself"; exit 3
fi
if [ "$(canon "$MRG_TOP")" = "$MAIN_TOP" ]; then
  echo "ABORT: $MRG is the main checkout"; exit 3
fi
if git -C "$MRG" symbolic-ref -q HEAD > /dev/null; then
  echo "ABORT: $MRG is on a branch - a scratch tree is detached, so this may be someone's worktree"; exit 3
fi
```

**Each line answers a way the check itself could be wrong.** *Absent* is decided by `-e`, never by the query failing, so a path that exists but cannot be read aborts rather than being recreated over. **Both sides are canonicalised - converted, lowercased, trailing slashes stripped.** A Git Bash script writes `/c/git/...` while `--show-toplevel` returns `C:/git/...`, and a comparison that normalises only one side still reports a legitimate worktree as a husk whenever the path carries a trailing slash or a different case: measured, the one-sided form was wrong in fourteen of twenty-one spellings of the seven real worktrees, while passing every husk correctly. **A guard that aborts real chains gets loosened by the next person until it passes husks**, so a false abort is not the harmless direction it looks like. `canon` falls back to the raw path when `cygpath` is missing, which makes every comparison fail and the guard abort - noisy, but closed; the natural pipeline form yields an empty string for every path instead, and two empty strings pass a husk. `worktree prune` stays in the recreate branch because `worktree add` refuses a path whose registry entry went stale. **Present but wrong is an abort, never an automatic delete** - a directory in a state nobody expected is where a script stops and a person looks.

**And the main checkout is refused explicitly, because it passes everything else.** It *is* its own worktree, so it satisfies *resolves to itself* in any spelling: measured, a caller that set the scratch path equal to the main checkout got *"scratch worktree verified"* and exit 0, with `reset --hard develop` as the next line. **That is the one configuration that reaches the damage *through* the guard rather than around it.**

**How this guard was arrived at is the argument for testing both halves.** Developer 3 found the hazard and wrote the first guard, tested against a planted husk and an absent path - the half proving it catches the danger. Cross-checking produced two guards, **and each had exactly one of the two properties**: Developer 3's corrected version canonicalised both sides but still passed the main checkout; the one first committed here refused the main checkout but compared one-sidedly. **Neither was safe to carry, and every test either author had run passed.** What exposed each was a case its author had not written: a real worktree spelled differently, and the scratch path set to the main checkout. **So verify a guard with the cases that must pass, in every spelling a caller might use, and with the one input that satisfies its condition while being the thing it exists to protect.**

**And refuse any scratch tree that is on a branch, because *its own worktree and not the main checkout* still passes someone else's.** A chain whose scratch path pointed at another session's feature worktree would pass both checks above, and `reset --hard develop` would destroy that session's uncommitted work - less than the whole board, and exactly as silent. **The two checks above are an exclusion list: not a husk, not the main checkout. A list excludes what its author thought of.** A scratch tree has a positive property instead - it is created detached and never checks out a branch - while the main checkout and every feature worktree are on one. On the day this was written that held without exception: three scratch trees detached, the main checkout and four feature worktrees each on a branch. So the last check identifies what the tree *is* rather than listing what it is not, and it closes the main-checkout case a second way. **It must run after the others:** on a husk, `symbolic-ref` answers for the main checkout, which is on `develop`. Its cost is that a chain which checks out a branch in its scratch tree is refused; none does. Proposed by Developer 3, using a live feature worktree as the demonstration.

**A verifier can fail to run, and only the case that must pass will show it.** *(Developer 3's account.)* Testing the guard with `cygpath` shadowed, the fake was put on `PATH` by its Windows-form path. `PATH` is colon-separated, so `C:/Users/...` split at the colon, was never searched, and the child quietly found the real `/usr/bin/cygpath`. The run reported the guard failing *open* - a real worktree came back verified - when the case had never been exercised at all. The husk in the same run aborted, and told us nothing: husks abort whether or not `cygpath` is present. **Only the case that must pass could expose a harness that never ran.** So a harness that simulates a condition asserts that condition inside itself before trusting any result - here, that the child resolves `cygpath` to the fake - and reports itself broken otherwise. **It is the same bug the guard exists to catch, a Windows-form path where an MSYS-form one was needed, turning up in the thing testing it.** **And asserting the precondition is not enough, because the verdict can be misread as well.** Verifying this section, the same case was run again with the precondition asserted and holding - and reported a real worktree, a husk and the main checkout all *passing*: fail-open, the worst result a guard can return. The guard was fine. The harness read the exit status after an unrelated command had already replaced it, so it was reporting on that command. **Put a known pass through the same capture before trusting any failure it reports**, and a known failure before trusting a pass; a verdict the harness has never been seen to return is not one it can be trusted to return. Both harness faults reported the most alarming possible outcome for a guard that was working, and both were caught only because the result contradicted an earlier run.

**Re-gate on the merged tree, not before it.** An hour's wait is long enough for `develop` to gain code an earlier run never saw - in one case a 311-line integration test and three validator changes.

**If it does happen, the work is recoverable and re-running anything is the wrong move.** The stash survives as a dangling commit pair: `git fsck --unreachable --no-reflogs`, find `WIP on develop` and `index on develop` at the failed merge's timestamp, then `git checkout <wip-commit> -- <path>` per file. Restore only what was present, and **never re-apply a deletion that was in flight** - re-applying somebody's half-finished delete is the one direction that destroys rather than restores.

**Before merging in the main checkout, read `git status` for files you did not touch.** A merge writes every path that differs between the branch point and the tip *regardless of the index*, so uncommitted churn from another session will either block the merge or tempt you into `git checkout --` on files that are not yours. Neither stash nor checkout is acceptable there — both destroy in-flight work. Apply your own files with `git checkout <branch> -- <paths>`, commit by pathspec, then verify with `git diff <branch> develop -- <your paths>` that the result matches what the merge would have produced. The branch is then not recorded as merged, which is a small honest loss of history; say so in the report.

**A DEVELOP COMMIT COMPETES WITH A RUNNING GATE EXACTLY AS ANOTHER GATE DOES.** The gate takes develop's tip
as its base and refuses to land onto a moved develop, so a commit straight to develop - a specification
document, a findings record, anything that legitimately bypasses a worktree - **stales every run already in
flight, and the verdict arrives only after the full backend suite has been paid for.** Three sessions hit it
in one day: a self-staled landing, a window granted on an old reading, and six commits onto a running gate's
base, which cost that session a third cycle.

**What made it invisible: a running gate cannot be seen from develop.** The lock is per scratch worktree and
says nothing about whose base it protects, and every rule about committing to develop is about **what** you
commit - pathspec, identity, cards - never about **when**. So the tree now carries the answer: **`bash
C:/git/EtAlii.Adp/.github/tools/gate/who-is-gating.sh`** prints one line per holder with the base it would be
staled by, or `GATING=none`, or `TELL_UNREADABLE=<dir>` with a non-zero exit. **Read it immediately before the
commit, not at the start of your turn**, and remember that **it warns and never authorises** - a clear read
does not make the board yours.

**Why the naming is a person rather than a check, which the tell cannot tell you itself: the board is
serialised by a DECISION, not by a lock.** Gate locks are per scratch worktree, so **they do not exclude
each other** - if a free slot were self-service, two sessions would read *free* in the same second and
**both would be correct**. That is the reason behind *warns and never authorises*, and it is worth having
because the sentence without its reason invites somebody to decide the warning is enough when the board
is obviously quiet. *(The Scrum master's.)*

**And the dangerous version of this arrives as HELPFULNESS, which is why it survives people who have
already learnt the rule.** On 2026-09-24 the holder of the board wrote that it would land and report
*so that the next session is not waiting on a message of mine* - **which makes the go come from whoever
FINISHED rather than from whoever is serialising.** That is self-service with an extra step, written
three hours after the same session had nearly collided with another over exactly this, and by a session
that had already withdrawn the same move twice that day.

**The costume is the whole difficulty: a person looking out for a violation is watching for
permission-shaped things, and a kindness is not shaped like permission.** *Saving somebody a message* and
*granting somebody a turn* look nothing alike and are the same act. **The tell generalises past the
board: if you are reasoning your way to the conclusion that you may proceed - or that somebody else may -
that reasoning is the evidence that you may not.** Ask instead; it costs one message and the round trip
you are saving was never the expensive part.

**And the tell is FAIL-OPEN in a chain, which is worse than it sounds because it reads as a check.**
`who-is-gating.sh` returns **2 only when it cannot read the board**, and **0 for a free board, for a
holder, and for a lock whose owner is not written yet alike** - so `tell && commit` runs the commit
whatever the board says. **Every `&&` chain ever written over it has been a print statement wearing a
check's clothes.** The state form, which is the recognisable half: **a verdict produced by nothing going
wrong looks identical to one produced by everything going right.** The remedy is to make the state that must
be REACHED the thing that gates, rather than the state that must be avoided the thing that aborts:

    TELL=$(bash .github/tools/gate/who-is-gating.sh)
    case "$TELL" in *GATING=none*) ;; *) echo 'HOLDER - refusing'; exit 9;; esac

**`--require-free` now does this inside the tool** (exit 0 only for a positive `GATING=none`), which is
the better place for it - but the pattern generalises past this one script, and the clause above about a
verdict comparison that errors into ALL GREEN is the same defect in a different file.

**And the one moment a develop commit is free is when your own base is already stale.** A run that will refuse
its fast-forward anyway cannot be made worse; that is the only window, and it is worth using deliberately
rather than waiting for a quiet board that may not come.

**And the base can move from outside the board entirely, which makes a refold the ordinary outcome rather than
an incident.** The first draft of this clause said a develop commit needs the Scrum master's gap the same way a
gate slot does. **It cannot: on 2026-09-23 the user landed six commits in under half an hour into the middle of
two consecutive gate runs** - both green on all four gates at 6678 tests, both refused - **and the user neither
asks for a gap nor should have to.** A clause that implies otherwise promises a protection it cannot deliver.
**It is one clause and not two, because the agent case and the user case differ only in whom you could ask and
not at all in what happens**; the remedy is identical, so splitting them would imply two remedies where there is
one. What follows is practical: **read the tell at the instant of use rather than trusting a placement** - the
gap is scheduling, the read is the check - and **treat the refold as a cost of doing business**, which is why
the gate reports `gates-green-but-base-stale-refold-needed` itself instead of leaving it to be discovered at the
fast-forward. **When the cadence makes refolds routine, the answer is the user's to give and not an agent's to
infer**: asked directly, they chose to batch landings until they stopped committing for the day. A peer offered
the narrower reading that a fast-forward onto an unmoved base was surely still permitted, and was right to be
refused - **that is the carve-out shape, and it is most persuasive exactly when the case looks safest.**

**STAGED SNAPSHOTS: COMMIT THEM BEFORE A GATE, AND AFTER A LANDING.** Same files, opposite order, and the
discriminator is which operation comes next. The existing rule - clear the index before merging - is about the
**merge**, which happens inside `gate.sh` at the START of a run. **Between a green gate and its fast-forward
the situation reverses**: a fast-forward writes no commit and cannot be invalidated by ownerless files in the
index, while committing them first DOES write a commit, which moves develop and invalidates the landing you
were about to run. **That cost a green gate at 6660 tests its landing**, and the session did not force it -
forcing would have discarded a commit of files belonging to nobody, **which are easier to destroy and no more
acceptable to.**

**The transferable half: the existing rule READS AS COMPLETE.** It is correct about the case it describes, so
a reader in the other case follows it confidently into a refused landing - which is why each half here names
the operation it is about rather than only the order.

## Git identity

**Commit under a per-task identity, `agent-<N>-<task>`, set differently depending on where you are.**

- **In a dedicated worktree:** `git config --worktree user.name "agent-<N>-<task>"`. **Not** plain `git config user.name`, which writes to `.git/config` - shared by the main checkout and every worktree - and so renames every other agent too.
- **In the shared main checkout:** set nothing, and pass the identity per command: `git -c user.name="agent-<N>-<task>" commit -F msg -- <paths>`. Several sessions use that one working tree at once, so *any* config there is shared between them, `--worktree` included. There is no per-session scope, and `git -C` does not provide one - it changes where a command runs, not whose identity it uses.
- **Never `--global`, and never touch `user.email`.**
- **Read it back with `git config --local user.name`.** Inside a worktree the plain form resolves through `config.worktree` and returns your own correctly-scoped identity, which looks identical to a leaked shared value - that has already prompted an unset nobody needed. Only `--local` shows what is actually in the shared file.

Without an identity you inherit the machine's global `user.name` in every checkout, so your work is indistinguishable from the user's own. The first version of this rule used plain `git config`; four sessions overwrote each other and two commits carry the wrong author.

**A `--worktree` identity binds to the DIRECTORY, not to the session - so gating in another session's live
scratch tree signs the merge as THAT SESSION.** And this is worse than a naming collision, which is the whole
point: **a collision announces itself.** Two sessions in one directory produce a refused `--ff-only`, a dirty
tree, something that stops you. This produces **a clean green with a wrong author, and nothing in anybody's
workflow inspects the author of a merge that passed.** The rule *one scratch tree per agent, named for the
agent* is true and **silent about the case that actually arises**: somebody else's tree exists, it is already
configured, and using it costs nothing visible. The near-instance was a session queued third for a gate slot,
choosing between `mrga2` and a live `mrga3` belonging to another session - it took its own, and had it not,
the merge for a `CLAUDE.md` change authorised in *its* session would have been signed by the other. Neither
session would have caught it: you do not audit the author of someone else's merge, and nobody audits the
author of a green one.

**Which sharpens the read-back above rather than repeating it.** The reason given there is that you might
inherit somebody's identity. The sharper reason is that **the inherited identity produces no symptom at all**
- so the read-back is not belt-and-braces, it is the only instrument.

**Why an identity rather than the `Co-Authored-By` trailer**, which was the alternative considered: the trailer is a per-commit act that can be forgotten, and it is forgotten *structurally* - present exactly when composing a message was a deliberate act, absent whenever the message came for free. `git commit -m "one line"` and `git merge --no-edit` are both ways of not writing one, which is why bookkeeping and merges - the two highest-frequency commit kinds - are the two that fail. Nine of twelve recent merges lacked it. An identity is set once per working tree and cannot be forgotten per commit: one is a discipline, the other is a property.

The trailer remains useful in one direction only, and the limit matters because **the inverse reading is the more useful one**. A present trailer proves an agent made the commit. An absent one proves nothing at all - so "which of these did a human do?" is exactly the question it cannot answer, and a test reliable only in the less useful direction will be used in the other one.


## A remedy that must be remembered is not a remedy

**The identity rule above is the pattern, not a special case: `git config --worktree user.name` is set once, in the directory, and every command run there inherits it. It cannot be got wrong later because there is no later moment at which it is applied.** Three failures in this repository, in three unrelated areas, were each fixed by moving a rule from something a person must recall into something the artifact carries. **The general form is the useful one: if a remedy requires the right action at the moment nobody is looking, it is a description of the failure rather than a fix for it.**

- **Identity.** *Remember to pass `-c user.name=` on every command that writes a commit object* failed, because `merge`, `rebase`, `revert` and `cherry-pick` all write one and only `commit` looks like it does. **The remedy is `--worktree`, set at creation.**
- **Gate scripts.** Each was produced by `sed`-editing the previous task's copy, so one landed a merge whose message described the item before it, and another had a greedy edit aimed at the message string swallow the `|| { echo "RESULT=merge-conflict"; exit 1; }` guard sharing its line. *Remember to update the message* is the rule that failed. **The remedy is that branch, identity and message are required arguments the script refuses to run without** - a parameter cannot be a stale copy of the last run. Its completion is that there is only one script: `.github/tools/gate/gate.sh`, committed and tested, so there is no previous task's copy to edit.
- **Deletion and writes.** *`cd` out of the tree first, then `rm -rf`* was already written down, **and it still failed three times in one session.** The Bash tool resets the working directory and says so - but that notice is **trailing information, printed at the end of the call that reset it and read, if at all, before the call that depends on it**, so the rule asks somebody to hold a fact across a tool boundary. **The remedy is `git -C <absolute>` and absolute paths for anything that deletes or writes**, which carries the location in the command the way `--worktree` carries identity in the directory. It fixes *where* a command runs, not *which repository answers*: aimed at a husk, an absolute `-C` still answers for the main checkout (see *Retiring a worktree*).

**The deletion case is worth the most detail, because its failure is silent and destructive at once.** `git worktree remove <relative>` refused with *Permission denied* - the shell stood inside the tree being removed. The natural next command, `rm -rf <the same relative path>`, resolved from inside that tree and targeted a path three levels deeper that did not exist. **`rm -rf` on a path that is not there exits 0 and prints nothing, because "the path is not there" is its success condition.** The chained `ls` then reported *No such file or directory*, which reads exactly like proof of removal and was the same lie told twice. **Every signal said retired; the husk was still on disk.** A deletion that *fails* is a good day - it is loud.

**So recognise the precursor: `git worktree remove` refusing from inside the tree.** The error itself is telling you the shell is in the wrong place, and the obvious follow-up is a relative deletion issued from exactly the directory that caused the error. **After that refusal, the next command must be absolute.**

**And when a written remedy fails anyway, the fix is not to repeat it louder.** Ask what it asks a person to do and when - **if the answer is "the right thing, unprompted, later", replace it with something the command or the directory carries.** The three above were each written down before they failed.

**A third kind of remedy, for when the artifact CANNOT carry it.** All three above are prevention: make the
command or the directory hold the rule so there is no later moment. That works when the command can be
changed, and **it cannot work for a search** - there is no argument to make required and no directory to carry
it, and nothing stops anybody grepping for the wrong string. So the remedy shifts from preventing the mistake
to **naming what the wrong result looks like**, because recognition costs nothing: you are already looking at
the output.

The instance, and the reason this is a category rather than a nicety: a comment declared a 60-second constant
"not set anywhere in this repository", eliminated by a grep for `FromSeconds(60)` when the code said
`AddSeconds(60)` - one builds a `TimeSpan`, the other offsets a `DateTime`, and the search was correct about
the string it was given. **The tell was in the result itself: that grep's only hit was the comment asserting
the absence.**

> **A search whose only hit is the claim that the thing is not there is the signature of a search that
> missed** - and it reads exactly like confirmation.

That is checkable on the first run. The action-shaped version - *re-run string searches, their blind spot is
invisible from their result* - is not, because at the moment of running a search you believe it. **If a rule
begins "always", "remember to" or "re-run", it asks for vigilance and will decay; if it begins "a result that
looks like X is", it asks for recognition and will hold.** Convert the first into the second wherever the
failure has a visible signature - and where it has none, that absence is itself worth writing down.

**Three zeros from one audit, every one of them the instrument rather than the file.** The clause above was
cold-read by another session, which quoted two sentences from this document back at me. Checking those
quotations, I was one message away from reporting them as fabrications:

- `git log -S "first mounted assertion"` returned **nothing, across every ref and every path** - because that
  sentence *wraps mid-phrase*, breaking between `mounted` and `assertion`. `-S` compares line-wise, so a
  phrase spanning a newline is invisible to it. Flatten the whitespace first and it is there.
- `grep -c "after a landing"` returned **0** on two different revisions - because the line is **shouted**:
  *STAGED SNAPSHOTS: COMMIT THEM BEFORE A GATE, AND AFTER A LANDING.* `grep -ci` finds it.
- **and the control was clean.** All twenty-four worktrees checked for an uncommitted copy the reader might
  have been looking at, every one of them clean - **which made the fabrication reading stronger rather than
  weaker.**

**Prose wraps and rules get shouted, so both blind spots are live every time anybody greps this file**, and
neither is visible in the result. Search a prose document case-insensitively, and flatten whitespace before
searching for any phrase longer than a few words.

> **A correct control on the wrong hypothesis strengthens the wrong conclusion.**

That control was rigorous and its answer was true. It eliminated the one innocent explanation I had thought
of - a working tree I could not see - and eliminating an innocent explanation is exactly what makes a guilty
reading credible. **A control protects only the hypothesis it was built against**, and mine was aimed at
*where the text might be* rather than at *whether my search could see it*. The reading it strengthened was
that a careful peer had invented quotations.

**And the discipline that actually caught this is the cheapest one in this document: report a search, not an
elimination.** The cold read's third finding said *"I searched six phrasings and could not find it - and I am
reporting a search, not an elimination."* It was reading the wrong revision entirely, so no phrasing could
have found what it was looking for. **Because it claimed only what it had run, the gap arrived as a question
and was settled in one command; had it claimed the clause was absent, the answer would have been a false
all-clear with nothing in it to invite a second look.** Two sessions were saved by that single hedge in one
day. **"I could not find it" and "it is not there" cost the same to write and differ by everything the
instrument cannot see.**

## A rule that is silent about its sibling case reads as complete

**A false rule gets caught. A true-but-partial one does not.** A rule whose antecedent covers one branch of a
fork and says nothing about the other is correct every time it fires, so nothing ever contradicts it - and the
case it omits does not come up until it does. Four instances, from four authors, in two days:

- **Approval versus rejection.** *The durable record of a verdict is the snapshot's `trigger`* is true of an
  approval and of a revision request, and **a rejection writes no snapshot at all** - so a rejected card and an
  unanswered one are indistinguishable to a reader following the rule exactly. The missing half is that the
  request JSON's `status` is the only record of a rejection.
- **Measured versus searched.** *Two eliminations, both by measurement, which is why they can be relied on* was
  true of the two it described. A third elimination in the same list was a **string search**, inherited at the
  same confidence, and it was wrong: the constant it declared absent sat forty-six lines below the claim. The
  missing half is that a search must be re-run rather than cited.
- **Per-test versus shared budget.** *A per-test constant would have fired in the isolated runs* is sound, and
  it says nothing about a budget **shared across five sequential awaits in one call** - which is what the
  deadline actually was. The missing half is that a per-call clock starts when the call is made, not when the
  await begins.
- **One go versus two.** *A go names a branch explicitly to the session that owns it* is correct, and silent
  about **two explicit gos outstanding at once**. A naming that has been issued and not yet taken up is still
  live, so the board was not free when the tell said it was - and no amount of re-reading the tell catches
  that, because **the tell cannot see a go sitting in somebody's inbox.** The missing half, in its enforceable
  form: **one naming is live at a time, and the issuer does not issue a second until the first reports.**

**State the missing half as the action its owner takes, not as an awareness.** "Be aware that two gos can be
outstanding" is unenforceable; "I do not issue a second until the first reports" is a thing somebody does or
does not do. The same distinction as [*A remedy that must be remembered is not a remedy*](#a-remedy-that-must-be-remembered-is-not-a-remedy).

**And note where the catch came from in the last case, because it decides where such rules live.** Not the
issuer's verification and not the actor's care: **the tell, read at the moment of use.** The issuer's reading
was correct when taken and stale by the time it arrived, and no check on their side could have covered that
window. That is the argument for *refuse a go whose premise changed* sitting with the session about to act -
demonstrated rather than asserted, and twice in one day, both caught by a session refusing.

## Precedent is not permission, and "not a merge commit" is not "straight onto develop"

**A session authorised to make a two-line change to `CLAUDE.md` looked at how that file had been changed
before, concluded the established route was direct commits to `develop`, and proposed following it - and the
route it saw did not exist.** The ruling went the other way: anything changing the repository's files goes
through a worktree and the four gates, and the **one** stated exception is a specification document, *because
the dashboard reads those from `develop` and nowhere else*. `CLAUDE.md` is not one and no dashboard reads it.
**The ruling was right, and the premise the session argued from was false.** Both halves are worth keeping,
because the second is the transferable one.

**The rule half, which stands on its own: a shortcut taken four times is indistinguishable from a rule that
never applied, and only the rule's own text and its stated exceptions can tell them apart.** Four commits in a
row look like a practice, and **nothing about them says whether anybody decided.** That is what makes reading
precedent as permission the natural mistake rather than a careless one - the count is exactly what makes it
persuasive.

**The measurement half, which is why this is here rather than in a correction: the query answered a
neighbouring question and returned a well-formed wrong answer.** The evidence was *"its last four changes were
single-parent commits"* - `30e9e42c`, `611f2ce3`, `97bc6d21`, `8770ca5e`, all four real, all four genuinely
single-parent. **But "single-parent" and "directly on `develop`" are different questions, and every ordinary
commit on a properly gated branch is single-parent**: the merge commit carries two parents, the work on the
branch carries one. So a parent-count test cannot separate *committed straight onto `develop`* from *committed
on a branch that was then merged*, which are precisely the two cases the rule distinguishes. **The test that
answers the real question is first-parent membership of `develop`** - `git rev-list --first-parent develop` -
and by it **all six of that file's changes came in through merged branches** - `30e9e42c`, for one,
as the second parent of `0bdd8e6d`. **The cited evidence was evidence of the rule being followed.**

**And a SECOND wrong answer came from the same real SHAs by a different route, so keep the two apart.** The
parent count asked a *neighbouring question*; the four-commit window asked **the right question of the wrong
sample**. The search stopped at four and missed a fifth, `60922acd`, **the most recent of all** and also on a
merged branch. **A window that stops short of the newest instance is how a history that contradicts you
reads as a history that supports you** - and it needs no faulty query at all, which is why it survives
fixing the query. Ask what the sample excludes, and make the newest instance the one you check first.

**The cost argument also runs backwards from how it was first framed.** *A markdown-only change cannot move
any of the four gates* was offered as a reason the gates were unnecessary. It is the reason they are **cheap**:
**a rule whose cost is one three-minute gate does not need an exception.** The expensive rules are the ones
worth arguing about.

**The half that pays forward: say in the commit message which route you took and why.** The landing that
followed this ruling records that it went through the gates **by ruling rather than by precedent** - so the
next reader of that file's history meets a commit that is not another silent instance, and can tell which were
deliberate. **A judgement that leaves no trace reads as a measurement.**

## Provenance is never in the name

**An identifier records what something was called, never how it came to be.** Three agents made this mistake in one afternoon: one inferred an agent from a branch-name substring, then a commit's *location* from its author string; another was about to infer authorship the same way and checked first. Each time the identifier looked like evidence of provenance and was in fact evidence of **configuration** - a branch name somebody chose, a `user.name` somebody had or had not set.

The trap is worth naming separately from "verify before believing", which everybody agrees with and nobody applies under time pressure. This one names the specific move to distrust, and it predicts every case above: **a provenance question is precisely the one whose answer is not in the output already in front of you.** The real answer costs a command; the name is right there.

A related failure with a different cause, worth keeping beside it: **if a number is going into a decision, compute it - do not read it off a list.** One of the corrections above was a miscount of output the agent had already produced and was looking at. No amount of checking-before-believing catches that; only counting does.

## Working under an open approval card

**Commit a specification revision immediately, before anything else.** Not after the next edit, not once the card is answered - immediately.

The reason is an observed incident. A committed requirements document was reverted **in the working tree** while its card was open: 198 lines back to 154, an entire revision gone, while the commit sat intact in history. `git diff --numstat` showed **8 insertions against 52 deletions**, and every one of the eight "added" lines was the pre-revision version of a line the agent had changed - a pure revert of its own work, with nothing of anyone else's mixed in. The working hypothesis is that the dashboard restores the snapshot it captured at request time when a verdict is recorded. It is a hypothesis, not a diagnosis: a second document that was *not* edited after its card was raised came through its verdict unchanged, which is consistent with the hypothesis but cannot discriminate, because there was nothing for a restore to undo.

**Committed, this is a recoverable event: restore from HEAD and reapply the feedback.** Uncommitted, it is lost work. The one agent this happened to survived it by having committed first out of habit, and nobody should have to be lucky.

**Check the document before your next edit, and restore from HEAD rather than re-typing if it has moved.** Re-typing reconstructs from memory what the tree already holds exactly.

**A document under an open card stays stable.** The person reading it should not have the text change under them. If something in it is genuinely wrong, fix it and say so in the same breath - never silently. New material waits for the verdict, which is also the safer order: anything added under an open card is exactly what a restore-on-verdict would discard.

Two things about cards that are noise rather than hazard, recorded because a careful reader got them wrong and warned the user about superseded content nobody was ever seeing: **a card renders the file rather than a copy**, so several cards on one document all show the same current content. And **an agent cannot delete a pending card** - the tool refuses. Only the user clears those, so an agent that finds a stale one reports it rather than trying.

**That last fact has a consequence the rule above does not cover, and it turns "fix it and say so" into a one-way door.** An agent that amends a document under a pending card cannot clean up after itself. The card is left pointing at a snapshot that no longer matches the file, the tool refuses to withdraw it, and the only exit is the user rejecting a card they never disagreed with. **Disclosure does not close it** - disclosing an amendment tells the user it happened; it does not restore the record, and if they approve the card as it stands, the durable verdict names text that is no longer there.

So the question to ask before amending under an open card is not *"is this worth changing?"* but ***"am I willing to ask the user to reject their own card for it?"*** Sometimes the answer is yes: a guard requirement with a hole in it, in a specification about that exact kind of hole, was worth the interruption. Usually it is no, and the material waits for the verdict - which is also the safer order, because anything added under an open card is exactly what a restore-on-verdict would discard.

## Specification bookkeeping

**CLAUDE.md is where a new rule is written; this document is where it settles.** A rule learned the hard way goes into CLAUDE.md first, with enough of its reasoning to be believed, because CLAUDE.md is loaded into every agent's context and steering documents are not. **Moving the narrative down here is then a step in the same pass that raises the approval card**, leaving CLAUDE.md the imperative and a pointer.

That ordering is not a preference; it is what the alternative cost. The original split asked an agent to put the reasoning somewhere nobody loads at exactly the moment they had just been burned and wanted the next agent spared - so the narrative stayed in CLAUDE.md, and within a day two sections had regrown their reasoning and two agents had written the same rule into both documents within an hour of each other, neither knowing. **It works for stable rules and fails for fresh ones**, which is the same lesson as the identity rule: a rule that cannot be got wrong beats one depending on vigilance at the moment nobody is looking.

(This paragraph took three passes to arrive, because the rule that fixes the split was itself held by the mechanism it exists to fix.)

This repository plans and tracks work in `.spec-workflow/` — steering documents, specifications, approvals, implementation logs — before implementation.

- **A set of files added or removed under `.spec-workflow/` is committed immediately, in its own commit.** Not left uncommitted, not bundled with unrelated changes. This covers the logs `log-implementation` writes, not only the documents you author by hand.
- **When a requirements, design or tasks document is approved through the dashboard, commit it and its approval-lifecycle files at that point** — even though approval changes or removes files rather than adding a fresh set.
- **Approval is granted through the dashboard and nowhere else.** Verbal approval is never accepted, including from the user in chat and including from another agent relaying that the card has cleared. Poll the card and read the verdict before proceeding.
- Use a short, descriptive commit message in the style already in the history ("Bumped approvals.", "Added gRPC core communication specs: requirements and design documents.").

## Checking that a specification's tasks cover its requirements

**Extract every requirement reference from the tasks document, list every acceptance criterion in the requirements document, and diff the two sets.** It takes a few lines of script and it finds what reading does not: on one specification it surfaced three requirements no task claimed, and the author's own account was that none of the three would have been found by re-reading. A second agent arrived at the technique independently on a different specification and found a requirement with four live sites and nothing pointing at it.

**Run it twice, and the second run asks a different question.** Before implementing, against the tasks, it asks *did anybody claim this?* After implementing, against the finished code - tracing each requirement to a file and a string rather than to a task's promise - it asks *does the code show this?* Those are not the same question, and the second has found a gap the first did not: a requirement claimed by a task, implemented in the design and in the module-authoring guide, and missing from the one document an adopter actually reads.

**The half that is easy to miss: an unclaimed requirement is not necessarily a gap.** Some requirements are properties of how a document is written rather than work any task performs - *the in-scope set follows from the criteria rather than a list*, *one task per module rather than per canvas*, *the modules needing no prerequisite come first*. No task can claim those, because no task does them; the document does them by existing in that shape.

So the diff's output splits in two, and **only one half is a defect**:

- ***No task claimed it*** - a real gap. Add the claim, or add the task.
- ***No task can claim it*** - satisfied by the document's own structure. State that explicitly, in the tasks document, naming the requirements it covers.

The two look identical in the diff's output and are told apart only by reading the list. Ten unclaimed requirements on one specification were seven of the first kind and three of the second. **Saying which is which converts the diff's answer from a silence into a statement** - and a reader who meets an unexplained silence will either add three pointless task claims or conclude the diff is noisy, and both are worse than the minute it takes to write the sentence.

**The acceptance criteria are the complete list of visible changes the work may make.** `centralized-selection`
first listed, case by case, which tests were allowed to change; three tests changed in one day for three
different reasons and **each needed its own dashboard card before implementation could land**. The user ruled
the general form instead: *a test may change only where it pinned behaviour a named acceptance criterion
changes; it names that criterion and changes only what the criterion changes.* An enumerated exemption list
grows once per discovery and blocks implementation on an approval each time; the general rule is answerable
from the approved text without amending it. **Keep both halves - name the criterion, and change only that far.**

**`selection-after-drag` is the one specification that does this, and it is worth copying by name.** Its
tasks document says outright that criterion 6.3 is the one no task can claim, and why. Nine completed
specifications were diffed on 2026-09-24; it is the only one whose silence a reader does not have to
reconstruct, and reconstructing it is precisely the work the diff exists to save. The other eight were clean
but mute.

**The extraction has a repeatable blind spot in this repository, and a mandated check with a reproducible
blind spot is worse than an ad-hoc one** - because the wrongness comes back the same every time and therefore
reads as confirmation. `backend-project-decomposition`'s requirements document carries a numbered **options**
list (*"Options: (a, recommended) … (b) …"*) alongside its acceptance criteria, and a regex that takes
`^\d+\.` under a `### Requirement` heading cannot tell the two apart. Criteria 6.1, 6.2 and 6.3 each appear
twice there - once as a criterion, once as an option - so that specification's criteria count is inflated and
those three identities are ambiguous. **Read the unclaimed list rather than only its length**, and where a
requirements document mixes options into numbered prose, say so in the diff's own report.

**And the second pass finds a class the first cannot: a criterion met by a SUBSTITUTE rather than by the thing
it names.** `backend-project-decomposition` 6.3 requires `jb inspectcode` "run and shown clean"; the tool
cannot evaluate this SDK's projects at all, so a committed guard - `NamespaceProviders.Tests` - was ruled the
substitute, and that test's own docstring records the ruling. The criterion is satisfied and the named
instrument was never run. **Re-reading the documents would never surface that, because both documents are
telling the truth** - only tracing the criterion to a file and a string does, which is the whole reason the
second run asks a different question.
## Bugs found during implementation or verification

**Every bug found leaves a guard behind, so it cannot silently return.** Whether it surfaced from a failing test, a code review, or a manual pass.

- **Preferred: a unit or integration test** in the existing projects (`src/backend/*.Tests` for C#, `*.test.ts(x)` under `src/client/src` for the client), written to fail before the fix and pass after it.
- **If it can only be reproduced through a running app**, add it to `tests.md` at the repository root as a step-by-step check — preconditions, actions, expected result — naming the spec and task it came from.

**A person signs the Tester in; the agent then runs the pass.** The app opens on a sign-in form and **no agent can type the credential** — entering a password into any field is prohibited by an operating constraint that explicitly survives being authorised, and which this document cannot lift. The user's ruling permitting the checked-in `admin`/`changeme` placeholder was genuine and remains their preference; it is simply not executable by an agent, and a project file cannot make it so.

So the arrangement that works: the Tester starts the build and reports it ready, a person signs in on that running instance, and the Tester executes every check in the session that follows. **Do not re-derive this.** Two agents reached opposite conclusions from the same instructions and neither was careless — one followed this document, the others followed the higher constraint — and the standoff froze eight checks for a day. The conflict is real, the higher instruction wins, and the answer is a person at the keyboard once rather than a better argument.

Recording *"still cannot be run"* **with the reason** is a result. Silence is not — and 43 of 72 entries currently carry no outcome at all.

**One signed-in session is the capacity, so testers do not parallelise.** `App.tsx`'s `Gate()` returns `LoginPage` whenever `isAuthenticated` is false, and there is no route to a project, diagram or canvas that does not pass it — no dev auth branch, no environment flag, nothing. **71 of 72 entries are behind that form**; the single exception needs no UI at all. Entry-level ownership prevents collision, it does not add capacity. A second Tester adds nothing until a second signed-in instance exists or the sign-in form is gone. this was written down, and recorded contradictory verdicts for the same kind of check; **a check recorded `pending` because of the sign-in form alone is a check that was not run.**

## Verifying that a test actually tests something

**A guard is accepted when it has been seen to fail for the right reason, never on a green run.** Sabotage the thing it is supposed to catch, watch the named test fail, and revert. Three ways that verification has come back green for the wrong reason, all found in practice:

- **A sabotage written with LF newlines does nothing.** The working tree is CRLF, so an LF-pattern replace matches nothing, changes nothing, and the suite passes — reading as robust code rather than as a sabotage that never ran. Assert the pattern matched *before* writing the file.
- **Sabotaging two call sites can pass the very test you are validating.** Making a module's render unfiltered in *both* its change path and its `UpdateView` keeps the two consistent with each other, so the interaction test stays green; the actual bug shape is one path alone. The obvious sabotage reads as a green light.
- **An assertion can be vacuous in a way the sabotage does not reveal.** A zoom test on a zoom-at-cursor canvas passes even with zoom dropped from the key entirely, because that gesture moves the origin too and re-fires the report regardless.

**A tree-walking guard is run against the real tree, not only its fixture**, anchored on two independent markers, and asserts it found a plausible number of files. One such guard silently found nothing because a folder name repeated higher in the tree; another indicted six compliant files by matching an import every module writes. Both passed their fixtures.

**A guard that was right can be made wrong by its own repair, and it looks most trustworthy at that moment.** The three failures above are guards that were never right. This is different: the drag-cost guard was correct, a repair changed how it measured, and the new measure passed over the very defect it guards. Its estimator was swapped from a whole-window minimum to a per-frame minimum to *harden* it against contention; but `pointermove` is continuous-priority, React may flush a frame's render after the handler returns, and the per-frame minimum finds the one frame that escaped - it read 1.11 over sabotaged code, a passing guard over the defect. The swap kept every assertion, the tolerance and the test's name; nothing about it looked like a new guard except that it was one. **Changing how a guard measures makes it a new guard, whose predecessor's sabotage-pass does not transfer** - and the repair context is where vigilance is lowest, because a green reading during a hardening is the most welcome reading there is. It was caught because the sabotage was re-run *because* the estimator changed, per the rule above - so this is evidence guard-must-fail earns its keep at the moment it feels most redundant, not a gap in it. The operational form: **any change to how a guard measures re-runs the guard's sabotage before the change is trusted, and records the new failing reading beside the old** - 17.91 beside the original 16.97 - so the new guard's strength is checkable by the next reader rather than asserted. (Developer 1, drag-cost guard, 2026-09-06.)

**And a guard can be unfalsifiable on the platform it runs on, which is neither of those.** A rule about path casing was guarded behaviourally: read a package id in different casing, expect the description back. **Under sabotage it passed - NTFS is case-insensitive, so the reader finds the file whichever casing it builds.** It was green whether or not the code was right, and it **could only ever have failed on a filesystem we do not run**. The fix was to assert against the constructed path rather than the observed behaviour, exposing the path-building member for exactly that and documenting why. **The distinction from the two failures above is worth keeping: there the test framework decided whether the defect could appear - React batching a window away, jsdom detaching an input, a missing `StrictMode`; here the operating system did.** *The platform decides whether the test can fail, not the test.* So when a rule is about a filesystem, a locale, a clock or an encoding, **ask which layer would have to change for the test to fail** - and if the answer is the machine rather than the code, the test is a description, not a guard.

**A fixture that isolates state also blinds the suite to that state's lifetime, and the isolation can be entirely correct.** Twenty-four integration flow classes here replace `IProblemStore` with a temp-rooted one, and the reason recorded beside them is a good one: a cache file naming a temp folder outlives the run that made it, later runs walk that dead root, and **605 such files once cost the suite 22,591 warnings and a hang**. **So the suite starts with an empty problem store by construction - and therefore cannot, even in principle, observe an entry that survives a restart.**

  That is exactly the defect it went on to miss: a **stale cached problem with no `stale` marker**, sitting beside three siblings that wore one, asserting a diagram type was unknown on a backend whose own startup log had just announced discovering it - **and surviving a rebuild and a restart.** Three sessions spent an evening diagnosing a module that was working.

  **This is not the blind-detector shape and the difference is the useful part.** There the instrument could not see a *form* of the thing; **here the fixture removes the thing before the instrument runs.** And the fixture is right - reverting it re-creates a worse problem - so **there is no bug to fix, only a class of defect the suite has traded away.** The remedy is therefore not to change the fixture but to **know which class it forfeits and cover that elsewhere**: anything whose defect *is* its lifetime - caches, cross-restart state, files a run leaves behind - is invisible to a suite that starts clean, and belongs in a written manual step instead. **Ask of any isolating fixture what it makes unobservable, because a suite cannot report its own blind spot** - it can only pass.

**Every failure above is a guard green when it should be red. The opposite exists, its damage is different in kind, and it is the one that spreads.** A guard that fires when nothing is wrong costs nobody a defect - it costs the *next* guard its authority. **A specification's tripwire read "if a task needs a change outside the module folder, the design was wrong"; the document's own first task listed the backend solution file, because a new project must enumerate itself there.** So the rule contradicted its own document, and it fired on the first developer to reach a second registry - the client's proto-generate script - which is the same shape: a module naming itself in a list. **The rule meant to catch the module's *concepts* leaking outward, and a registry entry is not a concept leak.** It was restated as *if a task needs to teach anything outside the module folder about the module's own domain, the design was wrong* - **testable against a change, where the original was testable only against a path.**

**What makes this worth its own entry is the failure mode, not the mistake.** The developer who met it argued rather than stopped, and the rule was fixed within the hour - **but that was a property of that developer, not of the rule.** The next one stops needlessly, or learns that this class of warning is usually wrong and stops reading it. **A guard that cannot be trusted to be right is worse than a guard that cannot fail, because the first hides one defect and the second trains people out of the habit.** So a guard needs both halves demonstrated: **seen to fail against the thing it guards, and seen to pass against the nearest thing it must not catch.** The second half is the one nobody runs.

**A detector encodes one form of a thing, so the absence of that form reads as the absence of the thing.** A guard's canary has two shapes, and a survey that knows only the first miscounts the second as missing: a `> N` population floor (`expect(files.length).toBeGreaterThan(20)`) and a named-member presence assertion (`Assert.Contains(scanned, … "SharedDocumentReader.cs")` — a file known to belong in the walked set must still be in it). Both pin the population non-empty and real; the named-member form is the stronger, because it also survives a population that is non-empty but *wrong*. A regex hunting canary-less guards that matched only `NotEmpty` and `Count == 0` reported every named-member canary as absent — and the guards it indicted were all sound.

**A floor belongs on structure, never on content.** A population floor counted over what documents *say* — how many links they carry — is itself a guard over prose, and fails the first legitimate edit that removes some: `DocumentationLinksTests`' `checkedHtmlLinks >= 5` was written to stop a vacuous pass and, on its first day, failed a correct unlink that left the catalog two links (2026-09-11). Put a floor on what the guard *walks* — files, modules — and ask whether a *pattern* still matches of a fixed sample, which does not change when the documents do.

**The tell is a count that moves when you sharpen the detector.** That hunt's figure swung 15 → 0 → unreliable across three refinements of one pattern, and the right move at the third was to stop refining and read the classes by hand. *A count that changes when you improve the instrument is measuring the instrument.* It is the sibling of *Checking that a command answered your question* below: a detector that sees only one form is a command that was never asked about the other. The day this was written logged at least five of the shape across four sessions — a hook-name grep blind to registration-through-composition, a filename match blind to `C4Export.Tests.cs`, a `Result:`-only search blind to a `**Verified**` line, a `DefaultBudget` grep wrong in both directions at once, and the `NotEmpty`-only canary detector itself.

**And a detector blind by construction produces surprises that look unrelated, which is how it survives.** A per-area dependency measure excluded qualified type references, so partially qualified ones (`Problems.ProblemBroadcaster`) were invisible to it; five separate extractions each hit a "surprise" qualified-name fix, **and nobody connected them, because no single area had enough of them to look systematic.** The pattern was hidden *by* being distributed. So the diagnostic is: **recurring surprises of the same shape in different places indict the instrument, not the places** — and the confirmation, when it came, was that the measure had been blind to the dependency in the very file whose dependencies were the open question. Prefer an instrument that resolves rather than pattern-matches: the compiler cannot have this class of blind spot.

**A detector can also match *itself*, and that failure has the opposite polarity and a different tell.** A process scan for held work kept returning three PIDs, and the PIDs changed every run - because each probe's own shell wrapper carried the search pattern in its command line, so the scan was counting its own instances. Building the pattern by concatenation, so the literal never appears in the probe, returns empty. **The blind detector under-reports and its tell is a suspiciously clean zero; the self-matching detector over-reports and its tell is a count that will not sit still across runs.** Both are the instrument answering about itself rather than about the tree, so the question to ask is the same one: *what in this result is the measurement rather than the thing measured?*

**An edit to a guard is invisible twice over, so read the guard and not the diff stat.** A test written
to pin a defect is the one kind of file where a weakening passes by construction: the suite stays green,
and a gate that never ran notices nothing either. **"The tests are green" answers identically whether
the guard still guards anything.** So when a change touches such a test, read it and say which defect
each part still pins.

**Two instances, both from 2026-09-22, both from edits that landed on `develop` outside the gate**
(found and measured by Developer 3, whose guards they are):

- Three commits added cancellation tokens and async file calls to `C4DocumentStore.FailedReload.Tests.cs`,
  `CausalLoopDocumentStore.SelfWrite.Tests.cs` and `TimelineDocumentStore.SelfWrite.Tests.cs`. Developer 3
  diffed **the assertions** rather than the files: none had changed, and the guards still guarded.
- In `AdpFileWriter.ConcurrentSaves.Tests.cs` a later edit changed `first.Wait(Patience, token)` to
  `first.WaitAsync(...)` inside a `finally`. **`WaitAsync` returns a task and does not block; the result
  is discarded, the method returns `void`, so there is no `CS4014` and the compiler says nothing.** The
  assertions are untouched and the test stays green - while its isolation is gone, because a save still
  running can write into a folder teardown is deleting.

**The second instance is why "read the assertions" is not enough: the damage arrived through the
WAITING code.** Read what the test does to reach its assertion - what it waits for, what it holds, what
it tears down - because a guard can be hollowed out without any assertion changing.

**An ungated commit and a guard edit are each survivable; together they are the case where nothing in
the system would have told anybody.**
**A guard that has already caught many instances of one mistake is not redundant, however much it looks it.**
Developer 1's task 26 red: a new host-booting test class omitted the problem-store temp-root override and
`ProblemStoreIsolationTests` caught it - **the twenty-third instance of the same mistake**, a leak that writes
into the developer's real profile instead of a test root. The argument for deleting such a guard is always
that the mistake is understood now; **the count is the argument against, and only a guard that keeps its count
can make it.** Read this beside the seen-to-fail rule above: one says a guard must be proven, this says a
proven guard must not be deleted because its mistake has started to look obvious.
### Guards that cannot fail, and the seven ways it happens

**Every guard below was green while the thing it existed to catch was happening.** None was vacuous in the
usual sense - most asserted something true. **The defect is always the same shape: the guard's subject is not
the thing that can break**, and the seven cases differ in how the subject came to be wrong.

**1. A guard whose subject the code under test gets to choose cannot fail.** Two guards asked *is the highlight
applied?* and found their subject by taking **the first node carrying an inline stroke** - which was the
wrapping `<g>`, exactly where the defect had put the paint. The paint was there, the value was right, the
assertion was true, **and the shape on screen did not change colour**, because an SVG child that states its own
stroke ignores an inherited one. **This is not the vacuous-test case: a vacuous test asserts nothing, while this
one asserted something true and irrelevant.** The subject was selected by a property the defect also satisfies,
so **the search and the assertion were the same claim made twice.** The fix is two independent moves and only
the pair works: name the subject **structurally** (the drawn shape), and assert what it **computes** with the
module's stylesheet loaded rather than what the library **states**. Proven by sabotage: put the paint back on
the group and eight cases fail. **What it cost to find: nothing in the four gates - somebody opened the app and
looked.**

**2. Sabotage the call site, not only the thing it calls.** A helper learned to mark its own scratch file, and
its first two guards called that helper **directly, passing the argument themselves**. Sabotaging the *publish*
- one argument dropped at one call site, the regression a later reader would actually cause - **left both guards
green**, and the failure record would have reverted to its misleading wording with every test passing. **A unit
guard cannot see a wiring regression, and the wiring is where the reader's evidence comes from.** The third
guard drives the real save.

**3. A positive assertion cannot detect a surplus.** A planted defect rendered resize handles for every type
that declares user sizing. The tasks document said the module's own suite would report it. **It does not:** that
suite presses `[data-resize="right"]` and never asserts the horizontal handles are **absent**, and the right
handle still exists when two more appear beside it. **A suite that presses a handle cannot report a handle that
should not exist.** So **when a defect ADDS something, only an absence assertion detects it** - and a document
must name the **detector** rather than a **companion**: *the module's suite passes unchanged* is true, worth
keeping, and holds in both worlds, which makes it no evidence about the change.

**4. A timing guard has its own load as a second subject**, and the remedy differs by what the guard asserts,
so a flat *avoid timing in tests* would be wrong about three of these four. **Ordering** - *the delete has not
finished while the save holds the turn* - cannot fail wrongly but can go **vacuous** if the pool never starts
the contender, so assert the evidence that the contender **ran**; and **say in the comment that the line doing
so is load-bearing**, or the next reader deletes it as redundant. **Latency** - *a slow diagnostic does not hold
the save open* - needs a clock, and is safe through its **ratio**: a 100-200 ms budget against a 3-5 s
assertion. **An async effect arriving** - *the late line appears* - should be released **deterministically**
where possible, and where not, **report what arrived instead** on timeout, or LATE and SWAPPED are
indistinguishable. **A race that needs load** - the concurrent-save guards - is one-directional: more load means
more chances to catch a defect and never a false pass, so leave those alone. **And a negative asserted from a
window cannot fail wrongly but can PASS wrongly**, which is the worse direction for a must-not guard: make
*never* observable by asserting the thing was entered, then asserting silence.

**5. At one subject a rule is a second copy of the data; at two it pays for itself.** A test that replaced a
process-wide logging pipeline starved every capture running beside it - **seven failures in three unrelated
classes, from a project that passed 117 of 117 alone.** The tree-wide rule was declined because **exactly two
places assign that pipeline**, one of them correct production code: a rule with a single subject is a copy of
the fact rather than a check on it. **So the trigger is written down instead: when a SECOND test needs to
replace the pipeline, give it the helper rather than the freedom.** And the fix was not a test collection -
**membership is the rule nobody keeps, measured at eighteen of nineteen classes never joining** - but a
replacement that carries the shared sink beside its own. **The scattered signature is its own lesson: seven
failures across three unrelated classes point everywhere except at the cause**, which is why the guard lives in
the only class that can cause it.

**6. An exact-count canary cries wolf, and this one is ours.** The gate's self-test pins its own case count so
that a case which silently stops running reddens. **It fired twice in one afternoon as a red with nothing
wrong** - once when thirteen cases were added, once when four more were - and the same criticism had just been
used to remove a case count from `docs/guards.md`'s prose, **so the number was moved rather than removed.** The
question is what the property actually is: if it is *no case was silently skipped*, **a floor plus a check that
no two cases share a description asserts it without rotting on every addition.** If it really is *the count is
exactly this*, it will keep crying wolf - **and a guard that cries wolf gets edited to agree rather than
investigated**, which is how a canary becomes a formality. Changing it inside the branch that trips it would
mix two decisions, so it is recorded here and changed on its own.

**And the counterexample belongs beside the criticism, because it arrived hours later on the same branch and it
is the one change the pinned count is good for.** Two comment-only edits were made to the gate's own scripts -
no behaviour, no cases - and the selftest returned `cases=167 wrong=0 expected=167`. **Had a comment
accidentally swallowed a case, the pinned count is the ONLY thing in the run that would have said so**: every
remaining case would still have passed, and a floor would have been satisfied too. So the criticism above is
narrower than it first reads: **an exact count cries wolf on ADDITIONS, and earns its keep on edits that are
not supposed to change the count at all.** A criticism carrying its own counterexample is much harder to
misapply than one without - the version without it invites somebody to delete the count on the next red.

**7. An emptiness check is satisfied by the failure mode, so floor on what SCALES with the work rather than
on what merely exists.** A harness built to time a diagram's `UpdateView` asserted `Assert.NotEmpty(baseline)`
to prove it was not timing an empty session. **It passed at one delta - which is also exactly what a document
that failed to parse produces.** The report would have read *2 ms* either way, with the same confidence and the
same green; the run was in fact sound, but only because its author looked inside the delta rather than trusting
that not-empty meant loaded. **The fix is the general form: the floor moved from the delta count to the ELEMENT
count**, which a document that did not load cannot satisfy. Distinguish it from clause 1 - the defect does not
choose the subject here - and from clause 3: the assertion is present and positive, and the surplus is not the
problem. **What is wrong is that the quantity floored does not grow with the work**, so the smallest passing
value and the failure mode are the same value. `docs/guards.md`'s own `KnownFloor = 25` against 35 actual paths
is the shape done right: it floors on the quantity that scales with the document, so an emptied document fails
while an added row needs no edit. (Developer 3, 2026-09-23, found in a measurement built for something else.)

## Running the backend tests

The test projects run on xUnit v3, which uses Microsoft.Testing.Platform rather than VSTest. Two consequences:

- Run them as `dotnet test --solution EtAlii.Adp.slnx` from `src/backend/`. Passing the solution positionally is rejected under this runner.
- `src/global.json` carries the `"test": { "runner": "Microsoft.Testing.Platform" }` opt-in the .NET 10 SDK requires; without it `dotnet test` refuses to run any test project.

Each test project is therefore an executable (`<OutputType>Exe</OutputType>`) — a v3 project hosts its own tests, and new test projects need that too.

**That makes the suite sensitive to Windows' 260-character `MAX_PATH`.** MSBuild's `Exists()` silently returns false above 260 characters, so in a deep checkout the SDK skips `_CreateAppHost`, `apphost.exe` is never produced, and — since a v3 test project's apphost *is* its test host — `dotnet test` reports `Zero tests ran` project after project instead of failing the build. **Read `Zero tests ran` as a broken build, never as an empty suite.** `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled` must be `1`, worktree names must be short, and `src/Directory.Build.targets` turns the failure into an explicit `ADP0001` error rather than a cryptic `MSB3030`.

**A verdict's dangerous input is missing, not wrong.** *(Developer 3's finding.)* A wrong value is compared and fails; a missing one is often never compared at all. The natural form of the rule above - `grep -q "Zero tests ran" <log> && refuse` - passes when the log is missing, because `grep` exits 2 on a missing file and the `&&` never fires. Two scripted gate chains on this board had exactly that, and in both the verdict reached green by falling through rather than by being earned. **And a chain run by hand is not exempt because a person reads the result** *(Developer 1's words)*: `grep -c "Zero tests ran"` prints `0` for an empty log exactly as for a good one, and that `0` is the number read as the good sign - a landing made by eye was protected only by a positive check its author carried in their head. A person reading output is a verdict with no refusal state. **So check it positively: require the log's `total:` above zero and `failed: 0`, so a missing or truncated log refuses rather than passing unread.** Write the verdict to start refused, with success assigned in one place behind every positive condition, and compare statuses as strings - `[ "" -ne 0 ]` is an error, and an error is false inside `if`. **Test it by deleting the log before the verdict reads it**, alongside a real log as the case that must pass.

**Judge every gate by an exit code captured into a variable before any SUBSEQUENT COMMAND - not merely before a pipe.** In a pipeline `$?` is the last command's status, so `dotnet test … | tail` reports `tail`'s success and a crashed or zero-test run reads as green. A zero-test run does exit non-zero — 5 for zero tests, 8 for a filter matching nothing — so the exit code carries the information that grepping the output destroys. `.github/workflows/build.yml` embodies this: every gate step is judged by its exit code and nothing greps output.

**AND A TRUNCATING COMMAND IN YOUR OWN PIPELINE PRODUCES A WELL-FORMED WRONG ANSWER** - `head`, `tail`,
`-m`, `--max-count`, a default page size. **A count taken from a command with a limit in it is a count OF
THE LIMIT unless you have checked otherwise**, and it is worse than a wrong pattern because the output
looks complete and the number is plausible.

**Twice in two days, from opposite directions, both in the author's own pipeline.** A `head -5` reported
**five** warning assertions where there were **seven**, and the five were reported to the board as a
measurement. And a probe piped into `head` reported **exit 0** where the script had returned **3**,
because `$?` is the last command's status - **read by somebody who had the exit-code clause above in
front of them at the time.** One hid a count, the other hid a status.

**The second is the more dangerous and it is worth saying why: the pipe made a CORRECT script look like a
FAIL-OPEN one.** The failure mode of the check was to **accuse the thing it was checking**. **A check that
produces false accusations is worse than one that is merely silent**, because somebody acts on it - and
what they act on is usually the innocent party, which is where the time goes.

**A pipe is not the only way to lose the status and it is not the common one.** A trailing `; echo "EXIT=$?"`
does identical damage and happens far more often - **precisely because it is what somebody adds in order to SEE
the exit code.** `$?` is read while the echo is being assembled, the echo then succeeds, and **the echo's own 0
becomes the wrapper's status.** In every instance below **the echoed value was right and the notification was
wrong**, which is the worst possible arrangement: the correct number is sitting in the output that nobody
re-reads once a green notification has arrived.

**Four instances on 2026-09-23, in four sessions**: a gate notification reporting exit 0 over a log saying
`RESULT=gates-red ( format=2 )`; a format run notified as 0 whose captured `FORMAT_EXIT` was 2; one from that
morning; and a gate run of mine that **printed `WRAPPER_EXIT=1` and was notified as exit code 0**, because a
`tail` of the log ran last. **Three of the four were found by somebody CHECKING another's report rather than re-reading their own** - the fourth instance above surfaced while verifying the other three, one session found its own by reading a peer's, and another found its second by correcting its first. **Nobody found their own by re-reading it**, which is a better argument for cross-checking than the count is. *(Architect 3's observation, about a set it collected.)*

**The guidance against this was already written and already correct**, which is the
argument for the remedy rather than against the rule.

**So make it structurally impossible at the point of invocation: end the command with the status you captured.**

    cmd > "$LOG" 2>&1; RC=$?; echo "EXIT=$RC"; exit $RC

The notification then agrees with the log and nobody has to know the rule. **Same shape as the git-identity
remedy: set it once where the command is written and it cannot be got wrong afterwards, which beats any rule
depending on vigilance at the moment nobody is looking.**

**And the diagnosis matters as much as the fix, because the obvious culprit was innocent.** `gate.sh` is
correct - **every failure path exits non-zero, including the `gates-red` path, and the green path's implicit 0
is right** - so a tooling change was drafted and correctly abandoned. **The fault is in how the runner is
invoked, not in the runner**, and a fix applied to the script would have left every hand-written invocation
still lying. *(Architect 3, who diagnosed it and withdrew its own proposed tooling fix.)*

**A note on the short form**: CLAUDE.md's summary of this rule still says *before any pipe*. **Read it as any
subsequent command** - the narrower wording points at the rarer cause, and that file is the user's to change.

Set `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` before gating; concurrent MSBuild nodes have been diagnosed as the cause of 0xC0000005 host crashes on this machine.

**The four gates, all of which must exit zero before a worktree merges into `develop`:**

1. `npm test` (from `src/client/`)
2. `npm run typecheck`
3. `dotnet format style --verify-no-changes --severity info` (from `src/backend/`)
4. `dotnet test --solution EtAlii.Adp.slnx`

CI runs the same four on every push and pull request, but that is the net under the discipline rather than a replacement for it: a red pipeline after a merge means the local step was skipped, and the merge was wrong.

**Running the client suite in a fresh worktree:** `src/client/src/generated/` is both gitignored and tracked, so a fresh worktree holds only the tracked stubs until codegen runs. `npx vitest` bypasses the `pretest` hook that runs it, and the suite then fails with dozens of `Failed to resolve import "../generated/..."` — which looks exactly like a broken build and is not. Run `npm run generate` once, or `npm test`, which runs it for you. Re-running `npx vitest` alone never helps.

## Vendored example data

**Diagram modules are tested against real published example data, not hand-written toys.** The point of vendoring real documents is that they exercise input shapes the author never had in mind - a fixture tests what its writer thought of, which is the half already covered by the code.

Agents may download it, under conditions that are not negotiable.

- **The licence must be permissive.** CC0, CC BY with the attribution carried, the W3C Software and Document Licence and similar. **Share-alike licences are not** - UNESCO's thesaurus was rejected on exactly that ground.
- **Download the licence file itself and keep it beside the data.** Not a link, not a line in a readme naming the licence: the actual file, vendored next to the documents it covers, so the terms travel with the data for whoever finds it later. Name it `LICENSE.md` whatever extension the server serves it under - the family's own provenance guard requires that name - and keep the text verbatim; only the filename follows the house convention.
- **Verify the licence again at acquisition**, rather than trusting what a specification recorded. Terms change between writing a spec and fetching the file.

**Verify from the data, not from the page.** Read the dataset's own licence statement - the `cc:license` triple, or whatever the format's equivalent is - rather than the download page around it. The STW thesaurus was vendored after its page turned out to carry **three conflicting signals at once**: prose saying CC BY 4.0, a `rel="license"` link pointing at ODbL, and a by-nc-sa badge. Two of the three are share-alike and would have disqualified the source. An agent reading the badge, or the link, would have reached a confidently wrong answer in either direction. The data's own statement settles it.

That is the same trap as *Provenance is never in the name*, one level up: the page around a file records how somebody described it, not what it is. A licence question, like a provenance question, is answered by the artefact rather than by its packaging.

**Say what the examples do not demonstrate.** A vendored corpus rarely covers every shape a requirement asks for. Record the gaps in the folder's readme, with the reason each candidate source was rejected, so the next reader meets the omission before wondering about it.

## Keeping documentation true

A change that makes a document untrue fixes it in the same change. Four artefacts carry that duty explicitly.

- **`docs/diagrams.md`** catalogs every diagram type ADP could support, and its row moves with the type's state. **State** carries the matching icon: 💡 identified, 📝 specified, ⏸️ to-do, 🛠️ work-in-progress, ⚗️ prototype, ✅ implemented. **Origin** is a MIME-type-style `<architecture-or-vendor>/<diagram-type>` tag (`uml/class`, `c4/context`); for notations with no owning standards body, the tool or author most associated with it serves as the vendor (`freeplane/mindmap`).
- **`docs/creating-a-diagram-module.md` and `docs/creating-an-editor-module.md`** — a change that moves a touch point either names (a renamed seam, a moved file, a changed registration shape) updates that document in the same change.
- **`docs/screenshots/`** — a UI change that makes an image misleading means retaking it, following the procedure in that folder's readme.
- **`.proto` files** are the primary API documentation for the public gRPC contracts and must stay self-explanatory: clear message and field naming, comments for non-obvious constraints.
- **Non-obvious architectural decisions belong in `tech.md`'s decision log**, not scattered through the code as comments. A decision recorded where it was implemented is findable only by whoever already knows where that is.

(`docs/dependencies.md` needs no rule here — its guard is a test.)

## Implementation order

Work a feature through these layers in order, finishing each — including its tests — before starting the next:

1. **Data model.** The POCOs and records the feature is about, in the `_Model` folder of the area they belong to. They depend on nothing and are testable alone.
2. **Persistence.** Reading and writing that model — text files under the opened workspace folder, per tech.md's *Diagram storage*, never a database.
3. **Wire protocol.** The `.proto` contract that carries it and the mapping between those messages and the model, per tech.md's *gRPC call shapes*.
4. **Client user interface.** Rendering it, and letting the user act on it.
5. **Additional client and backend business logic.** Whatever the feature still needs once the layers under it hold.

- **Each step carries its own tests.** Unit tests where the step is a unit — a model, a parser, a mapper, a component — and integration tests where the point is that layers meet, such as a gRPC flow against the real host or a file written and read back. **A step is done when something fails without it, not when it compiles.**
- **A step with nothing in it is skipped, not invented.** A feature that persists nothing gets no persistence step. This is an order of what comes before what, not a checklist to fill.
- **The order runs bottom-up because the dependencies do.** A wire contract designed before the model it carries ends up shaped by the transport rather than the domain, and a UI built before the contract invents state the backend then has to be talked into providing. Taken in this order, each layer is written against something that already exists and already passes its tests.

## Checking that the conventions are actually followed

Conventions that are only written down drift. Two tools check them and they see different things — run both, because passing one says nothing about the other.

- **`dotnet format style --verify-no-changes --severity info`**, from `src/backend/` against `EtAlii.Adp.slnx`, is the fast check: the Roslyn analyzers and the rules in `src/.editorconfig`, and nothing else. It may always be run without asking first.
- **JetBrains InspectCode is the thorough one, and authoritative for this team.** Development is mostly done in Rider, so the inspections a developer actually sees are ReSharper's — a far larger set than the Roslyn analyzers, covering design and structure rules `dotnet format` has no opinion about. InspectCode runs that same engine headlessly, honouring the repository's `.DotSettings` files and `src/.editorconfig`, so its verdict is the one Rider gives rather than a second standard. Install once with `dotnet tool install -g JetBrains.ReSharper.GlobalTools`, then invoke as `jb inspectcode`. Run it over the whole backend before a piece of work is considered finished — after the tests pass and the format check is clean, not instead of them. If it refuses the `.slnx` file, point it at the projects rather than adding a second solution file to keep a tool happy.
- **Treat an InspectCode finding as an `.editorconfig` finding is treated**: fix it, or decide deliberately that the rule does not fit and record that decision where the rule lives — in the `.DotSettings` file, with a note saying why. Silently ignoring findings turns the tool into noise, which is how a codebase ends up with a check nobody runs. **A finding left reported is the one option that is not available**, because a gate that always prints something is a gate nobody reads.
- **Expect a backlog on the first full run, and do not treat it as a gate on unrelated work.** What matters is that code being written now is clean and that any backlog shrinks, not that an unrelated change is blocked by something it did not cause.

**A warning nothing fails on is a finding nobody reads.** On 2026-09-22 the user cleaned up three
classes by hand, in three commits, three minutes apart, on code agents had written that week - and
**all four gates had passed on every one of them**. One class the build had been reporting all along:
six `xUnit1051` warnings saying a test was not passing its cancellation token. The finding was on
screen at every gate and nothing failed, so nobody read it. **A hand cleanup after a green gate means
the gate was the wrong shape, not that people should try harder.**

**So: count the pile, then make each rule either fail or be deliberately excused - and date the
count, because a census is a timestamp and not a fact.** Measured on `develop` at `89bb5f35`, a
forced full rebuild reported **8 warnings**: 3 `xUnit1051` (a test's cancellation token), 4
`xUnit1031` (a blocking wait in a test) and one protoc `Import google/protobuf/any.proto is unused`.
**Six minutes of the user's own editing later, at `f4c96823`, the same command reported 3**: the
`xUnit1051` sites were gone and two of the four `xUnit1031` sites with them. Both counts were right
when taken, and a plan built on the first would have fixed three sites that no longer existed.
**Re-measure immediately before acting, and say which commit the numbers describe.** Two sessions
counting the second one independently - by file and line, from separate builds - agreed exactly, which
is the only reason either is trustworthy.

None of the eight was a user-visible defect; both analyzer rules are about how a TEST waits, so their
failure mode is a hung or flaky gate. Measured on
`develop`, both times, with `--no-incremental`.

**Three things that measurement taught, each of which cost a wrong answer first:**

- **A cached build replays no analyzer.** `dotnet build` a second time prints none of these and reads
  as a clean tree. Count with `--no-incremental`, or in a fresh worktree.
- **Count twice and let disagreement be the alarm.** MSBuild's own summary said 8 where a parse of the
  log said 7. The extra one was the protoc warning, which prints as `warning : warning: ...` with no
  rule id - invisible to a filter keyed on a rule id. **The filter was blind to a whole class, and only
  the second count said so.**
- **Raw lines are not sites.** Each analyzer warning is logged once per compile pass, and a file linked
  into several projects is reported once per project: 141 raw findings for `IDE0001` were 16 sites, and
  14 raw xUnit lines were 7. **The site count is the honest number**; quoting the raw one overstates the
  pile by an order of magnitude.

**What the two gate commands can and cannot see, measured by planting each defect:**

- **`dotnet format style` sees only what `.editorconfig` raises.** A fully-qualified name a `using`
  already resolves passed the gate with zero findings until `IDE0001`/`IDE0002` were set to `warning`;
  then the gate exits 2 and names file, line and column.
- **A third-party analyzer's warning fails the build only through `WarningsAsErrors`.** Setting
  `dotnet_diagnostic.xUnit1051.severity = error` in `src/.editorconfig` left six warnings and a green
  build, twice, including on a forced rebuild - **why is not known, and is recorded as not known.**
  `<WarningsAsErrors>$(WarningsAsErrors);xUnit1051</WarningsAsErrors>` in `src/Directory.Build.props`
  turns the same six into errors and exit 1.
- **Clean the tree before raising a rule**, in its own commit, so a reviewer can see the gate was
  raised on a tree that already passed rather than bundled with the fixes that made it pass.
- **Where a rule's own subject is the deliberate behaviour, excuse the site and say why.** Four
  `xUnit1031` sites block on purpose: the code under test is synchronous, and racing two calls into it
  requires blocking. Those get a per-site suppression carrying the reason - *this test races two calls
  into a synchronous API; blocking is the subject* - never a rewrite of the guard to satisfy the rule.
  **A guard edited to quiet an analyzer is the hollowing-out this document warns about elsewhere.**

**The client side has no equivalent, and that is worth stating rather than implying.** `npm run
typecheck` is `tsc --noEmit`, which reports errors and exits nonzero - never warnings. `npm test` is
vitest. There is no ESLint configuration in the repository, so no rule set exists that could
accumulate unread findings. The `warn` lines in those logs are node's own `ExperimentalWarning` about
`localStorage`, once per worker, and say nothing about this code. **In a fresh worktree both commands
fail outright at `npm run generate` until `npm install` has run in `src/`** - a census that skips that
reports zero warnings because nothing ran, which is how this one nearly went wrong.

**What no analyzer here catches, so it stays guidance:** prefer the async file APIs in a test
(`File.WriteAllTextAsync` over `File.WriteAllText`) and `CancelAsync` over `Cancel`, both with the
test's cancellation token. `xUnit1051` covers whether a token is passed, not which API was chosen.
## Merges made before the identity rule

**Merges made before 2026-09-04 carry the machine owner's name, `vrenken`, and were made by agents.** They are deliberately not being rewritten. Anyone auditing later should read them as agent work with a known mechanical cause, not as the user's.

Measured on 2026-09-04: of the 61 merge commits in the preceding 18 hours, **44 were authored `vrenken`** and 17 carried an agent identity.

**Record a measurement with the window it was taken in, or do not record it.** That count is a *sliding* window and decays: the same command over 17, 18, 19 and 20 hours returns 39, 44, 51 and 66 `vrenken` merges. Two agents measuring the same fact half an hour apart got 45 and 44 and both were right. A number like this belongs in a durable document only with a fixed date or commit range attached, and the shape - most merges, not a few - is what actually survives.

**Two mechanisms compounded, and neither is the one first assumed.** Nothing was ever set locally: `.git/config` has no `[user]` section, so the name comes from **global**, and any tree without its own identity signs as the machine's owner. Saying that imprecisely cost real time - an earlier account described the shared value as wrong, and an agent went looking for a local value to fix and found none.

- **A `--worktree` identity binds to the directory, not to the agent.** Working in a tree somebody else created signs your commits as whoever made it.
- **The rule guarded `git commit` and nothing else.** `merge`, `rebase`, `revert` and `cherry-pick` all write commit objects and all take the ambient identity - and the landing step this document prescribes, `merge --ff-only`, **writes no object at all**. The commit that lands is the `--no-ff` merge made minutes earlier in a scratch worktree, where it feels like scratch work rather than like history. That is precisely where the names were lost.

**The rule is therefore written around writing a commit object, and the per-worktree identity comes first.** The agent whose merges were correctly attributed did not remember to pass an identity at merge time; it never had to remember, having set `--worktree` once when it created the tree. **A rule that cannot be got wrong beats one that depends on vigilance at the moment nobody is looking.**

Rewriting the 45-odd commits was considered and rejected: history surgery in a checkout that nine sessions are committing into is a worse incident than a wrong name.

## Checking that a command answered your question

**Ask what the command would print if your belief were false.** If the answer is "the same thing", it is not evidence.

Six instances in one day, across four sessions, each caught only because somebody re-measured:

- `git log --oneline` prints an identical line whoever authored it - **and it prints the same two rows for a
  landing that happened once as for one that happened twice.** The house pattern commits on a branch and merges
  `--no-ff` with the same message, so **both objects carry that message**: two rows, identical subjects, minutes
  apart, the same insertions/deletions stat on the same file - and at the tip *between* them the defect is still
  present, because the merge's first parent is develop's own tip and legitimately predates the fix. **That reads
  unmistakably as a revert-and-reapply**, which on a shared checkout is the shape worth panicking about.
  **`git log -1 --format='%P'` settles it: two parents means the second row is the merge of the first.** Measured
  on 2026-09-23 - one parent on the branch commit, two on the merge, identical subjects on both. **And the two
  instruments you reach for first are both silent in a way that resembles a result**: `git show --stat` prints a
  merge's combined diff, which looks like a second identical change and *confirms* the fear, while `git patch-id`
  returns **empty** for a merge, which reads as a failed comparison rather than as an answer. **A board taking
  several landings an hour will meet this, looking at somebody else's work with no context for it.** *(Developer
  4's, filed as a non-finding: it had the alarm drafted as "the fix may have landed twice" and spent three
  commands first - which is the *notice when you have only one source* clause working rather than failing, four
  hours after the same session watched it fail.)*
- `git config user.name` prints the effective value without its source - so it cannot distinguish your own correctly-scoped identity from one you inherited.
- **Three instruments answer the CR question wrongly, and each fails looking safe rather than loud.** `grep -c $'\r'` prints `0` for a file with no carriage returns **and** for a shell that never expanded the pattern - it reported a correctly-CRLF file as LF. `awk '/\r$/{n++} END {print n+0}'` prints `0` for a file that is **CRLF on every line**, because msys gawk strips the CR reading in text mode (2026-09-22); it has none of the tells that make the first suspect - no bashism, and a pattern that plainly says what it means - and a following `sed -i 's/\r*$/\r/'` then "did nothing" by the same measure, while `od -c` showed both files had been correct all along. And `od -c | grep -c '\\r'` counts **the document's own textual mentions of** `\r`: it returned 8370 on a file with **zero** CR bytes (2026-09-23), because that file is about line endings and discusses them. **`git ls-files --eol` for a tracked file, `od -c` for an untracked one, and a byte-level count of LF not preceded by CR when you need it in a script.**
- `md5sum` over whole files reports drift when only a namespace line differs.
- `git worktree add` fails where a chained `cd` cannot see it.
- A sabotage whose replacement pattern never matched leaves the suite green, which reads as robust code rather than as a test that never ran.

**A git command run in a husk is the same shape at its widest:** every command answers - for the main checkout - and nothing in any answer says so. Only `--show-toplevel` compared against the path asks the question - see *Retiring a worktree*.

**The rule reaches assertions and fixtures, and every example above is a command, which is why four of us read it and did not apply it.** Five instances in one day, all in test and gate tooling. **Two are a count standing in for an identity.** A prune test asserted how many log directories survived and never which, so reversing the ordering - which would have deleted the newest kept evidence - passed it green; the twelve fixtures were created in the same second, so "newest" was arbitrary and the assertion could not have caught it. A coverage canary asserts how many names were checked, not which. Both are true of the wrong dimension of the right subject. **Two are evidence answering the question it was built for.** `retire.sh`'s control reproduced the long paths its author had in mind, in `node_modules`, so it agreed with the fix for that cause and both real retirements still left a husk - the paths were in dotnet's `obj/`. And a flaky-test log vendored as a *must-pass* input for a verdict was later cited for what mechanism had failed: it contained the disproof of the claim its own vendor was making, who quoted it from memory of what it was for.

**And a fifth, measured rather than reasoned: anything printed on a passing test is not evidence, because nobody sees it.** The test platform buffers per-test console output and prints it only for tests that FAIL - found when a probe's lines vanished from a green run. So a diagnostic about a *silent success* - a teardown that failed while its test passed - is invisible on exactly the runs it exists to report, and **a check whose output exists but cannot be read is indistinguishable from one that never ran.** Such a report belongs somewhere a passing run still leaves it: an assertable value, or a file.

**So the instrument, which costs a sentence: name the dimension your assertion fixes - identity, order, count, presence, cause - and ask which other dimension could change without failing it.** For the prune test the answer was *the order*, and that is exactly what the planted defect changed. **A count in prose is an assertion too**: this paragraph said *four* while listing five, for a minute. **And for a fixture: read it against the new question rather than quoting it from memory of the old one.** A fixture is evidence for the question it was built for - citing it for another is a second measurement, and needs a second reading.

**It is the exit-code rule applied to the other half of a command's output.** That one says do not read success from what a command printed; this one says do not read a fact from a command that was never asked for it. It costs a sentence of thought rather than a second command, and it is a sharper instrument than "verify before believing" - which everybody agrees with and nobody applies under pressure - because it converts a vague duty into a specific question with an answer.

**Two of the six are worth their own paragraph, because both are about a command answering for something other than what you ran.**

**An exit code reported by a wrapper is not the exit code of the thing you ran.** A backgrounded suite's task notification carries the *wrapper shell's* status, not the runner's - so a notification saying `exit 0` sits above a log saying `TESTS_EXIT=1`, and both are accurate about different things. The agent that hit this caught it by reading the log rather than the notification. Ask the falsifying question of the notification itself: what would it say if the suite had failed? The same thing, because the wrapper still exited cleanly.

**The same fact about wrappers governs *stopping* work, not only reading it - and it has two halves that point opposite ways.**

**Killing a chain does not kill the work.** Stopping a backgrounded task stops what it points at: the wrapper pipeline, `bash chain.sh 2>&1 | tail`. **The script underneath runs on, and finishes whatever it was going to do.** One session stopped a merge chain precisely to avoid landing inside another session's window, read `develop` immediately after and honestly saw the old head - **and the script completed its own `--ff-only` minutes later, moving `develop` under an open window and costing the other session a full re-fold and re-gate.** The stop had reported success. Confirmed twice within the hour: a watch loop also outlived its own successful kill, found still running by a process scan, harmless only because it read rather than wrote. **So "nothing in flight" is not established by a stop reporting success. It needs a verified process kill, or the chain's own log showing a terminal `RESULT=`** - and the scan proving it must build its pattern by concatenation, or it matches its own probe. **Confirmed a third time on 2026-09-24, on a gate rather than a chain**: a stop reported success while `gate.sh` ran on through `dotnet format` and `dotnet test`, and only a process scan showed it. The new detail is what happened next - **the follow-up kill was REFUSED by the session's own permission classifier as interfering with a workload.** So the remedy was forced rather than chosen, and it was the one the paragraph below recommends anyway: let the fail-closed chain reach its own guard. **An agent cannot rely on being able to clean up after a stop that did not stop anything.**

**And therefore, when a chain is fail-closed, letting it run into its own guard is safer than killing it.** Because the wrapper can neither stop the work nor tell you that it failed to, **an attempted kill is a coin toss you will not see the result of.** A second session, warned mid-chain, declined to stop for exactly this reason: its chain merged, ran four gates green, and then refused at `FF=128` because `develop` had moved beneath it. **It discarded four green gates rather than claim them, because they described a tree that no longer existed.** Nothing needed cleaning up. **A guard that fails closed beats a coordinator who looks** - the chain's opening `reset --hard develop` makes its base correct by construction, where a human re-check in the window had by then failed twice. **So write chains that refuse, and prefer their refusal to your intervention.**

**A fresh-tree build is not a stricter gate - it is the only one that reads a different input.** Every other gate reads `obj/`, which is a cache of the last *successful* generation, so `dotnet test` prints the same green whether the generated code is current or stale. A generated-code break is therefore invisible to it **by construction**, not by bad luck: nine sessions went green through a window in which `develop` compiled in no fresh checkout. This is the same question asked of a build's input rather than a command's output - *what would this read if the generated code were broken?* The same cache - and the only way to change the answer is to build a tree that has none.

**AND A GATE WHOSE `MERGED` EQUALS ITS `GATED_ON_BASE` GATED NOTHING.** That one asks what a gate read; this one asks **what it read it ABOUT**, and it is the cheaper of the two to get wrong because every visible signal is correct. Edit a file, launch `gate.sh` before committing it, and the merge is a no-op: **all four gates pass, `RESULT=gates-green-ff-withheld` prints, and `TO_LAND` names one SHA twice** - `land.sh <base> <base>`, which fast-forwards `develop` to itself and reports `RESULT=landed`. **A clean landing record for a change still sitting in the working tree.** The four gates cannot tell you, and were not wrong: they gated `develop`, and their green is evidence about `develop` and nothing else. **`MERGED` against `GATED_ON_BASE` is the only line in the summary that distinguishes the two cases** - read by luck on 2026-09-24, on the way to running the `TO_LAND` line. **And `RESULT=landed` is not the answer either, because a fast-forward to the current head is a legal fast-forward.** The habit that catches it is the family's own question pointed at the gate: *which tree did this gate read, and which commit did it add to it?*

**A reused *workspace* does the same thing at a smaller scale.** A scratch merge worktree keeps its gate logs between runs, so mid-run I read `g-dotnet.log` for progress and announced the gates green - **the file was twelve minutes old, from a previous killed run, and that run's gate had not been reached yet.** Its content could not have revealed this; only its mtime could. **Delete a run's logs before the run, or read their timestamps, because a workspace answers for whichever run touched it last.**

**A derived *script* carries the last run's subject the same way, and two sessions found it independently in one evening.** Gate scripts were each produced by editing the previous task's - so one landed a merge whose message described the item before it (caught once before the fast-forward and fixed by re-merging after proving the tree hash identical; caught once after, and left, because rewriting `develop`'s history to improve a message is worse than the message), and another had a greedy `sed` aimed at the message string **swallow the `|| { echo "RESULT=merge-conflict"; exit 1; }` guard sharing its line**. **The gates judge the tree, so neither failure could be caught by a gate** - one corrupted only the record, and the other failed loudly by luck, where a guard removed without a syntax error would have merged unguarded and reported success. **Both sessions reached the same remedy, which is what makes it a rule rather than two anecdotes: the message and the identity become required arguments the script refuses to run without, never values inherited by copying.** A parameter cannot be a stale copy of the last run, and a guard on its own line cannot be eaten by an edit aimed at a string.

**A detector that cannot see a thing is not biased toward safety - it is biased toward whatever you were hoping for.** Git Bash's `ps` does not render full command lines, so a grep for a running poll loop returned nothing. **The same blindness had, an hour earlier, hidden a process that survived its kill and let a merge land inside another session's window; now it nearly caused a true report of "the watch is live" to be retracted.** Identical silence, opposite consequence - the first time it was the process you wanted dead, the second the one you wanted running. And a 0-byte log from a loop that has detected nothing is a silence meaning *working*, not *dead*. **Ask what the scan would print if the process were running: the same nothing.** Use `Win32_Process` with the full command line, and build the pattern by concatenation so the probe does not match itself.

**And apply it to your filters, which are the last place anyone looks, because they are the part that was deliberate.** A sweep of build husks was verified with `git status --porcelain | grep -v "audit.log\|.spec-workflow"`, reported as *git sees no change to tracked files*. **For the question it was written to answer - did my sweep leave the tree clean? - the filter was correct**, suppressing noise already seen and understood. Hours later the question became *did anything delete tracked files?*, and **that filter covers the subject**: a ` D` under `.spec-workflow` does not survive it. Ask the falsifying question of the check and it answers immediately - *what would this print if tracked files there had been deleted?* **The same clean output.**

**The distinguishing half is why nobody re-reads a filter.** A broken pattern invites suspicion the moment a result looks odd; **a considered exclusion does not, because it is the part of the command somebody thought about.** Deliberateness reads as reliability, so the filter is audited last and often not at all. **And the remedy is not to filter less** - the noise was real and suppressing it was right. It is that **a filter is fitted to a question, so when the question changes, re-read the filter before trusting the answer** - the same move as re-measuring against today's tree rather than quoting yesterday's number.

**Apply it to your own reports, not just to commands.** "Fixed" said from a worktree describes the worktree, not `develop` - and what you would have seen if it were *not* landed is exactly what you did see, the edited file in front of you. Say where a change is: committed on a branch, merged, or pushed.

**And do not write a guard over prose.** A guard on an owner string, a comment or a description fails when the text legitimately changes, so it must be edited in the same commit as the thing it guards - which makes it a second copy of the data rather than a check on it. Notice rot during work and fix it; do not automate an assertion about wording.

**And the same seam runs the other way: prose can trip a guard.** Verifying that `EtAlii.Adp.Backend` depended on no functional project meant enumerating 110 public types across six area projects and grepping each across every Backend source. **One hit came back, and it was a sentence**: `<c>HierarchyModelStore</c>` inside a `<remarks>` doc comment, describing a shared idle-timer pattern. Reported as a dependency it would have falsified a true result and cost the implementer a re-analysis over a comment. **A word-bounded grep cannot distinguish a type reference from a `<c>` tag, so on this question the count is not an answer - it is a list of lines to go read.** The same hit appeared before the landing as a candidate blocker and after it as the sole residue: identical output, opposite meaning, and both times the meaning came from opening the file.

**And it governs coordinating a landing across sessions, where the artifact is another session's state.** One afternoon's hold sequence needed three fixes, each an expectation read as a state: *message sent is not message received* - a hold reaching a mid-turn session arrives only when its turn ends, so it reached the fastest committer last and four commits landed inside a window already declared open; *a session title is not a landing stream* - a hold list built from what sessions call themselves missed a stream titled "Idle" that was committing every four minutes, and it moved `develop` for twenty-two minutes unseen; *a plan is not a state* - a window opened on a session's stated plan to be ready rather than its explicit "ready" line. The fixes are each the artifact over the expectation: **build the hold list from `git log --format=%an` over the window, not from session titles; confirm receipt before declaring the window open, not "message sent"; open only on an explicit "ready", not an inferred plan; and where one session cannot answer, let the watch stand in for its acknowledgement only if it has nothing landable** - a session with a gated branch or a staged merge must answer, or the watch is catching its breach after it starts rather than covering a session that was never going to move. And *"has nothing landable" is the session's own answer, not inferred from its commit history* - only the session knows whether a branch is gated and ready, an hour of quiet in the log is not that knowledge, and reading it from silence is the inference this whole scope exists to replace; an unanswered session is "possibly landable", never "nothing" read from a silent log. That watch - an independent read of `develop`'s own commit stream by a session other than the coordinator, ignoring what any session is titled - **is run every window, not as a breach alarm but as the routine answer to *is the tree still*.** It confirms stillness far more often than it catches movement, and it is the only thing that answers at all: the coordinator is blind between its own heartbeats, and a breach is the one event the session causing it cannot report. Two independent readings agreeing the tree is still - the watch and the coordinator's own `git log` - is the mechanism working, not a scar; the breaches are why it exists, the agreements are what it does.

**Two later fixes belong to the coordinator rather than to the sessions it holds, which is why the four above could not reach them.**

**Log the open at the moment the hold list is built, so *arranged* cannot silently diverge from *opened*.** One window was arranged, its hold list built, its acknowledgement requested - and then never opened, because a question arrived and the coordinator's turn moved on. A developer held for an hour on a message that was never sent. **This is the one failure in the family with no artifact to catch it**: the other four were wrong descriptions, contradicted by something readable, while this was an *absence* - nothing to read, nothing to disagree with, and the waiting session's only signal is silence, which is indistinguishable from a window still being arranged. **A session waiting on a coordinator has no instrument by construction**, so the obligation has to sit with the party that can discharge it; a clause addressed to the waiting session could not be acted on.

**And a hold reaching a session whose chain is already running must be answerable with "in flight, N minutes from ff", not only "received".** The clauses above assume a hold arrives before work starts. Twice it did not, and **"received" from a session mid-chain reads as compliance while a merge is still going to land inside the window** - the session is not defecting and its answer is honest, but the coordinator has been told the tree will be still and it will not be. Ask for the state of the chain, not for acknowledgement of the message.

**And the hold list's *population* is a lower bound, however good the instrument reading it.** `git log --format=%an` over the window is the right way to find who has been landing, and it still cannot see a session whose work has not landed **because it is all still coming** — a held branch, gated and waiting, authors nothing. Add branch-holding non-authors from `git worktree list`, which reports what exists on disk rather than who has committed. **This is the edge-measure shape at the coordination scope: correct instrument, wrong population.** **And those two keys are not exhaustive either, which this clause originally implied.** A third population exists: **committed work with no live holder** - four branches were found one commit ahead of `develop` with no worktree and no session, invisible to the author list *and* to the worktree list. Harmless for a hold, since nobody can land what nobody holds, **but a clause that overstates its own coverage is the failure this whole section is about.** `git for-each-ref` against `develop` is the third key.

**Session metadata is not that instrument, and two different things go wrong with it that want different fixes.** A session record's *branch* field is a cache and goes stale - one listed a branch that `git branch --list` shows does not exist in the repository at all. But a session's *cwd* field is usually accurate and still answers the wrong question: it reports where the session was started, not **which tree it is editing**, because a session edits any worktree it likes by absolute path. **The first is a stale value and the fix is to read disk; the second is a correct value being asked for something it never reported, and there is nothing to fix but the reading.** Conflating them sends someone to repair a field that was right.

**And it governs the scope of a measurement, where the thing answering for something else is your own earlier check.** A measurement's scope is part of its result, so report what was measured rather than what it suggests. Two instances in one evening, both from the same session, both passing the falsifying question only because the question was never asked:

- **A count taken against a change you are proposing is a statement about the proposal, not about the tree.** A descent set was measured across a folder boundary that a ruling of mine would have moved, and the result - one type newly "consumed outside" - was reported as a property of the codebase. The boundary never moved, because the ruling was withdrawn. *What would that count have printed if the proposal were wrong?* The same number: the proposal was unexecuted either way, so the measurement could only ever describe the hypothesis. Label such a count conditional, or do not report it.
- **Widening a claim does not widen the measurement under it.** One generated type was checked for client consumers and honestly had none; the ruling was then extended to eleven types with the words "same reasoning", and the check was not re-run. Forty-one client files consumed the other ten. *What would the one-type check have printed if the other ten were consumed?* The same zero - it was never asked about them.

- **And comparing two counts inherits both their scopes, so a difference between them may measure only the difference between the questions.** Two sessions hit this on one evening, in one specification, neither from carelessness. **A hand-maintained client generate list was reported as "13 entries against 60 module `api` folders"** - a gap implying years of drift - **when only 12 of those 60 folders contain a proto and all 12 are listed. The list was complete.** Within the hour the session that corrected it produced the twin: **`ProjectReference` *declarations* counted with `grep -c` and reported as dependency *edges*, where two wildcard references expand to about seventy-four projects apiece.** One number was never an edge count. **Both comparisons were arithmetically sound and both conclusions were false, because the two sides counted different things** - folders-present against folders-with-content, declarations against resolved edges.

  **The tell is that the reassuring direction gets checked least**: "the list is complete" and "fewer edges than feared" both invite agreement rather than a second look. **So before a difference becomes a finding, say aloud what each side counts** - not what it is called, what it counts - **and if the sentences are not the same sentence, the difference is about your instruments and not about the subject.** *Catching this shape in someone else's work is not the same as not having it*, which is the only reason it is written down: both sessions here had just corrected it in the other.

  **Two further instances arrived while this clause was being written, and they say what the check above misses: *direction and kind are part of what a degree counts, and a degree quoted without them is not a measurement.*** Arguing that a graph rule was right to be scoped to packages, its author offered a composition root's **87 outgoing** edges against a package hub's **26 incoming** - a true sentence about the wrong quantity, since the rule counts how many things reference a node. **The example was never at risk from the rule it was defending.** The developer who caught that then set the same node's **89 all-kinds out-degree** against a contract's **65 project-only in-degree** - **two different quantities twice over, direction and edge kind** - and 89 read plausibly, because a composition root with 89 outgoing edges is exactly what that repository should have.

  **These matter more than the two above because of when they happened.** The first was written *while gating this very clause*; the second was written *in the correction that caught the first*, in the same paragraph, about the same two nodes. **So the shape survived direct attention from two people who had just named it out loud** - which is why the remedy cannot be vigilance. **Say what each side counts, and say it with its direction and its kind**, because none of the three is visible in the number. The working figure was settled only by a third measurement neither party had made: an in-degree of 65 on the rule's own metric, larger than the largest package hub, which is the argument the out-degree was never able to make.

The two are one error at different moments: the first measures a boundary that exists only in a proposal, the second lets a narrow result stand for a wide assertion. **The tell in both is a claim whose scope grew after its evidence stopped**, and it is cheap to catch, because the scope of an assertion is visible in the sentence making it. Both were made in writing, an hour after the same session had recorded the neighbouring lesson in a specification - the plainest available proof that writing a rule down is not the same as it transferring.

**These are one idea at seven scopes** - a command, a wrapper reporting on a command, the cached input a command reads, a claim about your own work, a check written down, a hold coordinated across sessions, and the scope of a measurement - and they move together. Splitting them by which was learned first would break the argument.

**`DOTNET_TOTAL` IS NOT A COUNT OF BACKEND TESTS**, and the wrongness is invisible because both tiers
arrive in one number. `EtAlii.Adp.Client.Tests` enumerates **every client `*.test.ts` and `*.test.tsx` as
theory data** - one row per file plus one per test - so **a client-only change moves a backend total.**
On 2026-09-24 the total fell 6687 to 6681 and **two sessions reached two comfortable explanations from
opposite directions, both wrong**: *dead test methods went with dead code*, and *the deleted lines are in
a `.test.tsx`, therefore npm, therefore they cannot touch `DOTNET_TOTAL`.* The second is the more
instructive: **the reasoning was sound about the tier and wrong about the boundary**, and its author was
confident precisely because the file extension made it obvious.

**The method is the half that makes the clause usable, because the clause alone only says be careful.**
**Six as a NUMBER invited two plausible stories and supported neither. Six as NAMES closed it in one
diff**: `dotnet test --list-tests` at both commits, then `comm` over the sorted lists - nine gone, three
added, every one accounted for. **It enumerates theory ROWS individually**, which is what settled it, and
the seven that vanished were `ClientTests.EveryClientTestFile_Passes` and six
`ClientTests.EveryClientTest_Passes` rows for one deleted client file.

**And a count of `[Fact]`/`[Theory]` attributes was run, was RIGHT, and could not have found this** -
3643 to 3644, up by one, while the total fell by six. **The check was not sloppy; it was blind to the
thing that moved**, because the rows are data and not attributes. **A correct reading from a blind
instrument is the hardest kind to distrust.** What made the answer safe was a separate exclusion done
first: **54 assemblies in both runs and `skipped` identical at 44**, so *a project silently not running*
was ruled out independently of the name diff - without which a fully-explained minus six would still
have been consistent with it.

**And *two instruments disagree* can itself be the wrong reading: before treating a conflict as a fact about the
subject, check that both instruments measured the same POPULATION.** A specification recorded a label's contrast
at **1.37:1, a black label**, from a browser measurement. The session implementing its guard read the stylesheet
and got **1.17:1** - not black. It chased three routes to that black, **excluded all three, and reported the
conflict unresolved rather than inventing a fourth.** For hours the board held *a source reading and a browser
measurement disagree, and both instruments are working.* **They never disagreed.** One had measured labels; the
other had taken an **unclassed, empty `<text>` node** of the eight every canvas renders. **The artefact of that
mismatch was a plausible, stable, reproducible disagreement between two correct tools.**

**Why this is worse than a wrong count and deserves saying separately: a wrong count announces itself as a number
somebody can re-derive, while a false disagreement announces itself as evidence of a real conflict in the
subject - and it RECRUITS people to explain it.** Three careful investigations went into routes to a black that
was never there. **The check is the same one as always, asked of the pair rather than of either: did these two
measure the same members?** *(The false-disagreement framing is the Scrum master's; the underlying catch is
Developer 4's, which named the unclassed empty node as "how the next person will meet it" in the very message
that reported the conflict - and the next person had been Architect 2, three hours earlier, into an approved
specification.)*

**Several instances of this section's shape are collected under *Measure the thing, not something adjacent to it*** - a log line attributed by adjacency, a stopped task that was not a stopped gate, and evidence that lived only in a working tree.

**And the check inside a tool has the same blind spot: an assertion that its anchor EXISTS cannot tell you the
anchor is in the wrong PLACE.** Two instances on 2026-09-23, both in anchored patch scripts written specifically to
be safe. **A patch inserting nine numbered criteria anchored on criterion 7 where it meant 8** and produced the
order 1 2 3 4 5 6 7 9 8: every anchor was found, every `assert text.count(old) == 1` passed, and it was caught only
because the dry run printed the resulting sequence for a human to read. **A patch appending a clause to the end of
one section anchored on a heading that exists exactly once and sits 340 lines ABOVE that section** - so the clause
landed under an unrelated parent, with the assertion green and the diff the expected size. **In both cases the
wrongness was in a relation, and only presences were asserted.** So **assert a relation: the neighbour on the far
side, the ordering of two anchors, or the resulting sequence printed out** - the same remedy the tree-walking guard
rule above reaches for when it demands two independent markers rather than one. **The second instance was found by
printing the section boundaries afterwards and noticing the new heading's line number fell outside them** - which
is the cheapest possible check and was not in the script.

**Text proximity is not containment, so a regex over source answers *what is nearest in the file* and never
*what contains this*.** A survey of every diagram module took **the nearest `className` above
`<DiagramCanvas>`** as that canvas's wrapper and reported 16 of 19 modules with no wrapper height rule. It had
picked `c4-canvas-title`, `mindmap-canvas-loading`, `helm-canvas-rejection` - **sibling status divs, because
conditional JSX puts `{loading ? <div/> : null}` siblings BEFORE the canvas in source order.** The wrapper is
the first `className` after the final `return (`, and the figure is **11 of 16**. The same shape caught a tree
walk an hour later: the explorer's tree is **flat**, with depth carried in `padding-left`, so scoping a search
to a folder's `li` found nothing while the whole-tree search kept returning the first document in the file.

**Only a parse - or the DOM at runtime - answers containment.** When the same question was asked again on
2026-09-23 for the scrollbar defect, the count came from **parsing each `<DiagramCanvas>` element's own opening
tag**, and the answer it produced (16 canvases across 13 modules) then had to survive a second reading anyway,
because *how many canvases exist* and *how many modules exist* are different questions that the earlier survey
had blurred. **What saved the first one was hand-checking ONE row before believing the table** - `c4-canvas-title`
is visibly a title - **which is why the wrong number never reached a report. A table of nineteen plausible rows
invites belief; one hand-checked row costs a minute.**

**An edit that finds nothing to change and reports success is indistinguishable from an edit that had nothing to
do.** A script to drop four landed entries from a queue note printed **`dropped: 0`**, prepended its new record,
wrote the file and **removed nothing** - leaving the note claiming twenty-one queued with four already landed. It
reported success having done half the job. The cause was **mixed line endings in one file**: appends written
through a bash heredoc were LF, edits written through python were CRLF, so splitting on `"\r\n## "` matched
almost no heading. **A second attempt failed its own assertion, which is how the mechanism was found.** Four
hours later, in another session, a coverage script differenced its claimed set against an **empty** set and
printed **`UNCLAIMED: none`** - a clean pass from comparing nothing, because it looked for criteria written as
`1.1` while the document composes them from a heading plus a list. **Both operations reported success having
examined nothing, in sessions that had each spent that day writing clauses about exactly this - which is why the
remedy is an assertion and not more care.**

**Two rules. An edit that can find nothing to change asserts HOW MANY it changed** - `assert len(dropped) == 4`
is the whole difference between a silent no-op and a loud one - **and a count you do not believe is worth more
than a message saying it worked**, because `dropped: 0` was the only true signal on that run and it was printed
beside a line claiming the record had been written. **A file edited by two different tools acquires two
line-ending conventions, and every separator-based edit after that is a coin toss**: normalise on the way in.
*(This clause is the EDIT end of the absence clause under* Measure the thing*, which is the INSTRUMENT end;
`dropped: 0` and `UNCLAIMED: none` appear in both because each is a reading that examined nothing and a write
that changed nothing at the same moment.)*

**`git status` clean does not mean the working tree honours the line-ending policy, because status compares
NORMALISED content and the policy is about the bytes on disk.** Measured on the same seven files on 2026-09-23,
by two sessions, with **different answers** - which is the finding rather than a complication. Developer 4, in a
fresh worktree straight after `npm test`, saw all seven **modified with no content change at all**: `git diff
--numstat` returned **no rows**, not zero-line rows, and `git diff` printed only the LF-will-be-replaced
warning. An hour later, in the main checkout, `git status` reported **nothing dirty** while `git ls-files --eol`
showed **five of the seven as `w/mixed`**, one `w/lf` and one `w/crlf`. **Both readings were correct. Clean and
mixed at the same time is exactly what normalisation produces**: a file that normalises to the index's blob is
not modified, however its own bytes are arranged.

**The cause is that a MANDATORY gate is one of the generating scripts.** `npm test` in `src/client` runs
`pretest` -> `npm run generate` -> fourteen `buf generate` invocations, and buf writes LF into a tree whose
policy is `* text=auto eol=crlf`, with **no `.gitattributes` rule covering `src/client/src/generated/`**. The
house note already says generating scripts are the usual culprit; **what it does not say is that the gate every
agent must run before merging is one of them**, so every agent meets this in every fresh worktree.

**The hazard is attribution, not correctness.** A pathspec-limited commit misses them, which is the point of
the pathspec rule. **A bare `git commit -a` in the shared main checkout takes all seven under somebody else's
message** - the exact shape of the three misattributed commits already recorded above, with the aggravation that
these files look like real work in `git status` and contain none. **Restore with `git checkout --
src/client/src/generated/` WHILE THEY ARE STILL DIRTY, and know that the same command is a no-op once they are
not.** Measured both ways on 2026-09-23: straight after the gate it repaired the bytes, and in a checkout where
five of the seven were `w/mixed` with `git status` clean it **exited 0, printed nothing and changed nothing** -
git rewrites only what it considers modified, and normalisation had already made them unmodified. **Deleting the
seven files and then checking them out brought all seven back `w/crlf`.** So the drift that `status` cannot see
is also the drift `checkout` will not fix, and **the repair has to destroy the drifted copies to work** - which
is the *reports success having changed nothing* clause above, arriving in a repair rather than in a script.

**Report what each run measured rather than what git does, because the two checkouts behaved differently and
nobody could say why.** Asked to reproduce the clean-but-drifted state, the other session wrote the file both
half-CRLF and all-LF and **got a dirty file each time** - so `checkout` had something to rewrite and did.
Same `.git/config` on both sides, `core.autocrlf=true`, no worktree override, same attributes. **One
checkout's copies had normalised to the blob and the other's had not, and both states have since been
repaired, so neither is re-examinable.** That is the honest limit. **What survives it is the read-back:**
`git checkout -- <paths>` **exits 0 and prints nothing whether it rewrote seven files or none**, so *ask what
it would print if your belief were false* answers "the same thing". **A repair without a read-back is not a
repair, it is a hope** - and `git ls-files --eol` is the read-back in this case. *(The reproduction attempt,
and that sentence, are Developer 4's.)* Whether the durable fix is a `.gitattributes` line for
that folder or a generator that writes CRLF is **a house-policy question and not an agent's to settle** - the
two files that state the policy must agree, so changing it for one folder is a decision, not a tidy-up.
*(Developer 4 measured the symptom and explicitly declined to propose one over the other.)*

## Measure the thing, not something adjacent to it

**Everything below is one idea with ten worked cases, all measured between 2026-09-15 and 2026-09-22:**
**an artifact answers identically whether or not it is about the thing you are asking.** A guard, a count,
a log line or a sweep that watches a PROXY passes and fails for reasons that have nothing to do with its
subject - and reads as sound the whole time. The instances are grouped by what the proxy stood in for.

### A proxy for the subject itself

Three of these in one day, all Developer 3's, all on its own guards:

- **A proxy for the EVENT.** A test waited until the store handed out a DIFFERENT stack, then asserted the
  dropped one was disposed. Eviction removes the entry BEFORE disposing, so the replacement is observable
  first and the gap is real. It now waits for the disposal itself.
- **A proxy for the PROPERTY - machine speed.** A test asserted that a failure record names this process,
  but the holder query's two-second production budget expired under gate load, so the record truthfully said
  the query had not answered. The budget is settable for guards now, and the timeout is pinned deliberately
  elsewhere - against a query that sleeps thirty seconds, not against a busy machine.
- **A proxy for the ACTOR - a global seam.** A test installed a counting `FileHolders.Query` and asserted the
  count was zero. That seam is `static` and the assembly runs classes in parallel, so another class's
  deliberately failing save incremented a counter belonging to a test that asked nothing. It now records the
  PATHS queried and asserts its own is absent. **The collision was latent, not new**: the parallelism was
  always there, and the branch that exposed it merely added more failing saves.

**A global seam answers for whoever touched it, so a guard reading one must assert about its own subject BY
NAME rather than about a total.** Lead with this one: the first two read as flakes, while this reads as a
passing test until somebody adds another test to the assembly.

### The sweep that looks for them is the same shape

**Sweep for what can CHANGE, not for what can be ASSIGNED** (Developer 3's words, and its reasoning):
*settable* is a property of the DECLARATION and *mutable* is a property of the THING. A sweep matching
`static … { get; set; }` asks about declarations and cannot see a `static readonly ConcurrentQueue` that
anyone may enqueue into - `readonly` protects the reference and says nothing about the contents.

**So record how many seams were swept and BY WHAT QUESTION.** Instances named without their question read as
an inventory, and the next reader re-runs the same narrow question and gets the same reassuring answer.
Measured on 2026-09-22 over **1430 `.cs` files - 974 production, 456 test**:

- *Which statics can a test ASSIGN?* → **two**, `FileHolders.Budget` and `FileHolders.Query`, both real seams.
- *Which statics can a test CHANGE?* → **two more containers**: `LogCapture.Open`, safe by design because a
  capture collects only its own test's events and its own guards pin the concurrent-`Start` case; and
  `TestFolder.Failures`, read as a TOTAL by four sites while a sibling class wrote to it from its teardown -
  the same shape as the `Query` collision, with lower odds and identical latency, fixed by asking by path.
- Everything else the widened question found is a constant lookup table never written after initialisation,
  with one exception: `CommandDispatcher.Invokers`, a production-owned cache written through `GetOrAdd`.
  **No test reaches it TODAY - which is a candidate, not a clearance**, since that is exactly the shape that
  was latent in `FileHolders` until a branch added a test.
- **Four hits were false positives, all get-only expression-bodied properties** - `PipelineElementMapper`'s
  `PayloadTypeUrl`, `TestFolder.Failures` (an exposure, not a setter) and two in `EditorFixtures` - named here
  so a re-run is not surprised by its own noise.

**Same tree, same day, different answers - and the difference is in the question, not in the diligence.**

**A shared seam has TWO failure modes, and the remedy for one cannot fix the other.** *Misattribution* - the
seam records work, and a test reads a total that somebody else's work contributed to - is fixed by a better
ASSERTION: ask about your own subject by name, which is what `FileHolders.Query`'s counter and
`TestFolder.Failures` both needed, and both were fixed that way. *Contention* - two tests needing DIFFERENT
VALUES of one seam at the same moment - cannot be fixed by any assertion, because the values cannot both be
installed; it needs serialisation, or the removal of the sharing. Developer 3 hit the second the same evening
by adding a class that writes `FileHolders.Query` and `Budget`: five tests failed for each other's work, and
asking by path - the remedy that had just fixed the first mode - could not have helped. **Name which mode you
have before reaching for the remedy that worked last time.** The sequence is find, attribute, decide: the sweep
above finds the seam, the by-name assertion fixes what a better question can fix, and this says which residue
it cannot.

**A rule that depends on vigilance is a debt with a due date.** The remedy taken there was an xUnit collection,
which serialises the two classes - cheap now, and correct only while every future class touching the seam
REMEMBERS to join it. One that forgets gets five tests failing for each other's work, and **it will look like
flakiness rather than like a missed rule**, which is the expensive part. The alternative, making the seam an
instance, cannot be forgotten because there is nothing global left to collide over, and costs every production
call site an instance it has no use for. **Both were priced at the time of the decision rather than when
somebody trips, and the trigger for re-opening was written into the commit: a third class with its own needs.**
If the reminder ever fails, the answer is the instance, not a second reminder.

**The same trap in a different instrument, and it is the sharpest statement of the family** (Developer 3's,
from routing a text buffer through the shared writer): centralising the writer turned an in-place truncation
into a temp-then-replace publish. **A write in place raises `Changed`; a publish raises `Renamed`, as the
scratch file takes the destination's name.** Two editor sessions subscribed to `Changed` alone, so a save
stopped reaching an open editor - correct bytes, correct turn, and nobody told. The writer's own 106 tests
stayed green throughout, because they assert what lands on disk and a watcher's question is HOW IT GOT THERE.

**Why review does not catch it:** a FOLDER watcher naturally subscribes to creation and renaming, since that
is how files arrive in a folder; a SINGLE-FILE watcher reads as *"tell me when this file changes"*, and
`Changed` is the event whose NAME matches that sentence. **The wrong event set followed from the wrong
reading of the question.**

**The extent, read individually rather than counted, and recorded here because the branch that fixed it could
not carry it** (its merge message is fixed when the gate starts, and discarding a green cycle to add a sentence
would have cost the session behind it): **7 `FileSystemWatcher` constructions in production code across
backend, diagrams and editors. 5 complete** - `RootFolderWatcher`, `TrackedProblemRoot`,
`AnsibleWatchedFolder`, `HelmWatchedFolder` and `SolutionWatcher`, the last of which has no `Error` handler,
**a different gap, recorded with the module it belongs to. 2 subscribing to `Changed` alone**,
`PlainEditorSession` and `MarkdownEditorSession`, both fixed.

**Paired with the sweep line above, the pattern is the section's own: THE QUESTION YOU ASK DECIDES WHAT YOU
CAN SEE.** *Which statics can a test assign?* and *which can a test change?* are different questions over one
tree; *tell me when this file changes* and *tell me when this path's contents are replaced* are different
questions over one file. In both cases the narrower question returned a complete, reassuring, useless answer.

### A proxy for the wild actor

**A measured distribution describes the perturbations you were able to STAGE, not what the actor in the wild
is doing.** Two concurrent replaces produced `0x80070497` 29 times in 800. A delete racing a publish produced
it 185 times in 400 in one harness and 75 times in 400 in another - **the same staged actor, two
distributions** - so a delete looked dominant either way. **Then a third occurrence in the wild showed the
destination PRESENT with 132 bytes**, which is not the delete shape at all. The staged rate was evidence of
MECHANISM - that a delete can produce this error - and never of FREQUENCY in the field.

**And read a failure count for WHICH failure.** This clause said "305 in 400" of `0x80070497` until Developer 3
corrected it on the way to the gate. 305 was that harness's TOTAL across every code it produced - `0x800700B7`
148, `FileNotFound` 76, `0x80070497` 75, `0x80070005` 5, `0x80070020` 1 - and the pair that matters about it is
305 failures without the shared turn against 0 with it. **A total is not a distribution, and a harness's
headline number is whichever one its author was watching.** Ask which measurement a number IS before citing it
as the rate of one outcome.

**The same error reached from the opposite direction, and worth reading beside it:** eight runs plus a
full-suite re-run of the exact commit that went red, all green, is a failure to reproduce **and not a rate**
(Developer 2, refusing to quote it as one). One session had a number and no wild instance; the other had a
wild instance and no number. **Both are the same mistake: taking what you could stage for what happens.**

**A defect found in a test is worth looking for in the code it tests, and in the code beside that.** Developer
3 found machine speed deciding what a guard asserted, fixed it, and did not look at the production path one
file away - where the same two-second budget decides whether a failure record can name a holder at all, most
easily failing under exactly the load that produces the failure.
**A numeral is not a quantity until it says WHICH quantity it is - and two right numbers can contradict each
other.** Two sessions computed the same crossover and got 39.9 / 34.5 / 27.9 against 51.9 / 46.5 / 39.9 -
**every pair exactly 12 apart, which is the 6-unit padding at top and bottom.** One had measured the text
region's height at crossover, the other the band height requested, and the helper subtracts the padding from
what it is asked for. **Neither was wrong, and "39.9" meant two different things**; a reader meeting both has
no way to tell which. It was found by recomputing rather than by accepting, which is the only way it could
have been.

**And the instrument for what a number MEANS is the document that chose it, not the code.** Asked to settle a
height from the code, a session found the library's `DEFAULT_HEIGHT` at 40 while the diagram's shared height
is 48: **the code alone would have answered confidently and wrongly.** It read the design document instead.
***Measure it* is the right instinct for what a value IS and the wrong one for what it MEANS.**

**Correcting the source does not correct the copies, so the sweep is *where else does this number appear?* and
not *is the source right now?*** A conflated figure - a harness total read as this code's rate - originated in
one session's report, was inherited by another, and **had reached two memory notes and one steering clause
before either looked.** Each read plausibly alone; none pointed at the others. The fix was to re-grep the
number across the whole store rather than to repair the two mentions already known, **which is the only
version of the check that finds a copy nobody remembers making.** A quoted number is a well-formed wrong
answer, and those invite no second look.

**A verdict printed to stdout is a verdict that cannot be cited later**, which is the same idea at the
instrument end. Today a session reported *three consecutive gates refused* and could substantiate two: the
third run's `RESULT=` line had gone to a scrollback rather than to a log, so it could not be recovered even by
the session that produced it. **The gate writes its exit codes to files precisely so a number outlives the
moment** - a result that exists only in a transcript is, for every later purpose, a run that did not happen.
*(Developer 1's formulation, from correcting the number.)* **A second instance the same day: a task notification
reporting exit 0 over a log that said `RESULT=gates-red ( format=2 )`** - the true status had been echoed and then
overwritten by the echo's own success, so the citable record and the believed record disagreed, and the believed
one was the one nobody could check.

**A third was proposed for this clause and does not belong in it - worth recording because the proposal was mine
and a peer repeated it back approvingly.** I reported a selftest as having *vanished without printing a verdict*.
It had not: it ran throughout, and its output was invisible only because the command ended in `| tail -3`. **That
is a SWALLOWED verdict, not a lost one** - the run was citable the whole time by reading the file it was writing
to, which is how it was settled. It belongs with the two-blind-instruments paragraph and nowhere near here. **A
clause gains nothing from a third instance that is really a different failure**, and leaving it in would have
taught a reader that runs disappear.

### A proxy in the evidence

- **Evidence lives on the branch or in a scratchpad, never only in the working tree.** The overnight cleanup
  removed two worktrees holding finished, unlanded work; nothing was lost only because the commits were on
  their branches, and a branch ref outlives its directory. So: commit before you stop, even mid-task; judge a
  branch by `git log` or `merge-base`, never by whether its directory is there; and **copy a gate log out of
  `<worktree>/.git/…/adp-gate-logs/` when it is the only copy of something**, because pruning keeps ten runs.
- **A stopped task is not a stopped gate.** Withdrawing from a gate, stopping the background task killed the
  OUTER shell while the gate's own `bash` went on running `dotnet format`, and would have gone on to a second
  full suite beside another session's. After stopping one, look for the PROCESS, not the task:
  `Get-CimInstance Win32_Process` with full command lines (Git Bash's `ps` shows none), walk up from any
  surviving child to the gate's own shell, and kill that tree. Three checks made it safe to kill anything:
  read the lock's `owner` file before removing a lock; establish which PHASE your run reached before
  attributing a process to it (the run's log directory said `format.log` and no `dotnet-test.log`, so neither
  running suite could be mine); and **say what you cannot claim** - the withdrawn run's steps did overlap
  another gate's suite for ninety seconds, and that it went green afterwards is not evidence the overlap was
  harmless.
- **A check you run against your own copy is a rehearsal; the check that counts reads the copy that will be
  used.** The gate extracts its scratch-worktree guard from **`develop`'s** `processes.md`, not from the branch
  being gated - deliberately, so a branch cannot weaken the guard that gates it. So running the self-test in a
  worktree before committing a change to this file proves the change is *extractable*, and only the gate's own
  run proves the guard the gate will use is intact. Both were run for the change that added this section, and
  they answer different questions. **The same shape as every case above: the artifact you conveniently have
  is not always the artifact your question is about.**
- **Write the message you want before you launch, because afterwards the only way to change it is to throw the
  run away.** `gate.sh` creates the merge commit at line 122, BEFORE any of the four gates run, so once a run
  is up neither the branch's commits nor the merge message can be changed without discarding the run. That is
  not a defect - the gates must judge the exact commit that will land - but it does mean a message is an input,
  not an afterthought. It cost a real decision: a sweep's extent was asked for mid-suite, and folding it in
  would have spent a whole cycle with another session waiting, for a sentence already true and already
  reported. It landed as it was, and **the count lives in this section instead** - which is why the extent
  above names its own home.
- **Attribution by adjacency in a log.** A line carrying no subject of its own belongs to what the writer was
  last reporting - **the line ABOVE it**; the line below is merely next. Architect 1 read an `Exit code: 2`
  and named the assembly below it, which had PASSED, while the one above had just failed; Developer 3 caught
  it. The neighbours usually carry their own verdicts, so read those before attributing an orphan line. **And
  with several assemblies running in parallel, adjacent is not even sequential**: lines interleave by
  completion time, so only the verdict printed on a line is about that line's subject.

**Every string in a table meant to be MATCHED against reality is read out of the artifact that produces it,
never recalled and never invented.** A lookup table existed so that somebody holding a real log could match a
line against it, and one row carried an invented placeholder instead of the actual constant. **It would have
matched nothing while looking complete - which is worse than omitting the row**, because an absent row sends
the reader to the source and a wrong one sends them away satisfied. When the four strings were finally read out
of the code, **three were right from memory and one was not, and nothing in the table distinguished them.**

### A proxy inside the guard

- **An exact count over a window the test cannot bound asserts a property of the machine, not of the code.** A
  watcher test wrote ten files with a 150 ms settle delay and asserted exactly one report; nothing bounded
  those writes to that window, so an ordinary stall on a busy machine makes the watcher settle twice -
  **behaving exactly as specified** - and the test report a failure. Either make the precondition hold by
  construction (a settle delay far above any plausible burst) **and** stop asserting the count over that
  window (wait for quiescence, then assert one report arrives and none after). Neither half is enough alone.
  Recorded with its limit: eight runs and a full-suite re-run of the exact commit that went red all came back
  green, which is **a failure to reproduce with the cause identified, not a rate** (Developer 2).
- **A shared flag that either party may clear is not a record of who owes the work.** One field, two states it
  cannot tell apart - done, or dropped. This is **under-determination**, the same family as *a measurement
  consistent with two readings has told you nothing until you say which readings it excludes*.
- **A log line may only report what THIS code path observed.** A line that speaks for another party's work
  asserts something unobserved, and reads afterwards as confirmation of exactly the thing that did not happen:
  a flush skipped an entry believing a debounce callback had written it, the callback returned believing the
  flush would, and the `Debug` line said the write had happened (Developer 3).
- **A sabotage that fails to fail is evidence about the SABOTAGE, not about the guard.** Two cases, two
  mechanisms. A round trip through a string was meant to remove a file's byte-order mark and did not: decoding
  `EF BB BF` yields `U+FEFF`, re-encoding returns the same bytes, and the property never left. And disabling
  one post-delete exit changed nothing because **the next attempt's opening check tests the same condition and
  returns first** - one site looked like the decision while two sites were it. **A subject that checks a
  condition twice will survive the loss of either check, so a sabotage must remove the CONDITION, not one of
  its sites.** In the result the guard reports, a sound guard and a sabotage that never bit are identical - a
  green on the test you were perturbing. The primary tell is not in any result: **ask what the perturbation
  CHANGED**, which is answerable by reading the code. A weaker, free second tell: read the whole run, because
  a perturbation that reddens something you were not aiming at changed something you did not intend, while one
  that moves nothing anywhere most often means the property never left.
- **A planted proof needs its own control: that the refusal came from the mechanism and not from the plant.**
  Raising two analyzer rules to errors, the planted instance made the build exit nonzero and looked like
  proof; the plant had duplicated an attribute and the compile failed on `CS0579` **before any analyzer ran**.
  An exit code cannot tell those apart - read what the failure says. The tell that settled it was in the same
  log: the compiler flags showed the raised rules had reached the build, so the instrument was armed and the
  plant was at fault. **When a planted proof fails, ask first whether the instrument was even armed.** This is
  the mirror of *a test that passes against the defect is worse than no test*.
### The state you measured is a photograph, not a forecast

**THE FAMILY SENTENCE for a whole day of corrections, and it is Developer 3's: the state I measured was a
photograph, and I was treating it as the state I was acting on.** Every instance below is that idea at a
different scale, and two of them are the Scrum master's own:

- **A window granted on a lock read once**, before five commits and four card creations. The reading was
  accurate; treating it as a forecast was the error, **and the session had been told minutes earlier that the
  other run would refold and re-gate.**
- **"Five of seven watchers complete"** - true of the question *did it hear a rename*, false under a criterion
  that also required `Error`. **The count is a property of the question.**
- **305 quoted as a rate** when it was a harness's TOTAL across five failure codes, correct where it came from
  and wrong one line from a `0x80070497` discussion.
- **A commit subject describing the diff it was written for and not the one it landed with**, because a message
  file written for an earlier change was reused.
- **And the purest, because it has no interval at all: a process tree was measured, what was seen was killed,
  and the tree had moved before the reader finished reading its own output.**

**The discipline: re-read the state immediately before the act, not at the start of your turn** - and where the
state belongs to somebody else, ask rather than infer.

**And the interval is not carelessness - it is the length of a careful message.** Three instances in one
evening, from two sessions, every reading correct when taken. A session cold-reading this document's own new
clauses measured *"no unlanded branch touches this file - clean tree, `ahead=0`"*, stated it as verified, and
built a premise correction on it. **The branch had been created four minutes earlier and had no commit on it
yet**; the reflog dates the gap at 4m22s, and the base it measured genuinely was `develop`'s tip in that
window. So `ahead=0` was not merely defensible, it was **correct** - and **no re-phrasing, no better tool and
no re-run could have found a commit that did not exist yet.** Only re-reading could. The same session then
reported `ahead=1` and `GATING=none` in a later message; by arrival the tip had moved again and the gate lock
it read as free was held by the very branch it was writing about.

**That inverts the intuition the rule invites.** The reader assumes a stale reading is the mark of somebody
rushing. What expired all three was **four paragraphs of good reasoning written from them** - the more thought
between the reading and the act, the staler the reading. **So this is a tax on thoroughness, not on haste**,
and the sessions most exposed are the ones writing the most careful message. Two remedies, both cheap:
re-read immediately before the act rather than at the start of the turn, and **say when a state was read**, so
a reader can see the interval you could not.

**`ahead=0` also answers a narrower question than it appears to: ahead of WHICH `develop`, read WHEN.**

**Two clauses from the process-tree instance, both earned the hard way.** *A kill orphans, it does not
terminate*: the wrapper was killed and the script survived, the script was killed and the suite survived, the
suite was killed and **its test host survived and was still spawning children three minutes later - and each
kill reported success.** So the rule is not *kill the tree* but **verify by ancestry afterwards, because the
tree you can see is the tree at the moment you looked.** And *the discriminator is ancestry, not time and not
the command line*: a live gate spawns **the identical script from the identical path**, so a pattern match
would have killed the running gate along with its orphans, while **an orphan cannot be a child of a live
gate.** Time nearly works and then does not - two orphans spawned after the live run had started.

**And verify by ancestry is not enough on its own: ENUMERATE by ancestry too.** The walk that fixed
attribution was applied to a **name-filtered** candidate set, so it proved *no process of those two shapes
remains* rather than *no orphan remains*. **A name filter decides your coverage before the walk decides your
attribution**, and both of that day's process-hunt errors were that one thing - one in the open, one hidden
under a correct-looking walk.

### An instrument that reports absence must first prove it is present

**A clean result means one of two things, and they are not close: *nothing is wrong*, or *nothing happened*.**
*(The rule and the six instances are Developer 1's; the two constraints below are Architect 2's.)*
Six mechanisms produced the second while looking exactly like the first, all on 2026-09-23, none from
carelessness:

- **A silent logger.** A class held a logger bound to Serilog's default sink, so on the occurrence everyone was
  waiting for there was **no line at all** - and a missing line reads as *it did not happen*.
- **A degraded shell.** The client threw `useContextConnection must be used within a ContextConnectionProvider`
  from four components, **went on drawing the last document perfectly**, produced events, and round-tripped
  nothing. A negative measured there is worthless and looks identical to a negative that means something.
- **A 0x0 browser pane.** Sixteen geometry rows came back clean with every pane reporting `h: 0`. **Nothing
  throws**, which is what makes it expensive.
- **A wedged origin.** A console with no errors, while `curl` from outside returned 200 in 4.7 ms - the page was
  not reaching the server it appeared to be reading.
- **`UNCLAIMED: none`** from a coverage diff whose criteria set was **empty**: the right answer to the wrong
  question, printed in the format of the right one.
- **`dropped: 0`** from a filter that matched nothing, because the file it read had mixed line endings.

**And TWO blind instruments agreeing reads as corroboration, which is how a careful reader talks themselves into
the wrong answer.** A long-running script had to be checked for liveness. Its task output was empty **because the
command ended in `| tail -3`, which emits nothing until the pipeline exits** - so a running job and a dead one
print the same nothing. And a process check said zero **because the pattern named the script while the process is
`bash`** - so it prints zero whether the script is running or not. **Two independent readings, both incapable of
returning anything else, agreeing on *it died*.** The cost was real: a second copy was started, and two
concurrent runs of a suite that builds fixture worktrees can collide and redden for a reason belonging to
neither. **It was settled by reading the file the script was actually writing to** - 741 bytes, then 1062, with a
plausible last line - which is the one instrument that could have said *alive*. **Before concluding absence from
two agreeing checks, ask whether either COULD have said otherwise**; corroboration between two instruments blind
in the same direction is not corroboration.

**And an instrument can be HALF alive, which none of the six above can be.** A pane is 0x0 or it is not; an origin
answers or it does not. **An analyser can run, parse, emit a valid report, and be blind only to the question you
asked.** Measured on `jb inspectcode`: a correctly-parsed run reported `AccessToDisposedClosure = 0` **from a run
carrying 327 `Cannot resolve symbol` and `Ambiguous invocation` errors.** An analyser that cannot resolve the
method cannot decide whether a closure captures a disposed variable either - **so zero from a blind analyser is
indistinguishable from zero from a clean one, and nothing in the output says which you have.**

**Only a planted control separated them**: neutralise the three suppressions, confirm the warning appears (3),
restore, confirm it goes (0). **And the control's own run carried ZERO resolution errors, which is how the 327
were diagnosed as a cold-build artifact rather than a property of the tree** - the control proved the instrument
*and* explained the earlier run, which is the argument for planting one even when you are confident of the answer.
**So the liveness check must be specific to the FINDING you want, not to the tool running** - which is the
canary-sensitivity condition arriving from the other end: a check sensitive to *the analyser started* would have
passed here.

**The same tool also fails open when misconfigured**, which is the ordinary member of the family: pointed at the
wrong toolset it printed `No files to inspect were found`, **exited 0, and wrote a valid, empty report** - green
exit, well-formed output, zero findings, indistinguishable from a clean inspection. The invocation that works
here names the SDK and toolset explicitly (`--dotnetcoresdk`, `--toolset-path`), and **its output is SARIF JSON
whatever the `-o` extension says**, so an XML grep over it returns 0 - a second empty instrument inside ten
minutes. *(Developer 1's, all three.)*

**Why the pair matters more than either half: two `IDE0053` findings sat on `develop` until a gate tripped over
them and blocked every session.** `dotnet format` sees those and not ReSharper's inspections; `jb` sees
ReSharper's and not those. **A reader who reaches for the second tool and misconfigures it is told everything is
fine**, so both routes to noticing close at once. On the documentation half, stated as measured rather than as
relayed: there is **no `.config/dotnet-tools.json`**, so *not installed in this repository* is literally true of a
repo-local tool, **while the tool is installed globally** (`jetbrains.resharper.globaltools 2026.2.2`). **Both
readings are available and only one sends the reader to the tool - that ambiguity is the defect rather than a
falsehood**, and the sentence sits directly after an install command, which pushes the reader toward *absent*.

**The limit, stated so nobody reads this as a proposal: `jb inspectcode` is NOT a missing fifth gate.** A run
takes minutes and needs per-machine toolset flags. It answers a question `dotnet format` cannot, and that is all
it is for. *(Developer 1's limit, offered with the finding.)*

**Proof of presence is always one line** - `fetch('/favicon.ico')`, `innerWidth > 0`, *did anything else witness
this event*, an assertion that the set being filtered is non-empty - **and it is never written by someone reading
the failing output, because the failing output looks like success.** That is the whole difficulty: the moment you
would want the check is the moment nothing prompts you to write it.

**Two constraints on the canary, both of which it fails without.** **(a) It binds the NEGATIVE half only.** A
positive observation is self-proving - something was seen, so the instrument reached the subject. An absence is
the reading a broken instrument manufactures, so only absences need the proof. **(b) The canary must be sensitive
to the INSTRUMENT's failure and insensitive to the SUBJECT's absence.** Backwards, it either never fires, or it
fires on every genuine finding and gets deleted as noise within the week. *(Architect 2.)*

**A probe must assert a code AND a message that no failure path produces together, because an instrument check
whose pass code is also a failure code checks nothing.** The two gate scripts refuse differently, measured by
running both:

- **`who-is-gating.sh --no-such-flag` exits 3** with `TELL_REFUSED=<unrecognised argument …>`. Three is a
  *deliberate argument-refusal* code there, chosen because 2 already meant both *unreadable board* and
  *argument refused* - **a probe cannot rest on a code shared with a failure.** This is the script the probe is
  for.
- **`gate.sh --no-such-flag` exits 1** with the usage line and `RESULT=missing-arguments`, for any wrong
  argument count or empty argument. **`gate.sh` has no argument path that exits 3**; its 3 is the `aborted-*`
  family - missing `gate-lib.sh`, no main checkout, a stale copy run from a worktree, a non-empty log dir.

So *run the probe, expect 3* is true of the tell and **fail-open if said of `gate.sh`**: a genuinely aborted
probe would exit 3 and read as healthy, which is the one thing a probe exists to rule out. A probe for
`gate.sh` asserts **exit 1 and the `RESULT=missing-arguments` line** - exit 1 alone is also what a real refusal
returns, so it is the *pair* that says the script ran, validated, and refused.

**The general form, which is why the tell's 3 exists at all: an overloaded exit code cannot carry a proof - a
probe must require the value that no FAILURE can produce.** The tell's refusal code was originally 2, and 2 also
meant *the board could not be read* - **so on any unreadable board the probe passed against precisely the copy
it exists to detect.** The flag it was proving is only trustworthy if the copy answering has been shown to know
it, because an old copy ignores an unknown argument and exits zero, which reads as FREE.

**And the reason that survived three chances to be noticed is the better half: its first outside adopter did not
rest on the exit code at all - it read the message and reported what it had read.** So the artefact recorded a
pass and the weakness never surfaced. **When a check's careful users compensate for its weakness, the record
shows a pass** - which means a history of passes is not evidence that the check is sound, only that nobody
depended on the part that was broken. *(Both Architect 1's, offered with the instance.)*

**When you relay a fact you did not read yourself, put the instruction not to trust you in the same message as
the claim.** A relay said a card had been **rejected** and added, in the same breath, *wait for the verdict on
disk before touching the file - my word is not the verdict*. The disk said **approved**. **The instruction is the
only reason the error cost nothing**: the receiver had no independent reason to doubt a plausible message from a
reliable sender, and a plan to fold four corrections into a rejection would otherwise have been built on it.
**Three relayed statuses were wrong or early in one day** - a card announced as raised before it was, a develop
SHA quoted from memory three commits behind, and that rejection which was an approval - **and the only one that
cost nothing is the one where the sender told the receiver not to trust the sender.**

**Why this is a clause and not *be careful with relays*: it does not depend on the receiver being suspicious**,
and vigilance fails exactly when the relay is plausible and the sender reliable, which was true of all three.
**The sender always knows whether they read the artefact or are repeating someone else; the receiver never
does.** So the control belongs to the only party holding that information, and it costs one sentence.

**A fourth instance arrived in the message proposing where to file this clause, which is the best argument for it
available.** Its author cited a sentence as already being in this document - *a population has an owner and a
lifetime* - **which does not appear in it at all.** The citation came from memory of a message they had SENT
rather than of the file as it stands: **a claim about an artefact's contents, second-hand, delivered as if read**,
in the act of proposing the rule against exactly that. They found it by grepping rather than by being told.
*(Architect 2's, prompted by the relay that carried its own warning, and volunteered against itself; the framing
that this is cheaper than either party being more careful is the Scrum master's.)*

**The operational form, which is cheaper than any of this: when you pass news that touches somebody else's
AUTHORISATION, say what it does not authorise in the same sentence.** Two faults on 2026-09-23 had that exact
shape and neither was a relay of a status - both were news. *Develop's gate is green, which is your cue* was true
in its first half and an authorisation in its second: green was **necessary and not sufficient**, another session
was still gating, and the coordinator names the branch. The receiver refused it on those grounds. **The instinct
behind it is wanting to be useful with news, which is a good instinct to have and a bad one to act on
unqualified** - and the qualification costs one clause of one sentence.

**And the exposure is highest for whoever coordinates, because most of a coordinator's messages ARE relays and
their messages carry the most authority.** The Scrum master's own count for that day was three card verdicts
relayed from chat rather than read from disk, and one window granted on a stale reading - **a higher rate than any
session it was relaying to.** So the role least able to check every claim it passes is the role whose unchecked
claims cost most, which is the argument for the sender's one sentence rather than for the receiver's suspicion.

**A fifth instance, and the reason it is the strongest one in the clause: it happened AFTER both parties had
written this rule.** A miscount - *three runs today produced no citable verdict*, when only two were
substantiable - passed from the session that made it to the coordinator, who **relayed it onward without
checking, then relayed the correction, both inside an hour.** Neither party was careless and both had just
authored the clause. **The artifact caught it and the authors did not**, which is the whole argument for the
control being structural rather than attentional: a rule you wrote this morning does not protect you from a
plausible number this afternoon. Recorded as an instance rather than as anybody's credit - **the useful fact is
that writing the rule is not one of its remedies.**

**And the corollary for routing, which is not the same rule: route a question through a coordinator when they
hold context you lack - not when they would only be repeating you.** A condition was set that an
`.editorconfig` change come back through the Scrum master for the user; the session that had just measured the
problem put it to the user directly instead, with the confirmation of the relay folded in as one of the options.
**The condition was withdrawn, because a second hop adds latency without adding safety when the party holding the
evidence is the one asking.** The distinction is worth stating because both halves look like diligence: **routing
for context is diligence, routing for ceremony is delay** - and the coordinator is usually the only one who can
say which it would be. *(The Scrum master's, withdrawing its own condition.)*

**Its complement, and the harder half: notice when you have only ONE source.** The same session that refused to
invent an explanation for a measured conflict **invented one elsewhere the same day without noticing** - *this
stylesheet still reads that token, so the repair is one line* - carried it four hours, through three sessions and
into a commit message, **from a test header's past tense read as present.** It was not a lapse in care; the care
was identical. **The difference was that the conflict had two instruments and the claim had one, and nothing in
the writing said which.** A disagreement announces itself and forces routes to be excluded; **a single unchecked
source reads exactly like a verified one.** So refusal is what you do *after* the disagreement surfaces, by which
time the hard part is done. **`git log -S'<symbol>' -- <file>` answers *is this still true* and *what closed it*
in one command**, and a defect named in a comment is a historical record by construction - written at the moment
of the fix, and nobody revisits prose that still reads correctly. **And the tell that should have bought an extra
command rather than fewer: the claim flattered its author's own change**, making a token they were adding look
overdue rather than new.

**And that tell does more work than any skill at spotting a lone source, which is the honest version of this
clause and its author's own correction of a compliment.** The same session **recognised the shape immediately on
a claim about somebody else's work** - an alarm resting on a display it knew could not distinguish two readings,
checked in three commands before sending - and **missed it entirely on the claim about its own change**, four
hours apart, same care. **One success in the easy direction and one failure in the hard one is not evidence that
anybody can tell which kind of claim they are holding; it is evidence that a foreign claim is easier to see than
one's own.** So the clause promises less and holds: **the claims you will fail to recognise as single-source are
the ones that suit you** - which is why the flattery tell fires exactly where recognition does not. *(Developer
4's, correcting both a flattering framing offered to it - including by me - and then my compliment about its
recovery. It preferred the clause promise less than claim a discrimination it had demonstrated once, in the easy
direction.)*

**Those two and the two above are one subject, which is worth naming rather than leaving a reader to notice: how
a true-looking thing gets believed.** An **absence** manufactured by a dead instrument; a **conflict** that was
two instruments measuring different populations; a **relay** nobody could audit; and a **lone source** that reads
like a corroborated one. **Every wrong answer on 2026-09-23 came from a single source nobody crossed** - a grep, a
probe, a note, a filter, a header - **and the ones that were caught were caught because a second instrument
existed, not because anyone was more careful the second time.**

**A GATE-TOOLING CHANGE IS NEVER EXERCISED BY ITS OWN GATE**, so its acceptance is the self-test plus
**the first run after it lands** - never the landing run itself. `gate.sh` runs from the **main
checkout**, not from the merged tree the gates judge, so a change to it takes effect only once it is on
`develop`. **A reader who checks the introducing branch's own log for the new behaviour and does not find
it will conclude the change is broken**, and will be wrong for a reason that is invisible from the diff.

**The author is as exposed as the reader**: the session that added a `SELFTEST=` line to the gate's
summary said in writing that it expected to read that line in the gate which introduced it, and was
corrected by its own red run producing no such line. **So the acceptance has two halves and the second
one happens after the landing**: confirm the new behaviour on the next gate anybody runs, and treat its
absence THEN as the real finding.

**AN OVERLOADED EXIT CODE CANNOT CARRY A PROOF.** Two conditions sharing a number is fine for a human
reading a message and **useless for a check that must decide.** The probe below required `exit 2` from a
script where **2 meant both *the board could not be read* and *your argument was refused*** - so on any
unreadable board **it passed against precisely the copy it exists to detect.** Refusal now has a code of
its own, and the rule it satisfies is the general one: **a probe requires the value that no FAILURE can
produce**, because that is the only value whose presence proves the thing the probe is asking about.

**And the reason it survived three chances to be seen is worth more than the fix: WHEN A CHECK'S CAREFUL
USERS COMPENSATE FOR ITS WEAKNESS, THE RECORD SHOWS A PASS AND THE WEAKNESS NEVER SURFACES.** Its first
outside adopter **did not rest on the exit code at all** - it read the message, which names the caller's
own argument back and which an old copy cannot produce - **and it reported what it had read rather than
that the probe had passed.** Had it written *the probe passed*, the artefact would have recorded a pass
and the hole would still be there. **The author of a rule is the worst-placed person to notice their own
instance of it**: the same session wrote the script, the prose and the clause, and missed it in all three.

**A NEW STRICT FLAG IS NOT TRUSTWORTHY UNTIL THE COPY ANSWERING IT HAS BEEN PROVEN TO KNOW IT**, and this
belongs here rather than with the fail-open clauses because **the defect is not that the check read the
wrong state - it is that the instrument is not the one you think you are holding.**

**Every strict mode added to an existing CLI is fail-open against every copy that predates it**, because
**ignoring an unknown argument is the universal default**. The new flag's whole value is an exit code, and
an old copy returns the reassuring one. **The proof is the same shape as every other liveness proof: feed
the instrument something it MUST refuse, and require the refusal.**

    bash who-is-gating.sh --probe-unsupported > /dev/null 2>&1
    [ $? -eq 2 ] || { echo 'this copy predates the flag; its exit code means nothing'; exit 3; }

**The author's own instance, attached because a rule carrying its author's mistake survives a reader who
thinks it does not apply to them**: an hour after building `--require-free`, I ran it from the main
checkout, read `exit 0` as *the board is free*, and only then noticed **the flag was not on `develop` at
all**. The board was free and the printed `GATING=none` said so - **but the exit code could not have told
me otherwise, and I had reached for the exit code precisely because I had just made it mean something.**
Measured both ways: the same nonsense argument exits 0 against the old copy and 2 against the new one.
**A new instrument is most trusted in the hour it is built.**

**Its sibling is clause 7 under *Guards that cannot fail*, and the difference is worth holding.** That clause is
about a floor set on a quantity that does not scale with the work, so the failure mode satisfies it. This one is
about an instrument that never reached its subject at all. **A guard can be well-founded and still be read through
a dead instrument**, which is why the two do not substitute for each other.

## Records that were right when written

**The shape: correct when written, conclusion intact, premise moved underneath - and nothing in the artifact says which of its parts went stale.** This is not a wrong record and it is not a stale cache. It is advice that earned its place by solving a real problem, still reads as authoritative, and is now the cause of the next failure. **It is more dangerous than a bad instruction from a stranger, because it arrives pre-trusted** - and if it is your own note it arrives with the authority of a lesson you remember learning.

Four instances in one night, three of them caught only because somebody volunteered them:

- **The chained fast-forward.** A note said to chain reset, merge, gates and `--ff-only` into one command, so `develop` could not move between gating and landing. Correct about its problem. Two days later, following it moved `develop` under another session's open window: the chained write lives inside a process that a stop cannot recall, and `--ff-only` refuses a *moved* base while landing happily on an *unchanged* base whose window is merely open. **The remedy that solved the old failure was the mechanism of the new one.**
- **Archived versus write-once.** A survey found unguarded staleness only in paths that all happened to sit in one archived spec. **The evidence was equally consistent with "archived" and with "write-once", and the stronger reading was reported as the finding.** 45 became 69 within hours, in live specs. Its companion: a clean single-commit attribution reported after one task was a property of *that window*, not of the technique, and **nothing about a saved baseline announces that it has stopped buying attribution.**
- **A superseded counterfactual.** A deferred-reference entry argued that a dependency from `History` would have made a reference *permanent*, because History was the one area with no extraction scheduled. The user then scheduled it. **The entry's measurement still held, its conclusion still held, and only its illustration had inverted** - History now satisfies the rule it had been the counterexample to.
- **A queue count.** After a partial landing, three commits remained where two sessions both believed five, carried for an hour by both, until a script printed `commits to land: 3`. **A partial landing silently resizes the queue.**

**The remedy is to split a record into its parts before trusting or fixing it**, because they fail independently:

1. **The measurement** - what was counted, and by what command. Usually still true.
2. **The conclusion** - what was decided from it. Often still true.
3. **The premise** - what had to remain the case for the conclusion to follow. **This is what moves, and it is the part a record states least explicitly.**

Fix only what moved. Rewriting a whole entry because one clause went stale destroys the parts that were right, and **a wholesale "the old advice was wrong" loses the reason it was ever adopted** - which invites the next reader to rediscover the original problem and re-apply the harmful remedy. **Keep the superseded reasoning and label it superseded.**

**Ship the instrument with the figure.** A count carried in prose is unfalsifiable a week later; the same count with its command and its date is re-checkable in seconds, so a reader quoting a stale number is quoting a timestamp. This is the *terminal `RESULT=` line* discipline in a different medium: **do not report a value nobody can recheck.**

**And a restatement is not the fact it restates - which is a different failure from the one above, because here
the instrument was right and available the whole time.** Two instances, 2026-09-23. **The gate tell reported
`develop f0ad424b` while develop was at `751705d7`** - and the tell had not been consulted for that number; it
was **retyped from memory into a message**, by the session that had built the tell. **And `highlight.ts`'s doc
line for a constant disagrees with the expression four lines below it**, wrong in two directions at once - it
calls a floor a fallback and an absolute width a delta, against `Math.max(declared + 1, HIGHLIGHT_STROKE_WIDTH)`
- and the only declared width it is accidentally right about is the one where its two errors cancel. **In both
cases a copy drifted from its original, and in both cases the check was to read the original rather than to
trust the copy.**

**The tempting lesson is the wrong one and it is expensive: this is not evidence that the instrument is
unreliable.** The tell was correct and one command away throughout; the doc line sat four lines from the
expression that refutes it. **What drifted was the retelling**, so the remedy is to read the original at the
moment of quoting - not to hedge the instrument, and not to add a second instrument to check the first.
**Applies identically to a quoted SHA, a paraphrased constant, and a requirement's cited example.**
*(Architect 2, arguing for one entry with two instances rather than two entries - and the first instance is its
own report of somebody else's slip, which is why the pair was visible at all.)*

**A corollary about the tell specifically, from its first use in anger: the tell gave the base and `git
merge-base` said the base was stale, and neither alone would have.** The tell without the check reads as *a gate
is running, wait*; the check without the tell has no base to test. **A reader who has only one of the two has
half an instrument**, which is worth saying because the tell is the newer half and the tempting one to quote by
itself.

**And a note may never have been your reasoning at all.** The project memory store is shared: of 54 notes, 41 carry an `originSessionId` naming twelve distinct sessions. Those arrive in context before a session begins, with attribution stripped, reading as background rather than as somebody's argument - **the standing we refuse to grant a peer's message, granted automatically because it came in a file.** The mirror matters too: **correcting such a note in place leaves your reasoning under the original author's id**, so say in the note that the reasoning changed hands.

**A NOTE CANNOT REPORT ITS OWN STALENESS**, so a clause is cited by reading develop's copy and never from the
queue note that proposed it. `git show develop:<path> | grep -c '<phrase>'` answers *is it landed*; reading
your own note answers *did I mean to land it*, which is a different question that feels identical. **A false
citation is load-bearing: a reader who believes a clause is in steering will not queue it, and the entry then
looks discharged from both ends.** This is not a rule about carelessness - **the property you want, *is this
still true*, is not one the artifact can carry.**

**A second face: a measurement can be stale the moment it is PARAPHRASED, even by the person who took it.** A
grep returned 9; it was paraphrased as "nine places", told to two sessions, and repeated back as an argument.
Attributed to the entries they sat under, the truth was **seven entries across twelve lines** - and the commit
message then said "twelve separate sentences", which a line count does not measure either. **A count of
matching LINES answers a different question from a count of PLACES, and the paraphrase is where the question
changes, silently, with nothing recording that it did.**

**Five instances in two days, all found by reading the tree and none by re-reading the note**: ten clauses that
were eight, thirteen queued that were four, a branch described as next when it had landed, the nine-places
paraphrase - and, writing the clause above, **a queue note whose own placement guidance named the wrong
section**, which sent an insert into an unrelated parent with every assertion green. **The note that carries
this rule was itself the fifth instance of it.** It also has a success to its name, which is the reason to
trust the instrument rather than the recollection: a clause was nearly repaired on develop today because a
queue note said it overclaimed, and reading develop's copy showed the overclaiming form had never landed
there at all.

**An explanation's thoroughness is evidence about its HISTORY, not about its correctness - a comment's density
maps past incidents rather than present danger.** `highlight.ts` explains at length why the selection paint is
inline and why it sits on the drawn shape: **both were got wrong once, on screen, at a day's cost.** Four lines
below, the doc line for the width constant was wrong in two directions at once - *when no width is stated*
describes a fallback where the code has a **floor**, and *how much heavier* describes a delta where the value is
**absolute** - against `Math.max(declared + 1, HIGHLIGHT_STROKE_WIDTH)`. **The one declared width it is
accidentally right about is where its two errors cancel.**

**Why it survived: the line never caused a visible failure, so nobody ever had a reason to read it against the
code four lines below.** One session edited that file three times in a day and quoted the constant twice without
reading the prose; another quoted it correctly in a requirement **by reading the expression** and still did not
notice the sentence beside it. **The well-explained parts of a file are precisely the parts that once failed in
production; the unexamined part is the part that never has.** The check is cheap and narrow: **when you touch a
constant, read its own doc line against its own expression, in the same minute** - not the file's much-revised
explanations, which have been read many times, but the one sentence nobody has had a reason to doubt.
*(Architect 2's. Ruled to sit BESIDE the absence clause rather than be folded into it: that one is about what a
clean reading proves, this one about where attention goes, and a reader could act on either without the other.)*

**A hunt's record names what was checked and EXCLUDED, not only what was found - because the most attractive
wrong answer is what the next person will land on first.** While looking for an inherited fill, Developer 4
found that **every declared element renders an unclassed, empty `<text>` node**: at
`DiagramCanvas.tsx:2449`, `const label = type?.labels !== undefined ? "" : (element.label ?? "")`, so a type
declaring its own labels still gets the element's `<text>`, classless and contentless, beside the real
`library-element-label canvas-node-label` one. **It paints nothing and it is not a defect.** But SVG `fill`
inherits, there is one of these inside every declared element on the board, and **it is what a mounted
assertion hits before the selector is scoped** - which is precisely the moment somebody is hunting an inherited
fill.

**Recording it as checked-and-excluded is worth more than the shrug it deserves on its own**, and the
surrounding state is why: a label measured in the browser as computed `rgb(0,0,0)` when **neither
`--color-text` nor its fallback is black**, three source-level routes excluded by reading them rather than
guessing, **and the route unfound with no fourth invented to close it.** The browser instrument was
itself suspect at the time, so the measurement's state is *unknown* rather than wrong. **An open hunt with
three excluded routes and one honest gap is a better artifact than a closed one with a plausible answer** - and
the excluded routes are the part that stops the next session repeating the first hour.

**It resolved within hours, and the resolution is why the honest stop was not merely polite.** Re-measured by
**grouping the text nodes by class** instead of taking one from the element's group: the labels compute
`rgb(15,23,42)`, which is `--color-text`, and **the eight unclassed nodes compute `rgb(0,0,0)` and are all
empty.** The original probe had taken one of those eight. **The label was never black**, there was no fourth
route, and **the real figure is 1.17:1 against the 1.37:1 an approved specification had recorded.** So
continuing would have been hunting something that did not exist: **stopping was CORRECT, not just candid**,
and a session that had invented a fourth route would have found a plausible one and closed a hunt on a
measurement of invisible nodes. **The same trap caught the specification's author and then the first mounted
assertion written against it, inside an hour.**

**A gate log names a test as it WAS, not as it is** - the same defect as a comment citing line numbers in its
own file, one directory over. A census offered as evidence for a subscription-ordering hypothesis rested on two
failures of `SolutionWatcherTests.ABurstOfChanges_SettlesIntoOneReport`. **That test does not exist**: it was
renamed and rewritten as `ABurstOfChanges_CollapsesAndThenStops`, and both failures predate that fix by hours -
it had been flaky for asserting machine behaviour rather than a contract, was diagnosed as exactly that, and
closed a day before it appeared in a table as evidence about something else. **A failure name from a log is a
citation of a thing that may since have changed**, so resolve it against the tree before it carries weight.
*(Architect 1's, volunteered with two other defects in the same table: the mechanism could not arise, and three
of the four components on its clean side had no tests at all - a control arm that cannot express the outcome.)*

**And a file git calls modified while `git diff` shows NOTHING is not a git fault.** It is a working tree at LF
under a CRLF house style: `diff` normalises through `text=auto eol=crlf` and the stat entry does not, and
`git update-index --refresh` does not clear it. **Settle it by byte comparison** - `git show HEAD:<path>` against
the file, comparing lengths, CRLF counts and equality after `\r\n`→`\n`; the healthy reading is *index 0 CRLF,
working tree many, identical after normalisation*, which is `i/lf w/crlf` stated in bytes. **The fix is
`git checkout -- <path>`**, which rewrites the file from the index with the filter applied and leaves a clean
status; nothing is lost, because the content was already identical. `cp` and any regenerating tool are the usual
culprits, and it matters beyond tidiness: **a dirty tree makes `gate.sh` refuse with
`RESULT=gates-changed-tracked-files`**, so an invisible line-ending difference blocks a landing for a reason that
looks like nothing.

## A settled boundary

Everything this document deferred on its first pass has moved: vendored example data, and structure.md's documentation standards.

**One thing was surveyed as a candidate and stays where it is.** `tech.md`'s *Testing & quality* is about the artefact rather than about how work moves. Two of its clauses are technical standards (a suite runnable inside the local F5 experience, fast local tests preferred over hosted end-to-end ones) and two are code conventions (`<Classname>.Tests` for the file and `<Classname>Tests` for the class - mind the dot; and the arrange/act/assert shape). Neither kind is process.

That is recorded as settled rather than as pending, because the reason is the boundary itself: **moving them would make this document the place things go when nobody is sure, which is how a steering document stops being read.**
