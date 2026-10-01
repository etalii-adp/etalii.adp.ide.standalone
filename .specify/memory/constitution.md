<!--
Sync Impact Report
==================
Version change: (unfilled template) → 1.0.0
Bump rationale: first adoption. The file held only the scaffold's placeholders, so there is no
prior version to be compatible with; 1.0.0 is the initial ratified text.

Modified principles: none renamed - all placeholders replaced.
  [PRINCIPLE_1_NAME] → I. Files Are the Source of Truth
  [PRINCIPLE_2_NAME] → II. Specialized and Specified, Not Coded
  [PRINCIPLE_3_NAME] → III. One Contract, One Stream
  [PRINCIPLE_4_NAME] → IV. Every State Change Is a Command
  [PRINCIPLE_5_NAME] → V. Evidence Before Trust

Added sections:
  - VI. Delivered Through a Pull Request (a sixth principle; the scaffold carries five)
  - Technology and Code Constraints (was [SECTION_2_NAME])
  - Development Workflow and Quality Gates (was [SECTION_3_NAME])
  - Governance (filled)

Removed sections: none.

Sources the text was derived from (no user input was supplied with the command):
  - CLAUDE.md
  - .spec-workflow/steering/product.md, tech.md, roles.md
  - docs/architecture.md

Follow-up TODOs:
  - None deferred as placeholders. Two inferred choices need the user's confirmation and are
    listed in the command's final summary: the ratification date (taken as the day of first
    adoption, 2026-10-01) and the precedence rule in Governance (CLAUDE.md and the steering
    documents win over this file).

This report is scratch material for review and is to be removed before the file is committed.
-->

# EtAlii.Adp Standalone Host Constitution

## Core Principles

### I. Files Are the Source of Truth

- What a tool shows MUST live in a plain, diffable text file in the user's workspace, in the
  format that already owns it. A database row is never the record.
- A document ADP did not change MUST come back unchanged from a round trip. Constructs a module
  does not model are preserved, not dropped.
- ADP's own data MUST go in the `.adp` registration beside the document, never as an annotation
  inside a file another tool owns. Authored layout for a format with no layout of its own is
  ADP's data and follows that rule.
- The backend is the sole reader and writer of workspace files. The client MUST NOT touch the
  filesystem, and access is scoped to folders the user explicitly opened.

**Rationale**: drawings that travel with the work they describe are reviewed, branched and merged
with it. That holds only while the file stays openable by the tool that owns its format and its
diffs carry nothing ADP invented.

### II. Specialized and Specified, Not Coded

- Everything ADP offers is a **tool** of exactly one kind: a **diagram**, a **designer** or an
  **editor**. Code, text and documentation MUST use these words as the ADP terminology defines
  them; "designer" never means a tool in general.
- A tool type is a specification first. Host code is written only where the specification cannot
  express what is needed.
- A specification for a diagram type MUST cover file format, visualization, toolbox, and context
  actions and commands. An aspect that does not apply is stated as not applying, with the reason.
- A tool type MUST be a self-contained module discovered at runtime: no core file names it, and
  deleting its folder removes the type.
- A module MAY depend on the core abstractions. Core code MUST NOT depend on a particular tool
  type, and the canvas, storage and sync machinery MUST NOT know any one type's schema.
- Where a well-known notation, file format or convention exists, it MUST be adopted rather than
  replaced.

**Rationale**: a tool earns its place by knowing its subject, and a tool engineer specifies a
type once for every host. Both fail the moment the core learns about one type.

### III. One Contract, One Stream

- The `.proto` contracts in `src/api` are the only agreement between backend and client. Neither
  side keeps its own copy of a contract.
- Client-to-backend traffic MUST be unary `Action` calls. Backend-to-client traffic MUST ride the
  one server-streaming `Watch` the connection already holds.
- A feature that needs to push something new MUST add a member to that stream's message. It MUST
  NOT open a second stream.
- Per-connection state MUST be keyed by the connection id, MUST NOT be shared with another
  connection, and MUST be discarded when the stream ends.
- The stream carries deltas. The client MUST reconcile an incoming delta against its own unsaved
  edits and MUST NOT silently discard them.
- The server side is one ASP.NET Core process serving both the built client and the gRPC-Web
  endpoint.

**Rationale**: grpc-web gives a browser no bidirectional streaming, and over HTTP/1.1 each live
stream holds one of six connections per origin. A tab that held three streams wedged the origin
when a second tab opened.

### IV. Every State Change Is a Command

- Anything that changes state MUST be an `ICommand` with a matching `ICommandHandler<TCommand>`,
  dispatched through `IHistoryStack`. A service, gRPC method or context action provider MUST NOT
  perform the change inline.
- A handler MUST report the command that reverses it as `CommandResult.Inverse`. A change that is
  deliberately not undoable says why in the handler's own documentation.
- A handler MUST validate its own preconditions; undo and redo dispatch it long after the
  original call.
- A rejected command is a `CommandResult` carrying a message for the user, not an exception.
- Selection has one owner, `ContextService`. Something becomes selectable by registering an
  `IContextSourceResolver` and actionable through an `IContextActionProvider`, never through a
  bespoke RPC or a surface's own copy of the selection.

**Rationale**: undo/redo is a property users expect everywhere, so it has to be a property of the
system rather than something each feature remembers. One selection pushed to every surface means
a new action reaches the ribbon, the menu and the keyboard without any of them changing.

### V. Evidence Before Trust

- A check counts as evidence only if it would print something different were the belief false.
- A gate MUST be judged by the runner's own exit code, captured before any subsequent command,
  never by grepping output or by a wrapper's status. `Zero tests ran` is a broken build.
