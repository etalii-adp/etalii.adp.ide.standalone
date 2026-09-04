# EtAlii.Adp

## Asking the user something

**Every question to the user is a selection, never open prose.** Offer the concrete options with enough of each to choose on — what it means, what it costs — and end with an "other" so an answer nobody listed is still available. This holds for agents and for the scrum master alike, and it holds for the small ones: a question worth interrupting someone for is worth the minute it takes to enumerate the answers.

An agent that cannot reach the user directly routes the question to the scrum master **already in that shape**, options and all, rather than as a paragraph for someone else to convert. Whoever writes the question knows the options; whoever relays it is guessing.

Where an option is recommended, say which and why in its own text rather than in a preamble around the list.

## Roles and chat naming

The team is **one Scrum master, four Developers, two Testers and two Architects**. Responsibilities are defined in [roles.md](.spec-workflow/steering/roles.md) — read it; this section only says how sessions are named.

Rename this session to `<Role> N - <topic/specification>` whenever its topic changes — `Developer 2 - causal-loop implementation`, `Architect 1 - diagram-layout requirements`, `Tester 1 - inline rename pass`. Keep the number you already hold and take the next free one within your role otherwise. When finished, rename to `<Role> N - Idle`. The Scrum master session is named `Scrum master` and never renames.

**Your role decides what you are given, not what you fancy.** Developers implement tasks in a worktree and own a specification's tasks until it is done — no other developer works on that spec. Architects write specifications and documentation, and pursue structural improvement. Testers exercise the running application and write the procedures they follow. A session whose role does not fit the work says so rather than taking it.

