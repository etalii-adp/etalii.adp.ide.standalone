# Requirements Document

## Introduction

Peter chose on 2026-10-27 to specify a **shared player for DISL's `simulate` operations** in the client's canvas library (`src/client/src/canvas/library/`), rather than leaving the one module that has simulations to play them itself.

**Why now.** `databricks-disl-fbl` task 14 replaces the Databricks module's own engine, `src/diagrams/databricks/client/useSimulatedRun.ts`, by the definitions' `simulate` operations, and it waits on this player: its text says the player *"is not built inside the module"*. The module names its engine as standing in for **runtime gap A6** of that specification (`databricksBindings.ts`, *"THE SIMULATED ACTIONS, STANDING IN FOR RUNTIME GAP A6"*): each of the three Databricks definitions states its mock run, deploy or update as an operation whose body is a `simulate`, and no canvas library plays one.

**What a simulation is**, from DISL section 9.6 (the DISL specification in etalii.adp, `specifications/disl/DISL-specification.md`, read at `50e524f`): a run of the diagram played over time, step by step, without changing the model. It is the body of an operation, in place of `actions` or `plugin`, and declares the element types that take part (`for`), the transient attribute it writes (`state`), each element's state at step 0 (`initial`), each element's state at the next step (`next`), when the run finishes (`until`), the time between steps (`stepMs`, default 700), a hard stop (`maxSteps`, default 1000) and a status notice for while it runs and after it finishes (`notice`). Its expressions are evaluated in the `simulation` context (12.3), which binds `self`, `state`, `step`, `p`, `diagram` and `env`.

**This specification delivers the player and the proof that it plays the Databricks definitions as the module's engine does. It does not switch the Databricks module over**: that is task 14 of `databricks-disl-fbl`, which owns the module's files.

## What was measured

Read at `6faaeaeb3` on `develop`.

### What `useSimulatedRun` does today: the oracle

The engine is a React hook over `setInterval`. It is the behaviour this player must reproduce for Databricks, so it is recorded here in full.

- **Starting.** The canvas hands every action id to `intercept`. An id carrying the marker `.simulated.` is the engine's: it starts a show and answers true, and the caller sends nothing to the backend. Any other id answers false and travels as ever. The three ids are `databricks.simulated.run-job`, `databricks.simulated.deploy` and `databricks.simulated.pipeline-update`; an unknown id with the marker plays an empty show named "action".
- **The frames are computed once, at the start**, from the model as it is then. Starting a show while one plays stops the first and replaces it.
- **The job run** plays waves over the dependency graph. Every resolved task starts `pending`. Each wave first turns every `running` task to `succeeded` (or `failed`, for the one task a test-only profile names), then turns every `pending` task whose known upstream tasks have all finished to `running` or `skipped`, by its `run_if` and the condition outcome (always `true` unless the test-only profile says otherwise). A wave that changes nothing is still recorded as a frame, and then the run stops.
- **The deploy and the pipeline update** are ripples: the taking-part elements, ordered by position left to right and then top to bottom, each go from `pending` to `running` to `succeeded`, one change per step. The deploy ripples over resources and unmodelled configuration; the update over the libraries, the pipeline and its target.
- **The timing.** The first frame (everything `pending`) shows at once, with the marker `Simulated: <name>`. Each further frame shows one step later, a step being `stepMs`, 700 ms by default. One step after the last frame the marker becomes `Simulated: <name> — finished`, and the states of the last frame stay.
- **Ending.** The banner is a button reading `<marker> · dismiss`; pressing it clears the states and the marker. Unmounting the canvas stops the timer. **Nothing else ends a show**: an edit to the diagram while it plays, or after it finished, leaves it playing its precomputed frames or showing its last one.
- **Drawing.** The canvas puts each element's state on its payload as `simulated`, and the definition's declared class `databricks-sim-{payload.simulated}` paints it through `databricks.css`. Elements that take no part carry no state.
- **Never a file or the history.** A simulated id never reaches `executeAction`, a command or the undo history. The backend lists the three entries in the menu and answers their ids as a no-op (`DatabricksContextActionProvider`).
- **The menu entries.** `databricksBindings.ts` (`simulatedMenuActions`) declares, by module code, every operation with a `simulate` body as a menu entry the canvas runs itself (`invokedBy: [{ kind: "menu" }]`), under the wire id the definition's `x-databricks.actions` gives it. The canvas library's `actionForMenuEntry` (`definition/actions.ts`) and `useLibrarySelection` (`librarySelection.ts`) then hand such an entry to the module as `action-invoked` and send nothing, which is what keeps a simulation off the backend today.

