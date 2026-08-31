# Requirements Document

## Introduction

Today an `.adp` registration file sits beside the document it registers, as a peer in the hierarchy: `test.dsl` and `test.adp` are two siblings, and nothing in the tree says they belong together. This spec makes the relationship visible — the `.adp` file nests **under** the file or folder it belongs to, the subject becomes the primary entry, and opening a diagram starts from the thing the diagram is about rather than from the registration file.

Three consequences follow, and each is larger than it first appears:

- A **file** must be able to have children. The hierarchy contract currently states the opposite in as many words.
- One subject may carry **several** registrations, which needs a naming convention that survives rename and stays unambiguous.
- A registration may belong to a **folder**, in which case it has no base name at all — just `.adp`.

The measurements and code references below were taken from the tree at `21e806a3`.

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **file-native** premise: the nesting is a presentation of what the filesystem already contains. No index, no database, no hidden state — `test.first.adp` and `test.second.adp` are real files that a user can see, copy and commit, and the tree only stops pretending they are unrelated to `test.dsl`.
- [product.md](../../steering/product.md)'s **"A familiar surface"**: nesting a generated or companion file under its subject is what Visual Studio's file nesting and VS Code's file nesting both do, so the shape is one users arrive already knowing.
- [tech.md](../../steering/tech.md)'s **Context** rule — "the context service owns what is selected" — is the reason Requirement 7 does not adopt single-click activation. Selection is load-bearing.

## Requirements

### Requirement 1 — A registration names its subject

**User Story:** As a user with several diagrams about one file, I want their file names to say which file they belong to and to tell each other apart, so that I can read the relationship off the folder without opening anything.

**Current behaviour:** `DiagramFileName.WithExtension` produces exactly `<base>.adp`, and `DiagramFilePair.SiblingPathFor` derives the body by stripping `.adp` and appending the type's extension. So `test.adp` pairs with `test.dsl`.

#### Acceptance Criteria

1.1. A subject file MAY carry more than one registration. Their names SHALL take the form `<subject-base>.<qualifier>.adp` — `test.first.adp`, `test.second.adp` for a subject `test.dsl`.
1.2. The single-registration form SHALL remain `<subject-base>.adp`. Every existing project uses it, and Requirement 11 makes that continuity a hard requirement rather than a courtesy.
1.3. A registration belonging to a **folder** SHALL be named `.adp` exactly — the extension with no base name — and SHALL sit inside the folder it describes.
1.4. Qualifiers SHALL be validated the way base names already are, through `DiagramFileName`'s sanitisation, so a qualifier cannot introduce a path separator or a character the filesystem refuses.
1.5. WHEN a second registration is added to a subject that currently has the unqualified form THEN the naming SHALL be resolved so that both are unambiguous, and the design SHALL state whether the existing `test.adp` is renamed to a qualified form or left as-is alongside `test.second.adp`. Both are defensible; silently doing one of them is not.

### Requirement 2 — Body resolution must survive the qualified name

**User Story:** As a developer, I want `test.first.adp` to resolve to `test.dsl`, so that the new naming does not quietly break the pairing that rename, delete and open all depend on.

**Finding — this is the sharpest technical problem in the spec.** `DiagramFilePair.SiblingPathFor` strips one `.adp` and appends the extension. Given `test.first.adp` it therefore derives `test.first.dsl`, which is not the intended body and in general does not exist. Every caller of `SiblingOf` and `BodyOf` inherits this.

#### Acceptance Criteria

2.1. Body derivation SHALL resolve `<subject-base>.<qualifier>.adp` to `<subject-base><extension>`.
2.2. A genuine ambiguity SHALL be resolved explicitly: a subject legitimately named `my.config.dsl` has registration `my.config.adp`, which is indistinguishable by shape from subject `my.dsl` with qualifier `config`. The design SHALL choose a rule — existence-first (prefer `<full><extension>` when that file exists, else strip the last qualifier) is the obvious candidate — and SHALL cover it with tests in both directions.
2.3. The existing `body:` header SHALL remain the explicit escape hatch. WHERE derivation is ambiguous or the body is not a name-derived sibling, the header SHALL take precedence over any derivation rule.
2.4. The **ownership** model SHALL be restated for the many-to-one case. `DiagramFilePair` currently marks a header-named body `IsOwned: false` precisely so that one of several registrations cannot carry a shared body off during rename or delete. With several registrations name-derived from one subject, ownership becomes many-to-one, and the design SHALL say which operations act on the subject and which act on a single registration.
2.5. The path-escape guard in `ResolveWithin` SHALL be preserved unchanged. A `body:` header is user-editable text, and following it outside the project root would turn an `.adp` file into a way to read arbitrary files through the backend.

