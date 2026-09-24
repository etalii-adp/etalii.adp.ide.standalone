# Requirements Document

## Introduction

This spec changes the **Add** context action when the selection is a **folder**, and corrects the shipped registrations that got the old shape.

A registration that registers a *file* names that file: `courier.dsl` is registered by `courier.adp`. A registration that registers a *folder* is different — it lives **inside** the folder it registers, so the folder's own name already says what is registered, and any name on the `.adp` is redundant at best and misleading at worst (`infrastructure/structure.adp` reads as though something called "structure" were registered). The correct shape for a folder-subject registration is therefore the bare extension: `infrastructure/.adp`.

Two things follow, and this spec covers both:

1. **The Add dialog** must stop asking for a name when the chosen type registers a folder — as a *condition inside the existing dialog*, not a second dialog. The type is still chosen exactly as it is today.
2. **The registrations already on disk** that carry a name where they should be bare must be corrected, with a guard so the dialog cannot reintroduce them.

### Survey (as of develop `97f64aee`)

Measured directly, on a tree several agents are changing concurrently; the design re-measures against its own commit.

**Folder-subject modules — exactly two.** `Subject: DiagramSubject.Folder` is declared in precisely two definitions: `ansible/structure` (`src/diagrams/ansible-structure/.../Diagram.cs:32`) and `helm/chart` (`src/diagrams/helm-charts/.../Diagram.cs:33`). Nothing else in the tree registers a folder, so the scope of the correction is structurally bounded — there is nowhere else for a mis-named folder registration to hide.

**Registrations — 116 `.adp` files, of which 1 is bare.** The bare one is `src/examples/diagrams/ansible-structure/wordpress-nginx/.adp`, and it is the only registration in the tree already in the correct folder-subject shape. The other 115 are named, and **most of them are correct**: a file-subject registration *should* be named. The correction is not "rename everything".

**Folder-subject registrations — 14 total: 1 already bare, 13 to correct.**

`ansible/structure` — 7 (1 bare, 6 to correct):

- `src/diagrams/ansible-structure/examples/example 1/infrastructure/structure.adp`
- `src/examples/diagrams/ansible-structure/example 1/infrastructure/structure.adp`
- `src/examples/diagrams/ansible-structure/jboss-standalone/structure.adp`
- `src/examples/diagrams/ansible-structure/phillips_hue/structure.adp`
- `src/examples/diagrams/ansible-structure/rust-module-hello-world/structure.adp`
- `src/examples/diagrams/ansible-structure/tomcat-memcached-failover/structure.adp`
- `src/examples/diagrams/ansible-structure/wordpress-nginx_rhel7/structure.adp`
- already correct, leave alone: `src/examples/diagrams/ansible-structure/wordpress-nginx/.adp`

`helm/chart` — 6, all to correct:

- `src/diagrams/helm-charts/examples/hello-world/helm-chart.adp`
- `src/diagrams/helm-charts/examples/nginx/helm-chart.adp`
- `src/diagrams/helm-charts/examples/prometheus/helm-chart.adp`
- `src/examples/diagrams/helm-charts/hello-world/helm-chart.adp`
- `src/examples/diagrams/helm-charts/nginx/helm-chart.adp`
- `src/examples/diagrams/helm-charts/prometheus/helm-chart.adp`

**The two copies are not symmetric.** Helm's module examples and showcase copies mirror each other exactly (`hello-world`, `nginx`, `prometheus` in both) — six files, three pairs. Ansible's do not: the module ships only `example 1`, while the showcase ships fourteen folders, of which seven carry a folder-subject registration. Several showcase ansible folders (`lamp_simple`, `mongodb`, `windows`, …) carry no registration at all, which is outside this spec.

**The dialog as it stands.** For a folder target, `AddDiagramContextActionProvider.ExecuteAsync` answers `ContextExecutionRequiresChoice` with a `ChoiceDialogPrompt` carrying the type tree and a **prompt-level** `name_field`; `CommitAsync` then validates the typed name and writes `<name>.adp`. The structural crux for this change: **whether a name is wanted depends on the option the user picks, but `name_field` is a property of the whole prompt.** There is precedent for per-option data driving that field — `ContextOption.suggested_value` is already computed per option when the prompt is built, precisely so picking an option costs no round trip — so a per-option signal is the natural shape. This document states the required behaviour; the design chooses the carrier.

**A rename risk worth naming.** `structure.adp` and `helm-chart.adp` appear in 40 places across the tree. Nearly all are test-owned fixtures that create their own registration in a temp folder, so correcting the *shipped examples* does not break them. But at least one encodes the old shape as a definition — `EntryDiagramStates.Tests.cs:105` reads `DeclaresFolderSubject(registration) => registration == "structure.adp"` — so after this change some tests describe a shape the product no longer creates. They are not thereby wrong (see Requirement 4), but the design should say which of them it touches.

## Alignment with Product Vision

*Files are the source of truth*, and a file's name is part of what it says. A folder-subject registration named `structure.adp` states something untrue about what it registers; the bare `.adp` states exactly what is true — "this folder is the subject." The dialog should make the truthful shape the only one it can produce.

## Requirements

### Requirement 1 — The Add dialog suppresses the name for a folder-subject type

**User Story:** As a user adding a diagram to a folder, I want the tool to stop asking me for a file name it is not going to use, so that I cannot supply one that is silently discarded or wrongly honoured.

#### Acceptance Criteria