### What the three definitions declare

`databricks-job.dis`, `databricks-bundle.dis` and `databricks-pipeline.dis` (DISL 0.3) each declare one `simulate` operation: `simulateRun` on `Task`, `simulateDeploy` on `Resource` and `Unknown`, `simulateUpdate` on `Library`, `Pipeline` and `Destination`. Each writes the `transient: "viewer"` attribute `simulated`, steps every 700 ms, and states its notice texts, which are the hook's marker texts exactly. Their expressions call user functions (`upstreamSettled`, `mayRun`, `rippleRank`, `deployMembers`, `updateMembers`) and CEL members such as `incomingOf`, `nodesOfType`, `view.bounds.center()` and `positionIn`. None declares parameters, so there is no failure path and every condition answers `true`, which is also what the canvas gets today: `DatabricksCanvas` passes the hook no profile.

### Where the definitions and the oracle disagree

Two places, each put to Peter as a question below.

- **When the run finishes.** DISL evaluates `until` after each step and finishes the run when it holds. Played that way, the definitions finish **at the step of the last change**. The hook says `— finished` **one step after its last frame**, and its job run also records one unchanged frame before stopping. So the states at every step agree, but the finished notice appears **one step (700 ms) later today for the deploy and the update, and two steps (1400 ms) later for the job run**.
- **What ends a run.** DISL requires that committing any transaction, changing the viewpoint, closing the view or dismissing the notice ends the run and clears the states. The hook ends only on dismissal, on another show starting, or on unmount; an edit leaves it playing frames computed from the model before the edit.

### What the library has and lacks

The library draws notices (DISL 6.13) today, but only a budget's (`compileNotation.ts`, *"the library draws a budget's notice and no other"*). It reads no `simulate` body. **The client has no CEL evaluator; the backend has one** (`EtAlii.Adp.Specification.Disl`, whose context table already declares `simulation` with its six names). Where the expressions are evaluated is the design's choice and is not decided here.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | When does the finished notice appear: at DISL's moment or at the hook's? | **(default) DISL's moment**: the run finishes when `until` holds. Every state at every step is the hook's, and the one visible change is that `— finished` appears one step earlier for the deploy and update and two steps earlier for the job run; the parity test names that difference and nothing else. Recommended, because it is DISL's normative rule for every module, and the hook's later moment is an artefact of how it stores frames, which the job run's `until` could not even express without a language change. · **The hook's moment**: the definitions' `until` are changed in etalii.adp to finish later; the deploy and update can say `step >= 2 * size + 1`, but the job run cannot say "two steps after the last change" without new language, so this needs an etalii.adp change and possibly a DISL one first. · Other. | 6.3, 9.2 |
| Q2 | Does an edit end a run, as DISL requires, or leave it playing, as the hook does? | **(default) An edit ends it**: any change to the diagram's model, from this canvas or from the same file open elsewhere, ends the run and clears the states. Recommended, because it is a DISL **MUST** and because the hook's frames go stale on an edit (a removed task keeps a badge, a new one has none). The visible change is that the badges and the banner disappear on an edit. · **Edits leave it playing**, as today: the player departs from a DISL **MUST**, recorded as a runtime deviation in the library. · Other. | 7.2, 9.2 |
| Q3 | The hook's failure path and condition outcome are test-only knobs that the definitions cannot express. Is the failure path part of the oracle? | **(default) No**: the oracle is what ships, which is the hook with no profile; the player's own tests prove a failing step and `p` with a test definition that declares a parameter. Recommended, because no user can reach the failure path today. · **Yes**: the job definition gains a `failTaskId` parameter in etalii.adp, so the oracle's failure case runs on the real definition, which needs an etalii.adp change and a way to pass the argument. · Other. | 1.5, 9.1 |

## Alignment with Product Vision

The product direction (2026-09-26) is one definition per tool, interpreted by a core in each host. A simulation is part of the definition, so playing it is the core's work, not a module's. `structure.md` requires canvas infrastructure to carry no diagram-type knowledge and a new diagram type to be addable without touching core code; a player written once lets any diagram module offer a simulation by declaring it. `product.md`'s *Don't reinvent, integrate*: the one engine that exists today is module code standing in for a library that does not yet read the definition.

## Requirements

### Requirement 1: The player plays what DISL 9.6 says

**User Story:** As a tool engineer, I want a `simulate` operation in my definition to play as DISL says, so that I specify a simulation once and every host plays it the same.

#### Acceptance Criteria

