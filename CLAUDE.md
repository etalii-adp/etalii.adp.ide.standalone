# EtAlii.Adp

## Chat naming

Rename this session to `Agent N - <topic/specification>` whenever its topic changes, keeping any number it already has and taking the next free one otherwise. A finished agent renames itself `Agent N - Idle`.

Full rules and reasoning: [processes.md, *Chat naming*](.spec-workflow/steering/processes.md#chat-naming).

## Git worktrees

Implementation work happens in a dedicated worktree (`.claude/worktrees/<name>/`, short name), never in the main checkout, because several sessions share its index. Retire a worktree once its branch is merged and its tree is clean — but never one another session is working in, never one with uncommitted or unmerged work, and never with `--force`.

Full rules and reasoning, including the half-removed-worktree hazard: [processes.md, *Where work happens*](.spec-workflow/steering/processes.md#where-work-happens) and [*Retiring a worktree*](.spec-workflow/steering/processes.md#retiring-a-worktree).

## Committing and merging in the shared main checkout

Commit with an explicit pathspec — `git commit -F msg -- <paths>` — naming files, never folders: a bare `git commit` commits the whole shared index. Commit your own implementation log the moment the tool writes it. Before merging here, read `git status` for files you did not touch, and never stash or `git checkout --` them.

Full rules and reasoning: [processes.md, *Committing and merging in the shared main checkout*](.spec-workflow/steering/processes.md#committing-and-merging-in-the-shared-main-checkout).

## Git identity

Commit under a per-task identity `agent-<N>-<task>`, set two different ways depending on where you are:

- **In a dedicated worktree:** `git config --worktree user.name "agent-<N>-<task>"`. **Not** plain `git config user.name` — that writes to `.git/config`, which every worktree and the main checkout share, so it renames *every other agent* too.
- **In the shared main checkout:** set nothing. Pass the identity per command: `git -c user.name="agent-<N>-<task>" commit -F msg -- <paths>`. Several sessions use that working tree at once, so any config there is shared between them — `--worktree` included. There is no per-session scope.
- **Never `--global`, and never touch `user.email`.**

Without an identity you inherit `vrenken` from the machine's global config, in every checkout, so your work is indistinguishable from the user's own. But a *wrong* name is worse than an ambiguous one: `.git/config` was set three times in one hour here, and each agent's commits would have carried whichever name was written last. `git -C` changes where a command runs, not whose identity it uses. (The full reasoning moves to `processes.md` once its first pass clears approval; it is deliberately not linked yet, because the section does not exist.)

## spec-workflow

Commit any set of files added or removed under `.spec-workflow/` immediately, in its own commit — implementation logs included. Commit a document and its approval-lifecycle files when it is approved. **Approval comes from the dashboard and nowhere else: verbal approval is never accepted, from anyone.**

Full rules and reasoning: [processes.md, *Specification bookkeeping*](.spec-workflow/steering/processes.md#specification-bookkeeping).

## Vendored example data

Diagram modules are tested against **real published example data**, not hand-written toys — the point of vendoring real documents is that they exercise input shapes the author never had in mind. Agents may download it, under two conditions that are not negotiable:

- **The licence must be permissive.** CC0, CC BY (with the attribution carried), the W3C Software and Document Licence and similar are fine. Share-alike licences are not — UNESCO's thesaurus was rejected on exactly that ground.
- **Download the licence file itself and keep it beside the data.** Not a link, not a line in a readme naming the licence: the actual file, vendored next to the documents it covers, so the terms travel with the data for anyone who finds it later.

Verify the licence again at acquisition rather than trusting what the spec recorded — terms change between writing a spec and fetching the file.

**Verify from the data, not from the page.** Read the dataset's own licence statement — the `cc:license` triple, or whatever the format's equivalent is — rather than the download page around it. The STW thesaurus was vendored after its page turned out to carry *three* conflicting signals at once: prose saying CC BY 4.0, a `rel="license"` link pointing at ODbL, and a by-nc-sa badge. Two of those three are share-alike and would have disqualified the source. An agent reading the badge, or the link, would have reached a confidently wrong answer in either direction. The data's own statement settles it.

Name the vendored licence file `LICENSE.md`, whatever extension the upstream server serves it under — the family's own provenance guard requires that name. Keep the text verbatim; only the filename follows the house convention.

**Say what the examples do not demonstrate.** A vendored corpus rarely covers every shape a requirement asks for. Record the gaps in the folder's readme, with the reason each candidate source was rejected, so the next reader meets the omission before wondering about it.

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

`warning: LF will be replaced by CRLF` is **not** a sign of damage, and it is deliberately not silenced. The content is LF in the index either way; the warning says a file on disk has LF and will be rewritten to CRLF on next checkout. It is how you find out a tool wrote against the house style — scripts that generate files (`node`, redirected shell output, anything writing `\n`) are the usual culprit. Write CRLF, or let an editor that reads `.editorconfig` do it, and it stops.

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