1. WHEN the Add action is invoked on a folder THEN the diagram type SHALL still be chosen exactly as it is today, in the same dialog, with the same tree of options — this spec adds a condition, it does not add a dialog.
2. WHEN the option selected in that dialog is a **folder-subject** type THEN the name field SHALL NOT accept a name, and the registration SHALL be written as the bare extension `.adp` inside the selected folder.
3. WHEN the name field is suppressed THEN the dialog SHALL say why in place of the field (for example: the registration is created as `.adp` inside the folder, because the folder is the subject) — a field that silently vanishes is its own usability defect.
4. WHEN the option selected is a **file-subject** type THEN the name field SHALL behave exactly as it does today, including its suggested value; this change SHALL NOT alter the file-subject path.
5. WHEN the user switches between a folder-subject and a file-subject option inside the open dialog THEN the name field SHALL follow the current selection, without a round trip and without closing the dialog.
6. WHEN a name is submitted for a folder-subject type anyway (a stale or scripted client) THEN the commit SHALL ignore or refuse it rather than write `<name>.adp`; the backend SHALL NOT depend on the client having hidden the field.

### Requirement 2 — One folder holds one registration, and the collision is spoken to

**User Story:** As a user, I want to be told when a folder already has its registration, rather than have my Add silently fail or overwrite something.

#### Acceptance Criteria

1. WHEN a folder already contains a bare `.adp` and a folder-subject type is offered for it THEN the dialog SHALL make that option unavailable with a reason naming the existing registration — following the established convention that an unavailable action carries a reason rather than being omitted.
2. IF such an Add reaches commit regardless THEN it SHALL be refused with a sentence the user can act on, and SHALL NOT overwrite the existing registration.
3. WHEN a folder already contains a *named* folder-subject registration (the legacy shape, pending correction) THEN the same SHALL hold: the folder already has its registration, so a second one is refused rather than written alongside it.

### Requirement 3 — Correct the 13 named folder-subject registrations

**User Story:** As a reader of the shipped examples, I want them to demonstrate the correct shape, so the examples teach the rule rather than contradict it.

#### Acceptance Criteria

1. WHEN the correction is applied THEN each of the 13 registrations listed in the survey SHALL be renamed to the bare `.adp` in its own folder, and the already-bare one SHALL be left untouched.
2. WHEN a registration is renamed THEN its **content SHALL be byte-identical** before and after — this is a rename, not a rewrite; the MIME first line and its terminator are unchanged.
3. WHERE both a module copy and a showcase copy exist (helm's three pairs, ansible's `example 1`) THEN **both SHALL be corrected.** Both are shipped material a reader opens, and leaving either wrong ships a counter-example. The example-replication guard has been disconnected, so the two copies need no longer match byte-for-byte and therefore need not be corrected in the same commit — but neither copy may be left in the old shape.
4. WHEN the rename lands THEN every test and example-walking guard that opens these registrations SHALL still pass (`ExampleRegistrationTests` walks both trees), and the affected diagrams SHALL still open in the running app — folder-subject routing finds the registration inside the folder either way.

### Requirement 4 — Reading a named folder-subject registration keeps working

**User Story:** As a user with an existing project, I want my `infrastructure/structure.adp` to keep opening after this change, so a correction to ADP's own examples is not a breaking change to my files.

#### Acceptance Criteria

1. WHEN a folder contains a *named* registration whose MIME names a folder-subject type THEN routing SHALL still resolve it and the diagram SHALL still open — creation is constrained by this spec, **reading is not**.
2. WHEN the correction is applied to this repository's examples THEN it SHALL NOT be accompanied by a change that rejects named folder-subject registrations elsewhere; a user's project is not migrated by us.
3. WHERE a test encodes the legacy named shape as a fixture THEN it MAY remain as coverage of the read path, and the design SHALL say which such tests it keeps, changes, or gives a bare-shape counterpart.

### Requirement 5 — A guard, so the dialog cannot reintroduce the problem

**User Story:** As a maintainer, I want the old shape to be impossible to ship again, because a correction without a guard is a correction with a countdown on it.

#### Acceptance Criteria

1. WHEN any shipped example under `src/diagrams/*/examples/` or `src/examples/` contains a folder-subject registration carrying a name THEN a test SHALL fail, naming the offending file. The check SHALL derive "folder-subject" from the deployed catalog rather than a hard-coded list of module names, so a third folder-subject module is covered on arrival.
2. WHEN the Add action commits a folder-subject type on a folder THEN a test SHALL assert the created file is exactly `.adp`.
3. WHEN this spec is implemented THEN the four existing gates SHALL stay green.

### Requirement 6 — Sequencing with the two modules' owners

**User Story:** As a coordinator, I want this correction not to collide with the agents who own these very example trees.

#### Acceptance Criteria

1. WHEN the correction is scheduled THEN the overlap SHALL be named explicitly: `ansible-refinements` and `helm-charts` own exactly the two example trees this spec renames files in, and a rename racing another agent's edits to the same folders is a merge conflict by construction.
2. WHEN implementation begins THEN the dialog change (Requirements 1, 2) and the file correction (Requirement 3) MAY be sequenced separately — the dialog change touches no example files and can land first without waiting on either module.

## Non-Functional Requirements

### Usability

- The suppressed name field must read as an explanation, not an absence. The user should learn *why* the name is not theirs to choose.

### Reliability

- No Add may overwrite an existing registration. The rename correction changes names only, never bytes.

### Compatibility

- Creation is constrained; reading stays tolerant. A project in the wild carrying a named folder-subject registration keeps working, and nothing in this spec migrates a user's files.

### Code Architecture and Modularity

- The condition belongs in the existing add-action provider and the existing choice dialog. No new dialog type, and no per-module special case: the rule is derived from the definition's own `Subject`, so a future folder-subject module inherits the behaviour with no extra code.