1. WHEN a simulation starts THEN every element of the types its `for` names SHALL take its `initial` state at step 0, and only those elements SHALL carry a state.
2. WHEN each further step is computed THEN every taking-part element's `next` SHALL be evaluated against the previous step's states, all elements at once, and the new states SHALL be applied together (a simultaneous update).
3. WHEN a step has been applied THEN `until` SHALL be evaluated once, with `self` bound to the diagram, and the run SHALL finish when it holds, or when `maxSteps` (default 1000) steps have been played.
4. The time between steps SHALL be `stepMs`, 700 ms when absent; `stepMs: 0` SHALL play the steps without delay.
5. The expressions SHALL be evaluated in DISL's `simulation` context, with `self`, `state`, `step` (from 1), `p` (the operation's parameters), `diagram` and `env` bound as section 12.3 says, and with the definition's user functions callable as anywhere else.
6. WHEN an expression fails to evaluate THEN the run SHALL end and clear its states, and the failure SHALL be reported where the library reports a refused action, naming the operation; it SHALL NOT leave a half-played run on the canvas.

### Requirement 2: Any diagram module, with no module code

**User Story:** As a module author, I want a simulation to work from my definition alone, so that I write no engine and no glue for it.

#### Acceptance Criteria

1. WHEN a definition declares an operation whose body is a `simulate` THEN the library SHALL recognise it and play it, without the module declaring a menu entry, intercepting an action id or holding any simulation state.
2. The library SHALL name no diagram type, no element type and no module in its non-test code, and SHALL NOT recognise a simulation by a marker in an action id.
3. WHERE a module maps the definition's operation names to the backend's wire ids (as `x-databricks.actions` does) THEN that existing mapping SHALL be all the module provides; no module SHALL write code per simulation.
4. A module SHALL adopt the player by deleting its own engine and glue; nothing SHALL replace them in the module.

### Requirement 3: A simulation never reaches a file or the history

**User Story:** As a user, I want a simulated run to be only a show, so that pressing it can never change my file.

#### Acceptance Criteria

1. WHEN a simulation is started or plays THEN no command SHALL be dispatched, no undo step SHALL be added, the diagram SHALL NOT be marked modified, nothing SHALL be persisted, no hook SHALL fire and no constraint SHALL see the states.
2. WHEN a simulation is started from the shared menu THEN nothing SHALL be sent to the backend to run it. The backend's list still decides which entries the menu offers, under which label.
3. A test SHALL assert 3.1 and 3.2 and SHALL first be seen to fail against a player that deliberately dispatches a command when a run starts.

### Requirement 4: The state is drawn by the definition

**User Story:** As a tool engineer, I want the state a simulation writes to be read like any attribute, so that my notation says how a running or failed element looks.

#### Acceptance Criteria

1. WHILE a run plays or shows its finished states THEN each taking-part element's `state` attribute SHALL hold its current state, readable by the definition's notation (its conditions, styles and declared classes) as viewer state.
2. WHEN the states are cleared THEN the attribute SHALL be absent again, so a notation condition on its presence no longer holds.
3. FOR the Databricks definitions THEN every element SHALL be drawn with the same `databricks-sim-*` class, at the same moment, as today.

### Requirement 5: The notice

**User Story:** As a user, I want to see that what plays is a simulation and to be able to stop it.

#### Acceptance Criteria

1. WHEN a run starts THEN the notice's `running` text SHALL be shown, and WHEN it finishes THEN its `finished` text SHALL replace it.
2. The notice SHALL be exposed to assistive technology as a status region, as every DISL notice is.
3. The notice SHALL offer a dismissal, and WHEN it is dismissed THEN the run SHALL end and its states SHALL clear.
4. FOR the Databricks definitions THEN the notice SHALL read and look as today's banner does, its dismissal included.
5. WHERE an operation declares no `notice` THEN the run SHALL still be dismissible.

### Requirement 6: The timing

**User Story:** As a user, I want a run to unfold at a pace I can follow, the same pace as today.

#### Acceptance Criteria

1. WHEN a run starts THEN step 0's states and the running notice SHALL be shown at once.
2. Each further step SHALL be shown `stepMs` after the one before it.
3. The finished notice SHALL appear when the run finishes under 1.3, which is the step at which `until` first holds (Q1, default).
4. The player SHALL be testable without waiting in real time: a test SHALL be able to advance its clock.

### Requirement 7: What ends a run

**User Story:** As a user, I want a run to stop when it no longer describes what I see.

#### Acceptance Criteria