Full rules and reasoning: [processes.md, *Chat naming*](.spec-workflow/steering/processes.md#chat-naming).

## Git worktrees

Implementation work happens in a dedicated worktree (`.claude/worktrees/<name>/`, short name), never in the main checkout, because several sessions share its index. Retire a worktree once its branch is merged and its tree is clean — but never one another session is working in, never one with uncommitted or unmerged work, and never with `--force`.

Full rules and reasoning, including the half-removed-worktree hazard: [processes.md, *Where work happens*](.spec-workflow/steering/processes.md#where-work-happens) and [*Retiring a worktree*](.spec-workflow/steering/processes.md#retiring-a-worktree).

## Committing and merging in the shared main checkout

Commit with an explicit pathspec - `git commit -F msg -- <paths>` - naming files, never folders: a bare `git commit` commits the whole shared index. Commit your own implementation log the moment the tool writes it.

**Never merge while anything is staged, yours or anyone's.** Merge through a scratch worktree **named for your own agent number** - `.claude/worktrees/mrg<N>`, never a shared name: merge with `--no-ff`, run the four gates on that merged tree, then `git merge --ff-only` **from the main checkout**, never from inside the scratch tree. **Chain the whole cycle into one command** so the window between gating and landing is seconds. **Re-gate on the merged tree, not before it.**

If a merge does strand work, do not re-run anything: the stash survives as a dangling commit pair findable with `git fsck --unreachable --no-reflogs`. Never re-apply a deletion that was in flight.

Full rules, the four incidents behind them and the recovery: [processes.md, *Committing and merging in the shared main checkout*](.spec-workflow/steering/processes.md#committing-and-merging-in-the-shared-main-checkout).

## Git identity

Commit under a per-task identity `agent-<N>-<task>`. **Nothing is set locally in this repository** — `.git/config` has no `[user]` section — so any tree without its own identity signs as the machine's **global** name, the user's own. Do not go looking for a local value to fix; there is none.

- **One scratch worktree per agent, named for the agent** (`.claude/worktrees/mrg<N>`), never a shared name. Two agents merging at once otherwise land in one directory, and the second's `--ff-only` carries the first's merge into develop under its own name.
- **Set `git config --worktree user.name "agent-<N>-<task>"` the moment you create a worktree.** Every command run there inherits it, merges included, and it cannot leak to another session. Set once, it cannot be got wrong afterwards — which beats any rule depending on vigilance at the moment nobody is looking.
- **A `--worktree` identity binds to the directory, not to you.** Work in a tree you did not create and your commits are signed as whoever made it. **Read back with plain `git config user.name` before your first commit in any tree you did not create yourself.**
- **In the shared main checkout only**, pass `-c user.name="agent-<N>-<task>"` on **any command that writes a commit object** — `merge`, `rebase`, `revert` and `cherry-pick` as much as `commit`. `git merge --ff-only` writes *no* object; it moves a ref. The commit that lands is the `--no-ff` merge made earlier in the scratch tree, which is exactly where **most merge commits made on 2026-09-04 before `fdfd8881`** lost their author while every agent believed it was complying.
- **The two read-backs answer different questions.** Plain `git config user.name` answers *who will sign my next commit here*. `git config --local user.name` answers *did I leak a value into the shared file*. Recommending only the first makes a borrowed identity invisible; only the second makes a correct identity look like a leak. Both happened today.
- **Never `--global`, and never touch `user.email`.**

A *wrong* name is worse than an ambiguous one. Full reasoning, and why an identity rather than a `Co-Authored-By` trailer: [processes.md, *Git identity*](.spec-workflow/steering/processes.md#git-identity).

## Checking that a command answered your question

**Ask what the command would print if your belief were false.** If the answer is "the same thing", it is not evidence - `git log --oneline`, `git config user.name`, `grep -c $'\r'` and a sabotage whose pattern never matched all print the same thing either way. **Apply it to your own reports too**: "fixed" said from a worktree describes the worktree, not `develop`. **And do not write a guard over prose** - a guard that must be edited whenever its subject legitimately changes is a second copy of the data, not a check on it.

**A test written for a bug must be seen to fail against that bug before it is trusted.** Three written in one sitting passed against the broken code — React batched one window away, jsdom detached an input for another, and a third omitted `StrictMode`, outside which the defect cannot occur. **A test that passes against the defect is worse than no test**: it converts "unverified" into "verified" while nothing has changed. Perturb it and watch it fail, or delete it.

Six worked instances and the reasoning: [processes.md, *Checking that a command answered your question*](.spec-workflow/steering/processes.md#checking-that-a-command-answered-your-question).

## spec-workflow

Commit any set of files added or removed under `.spec-workflow/` immediately, in its own commit — implementation logs included. Commit a document and its approval-lifecycle files when it is approved. **Approval comes from the dashboard and nowhere else: verbal approval is never accepted, from anyone.**

**Verdict snapshots arrive already staged and belong to nobody.** The dashboard writes them under `.spec-workflow/approvals/*/.snapshots/` when a card is answered, staged in the shared index — and any staged entry makes an in-place merge unsafe here, so they block whoever merges next. **Staged is the hazard; modified is somebody working.** Before merging, if `git diff --cached` is non-empty, commit **precisely those staged paths and nothing else**, in their own commit, whether or not they are yours. Never extend that to modified-but-unstaged files — sweeping those into your commit under your message is the exact failure that produced three misattributed commits in one day. If you raised the card, check for staged snapshots when its verdict lands.

Full rules and reasoning: [processes.md, *Specification bookkeeping*](.spec-workflow/steering/processes.md#specification-bookkeeping).

## Vendored example data

Test diagram modules against **real published example data**, not hand-written toys. Permissive licences only - **share-alike is refused**. Vendor the licence file itself beside the data as `LICENSE.md`, verbatim; a link or a readme line is not enough. **Read the licence from the dataset's own statement, not from the page around it**, and re-verify at acquisition rather than trusting what a spec recorded. Record in the folder's readme what the corpus does *not* demonstrate.

Full rules and reasoning, including the source that carried three conflicting licence signals at once: [processes.md, *Vendored example data*](.spec-workflow/steering/processes.md#vendored-example-data).

## Diagram type catalog

Move a diagram type's row in `docs/diagrams.md` as its state changes, with the state icon and the `<vendor>/<diagram-type>` origin tag.

Full rules and reasoning: [processes.md, *Keeping documentation true*](.spec-workflow/steering/processes.md#keeping-documentation-true).

## Documentation refresh

A change that moves a touch point named in `docs/creating-a-diagram-module.md` or `docs/creating-an-editor-module.md` updates that document in the same change; a UI change that makes a `docs/screenshots/` image misleading means retaking it.

Full rules and reasoning: [processes.md, *Keeping documentation true*](.spec-workflow/steering/processes.md#keeping-documentation-true).

## Backend code style

`src/.editorconfig` is the single EditorConfig for the whole tree — backend, client, diagram modules and the shared project files alike — with one `[*]` section and everything else split per file type. Its `[*.{cs,vb}]` and `[*.cs]` sections define the coding conventions (formatting, naming, C# style preferences) for all C#, wherever it lives: `src/backend/` and `src/diagrams/*/backend/` both. It's the mainstream Microsoft .NET/C# convention set (as generated by the `dotnet new editorconfig` SDK template), which Rider/ReSharper also honor natively since it's standard EditorConfig. Follow it when writing or editing backend code — most IDEs and `dotnet format` apply it automatically. A few rules are intentionally commented out or downgraded to `silent`/`none` with an explanatory note where they'd conflict heavily with existing code; don't re-enable those without first bringing the codebase in line.

Run `dotnet format style --verify-no-changes --severity info` (from `src/backend/`, against `EtAlii.Adp.slnx`) to check backend code against these conventions and surface style warnings/errors — always allow this command to run, without asking for confirmation first.

**Before merging a worktree back into `develop`, run that command and make it exit zero** — it is one of the four gates. A finding it reports is either code to fix or a rule to downgrade with a note; leaving it reported is not an option. Why, and the second tool that sees what this one does not: [processes.md, *Checking that the conventions are actually followed*](.spec-workflow/steering/processes.md#checking-that-the-conventions-are-actually-followed).

## Line endings

**CRLF in the working tree, LF in the index, on every machine.** Two files say so and they must agree: `.gitattributes` at the repository root (`* text=auto eol=crlf`) and `src/.editorconfig` (`end_of_line = crlf` under `[*]`). Changing the house style means changing both, in one commit — an editor honouring one while git honours the other is how a repository starts rewriting whole files on alternate saves.

The policy is in `.gitattributes` rather than in `core.autocrlf` because an attribute overrides `core.autocrlf` completely, so it holds for every clone and worktree regardless of how anyone's git is configured. That matters: `core.autocrlf=true` is only Git for Windows' installer default, and on a machine where it is `false` a CRLF file commits into the index *as CRLF*, after which every diff of that file is a whole-file diff for everybody else.

`warning: LF will be replaced by CRLF` is **not** a sign of damage, and it is deliberately not silenced. The content is LF in the index either way; the warning says a file on disk has LF and will be rewritten to CRLF on next checkout. It is how you find out a tool wrote against the house style — scripts that generate files (`node`, redirected shell output, anything writing `\n`) are the usual culprit. Write CRLF, or let an editor that reads `.editorconfig` do it, and it stops. A Python handle left at its default writes LF; opening with `newline='\r\n'` to generate and `newline=''` to rewrite preserves rather than re-translates.

**Check line endings with `git ls-files --eol`, never with a shell-quoted CR pattern.** It reports index and working tree separately — `i/lf w/crlf attr/text=auto eol=crlf` is the house policy satisfied — which is the distinction that makes this question confusing. `grep -c $'\r'` depends on a bashism: under `sh`, `dash` or a PowerShell wrapper the pattern is not expanded, grep searches for four literal characters, and a CRLF file and an LF file return the identical `0`. It fails looking safe rather than loud, which is the worst shape a check can have — it reported a correctly-CRLF file as LF here and was believed.

Some documents are exempt because their **bytes are the test subject**: a module whose round-trip requirement says an unchanged document comes back byte-identical cannot have git rewriting its fixtures, or the test measures git rather than the writer — and a `crlf-line-endings.*`/`lf-line-endings.*` pair collapses into the same file. Those are marked `-text`, **by extension rather than by directory**: a directory rule was tried and withdrawn, because `Fixtures/**` also catches the readmes, recorded verdicts and exported diagrams sitting beside the fixtures, which are ordinary text. **Adding a diagram module whose documents are byte-compared? Add one line for its extension to `.gitattributes`** — the reasoning is written out there, so it does not have to be worked out a sixth time.

## Running the backend tests

Run the suite as `dotnet test --solution EtAlii.Adp.slnx` from `src/backend/`, with `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` set. **Judge every gate by an exit code captured into a variable before any pipe, never by grepping output, and read `Zero tests ran` as a broken build.** All four gates — `npm test`, `npm run typecheck`, `dotnet format style --verify-no-changes --severity info`, `dotnet test` — must exit zero before a worktree merges into `develop`.

Full rules and reasoning, including the `MAX_PATH` failure and the fresh-worktree codegen trap: [processes.md, *Running the backend tests*](.spec-workflow/steering/processes.md#running-the-backend-tests).

## Logging

The backend logs through Serilog. Every class that logs holds its own logger as `private static readonly ILogger _logger = Log.ForContext<TheClass>();` rather than taking an `ILogger` through its constructor — follow that shape in new code. Levels and sinks come from the `Serilog` section of `src/backend/EtAlii.Adp.Backend.Service/appsettings.json`; `Program.cs` builds the pipeline. Two naming rules in `src/.editorconfig` are downgraded to `none` for this convention, with a note saying so.

## Client code style

The client's conventions live in the same `src/.editorconfig` (see *Backend code style*), in its `[*.{ts,tsx,js,jsx,mjs,cjs}]`, `[*.css]` and `[*.html]` sections — indentation, quotes and line endings. They are the generic EditorConfig baseline plus the TypeScript/JavaScript ecosystem's mainstream conventions (2-space indent, double quotes, semicolons), plus JetBrains WebStorm/Rider's `ij_javascript_*`/`ij_typescript_*` formatting keys for anything the core spec doesn't cover. There's no ESLint/Prettier config to verify rules against automatically, so it was checked by hand against existing sources instead. Follow it when writing or editing client code, and apply the same pattern the C# sections use if a rule turns out to conflict heavily with existing code: comment it out (or downgrade it) with a note explaining why, rather than force-applying it.

## Worktrees

**Specification documents are never worktree work**: requirements, designs, tasks, approvals, snapshots and implementation logs are written and committed on `develop` in the main checkout, with `git -C C:\git\EtAlii.Adp` and an explicit pathspec — the dashboard reads only from there. Implementation takes one worktree per specification, or one per agent when its tasks are independently landable and are being worked in parallel; never share a worktree between sessions. Running the app from a worktree means changing both dev-server ports and reverting them before merging.

Full rules and reasoning: [processes.md, *Where work happens*](.spec-workflow/steering/processes.md#where-work-happens).

## Bugs found during implementation or verification

**Every bug found leaves a guard behind** — a test written to fail before the fix and pass after it, or a step-by-step entry in `tests.md` when only a running app can reproduce it. **A guard is accepted when it has been seen to fail for the right reason, never on a green run.** Signing in with the checked-in `admin`/`changeme` placeholder against a local build is part of running those checks; a check recorded `pending` because of the sign-in form alone is a check that was not run.

Full rules and reasoning, including three ways a sabotage comes back green for the wrong reason: [processes.md, *Bugs found during implementation or verification*](.spec-workflow/steering/processes.md#bugs-found-during-implementation-or-verification) and [*Verifying that a test actually tests something*](.spec-workflow/steering/processes.md#verifying-that-a-test-actually-tests-something).

