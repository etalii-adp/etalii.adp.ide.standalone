# Design Document

## Overview

Thirteen diagram-module backends each carry their own copy of the same six or seven jobs: loading and reloading a document, saving it, working out what changed, reacting to a change on disk, restoring lines for an undo, turning a YAML node into a line range, and parsing the ids a canvas gesture sends. The requirements measured which copies are the same job and which have drifted. This design says what each shared piece is, where the module's own part of it stays, and in what order the thirteen move onto it.

**Four of the requirements' criteria are already satisfied on `develop`**, because the user ruled the defects the scan found fixed ahead of the specification rather than inside it. They are cited here rather than re-designed (*What landed ahead*).

**One thing is deliberately not decided here: where the shared types live.** Placement is the user's judgement, so it is asked with the reference counts as cost (*Placement*). Everything else below is the same under every answer: only the namespace and project of each shared type change.

## Steering Document Alignment

### Technical Standards (tech.md)

- **Decision 7**, commands for every state change: the restore-lines edit (R6) is one command with one inverse, shared, rather than three copies.
- **Decision 11**, concurrent saves take turns and are not retried: the shared save (R3) inherits the per-destination turn without knowing about it, and **deletes now take the same turn** (below).
- **Diagram storage**: the backend owns reading and writing; a module supplies how its own document parses, and nothing else.

### Project Structure (structure.md)

- A module adds behaviour through declared seams, not by copying its neighbour. Every candidate here began as an exact copy and was then edited in one place, which is what made the drift invisible.

## What landed ahead

The user ruled three defects found by the scan fixed immediately rather than inside this work. They are on `develop`, and each satisfies a criterion this design therefore does not restate:

| Criterion | Landed as | What it does |
| --- | --- | --- |
| R4.3 (a removal is always sent) | `350b8f9e`, merged at `88c26d44` | c4 sends removals when a document changes, so an element deleted in a text editor leaves the canvas. |
| R2.3 (a store ignores a reload of its own save) | `350b8f9e` | causal-loop no longer marks its own diagram unreadable while saving. |
| R2.4 (a failed reload keeps the last good document) | `350b8f9e` | and R2.5's confirmation of absence arrives as **`IDiagramDocumentReloader.BodyDeleted`**: the bridge sends it on a `Deleted` event, on a rename to anything but `<body>~RF<hex>.TMP`, and for a body missing after a watcher overflow. **This design adopts that signal rather than re-deciding it.** |
| R11.2 (an empty relation end is refused) | `d2fb4e7e` | causal-loop's parser refuses an empty variable id and its relation parser refuses an empty end, closing a path that silently wrote `link a ->  +`. |

**Two more changes belong to the same discipline and are in flight rather than landed:**

- **Deletes take the same per-destination turn as saves.** Developer 3 measured a delete racing a save at **305 failures in 400**; with the turn, **0 in 400**. Six call sites move onto `AdpFileWriter.Delete`. **R2 and R3 as approved state only the save half, so this design states the delete half**: any code that removes a published path takes the same turn, and the turn is the writer's to hold, not each caller's.
- **`TextFileBuffer.SaveAsync` writes a user-editable file with a raw `FileStream`** (`FileMode.Create`, `FileAccess.Write`, `FileShare.Read`): truncate in place, no temp-then-replace. The user ruled it fixed now, routed through `AdpFileWriter`, **with the guard widened**: `ShapeOfFileAccess.Tests`' unguarded-write rule is the single regex `\bFile\.WriteAll(Text|Lines|Bytes)\s*\(`, which that line does not match (measured). So the category read empty because the pattern knew one spelling. The widened rule keeps the existing allow-list mechanism, so a deliberate raw write stays possible with a stated reason.

**Both are Developer 3's to land. This design cites them; it does not carry them.**

## Placement — ruled by the user

Every shared piece below has to live somewhere, and the reference graph makes the cost of each answer measurable rather than arguable. Measured on `develop`: **61 module main projects** (13 implemented, 48 stubs). Of those, **61 reference `EtAlii.Adp.Diagram`**, **13 reference `EtAlii.Adp.Backend`**, and **6 reference `EtAlii.Adp.Documents`**.