### Requirement 3 — A file may have children

**User Story:** As a user, I want to expand a file that has diagrams, so that I can reach a specific diagram without hunting for its registration file among its siblings.

**Finding:** `hierarchy.proto`'s `Entry.has_children` is documented as "for a folder: whether it currently has any entry inside it; **always false for a file**". `EntryKind` has exactly two values, `FILE` and `FOLDER`.

#### Acceptance Criteria

3.1. `Entry.has_children` SHALL become meaningful for a file, and its comment SHALL be corrected. A file's `has_children` SHALL be true when it has at least one registration nested under it.
3.2. A registration entry's `parent_id` SHALL be the entry id of its subject file, not of the containing folder.
3.3. WHETHER a nested registration is reported as `FILE` or as a new `EntryKind` value SHALL be decided in the design. A distinct kind makes the tree's icon and activation rules explicit rather than inferred from the name ending in `.adp`; adding an enum value is a contract change that every client must tolerate. The trade-off SHALL be stated, not assumed.
3.4. Sort order SHALL be defined: today folders sort before files. Registrations nested under a subject SHALL have a stated, stable order, since Requirement 7 depends on which one is "first".
3.5. A registration SHALL NOT also appear as a sibling of its subject. Nesting relocates it in the tree; it does not duplicate it.

### Requirement 4 — A folder may be a subject

**User Story:** As a user with a diagram describing a whole folder, I want the diagram to hang off that folder, so that the thing I click is the thing the diagram is about.

**Context:** this is not hypothetical. `ansible-structure` reads a project tree rather than a single document, which is why it has no document store — a folder-scoped registration is the natural shape for that class of diagram.

#### Acceptance Criteria

4.1. A file named exactly `.adp` inside a folder SHALL register a diagram whose subject is that folder.
4.2. The folder SHALL be the primary entry for activation purposes, per Requirement 7.
4.3. **A `.NET` path pitfall SHALL be verified and guarded.** Measured, after this criterion's own demand for a test caught the original text asserting the exact opposite: `Path.GetExtension(".adp")` returns `".adp"`, and `Path.GetFileNameWithoutExtension(".adp")` returns the **empty string**. .NET does not apply the Unix "leading dot means no extension" convention; that assumption was wrong in both directions. The consequence inverts with it: reasoning about the *extension* of a bare `.adp` is safe, and reasoning about its *name* is the hazard, because the base name is empty. That is the same hazard 4.4 records for `StripExtension(".adp")`, so it is one problem rather than two. The design SHALL audit the name-based call sites, and SHALL ship a test asserting these two values so the corrected fact cannot decay back into folklore.
4.4. `DiagramFileName.StripExtension(".adp")` currently yields an empty base name, and `SiblingPathFor` would then produce a path that is just the extension. Folder-scoped registrations SHALL be excluded from sibling derivation rather than allowed to produce such a path.
4.5. WHETHER a folder may carry more than one registration — and if so, how they are named, given that Requirement 1.3 leaves no room for a qualifier — SHALL be decided in the design. If the answer is "one per folder", that SHALL be enforced with a clear error rather than left as an accident of naming.

### Requirement 5 — Rename works in both directions

**User Story:** As a user renaming a file, I want its diagrams to follow it, so that renaming does not silently orphan every diagram about that file.

**Finding:** `RenameEntryCommandHandler` currently resolves the *owned sibling* of the `.adp` being renamed and renames it alongside — the rename is driven **from** the registration. Under this spec the subject becomes the primary entry, so the common case inverts: the user renames `test.dsl`, and the registrations must follow.

#### Acceptance Criteria

5.1. WHEN a subject file is renamed THEN every registration nested under it SHALL be renamed to match, preserving each qualifier: renaming `test.dsl` to `prod.dsl` SHALL produce `prod.first.adp` and `prod.second.adp`.
5.2. WHEN a registration is renamed THEN only its qualifier SHALL be affected, and a rename that would change the subject portion SHALL be refused with an explanation rather than silently re-pointing the registration at a different file.
5.3. The rename SHALL be atomic in effect: IF any part of the set cannot be renamed THEN the operation SHALL leave the set as it was, rather than half-renamed. A half-renamed set is worse than a refused rename, because it orphans some registrations while appearing to have succeeded.
5.4. A rename that would collide with an existing name SHALL be refused, and the refusal SHALL name the conflict.
5.5. Renaming a folder that carries a `.adp` SHALL keep the registration inside it. Since the registration has no base name, nothing about it changes — this criterion exists so the case is tested, not because it needs logic.
5.6. WHEN a subject is renamed such that its registrations' derived body no longer resolves THEN the failure SHALL surface as an error, not as a silently unopenable diagram.

