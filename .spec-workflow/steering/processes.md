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

**Retire a worktree once its branch is merged into `develop` and its working tree is clean:** `git worktree remove .claude/worktrees/<name>`. One is created per piece of work, so without this they only accumulate — twenty-four had built up before anyone counted.

Four rules make that safe, and none is optional.

- **Never remove a worktree another session is still working in.** Merged and clean describe the *branch* and say nothing about whether somebody is sitting in the directory. List the live sessions first — their names match the worktree directory names — and leave those alone. Four live sessions were deregistered underneath their owners in one sweep, which is why this is first.
- **Never remove a worktree with uncommitted changes or unmerged commits.** Raise it for a decision. Housekeeping that destroys work is not housekeeping.
- **Removing a worktree does not remove its branch,** so a merged branch's commits stay reachable either way. Do not delete branches as part of this.
- **Removal often fails on Windows**, with `Filename too long` (deep `node_modules` paths) or `Permission denied` (a dev server, an IDE, or your own shell sitting inside the directory). Git still deregisters the worktree; only the directory deletion fails. **Never `--force`.** For `Permission denied`, move your shell out and retry. For `Filename too long`, delete the leftover directory by hand with a long-path-capable command — and look before deleting, because a leftover folder can still hold source.

**A half-removed worktree is worse than either outcome.** When the deletion fails, the directory survives without its `.git` file, so every git command run inside it walks up and resolves against the main checkout — `git status` there reports the *main checkout's* dirty files, and `git add -A` would commit another session's work. The files themselves are safe: copy anything uncommitted out, then start again with `git worktree add`. (`Filename too long` is git's own limit, not the OS one; it wants `core.longpaths=true`, which is the user's call to set.)

## Committing and merging in the shared main checkout

Two distinct hazards, and the rule for one does not cover the other.

**Commit with an explicit pathspec: `git commit -F msg -- <paths>`.** `git add <path>` stages one file, but a bare `git commit` commits *the whole index*, including whatever another session has staged into it. This has swept another agent's files into an unrelated commit under the wrong message and the wrong identity at least four times. "Stage carefully" is not the fix, because care is not what fails — **in this repository a directory pathspec is a sweep by construction**, since the main checkout nearly always holds somebody's uncommitted work. Name files, never folders.

**Commit your own implementation log the moment the tool writes it.** The other half of the same failure, and the half that is easier to miss: an uncommitted file left in the shared checkout is what turns somebody else's careless pathspec into a wrong commit message. The sweeper gets blamed; whoever left the file there supplied it. This is already what *Specification bookkeeping* asks for — it was being read as covering the documents rather than the logs the tool writes for you.

**Never merge while anything is staged - yours or anyone else's.** `git merge` requires the index to match HEAD; when it does not, git stashes the working state and, on the failure path, does **not** restore it. That silently reverted or deleted **26 paths belonging to three other sessions**, reporting only `Index was not unstashed. Merge with strategy ort failed.` - a single line for two dozen files of other people's work. Reading `git status` first is necessary and not sufficient: it tells you what a merge would *write*, not that attempting one is unsafe.

**Merge through a scratch worktree rather than waiting for a clean index.** The mechanism is the point: `git merge` builds the merge commit's tree *from the index*, which is why a foreign staged file blocks it and why its failure path is what dangles other sessions' work. **A fast-forward builds no tree from the index and does not care.** So create a scratch worktree at the develop tip, `git merge --no-ff` your branch there, run the four gates *in it* - on the actually-merged tree - then in the main checkout `git merge --ff-only` that scratch branch. The last step succeeds with foreign files staged, and leaves them staged and untouched. The resulting history is identical to merging in place, and the route never puts the main checkout in a state where git must save and restore anybody's work - so it **cannot** reproduce the incident rather than merely avoiding it. Waiting for a clean index is not a workable alternative: one staged file has sat in this checkout for over two hours.

**Re-gate on the merged tree, not before it.** An hour's wait is long enough for `develop` to gain code an earlier run never saw - in one case a 311-line integration test and three validator changes.