| Option | New project references needed | What it costs, and what it buys |
| --- | --- | --- |
| **(a) `EtAlii.Adp.Diagram`** | **none** — every module already references it | Cheapest by a wide margin. The cost is conceptual: that project is the diagram *contract* (elements, deltas, sessions), and the store lifecycle is about documents on disk. A reader looking for "how a module saves" would not look there first. |
| **(b) `EtAlii.Adp.Documents`** | **7** (the implemented modules that do not reference it yet) | Where `LineDocument`, `LineSplice`, `AdpFileWriter` and `SharedDocumentReader` already live, so the store lifecycle, the save result and the YAML range sit beside the primitives they use. The seven references are one line each. |
| **(c) a new project** | **13**, plus a project | The cleanest boundary and the worst fit with the backlog: the user's post-decomposition list asks whether `EtAlii.Adp.Common` can be *removed*, so adding a project runs the other way. |

**THE USER RULED (b), `EtAlii.Adp.Documents`**, given as a selection via the Scrum master on 2026-09-22. **The stated cost is seven new project references**, one line each, in the implemented modules that do not reference it yet. The reason recorded with the ruling: the pieces are about documents, the primitives they compose are already there, and seven one-line references is a smaller cost than a reader looking in the wrong place for years. **So every shared type below lives in `EtAlii.Adp.Documents`**, and the options are kept above rather than deleted, so a later reader meets the cost of the answer that was not taken rather than re-deriving it. The cross-tier fixtures (R9–R13) are a separate question, answered below.

**Where the shared fixtures live** was mine to take rather than ask, and the user let it stand. They must be readable by the backend suite and by the client suite, so they belong outside both: **`src/fixtures/cross-tier/`**, one JSON file per rule (`row-rounding.json`, `text-metric.json`, `gesture-ids.json`, `element-types.json`, `permanent-statuses.json`), each carrying its cases and the rule's own statement in a `reason` field. The alternative — keeping each beside its backend owner and having the client reach across — was rejected: a client test reading `src/backend/...` inverts the dependency the whole tree is arranged to avoid. That reason is why it stands; it is not a preference.

## The shared pieces

### S1 — One line document (R1)

`databricks`, `rdf` and `azure-pipeline` read and splice through core's `LineDocument`; their private document and line types go. **azure-pipeline's copy has drifted twice** and core's behaviour wins, by the user's ruling: on a file with equal LF and CRLF counts a newly inserted line takes core's ending, and a range whose end precedes its start is refused.

- **The module keeps** its parser and writer: which lines mean what is its own.
- **Proof**: each module's existing byte-identical fixture corpus, plus one new fixture per module with equal LF and CRLF counts, showing which ending an inserted line takes.

### S2 — One document-store lifecycle (R2)

One shared implementation of `GetOrLoad`, `Forget`, `Load`'s read-or-open-empty opening, `Reload` and `Save`'s bookkeeping. A module supplies two things and nothing else: **how its text parses into its own entry**, and **what its change event carries**.

- **Self-write suppression (R2.3) is in the shared piece**, so no store can lack it.
- **A failed reload keeps the last good document (R2.4)**, and a missing body is confirmed through **`BodyDeleted`** (R2.5) rather than by timing.
- **Read-only stores** (sparql) use the same lifecycle without a save (R2.7).
- **mindmap keeps two differences** and they are declared rather than tolerated (R2.8): it skips documents it never loaded, and its change event names the kind of structural change. Both are expressed by the module's own event type, not by an override of the lifecycle.
- **Deletes take the writer's turn**, as above.
- **The shared lifecycle is where the watcher obligation (R2.9) is INTENDED to be met**, so that a module cannot get it wrong by omission - which is how two editor sessions came to hear `Changed` and not `Renamed` while the writer's own 106 tests stayed green. **As of today it is not met there: both editor sessions construct their own `FileSystemWatcher` and now subscribe correctly by themselves, and nothing in R2.9 requires the subscription to move.** So this is a direction for the shared piece rather than a property the tree currently has. Known gap recorded rather than fixed here: `SolutionWatcher` has no `Error` handler; it belongs to its own module and is in `dotnet-dependency-graph/findings.md`.

### S3 — One save result, and a guard against ignoring it (R3)

One result type carrying an error and a warning, both empty on success — wardley-map's `WardleyPublishResult` shape, moved and shared. Mindmap's throwing `Save` and the seven string-returning ones convert; every caller converts with them.

**The ignored-result guard** is the part worth designing rather than assuming. A discarded result must be a finding, and there are two candidate mechanisms:

- **A Roslyn analyzer attribute** (`[MustUseReturnValue]`-style) needs an analyzer package this tree does not have, and the census showed the tree carries no analyzer beyond the SDK's and xUnit's.
- **A source-walking test**, the mechanism `ShapeOfFileAccess.Tests` already uses for file access: walk every `.cs` file, find invocations of a save whose result is discarded (an expression statement rather than an assignment, a condition or an argument), and report each with its file and line, with an allow-list carrying a reason per entry.

**The source-walking test is chosen**, because the tree already has one of exactly that shape, with an allow-list convention, and because it needs no new dependency. **Its own guard**: a planted discard is reported, and a planted *use* is not; both seen before the guard is trusted.

### S4 — One change-detecting diff (R4)

One diff: what is new or changed is added, what is gone is removed, and equality means equal position, type and payload bytes. The six identical copies collapse into it. `ansible-structure` and `helm-charts` stop resending everything.

- **Delta order (R4.5)** is settled here: **removals first, then additions**, which is `dotnet-dependency-graph`'s order today. The reason is the client's fold: an add is an upsert keyed on id, so removing first can never delete something the same batch just added, while the reverse order relies on the ids being disjoint — **true today, and true ONLY BY ACCIDENT.** That sentence is the one that should stop anyone reversing this later: the order is not a style choice, and nothing in the code enforces the property the other order depends on.
- **Proof**: each converted module's existing session tests, plus the c4 removal guard that `350b8f9e` landed, which is the case this order exists for.

### S5 — One document-change handler (R5)

One shape: ignore changes to other paths, render, diff (S4), raise if there is anything, and on a read failure log a warning naming the path without raising. `azure-pipeline`, `c4` and `causal-loop` adopt it.

- **causal-loop stops catching every exception.** What reaches the caller instead: the handler catches read failures only, and anything else propagates to the reload bridge, **which already guards each document's reload separately** (`DiagramDocumentReloadBridge`'s per-document `try` on the watcher-error path). So an unexpected failure costs that document's reload and is logged with its path, rather than being swallowed with a generic message.
- **mindmap keeps its structure-aware reaction** (R5.3): it shares the path filter, the raise and the failure handling, and supplies its own body.

### S6 — One restore-lines edit (R6)

The undoable edit that puts a line range back, shared by `causal-loop`, `databricks` and `rdf` today and available to any module. One command, one handler, one inverse.

### S7 — One YAML node range (R7)

The rule that turns a parsed YAML node into the lines it occupies, identical in three modules and near-identical in `azure-pipeline`. **The difference is read and decided in implementation, not guessed here**: the three agree, so the three win unless azure-pipeline's variant is shown to handle an input they get wrong — and the task carries a test on an input that tells the two apart.

## The cross-tier rules (R9–R13)

Each is computed on both sides of the wire without crossing it, so drift is invisible to both suites. **Each has one owner, one implementation on the owning side, and one fixture both suites read.** The backend owns all five; `client-centralization` cites them.

| Rule | The backend's one implementation | The fixture's cases |
| --- | --- | --- |
| **R9 row rounding** | one function, rounding half away from zero (timeline's and dependency-graph's identical bodies) | exact halves either side of zero, negative zero, a negative row, a value between lines |
| **R10 text metric** | one function, characters × font size × average advance, at the four modules' shared values (14 and 0.55). **Padding, minimum and maximum stay each module's own declared values** | an empty string, one character, a long string, a string with spaces, at two font sizes |
| **R11 gesture ids** | one grammar for `new:` (both shapes, `x,y` and `x,row`) and `rel:`, refusing an empty source or target | valid ids of both placement shapes, and the invalid ones: empty end, missing arrow, trailing separator |
| **R12 type strings** | the 79 constants stay where they are — the fixture is **produced from them**, per module | every module's element and relation type strings, keyed by module |
| **R13 permanent statuses** | the list named once in core, beside the services that raise them (`FailedPrecondition`, `Unimplemented`) | each status code and whether it is permanent |

- **R12 is a generated fixture, not a maintained one.** A backend test writes it from the constants and fails if the checked-in copy differs, so the constants stay the source and the file stays honest. **Developer 1's centralized-selection task 26 guard reads those same constants**, so it is a known consumer: if the fixture's shape moves, that guard moves with it.
- **R13's "one place" is a named list, not a new abstraction.** Core already decides this in three services; the change is that they read one list rather than three literals, and the fixture pins it.

## Behaviour changes only where a criterion says so (R8)

