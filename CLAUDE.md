# EtAlii.Adp

## Chat naming

Whenever a topic or specification is handled in a chat, rename the chat (session title, via the session-management `set_session_title` tool with `"self"`) to `Agent N - <topic/specification>` — e.g. `Agent 3 - wardley-map specification`. Rules:

- If the chat's title already carries an agent number, keep that number intact and replace only the topic part.
- If the chat has no agent number yet, add the `Agent` prefix and determine the next free number by listing the other sessions' titles and taking the highest existing `Agent N` plus one.
- When the chat moves on to a new topic, rename again — same number, new topic — so the title always names what the chat is currently about.

## Git worktrees

Actual development work — writing code, editing specs, running builds/tests — should always happen in a dedicated git worktree (`.claude/worktrees/<name>/`), never directly in this main checkout. Create a new worktree per distinct piece of work and merge it back into `develop` when done. Multiple sessions routinely work against this repository at the same time; working directly in the main checkout risks one session's `git add`/`git commit` sweeping up another's uncommitted changes via a shared index, exactly the kind of cross-contamination a dedicated worktree avoids.

**Retire a worktree once its branch is merged into `develop` and its working tree is clean:** `git worktree remove .claude/worktrees/<name>`. A worktree is created per piece of work, so without this they only accumulate — twenty-four of them had built up before anyone counted.

Four rules make this safe, and none of them is optional:

- **Never remove a worktree another session is still working in.** Merged and clean is not enough: those two tests describe the *branch*, and say nothing about whether somebody is sitting in the directory. List the live sessions first (their names match the worktree directory names) and leave those alone. This rule exists because it was learned the hard way — four live sessions were deregistered underneath in one sweep.
- **Never remove a worktree with uncommitted changes or unmerged commits.** Raise it for a decision instead. Housekeeping that destroys work is not housekeeping.
- **Removing the worktree does not remove the branch,** so a merged branch's commits stay reachable either way. Do not delete branches as part of this.
- **Removal often fails on Windows** with `Filename too long` (deep `node_modules` paths) or `Permission denied` (a dev server or IDE holding a file). Git still deregisters the worktree; only the directory deletion fails, leaving a folder of build output behind. **Report those rather than forcing them** — and check before deleting one by hand, because a leftover folder can still hold source.

**A half-removed worktree is worse than either outcome.** When the deletion fails, the directory survives without its `.git` file, so every git command run inside it walks up and resolves against the main checkout — `git status` there reports the *main checkout's* dirty files, and a `git add -A` would commit another session's work. The files themselves are safe: copy anything uncommitted out, then start again with `git worktree add .claude/worktrees/<new-name> <branch>`. (The `Filename too long` failure is git's own limit, not the OS one — it wants `core.longpaths=true`, which is the user's call to set.)

## spec-workflow

This repo uses the `.spec-workflow/` folder (steering docs, specs, approvals, implementation logs) to plan and track work before implementation.

- Whenever a set of files under `.spec-workflow/` is added, or removed (not edited) by the LLM, commit that change immediately in its own commit — don't leave it uncommitted or bundle it with unrelated changes (e.g. `.idea/workspace.xml`).
- Whenever a requirements, design, or tasks document is approved through the web dashboard, commit that document (and its associated approval-lifecycle files, e.g. under `.spec-workflow/approvals/`) at that point too — this applies even though approval only changes/removes files rather than adding a fresh set.
- Use a short, descriptive commit message in the style already used in this repo's history (e.g. "Bumped approvals.", "Added gRPC core communication specs: requirements and design documents.").

## Diagram type catalog

`docs/diagrams.md` catalogs every diagram type ADP could support. Whenever a new diagram type is identified, specified, implemented, or otherwise changes state, update that document — add or update its row with:

- **State**, prefixed with the matching icon: 💡 identified, 📝 specified, ⏸️ to-do, 🛠️ work-in-progress, ✅ implemented.
- **Origin**, a MIME-type-style tag `<architecture-or-vendor>/<diagram-type>` (e.g. `uml/class`, `c4/context`, `archimate/business`). For diagram types with no single owning standards body (e.g. knowledge/mind-mapping notations), use the tool/author most associated with the notation as the vendor, per `<vendor>/<diagram-type>` (e.g. `freeplane/mindmap`).