### Requirement 6 — Delete and move follow the same relationship

**User Story:** As a user deleting a file, I want a predictable answer about what happens to its diagrams, so that I am not left with registrations pointing at nothing.

**Finding:** `DeleteEntryCommandHandler` already resolves and deletes the owned sibling. Nesting changes what "owned" means for a subject with several registrations.

#### Acceptance Criteria

6.1. The behaviour when a subject is deleted SHALL be defined explicitly — cascade to its registrations, or refuse, or orphan them visibly. The design SHALL choose one and say why.
6.2. Deleting one registration SHALL NOT delete the subject, and SHALL NOT delete a body that other registrations still reference. This is the guarantee `IsOwned` exists to provide today and it SHALL survive.
6.3. WHERE move or copy is already supported for hierarchy entries, it SHALL be checked against the nesting model and any gap recorded. This spec does not add move; it requires that move not be left silently broken by nesting.

### Requirement 7 — Activation opens the right diagram

**User Story:** As a user, I want opening a file with one diagram to be one gesture, and opening a specific diagram among several to be direct, so that the common case is fast and the precise case is possible.

**A conflict in the request, stated plainly.** The request says "clicking the primary file/folder will open up the corresponding diagram" and, for the multiple case, "double clicking". The tree today activates on **double-click** (`ExplorerTreePanel.tsx:900`, `onDoubleClick={() => onActivate(...)}`) and uses single-click for selection. Making single-click open a diagram would mean a user can no longer select a file without opening something — and selection is what the context service, the property grid and the context menu all read. This spec therefore treats "clicking" as the tree's existing **activation** gesture rather than as a literal single click. If a literal single-click open is genuinely wanted, it is a change to the tree's interaction model for every node type and needs its own decision.

#### Acceptance Criteria

7.1. WHEN a subject with exactly one registration is activated THEN that diagram SHALL open, without the user expanding anything.
7.2. WHEN a subject with several registrations is activated THEN the default registration SHALL open, and "default" SHALL be defined by the stable order from Requirement 3.4 rather than by whichever the filesystem happened to return first.
7.3. WHEN a nested registration is activated directly THEN that specific diagram SHALL open, regardless of which is default.
7.4. WHEN a subject with **no** registration is activated THEN the existing behaviour for that file SHALL be unchanged. This spec SHALL NOT change what happens to ordinary files.
7.5. Activation SHALL work identically for a folder subject with a `.adp` as for a file subject.
7.6. Keyboard activation SHALL work wherever mouse activation does, and `aria-expanded` SHALL be set correctly for an expandable file — it is currently emitted only for folders (`ExplorerTreePanel.tsx:893`).

### Requirement 8 — Orphans and broken pairs are visible, not silent

**User Story:** As a user, I want a registration whose subject is missing to be visible, so that a broken diagram is something I can see and fix rather than something that quietly vanishes.

#### Acceptance Criteria

8.1. A registration whose derived subject does not exist SHALL still appear in the tree, at the level it sits on disk, rather than being hidden because it has no parent to nest under.
8.2. Such an entry SHALL be distinguishable from a correctly-nested one.
8.3. WHEN the missing subject is created THEN the registration SHALL re-parent under it without requiring a refresh, per Requirement 10.
8.4. A registration naming an unknown diagram type SHALL keep its current behaviour, which is to resolve to no definition and therefore no body. This spec SHALL NOT make an unknown type an error.

### Requirement 9 — The client tree must learn that files expand

**User Story:** As a client developer, I want every place that assumes "only folders expand" found and changed together, so that the tree does not half-support nesting.

**Finding:** `ExplorerTreePanel.tsx` gates on `EntryKind.FOLDER` in **nine** places — the expandable computation at line 887 (`isExpandable = isFolder && node.hasChildren`), the `aria-expanded` attribute at 893, the children render at 954, child loading at 219, keyboard navigation at 716 and 735, the sort comparator at 63-64, the icon selection at 325, and the activation-toggle at 593.

This count was originally six. The three that were missed — sort, icon, activation-toggle — all fail as "looks slightly wrong" rather than "does not work", which is precisely why 9.1 demands an exhaustive audit: a manual pass finds that class of site last, or never.

#### Acceptance Criteria