A test changes in this work only where it pinned behaviour a named criterion changes, it names that criterion, and it changes only what the criterion changes. **Three places will need it, and they are named now so a reviewer can tell them from drift:**

1. azure-pipeline's line-ending fixture, where R1.2 changes which ending an inserted line takes on a tied file.
2. `ansible-structure`'s and `helm-charts`'s session tests, where R4.2 changes a resend-everything into a diff.
3. mindmap's and the seven string-returning stores' save tests, where R3.1 changes what a failed save returns.

Everything else keeps its assertions, and the full backend suite plus every byte-identical corpus passes (R8.2).

## Migration order

The order is by risk, not by module:

1. **S3 the save result**, because every later step touches saving and the result type should not change twice.
2. **S2 the lifecycle** (with the delete turn), the largest single reduction: 10 stores.
3. **S4 the diff** and **S5 the handler**, which the lifecycle's change event feeds.
4. **S1 the line document**, three modules, independent of the above.
5. **S6, S7**, small and independent.
6. **R9–R13 the cross-tier rules**, last, because each needs a fixture both suites read and `client-centralization` cites them.

**`functional-decomposition-graph`'s module half waits on S2, S3, S4, S5, S6, S7 and R11**, by the user's ruling that it should not write a fourteenth copy. Its tasks name those requirements, so the dependency is written down rather than remembered.

## Error Handling

1. **A save fails** — the result carries the error (S3), the edit stays in memory, the user sees a sentence and can retry.
2. **A reload cannot read the body** — the last good document is kept and a warning names the path (R2.4).
3. **The body is deleted** — `BodyDeleted` arrives and the document becomes empty, because a body that is gone is an empty diagram (R2.5).
4. **Another program republishes an unchanged body while reloads arrive** — no reload installs an empty or unreadable document (R2.6).
5. **A save and a delete race** — both take the same per-destination turn; the measured 305-in-400 failure rate becomes 0 in 400.
6. **A module's parse throws** — the lifecycle reports it as that document's problem; one document's failure never costs another's reload.

## Testing Strategy

Every guard is seen to fail against a stated planted defect before it is trusted.

### Unit

- The shared lifecycle: self-write suppression, keep-last-good, `BodyDeleted`, read-only mode, and mindmap's two declared differences.
- The save result: each converted store's failure path, and **the ignored-result guard with both controls** (a planted discard reported, a planted use not).
- The diff: added, changed, removed, and **removals before additions**.
- The change handler: path filter, read failure warns without raising, and causal-loop's narrowed catch.
- S1: byte-identical round trips per module, plus the equal-counts fixture.
- S7: the input that tells the three copies from azure-pipeline's.

### Cross-tier

- Each fixture is read by a backend test and by a client test. **A rule is only centralized if one test fails when either side changes**, so each fixture's backend test asserts the implementation against the file, and R12's test regenerates the file and fails on a difference.

### Integration

- Every module's existing session, command and context tests, unchanged except where R8 names.
- The byte-identical corpora, unchanged.

## Requirement Coverage

| Requirement | Where |
| --- | --- |
| R1 one line document | S1 |
| R2 store lifecycle | S2; R2.3–R2.5 landed ahead (`350b8f9e`) |
| R3 save result and its guard | S3 |
| R4 diff, and delta order | S4; R4.3 landed ahead |
| R5 change handler | S5 |
| R6 restore edit | S6 |
| R7 YAML range | S7 |
| R8 behaviour changes only where named | *Behaviour changes only where a criterion says so* |
| R9 row rounding | cross-tier table |
| R10 text metric | cross-tier table |
| R11 gesture grammar | cross-tier table; R11.2 landed ahead (`d2fb4e7e`) |
| R12 type strings | cross-tier table, generated fixture |
| R13 permanent statuses | cross-tier table |

## Sources

- The approved requirements, and the scan they rest on (`develop` `eb019687`, cross-tier items at `fba7040a`).
- Landed ahead: `350b8f9e`, `d2fb4e7e`, merged at `88c26d44`.
- Developer 3's measurements: the delete-versus-save race (305 of 400, then 0 of 400), and `TextFileBuffer`'s raw write with the guard's single-spelling regex.
- The reference counts for *Placement*, measured on `develop` at `fb39af1b`.
- `client-centralization` for the client half of R9–R13; `centralized-selection` task 26 as a known consumer of R12's constants.