**If it does happen, the work is recoverable and re-running anything is the wrong move.** The stash survives as a dangling commit pair: `git fsck --unreachable --no-reflogs`, find `WIP on develop` and `index on develop` at the failed merge's timestamp, then `git checkout <wip-commit> -- <path>` per file. Restore only what was present, and **never re-apply a deletion that was in flight** - re-applying somebody's half-finished delete is the one direction that destroys rather than restores.

**Before merging in the main checkout, read `git status` for files you did not touch.** A merge writes every path that differs between the branch point and the tip *regardless of the index*, so uncommitted churn from another session will either block the merge or tempt you into `git checkout --` on files that are not yours. Neither stash nor checkout is acceptable there — both destroy in-flight work. Apply your own files with `git checkout <branch> -- <paths>`, commit by pathspec, then verify with `git diff <branch> develop -- <your paths>` that the result matches what the merge would have produced. The branch is then not recorded as merged, which is a small honest loss of history; say so in the report.

## Git identity

**Commit under a per-task identity, `agent-<N>-<task>`, set differently depending on where you are.**

- **In a dedicated worktree:** `git config --worktree user.name "agent-<N>-<task>"`. **Not** plain `git config user.name`, which writes to `.git/config` - shared by the main checkout and every worktree - and so renames every other agent too.
- **In the shared main checkout:** set nothing, and pass the identity per command: `git -c user.name="agent-<N>-<task>" commit -F msg -- <paths>`. Several sessions use that one working tree at once, so *any* config there is shared between them, `--worktree` included. There is no per-session scope, and `git -C` does not provide one - it changes where a command runs, not whose identity it uses.
- **Never `--global`, and never touch `user.email`.**
- **Read it back with `git config --local user.name`.** Inside a worktree the plain form resolves through `config.worktree` and returns your own correctly-scoped identity, which looks identical to a leaked shared value - that has already prompted an unset nobody needed. Only `--local` shows what is actually in the shared file.

Without an identity you inherit the machine's global `user.name` in every checkout, so your work is indistinguishable from the user's own. The first version of this rule used plain `git config`; four sessions overwrote each other and two commits carry the wrong author.

**Why an identity rather than the `Co-Authored-By` trailer**, which was the alternative considered: the trailer is a per-commit act that can be forgotten, and it is forgotten *structurally* - present exactly when composing a message was a deliberate act, absent whenever the message came for free. `git commit -m "one line"` and `git merge --no-edit` are both ways of not writing one, which is why bookkeeping and merges - the two highest-frequency commit kinds - are the two that fail. Nine of twelve recent merges lacked it. An identity is set once per working tree and cannot be forgotten per commit: one is a discipline, the other is a property.

The trailer remains useful in one direction only, and the limit matters because **the inverse reading is the more useful one**. A present trailer proves an agent made the commit. An absent one proves nothing at all - so "which of these did a human do?" is exactly the question it cannot answer, and a test reliable only in the less useful direction will be used in the other one.

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

## Running the backend tests

The test projects run on xUnit v3, which uses Microsoft.Testing.Platform rather than VSTest. Two consequences:

- Run them as `dotnet test --solution EtAlii.Adp.slnx` from `src/backend/`. Passing the solution positionally is rejected under this runner.
- `src/global.json` carries the `"test": { "runner": "Microsoft.Testing.Platform" }` opt-in the .NET 10 SDK requires; without it `dotnet test` refuses to run any test project.

Each test project is therefore an executable (`<OutputType>Exe</OutputType>`) — a v3 project hosts its own tests, and new test projects need that too.

**That makes the suite sensitive to Windows' 260-character `MAX_PATH`.** MSBuild's `Exists()` silently returns false above 260 characters, so in a deep checkout the SDK skips `_CreateAppHost`, `apphost.exe` is never produced, and — since a v3 test project's apphost *is* its test host — `dotnet test` reports `Zero tests ran` project after project instead of failing the build. **Read `Zero tests ran` as a broken build, never as an empty suite.** `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled` must be `1`, worktree names must be short, and `src/Directory.Build.targets` turns the failure into an explicit `ADP0001` error rather than a cryptic `MSB3030`.