- A test written for a bug MUST be seen to fail against that bug before it is trusted. A test
  that passes against the defect is deleted or fixed.
- Every bug found MUST leave a guard behind: a test that failed before the fix, or a step-by-step
  entry in `tests.md` when only a running app can reproduce it.
- A claim about generated code MUST be backed by a build of a freshly created worktree.
- A specification's tasks MUST be diffed against its acceptance criteria before and after
  implementation, and at least two traces MUST be read back to their artefacts.
- Tool modules MUST be tested against real published example data under a permissive licence,
  with the licence file vendored verbatim beside the data. Share-alike is refused.

**Rationale**: every rule here follows a green result that was believed and was wrong. A check
that cannot fail converts "unverified" into "verified" while nothing has changed.

### VI. Delivered Through a Pull Request

- Anything that changes the repository's files MUST happen in a dedicated worktree on a
  `features/<name>` branch, never in the main checkout.
- The one exception is a specification document and its approvals, snapshots and logs, which are
  written on `develop` in the main checkout and committed with an explicit pathspec naming files.
- Nothing else reaches `develop` except through a merged pull request. A worktree is never merged
  locally.
- A specification is owned by one Developer until every one of its tasks is done.
- An agent MUST commit under its own per-task identity, set with `git config --worktree` when the
  worktree is created. `--global` and `user.email` are never touched.
- Approval of a specification document comes from the dashboard. A small amendment is the sole
  exception: it is ruled in chat as a selection and applied by the document's owner. Nothing is
  edited under a pending card.

**Rationale**: several sessions share one main checkout and its index. Each of these rules
closes a way one session's work has landed under another's name or been lost.

## Technology and Code Constraints

- **Stack**: .NET on the SDK pinned by `src/global.json`; React with TypeScript built by Vite;
  gRPC over grpc-web; a canvas drawn as SVG by the shared library in `src/client/src/canvas`.
  A module's canvas composes that library rather than drawing its own surface.
- **Local first**: development, testing and debugging MUST work in one F5 run, locally, with
  local-only persistence and no hosted dependency. Fast local unit and integration tests are
  preferred over end-to-end tests against hosted infrastructure.
- **Code style**: `src/.editorconfig` is the single convention set for C#, TypeScript, CSS and
  HTML. A rule that conflicts heavily with existing code is downgraded with a note, never
  silently ignored.
- **Async**: no blocking call inside an `async` method where an async overload exists. Tests pass
  `TestContext.Current.CancellationToken` to every call that takes a token.
- **Types**: one entity per file, a command and its handler excepted. A class, record, enum or
  interface is never nested inside another type. Data-carrying types live in the area's `_Model`
  folder.
- **Tests**: file `<Classname>.Tests`, class `<Classname>Tests`, arrange-act-assert.
- **Namespaces**: a folder that should not contribute to the namespace is fixed in the project's
  `.DotSettings`, not by changing the namespace.
- **Logging**: Serilog, with each logging class holding
  `private static readonly ILogger _logger = Log.ForContext<TheClass>();`.
- **Styling**: client styling is centralized; inline styles are avoided.
- **Line endings**: CRLF in the working tree, LF in the index. `.gitattributes` and
  `src/.editorconfig` MUST agree, and line endings are checked with `git ls-files --eol`.

## Development Workflow and Quality Gates

- **Implementation order**: data model, persistence, wire protocol, client UI, then remaining
  logic. Each layer is finished with its tests before the next begins.
- **The four gates**: `npm test`, `npm run typecheck`,
  `dotnet format style --verify-no-changes --severity info` and
  `dotnet test --solution EtAlii.Adp.slnx` MUST all exit zero in the worktree before its branch
  is pushed. CI runs the same four on the pull request.
- **Merge**: a pull request is merged with a merge commit, not a squash. Afterwards the branch is
  deleted and the worktree retired with `retire.sh`, never with `--force`.
- **Documentation stays true**: a change that makes a sentence in `docs/architecture.md`,
  `docs/solution-structure.md` or a module-creation guide false updates that document in the same
  change. A tool type's row in `docs/tools.md` moves as its state changes. A UI change that makes
  a screenshot misleading means retaking it.
- **Specification bookkeeping**: files added or removed under `.spec-workflow/` are committed at
  once, in their own commit. Staged verdict snapshots are committed as precisely those paths.
- **Questions to the user**: every question is a selection with concrete options and an "other",
  never open prose.
- **Roles**: one Scrum master, four Developers, two Testers and two Architects. A session whose
  role does not fit the work says so rather than taking it.

## Governance

- **Precedence**: this constitution states the principles a specification, plan or task list is
  checked against. The operative rules and their reasoning live in `CLAUDE.md` and the steering
  documents under `.spec-workflow/steering/`. Where this file and one of those disagree, that
  document wins and this file is stale and MUST be amended.
- **Amendment**: an amendment is proposed with its reason and approved by the user. A change of a
  word, a sentence or a count follows the small-amendment rule in Principle VI; anything that
  adds, removes or redefines a principle requires explicit approval of the new text.
- **Versioning**: semantic. MAJOR for a principle removed or redefined incompatibly, MINOR for a
  principle or section added or materially expanded, PATCH for clarifications and wording.
- **Compliance review**: every plan MUST record a check against these principles before design
  and again after it. A pull request's review verifies the same. A deviation MUST be written down
  with the simpler alternative that was rejected and why.
- **Runtime guidance**: `CLAUDE.md` is the guidance file read at the start of every session.

**Version**: 1.0.0 | **Ratified**: 2026-10-01 | **Last Amended**: 2026-10-01
