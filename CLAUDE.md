# EtAlii.Adp

## Asking the user something

**Every question to the user is a selection, never open prose.** Offer the concrete options with enough of each to choose on — what it means, what it costs — and end with an "other" so an answer nobody listed is still available. This holds for agents and for the scrum master alike, and it holds for the small ones: a question worth interrupting someone for is worth the minute it takes to enumerate the answers.

An agent that cannot reach the user directly routes the question to the scrum master **already in that shape**, options and all, rather than as a paragraph for someone else to convert. Whoever writes the question knows the options; whoever relays it is guessing.

Where an option is recommended, say which and why in its own text rather than in a preamble around the list.

## Chat naming

Rename this session to `Agent N - <topic/specification>` whenever its topic changes, keeping any number it already has and taking the next free one otherwise. A finished agent renames itself `Agent N - Idle`.

Full rules and reasoning: [processes.md, *Chat naming*](.spec-workflow/steering/processes.md#chat-naming).

## Git worktrees

Implementation work happens in a dedicated worktree (`.claude/worktrees/<name>/`, short name), never in the main checkout, because several sessions share its index. Retire a worktree once its branch is merged and its tree is clean — but never one another session is working in, never one with uncommitted or unmerged work, and never with `--force`.

Full rules and reasoning, including the half-removed-worktree hazard: [processes.md, *Where work happens*](.spec-workflow/steering/processes.md#where-work-happens) and [*Retiring a worktree*](.spec-workflow/steering/processes.md#retiring-a-worktree).

## Committing and merging in the shared main checkout

Commit with an explicit pathspec - `git commit -F msg -- <paths>` - naming files, never folders: a bare `git commit` commits the whole shared index. Commit your own implementation log the moment the tool writes it.

**Never merge while anything is staged, yours or anyone's.** A failed merge stashes the working state and does not restore it; that lost 26 paths of three sessions' work in one line of output. **Merge through a scratch worktree named for your own agent number — `.claude/worktrees/mrg<N>`, never a shared name.** Two agents merging at once otherwise land in one directory, and the second one's `--ff-only` carries the first one's merge into develop underneath its own — gated together, attributed to one landing, and nothing looking wrong afterwards. That has already happened once. An "already exists" worktree may be someone's live tree rather than an abandoned husk: take a different name, never reset what is there. Prefer `git -C <path>` over `cd`, so a failed `worktree add` cannot leave the next command running somewhere silently wrong.

In it: merge your branch with `--no-ff`, run the four gates on that merged tree, then `git merge --ff-only` it in the main checkout - a fast-forward builds no tree from the index, so foreign staged files neither block it nor get touched. **Re-gate on the merged tree, not before it.**

**Chain the whole cycle into one command** — `reset --hard develop`, merge, four gates, `--ff-only` — so the window between gating a tree and landing it is seconds. With several sessions committing minutes apart and a gate run taking about three, a `--ff-only` issued separately is routinely refused; one agent was refused four times running. Refusal is the mechanism working: twice, develop had gained code the earlier gate never saw. **Run that final `--ff-only` from the main checkout, never from inside the scratch worktree** — run in the wrong place it merges the branch into itself and prints `Already up to date`, a success message for something that did not happen.

If it does happen, do not re-run anything: the stash survives as a dangling commit pair findable with `git fsck --unreachable --no-reflogs`. Never re-apply a deletion that was in flight.

Full rules, the incident and the recovery: [processes.md, *Committing and merging in the shared main checkout*](.spec-workflow/steering/processes.md#committing-and-merging-in-the-shared-main-checkout).

## Git identity

Commit under a per-task identity `agent-<N>-<task>`, set two different ways depending on where you are:

- **In a dedicated worktree:** `git config --worktree user.name "agent-<N>-<task>"`. **Not** plain `git config user.name` — that writes to `.git/config`, which every worktree and the main checkout share, so it renames *every other agent* too.
- **The rule is about writing a commit object, not about `git commit`.** `merge`, `rebase`, `revert` and `cherry-pick` all write one and all take the ambient identity. Guarding `git commit` alone is why **45 of 61 merge commits in one 18-hour stretch were authored with the machine's global name** while every agent believed it was complying.
- **In the shared main checkout:** set nothing; pass the identity on every such invocation — `git -c user.name="agent-<N>-<task>" merge …` as much as `… commit …`. Note that `git merge --ff-only`, the landing step, **writes no commit object at all** — it moves a ref. The commit that lands is the `--no-ff` merge made earlier in the scratch worktree, where it feels like scratch work and nobody is thinking about attribution. That is exactly where the names were lost.
- **A `--worktree` identity belongs to the directory, not to you.** Work in a worktree you did not create and your commits are signed as whoever did. That is why the scratch worktree must carry your own agent number: one shared name produced a wrong location *and* a wrong author at once, coherently enough that the agent involved disclosed a collision to the wrong agent entirely. Set `git config --worktree user.name` when you create a worktree; a worktree without one signs commits as the machine's global name — the user's own name, in the user's own history.
- **Two read-backs, and they answer different questions.** `git config --local user.name` answers *did I leak into the shared file* — it shows only `.git/config`. Plain `git config user.name` answers *who will sign my next commit* — it resolves through `config.worktree`. Use `--local` after setting an identity; use the plain form after entering a worktree you did not just create. Several sessions use that working tree at once, so any config there is shared between them — `--worktree` included. There is no per-session scope.
- **Never `--global`, and never touch `user.email`.**
- **Read it back with `git config --local user.name`, not `git config user.name`.** Inside a worktree the plain form resolves through `config.worktree` and returns your own correctly-scoped identity — which looks identical to a leaked shared value and has already prompted an unset that was not needed. Only `--local` shows what is actually in the shared `.git/config`.

Without an identity you inherit the machine's global name in every checkout, so your work is indistinguishable from the user's own - and a *wrong* name is worse than an ambiguous one. Full reasoning, and why an identity rather than a `Co-Authored-By` trailer: [processes.md, *Git identity*](.spec-workflow/steering/processes.md#git-identity).

## spec-workflow

Commit any set of files added or removed under `.spec-workflow/` immediately, in its own commit — implementation logs included. Commit a document and its approval-lifecycle files when it is approved. **Approval comes from the dashboard and nowhere else: verbal approval is never accepted, from anyone.**

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