**Judge every gate by an exit code captured into a variable before any pipe.** In a pipeline `$?` is the last command's status, so `dotnet test … | tail` reports `tail`'s success and a crashed or zero-test run reads as green. A zero-test run does exit non-zero — 5 for zero tests, 8 for a filter matching nothing — so the exit code carries the information that grepping the output destroys. `.github/workflows/build.yml` embodies this: every gate step is judged by its exit code and nothing greps output.

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

- `git log --oneline` prints an identical line whoever authored it.
- `git config user.name` prints the effective value without its source - so it cannot distinguish your own correctly-scoped identity from one you inherited.
- `grep -c $'\r'` prints `0` for a file with no carriage returns **and** for a shell that never expanded the pattern. It reported a correctly-CRLF file as LF.
- `md5sum` over whole files reports drift when only a namespace line differs.
- `git worktree add` fails where a chained `cd` cannot see it.
- A sabotage whose replacement pattern never matched leaves the suite green, which reads as robust code rather than as a test that never ran.

**It is the exit-code rule applied to the other half of a command's output.** That one says do not read success from what a command printed; this one says do not read a fact from a command that was never asked for it. It costs a sentence of thought rather than a second command, and it is a sharper instrument than "verify before believing" - which everybody agrees with and nobody applies under pressure - because it converts a vague duty into a specific question with an answer.

**Two of the six are worth their own paragraph, because both are about a command answering for something other than what you ran.**

**An exit code reported by a wrapper is not the exit code of the thing you ran.** A backgrounded suite's task notification carries the *wrapper shell's* status, not the runner's - so a notification saying `exit 0` sits above a log saying `TESTS_EXIT=1`, and both are accurate about different things. The agent that hit this caught it by reading the log rather than the notification. Ask the falsifying question of the notification itself: what would it say if the suite had failed? The same thing, because the wrapper still exited cleanly.

**A fresh-tree build is not a stricter gate - it is the only one that reads a different input.** Every other gate reads `obj/`, which is a cache of the last *successful* generation, so `dotnet test` prints the same green whether the generated code is current or stale. A generated-code break is therefore invisible to it **by construction**, not by bad luck: nine sessions went green through a window in which `develop` compiled in no fresh checkout. This is the same question asked of a build's input rather than a command's output - *what would this read if the generated code were broken?* The same cache - and the only way to change the answer is to build a tree that has none.

**Apply it to your own reports, not just to commands.** "Fixed" said from a worktree describes the worktree, not `develop` - and what you would have seen if it were *not* landed is exactly what you did see, the edited file in front of you. Say where a change is: committed on a branch, merged, or pushed.

**And do not write a guard over prose.** A guard on an owner string, a comment or a description fails when the text legitimately changes, so it must be edited in the same commit as the thing it guards - which makes it a second copy of the data rather than a check on it. Notice rot during work and fix it; do not automate an assertion about wording.

**These are one idea at five scopes** - a command, a wrapper reporting on a command, the cached input a command reads, a claim about your own work, and a check written down - and they move together. Splitting them by which was learned first would break the argument.

## A settled boundary

Everything this document deferred on its first pass has moved: vendored example data, and structure.md's documentation standards.

**One thing was surveyed as a candidate and stays where it is.** `tech.md`'s *Testing & quality* is about the artefact rather than about how work moves. Two of its clauses are technical standards (a suite runnable inside the local F5 experience, fast local tests preferred over hosted end-to-end ones) and two are code conventions (`<Classname>.Tests` for the file and `<Classname>Tests` for the class - mind the dot; and the arrange/act/assert shape). Neither kind is process.

That is recorded as settled rather than as pending, because the reason is the boundary itself: **moving them would make this document the place things go when nobody is sure, which is how a steering document stops being read.**