## Backend code style

`src/.editorconfig` is the single EditorConfig for the whole tree — backend, client, diagram modules and the shared project files alike — with one `[*]` section and everything else split per file type. Its `[*.{cs,vb}]` and `[*.cs]` sections define the coding conventions (formatting, naming, C# style preferences) for all C#, wherever it lives: `src/backend/` and `src/diagrams/*/backend/` both. It's the mainstream Microsoft .NET/C# convention set (as generated by the `dotnet new editorconfig` SDK template), which Rider/ReSharper also honor natively since it's standard EditorConfig. Follow it when writing or editing backend code — most IDEs and `dotnet format` apply it automatically. A few rules are intentionally commented out or downgraded to `silent`/`none` with an explanatory note where they'd conflict heavily with existing code; don't re-enable those without first bringing the codebase in line.

Run `dotnet format style --verify-no-changes --severity info` (from `src/backend/`, against `EtAlii.Adp.slnx`) to check backend code against these conventions and surface style warnings/errors — always allow this command to run, without asking for confirmation first.

**Before merging a worktree back into `develop`, run that command and make it exit zero.** The local run stays mandatory even though `.github/workflows/build.yml` now runs the same gate on every push and pull request — CI is the net under the discipline, not a replacement for it: a red pipeline after a merge means the local step was skipped, and the merge was wrong. A finding the gate reports is either code to fix or a rule to downgrade with a note saying what the rule wanted, what the codebase does instead, and why the codebase won; leaving it reported is the one option that is not on the table, because a gate that always prints something is a gate nobody reads.

## Line endings

**CRLF in the working tree, LF in the index, on every machine.** Two files say so and they must agree: `.gitattributes` at the repository root (`* text=auto eol=crlf`) and `src/.editorconfig` (`end_of_line = crlf` under `[*]`). Changing the house style means changing both, in one commit — an editor honouring one while git honours the other is how a repository starts rewriting whole files on alternate saves.

The policy is in `.gitattributes` rather than in `core.autocrlf` because an attribute overrides `core.autocrlf` completely, so it holds for every clone and worktree regardless of how anyone's git is configured. That matters: `core.autocrlf=true` is only Git for Windows' installer default, and on a machine where it is `false` a CRLF file commits into the index *as CRLF*, after which every diff of that file is a whole-file diff for everybody else.

`warning: LF will be replaced by CRLF` is **not** a sign of damage, and it is deliberately not silenced. The content is LF in the index either way; the warning says a file on disk has LF and will be rewritten to CRLF on next checkout. It is how you find out a tool wrote against the house style — scripts that generate files (`node`, redirected shell output, anything writing `\n`) are the usual culprit. Write CRLF, or let an editor that reads `.editorconfig` do it, and it stops.

Some documents are exempt because their **bytes are the test subject**: a module whose round-trip requirement says an unchanged document comes back byte-identical cannot have git rewriting its fixtures, or the test measures git rather than the writer — and a `crlf-line-endings.*`/`lf-line-endings.*` pair collapses into the same file. Those are marked `-text`, **by extension rather than by directory**: a directory rule was tried and withdrawn, because `Fixtures/**` also catches the readmes, recorded verdicts and exported diagrams sitting beside the fixtures, which are ordinary text. **Adding a diagram module whose documents are byte-compared? Add one line for its extension to `.gitattributes`** — the reasoning is written out there, so it does not have to be worked out a sixth time.

## Running the backend tests

The test projects run on xUnit v3, which uses Microsoft.Testing.Platform rather than VSTest. Two consequences:

- Run them as `dotnet test --solution EtAlii.Adp.slnx` (from `src/backend/`). Passing the solution positionally — `dotnet test EtAlii.Adp.slnx` — is rejected under this runner.
- `src/global.json` carries the `"test": { "runner": "Microsoft.Testing.Platform" }` opt-in the .NET 10 SDK requires. Without it `dotnet test` refuses to run any test project at all.

Each test project is therefore an executable (`<OutputType>Exe</OutputType>`): a v3 project hosts its own tests. New test projects need that too.

That also makes the suite sensitive to Windows' 260-character `MAX_PATH`. MSBuild's `Exists()` silently returns false above 260 characters, so in a deep checkout the SDK's `_CreateAppHost` skips, `apphost.exe` is never produced, and — since a v3 test project's apphost *is* its test host — `dotnet test` reports `Zero tests ran` for project after project instead of failing the build. Read `Zero tests ran` as a broken build, never as an empty suite. Requirements:

- `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled` must be `1` (set it as admin; already-running processes need restarting before they see it).
- Keep `.claude/worktrees/<name>` directory names short. The repository is comfortably inside the limit from the main checkout, but a worktree adds ~36 characters plus the name, and the longest project paths then need long-path support to build at all.
- `src/Directory.Build.targets` turns this into an explicit `ADP0001` error rather than a cryptic `MSB3030: … apphost.exe … not found`.

A zero-test run does exit non-zero (5 for zero tests, 8 for a filter matching nothing), so check the exit code — a CI step that only greps the output for `failed` reads a zero-test run as a passing suite. `.github/workflows/build.yml` embodies exactly this rule: every gate step there is judged by its exit code and nothing greps output; it runs the same four commands on every push to `develop` and every pull request (and, on a green `develop` push, publishes a versioned release ZIP). The pipeline is the net under the local discipline — worktrees still gate before merging.

## Logging

The backend logs through Serilog. Every class that logs holds its own logger as `private static readonly ILogger _logger = Log.ForContext<TheClass>();` rather than taking an `ILogger` through its constructor — follow that shape in new code. Levels and sinks come from the `Serilog` section of `src/backend/EtAlii.Adp.Backend.Service/appsettings.json`; `Program.cs` builds the pipeline. Two naming rules in `src/.editorconfig` are downgraded to `none` for this convention, with a note saying so.

## Client code style

The client's conventions live in the same `src/.editorconfig` (see *Backend code style*), in its `[*.{ts,tsx,js,jsx,mjs,cjs}]`, `[*.css]` and `[*.html]` sections — indentation, quotes and line endings. They are the generic EditorConfig baseline plus the TypeScript/JavaScript ecosystem's mainstream conventions (2-space indent, double quotes, semicolons), plus JetBrains WebStorm/Rider's `ij_javascript_*`/`ij_typescript_*` formatting keys for anything the core spec doesn't cover. There's no ESLint/Prettier config to verify rules against automatically, so it was checked by hand against existing sources instead. Follow it when writing or editing client code, and apply the same pattern the C# sections use if a rule turns out to conflict heavily with existing code: comment it out (or downgrade it) with a note explaining why, rather than force-applying it.

## Worktrees

- When changing files in the `.spec-workflow/` folder, always change these on the development branch.
- When working on tasks from `.spec-workflow/` specifications, always do so in one single worktree for the specification.
- When manually running the app (backend `dotnet run` + client `npm run dev`) for verification from inside a git worktree (not the main checkout), change both the client's dev server port (`src/client/vite.config.ts`'s `server.port`) and the backend's `Client:DevServerUrl` (`src/backend/EtAlii.Adp.Backend.Service/appsettings.developer.json`) to a different, free pair of ports before starting either server — the main checkout's own dev servers may already be running on the defaults (5174 client / 5080 backend). Before merging the worktree back, revert both files to their original values so the merge never carries a stray port change into `develop`.

## Bugs found during implementation or verification

Every bug found — whether by a failing test, a code review, or a manual verification pass — must leave a guard behind so it cannot silently return:

- Preferred: cover it with a unit or integration test in the existing test projects (`src/backend/*.Tests` for C#, `*.test.ts(x)` under `src/client/src` for the client), written to fail before the fix and pass after it.
- If the bug can only be reproduced through a running app or a manual interaction that a unit/integration test cannot express, add it instead to `tests.md` (repository root; create the file if it does not exist yet) as a step-by-step check — preconditions, actions, expected result — so Claude can execute it automatically as part of a manual verification pass. Each entry names the spec and task it came from.