1. A run SHALL end, and its states SHALL clear, WHEN its notice is dismissed, WHEN the view is closed, and WHEN the viewpoint changes.
2. A run SHALL end, and its states SHALL clear, WHEN the diagram's model changes, whether the change was made in this canvas or arrived from the same file open elsewhere (Q2, default).
3. WHEN a simulation starts while another plays on the same canvas THEN the first SHALL end and the second SHALL replace it.
4. WHEN a run finishes THEN its last states and the finished notice SHALL stay until one of 7.1 to 7.3 ends it.

### Requirement 8: Read-only mode

**User Story:** As a reader of a diagram I may not edit, I want to play its simulation all the same.

#### Acceptance Criteria

1. WHEN the canvas is read-only THEN the player SHALL play a simulation exactly as it does otherwise, because a simulation changes only viewer state (DISL 9.4, 9.6).

### Requirement 9: The Databricks engine is the oracle

**User Story:** As Peter, I want the player proven against the engine it replaces before the module switches over, so that no visible behaviour of Databricks changes except what this document names.

#### Acceptance Criteria

1. A parity test SHALL play each of the three Databricks definitions through the player and through `useSimulatedRun`, over the Databricks example corpus, and SHALL compare, step by step, every element's state and the time at which it is shown.
2. The parity test SHALL agree everywhere except the differences Q1 and Q2 rule in, each named in the test by its question; any other difference SHALL fail it.
3. The parity test SHALL be seen to fail against a perturbed player, such as one that applies `next` element by element instead of all at once, before it is trusted.
4. The parity test SHALL live with the Databricks module's tests, so that the library itself names no module (2.2), and SHALL be kept by `databricks-disl-fbl` task 14 when it removes the hook, with the hook's frames written into it as the expected values.
5. The player's own tests in the library SHALL cover every criterion of Requirements 1 to 8 with definitions written for the library's tests, so that the player is proven for any module and not only for Databricks.

### Requirement 10: Documentation

**User Story:** As a module author, I want to find out how to give my diagram a simulation without reading the library.

#### Acceptance Criteria

1. `docs/creating-a-diagram-module.md` and `docs/diagram-module-client-api.md` SHALL say that a `simulate` operation in the definition is played by the library, and what a module provides for it, which is nothing beyond its wire-id mapping (2.3).
2. WHERE the player makes a sentence of `docs/architecture.md` or `docs/solution-structure.md` false THEN that page SHALL be updated in the same change.
3. The comments in `definition/actions.ts` and `librarySelection.ts` that name Databricks' simulated runs as the reason a menu entry is never sent SHALL be brought in line with the player in the same change, stating the reason without naming a module.

## Non-Functional Requirements

### Code Architecture and Modularity

- The player belongs to the canvas library; what a simulation does belongs to each definition. Nothing in the player names a diagram type.
- A simulation is viewer state. It stays off the command bus by construction, not by a check at the moment a command is about to be sent.

### Performance

- A step of the Databricks corpus SHALL be computed and drawn well within its 700 ms; a step that takes longer than `stepMs` SHALL delay the next step rather than drop or overlap it.

### Reliability

- One run per canvas at a time, and one source of truth for its states.
- A test written for a defect found while building the player SHALL be seen to fail against that defect first.

### Usability

- jsdom is not evidence for anything drawn. A `tests.md` entry SHALL record a real-browser pass in both themes of a simulation played by the player, on a library test definition or, once task 14 has switched it, on Databricks.

## Out of scope

- Switching the Databricks module to the player, removing `useSimulatedRun.ts` and `simulatedMenuActions`, and its browser pass: `databricks-disl-fbl` task 14.
- Honouring reduced-motion settings by playing without delay, which DISL allows (**MAY**) and the hook does not do; adopting it would change the timing this document keeps.
- Simulations in hosts other than the standalone client, and in headless validators, which never run them.
- A way for the user to pass a simulation's parameters (see Q3).

## Sources

- DISL section 9.6, with 4.3 (transient attributes), 6.13 (notices), 9.4 (viewer-state operations) and 12.3 (contexts), in etalii.adp at `50e524f`.
- `src/diagrams/databricks/client/useSimulatedRun.ts` and `useSimulatedRun.test.ts`, `DatabricksCanvas.tsx` and `databricksBindings.ts`, read at `6faaeaeb3`.
- The three Databricks definitions under `src/diagrams/databricks/definition/`, whose `simulate` operations and user functions were read from the files.
- `src/client/src/canvas/library/definition/actions.ts` (`actionForMenuEntry`), `librarySelection.ts` and `disl/compileNotation.ts` (notices).
- `databricks-disl-fbl` requirements (finding A6, Requirement 8.2) and tasks (task 14).
- `DatabricksContextActionProvider.cs`, which answers the simulated ids as a no-op.