9.1. Every folder-gated site SHALL be audited and updated, and the audit SHALL be exhaustive rather than driven by whichever site broke first in manual testing.
9.2. Expansion, collapse and child loading SHALL work for an expandable file exactly as for a folder.
9.3. Keyboard navigation — expand, collapse, move to first child, move to parent — SHALL treat an expandable file as expandable.
9.4. Icons SHALL distinguish a nested registration from its subject, consistent with the existing Material Design icon treatment.
9.5. The sort that currently places folders before files SHALL be reviewed against nested entries and its intended result stated.

### Requirement 10 — Watched changes re-parent correctly

**User Story:** As a user creating a diagram beside a file, I want the tree to nest it immediately, so that the relationship is not something I have to reload to see.

**Context:** the hierarchy is watched and pushed per connection, with `EntryCreated`, `EntryRemoved`, `EntryRenamed` and `EntryUpdated` messages.

#### Acceptance Criteria

10.1. WHEN a registration is created on disk THEN it SHALL arrive nested under its subject, and the subject's `has_children` SHALL be pushed as an `EntryUpdated` if it was previously false.
10.2. WHEN the last registration under a subject is removed THEN the subject's `has_children` SHALL be pushed as false.
10.3. WHEN a subject file is created after its registration already exists THEN the orphan of Requirement 8 SHALL re-parent, which requires a move rather than a rename — the design SHALL confirm the existing change messages can express re-parenting, and add one if they cannot.
10.4. Ordering SHALL be preserved: a client that applies the pushed changes in order SHALL arrive at the same tree as one that lists fresh.

### Requirement 11 — Existing projects keep working

**User Story:** As an existing user, I want my current diagrams to open after this change, so that a presentation improvement does not cost me my files.

#### Acceptance Criteria

11.1. An existing `test.adp` beside `test.dsl` SHALL continue to resolve, open, rename and delete exactly as before, and SHALL now additionally nest.
11.2. No file on disk SHALL be renamed, moved or rewritten as a consequence of adopting this spec. The nesting is a presentation change over unchanged files, except where the user explicitly renames something.
11.3. WHERE Requirement 1.5 decides that adding a second registration renames the first, that rename SHALL be an explicit, user-visible consequence of an explicit action, never a migration that runs on open.
11.4. A project opened by an older client SHALL remain readable — the files are unchanged, so this SHALL be confirmed rather than engineered.

### Requirement 12 — Verification

**User Story:** As a maintainer, I want this change gated the way the rest of the backend is, so that a hierarchy change cannot land half-verified.

#### Acceptance Criteria

12.1. `dotnet test --solution EtAlii.Adp.slnx` SHALL report `failed: 0`, checked by exit code, since a zero-test run exits 5 while printing no failures.
12.2. `dotnet format style --verify-no-changes --severity info` SHALL exit zero.
12.3. The naming, derivation and ambiguity rules of Requirements 1 and 2 SHALL be covered by unit tests against `DiagramFileName` and `DiagramFilePair`, which already have test files.
12.4. Rename in both directions, including the atomicity of 5.3 and the refusal of 5.2, SHALL be covered by tests on `RenameEntryCommandHandler`.
12.5. The client tree's expandable-file behaviour SHALL be covered in `ExplorerTreePanel.test.tsx`.
12.6. Per [CLAUDE.md](../../../CLAUDE.md), any bug found during implementation or verification SHALL leave a guard behind — a test where one can express it, an entry in `tests.md` where only a running app can.

## What this spec does not change

- **No new diagram type.** This is hierarchy and routing, so [docs/diagrams.md](../../../docs/diagrams.md) needs no new row and the four mandatory aspects of a diagram-type spec do not apply.
- **No change to the `.adp` file format.** The first line stays the MIME type; `body:` and `view:` headers keep their meaning.
- **No change to how a diagram renders** once opened.
- **No change to the tree's activation gesture** for any node type, per Requirement 7's note.

## Open questions for the design phase

1. **Requirement 2.2** — the `my.config.dsl` ambiguity. Existence-first derivation is the obvious rule, but it makes resolution depend on what is on disk at the time, which is a behaviour worth entering deliberately.
2. **Requirement 3.3** — a new `EntryKind` for registrations, or reuse `FILE`? A distinct kind makes the client's rules explicit; it also widens the contract.
3. **Requirement 1.5** — when a subject gains its second registration, is the first renamed into the qualified form?
4. **Requirement 4.5** — may a folder carry more than one registration, given that the bare `.adp` name leaves no room for a qualifier?
5. **Requirement 6.1** — deleting a subject: cascade, refuse, or orphan?
