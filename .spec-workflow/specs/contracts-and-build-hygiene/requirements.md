# Requirements Document

## Introduction

A read of the contracts, the build configuration and the documentation that describes them, at develop `469b1fbd`. Nine findings: **five defects** — something is wrong now — and **four items of drift**, where a document was true when written and misleads a reader today.

The build configuration itself came out clean, and that is worth recording as loudly as the defects, because it is what makes the defects legible: one `TargetFramework` (`net10.0`) in `src/Directory.Build.props:5` with no project overriding it, `Nullable` and `ImplicitUsings` enabled centrally, **zero** floated package versions (every `PackageReference` version lives in `src/Directory.Packages.props`), and **every** `*.Tests.csproj` declaring `<OutputType>Exe</OutputType>` — the omission that would report as `Zero tests ran` rather than as a broken build. All four gates pass locally on `469b1fbd`: `dotnet test` **0** (4,399 tests, 0 failed), `dotnet format style` **0**, `npm test` **0** (82 files, 809 tests), `npm run typecheck` **0**.

The most serious finding is that **the release pipeline has been attaching version tags to the wrong commits**, and it has done so at least three times.

## Alignment with Product Vision

A contract is a promise about what the other side may rely on, and a tag is a promise about which source produced a binary. Both are load-bearing here: *files are the source of truth*, and a release that cannot say which commit it came from breaks that at the one point where a user outside the repository is looking. The documentation rules in CLAUDE.md exist for the same reason — a catalog that reports a state the product does not have is a file lying about the product.

## Requirements

### Requirement 1 — A release tag names the commit it was built from

**User Story:** As someone downloading a release, I want its tag to point at the source that produced it, so the version I read in the app tells me what I am running.

The defect. `.github/workflows/build.yml:154` runs `gh release create "v$SEMVER" "<zip>" --title ... --generate-notes` with **no `--target`**. Without one, the tag is created at the repository's default branch tip at API time rather than at `github.sha`, so the tag's commit and the built commit are the same only by luck.

It has not been luck. On origin right now:

- `v0.1.461-alpha`, `v0.1.465-alpha` and `v0.1.476-alpha` **all point at the same commit**, `f5a755c1`.
- `v0.1.469-alpha` points at `8dfb7f64`, which is a **descendant** of `f5a755c1` — a newer commit carrying a *lower* version than 476.

Neither is possible if each run tagged the commit it built: Nerdbank.GitVersioning's height rises monotonically along ancestry, so one commit has one version, and a descendant's version is higher. The tags are misattributed. The precise sequence cannot be reconstructed from the repository alone — that needs the Actions logs, which are unreadable from a development machine because the repository is private and neither `gh` nor a token is available there.

#### Acceptance Criteria

1. WHEN the release job creates a tag THEN it SHALL pass the built commit explicitly (`--target ${{ github.sha }}`), so the tag cannot land anywhere else.
2. WHEN a release is published THEN the tag's commit, the ZIP's name, and the version stamped into the shipped assemblies SHALL all describe the same build — the `tests.md` "one build, one number, four places" check made structural rather than manual.
3. WHERE existing tags are already misattributed THEN this spec SHALL decide their fate explicitly rather than silently: the affected tags are `v0.1.461-alpha`, `v0.1.465-alpha`, `v0.1.469-alpha` and `v0.1.476-alpha`, and deleting or retaining a published tag is the user's call, not an implementation detail.

### Requirement 2 — The republish guard guards the commit, not just the version string

**User Story:** As a maintainer, I want the pipeline to refuse to publish the same source twice, because a second release of one commit under a new number is exactly what makes a version number meaningless.

`build.yml:105` refuses only when a tag with the same *name* already exists. `f5a755c1` was published three times under three names and the guard passed every time — it was never asked the question that mattered.

#### Acceptance Criteria

1. WHEN the release job runs for a commit that an existing tag already points at THEN it SHALL stop, naming that tag.
2. WHEN the version string collides THEN the existing loud refusal SHALL remain — this requirement adds a check, it does not replace one.
3. WHEN either refusal fires THEN the gate job's own verdict SHALL stand unaffected, as it does today.

### Requirement 3 — Two release runs cannot race

**User Story:** As a maintainer, I want overlapping pushes to serialise, so two runs cannot both decide what the latest release is.

The workflow declares no `concurrency` group, so pushes landing close together run their release jobs in parallel. That is the mechanism by which Requirements 1 and 2 became visible defects rather than latent ones.

#### Acceptance Criteria

1. WHEN a push to `develop` starts a run while another is in flight THEN the release work SHALL serialise rather than overlap.
2. WHEN runs are serialised THEN a superseded run SHALL NOT publish a release for a commit that is no longer the tip, and SHALL say so rather than failing silently.
3. WHEN the gates job is the only job that runs (a pull request) THEN nothing about its behaviour SHALL change.

### Requirement 4 — The contracts carry no vocabulary the product does not use

**User Story:** As someone reading a `.proto` to learn what the wire can express, I want every field and value in it to mean something, so I do not build on a promise nothing keeps.

Measured across all 22 `.proto` files: 122 enum values, of which **12** are referenced by no hand-written code on either side (build output under `obj/` and `generated/` excluded, since generated code mentions everything).

- `src/diagrams/c4/api/c4.proto:13-21` — the whole of `C4ElementKindProto`, nine values. The live vocabulary is the module's own C# `C4ElementKind`, used throughout `C4ContextActionProvider.cs`; the proto enum is a parallel duplicate that never reaches the wire. Two enums for one concept is how they come to disagree.
- `src/api/context.proto:110` `RENAME_REQUEST` and `:111` `FOCUS_ONLY` — the gesture-shaped pair of `ContextSelectionAction`. Agent 3 already judged that leaning on `RENAME_REQUEST` would be wrong because it describes a gesture rather than a property of a prompt; `FOCUS_ONLY` is its sibling.
- The remaining two are `*_UNSPECIFIED = 0` values in `azure-pipeline` and `c4`. **These are correct as they stand** — proto3 wants a zero default — and this spec must not "clean" them.

#### Acceptance Criteria

1. WHEN a value or field exists in a contract THEN either something SHALL use it, or a comment SHALL say why it is reserved.
2. WHEN a concept is modelled twice — once in a contract and once in hand-written code — THEN one SHALL be removed or the two SHALL be generated from the other; `C4ElementKindProto` and `C4ElementKind` are the case in hand.
3. WHEN a value is removed from a contract THEN its field number SHALL be `reserved`, because a number reused later is a wire-compatibility break that no test would catch.
4. WHERE a value is a proto3 zero default THEN it SHALL be left alone.

### Requirement 5 — CLAUDE.md and `docs/diagrams.md` agree on the state vocabulary

**User Story:** As an agent about to update the catalog, I want one list of states, so I do not "correct" fifteen rows into being wrong.

`CLAUDE.md:53` lists five states — 💡 identified, 📝 specified, ⏸️ to-do, 🛠️ work-in-progress, ✅ implemented. `docs/diagrams.md:18` defines a sixth, **⚗️ Prototype**, and **15 rows use it** — more than any other state except 💡. An agent following CLAUDE.md literally has no icon for the most common non-candidate state and would reasonably rewrite those rows.

#### Acceptance Criteria

1. WHEN the two documents describe the same vocabulary THEN they SHALL list the same states, and one of them SHALL be named as the authority the other defers to.
2. WHEN a state is added or removed THEN both SHALL change in the same commit.
3. This spec SHALL NOT edit CLAUDE.md as part of the scan that found this; the change belongs to whoever owns that document, with the contradiction stated.

### Requirement 6 — The catalog can be read as what ADP actually offers

**User Story:** As a reader of `docs/diagrams.md`, I want a row's state to tell me what the app does with that type, because that is the only reason to consult it.

The drift, and its cause. `docs/diagrams.md` carries 64 rows marked 💡 Identified, which its own legend defines as *"Recognized as a candidate diagram type; no spec yet"*. But `src/backend/EtAlii.Adp.Backend.Service` references **every** module by glob (`..\..\diagrams\*\backend\*\*.csproj`), all 59 module folders hold a backend project, and every `DiagramDefinition` in them is therefore discovered and offered in the Add dialog. `uml/class` is the plain case: `src/diagrams/uml-class/backend/.../Diagram.cs` registers a definition with a title, a description and an icon, and `docs/diagrams.md:31` calls it 💡 Identified with no spec.

Roughly fifty definitions are name-and-icon-only placeholders of this kind. That is not carelessness in the rows — **the vocabulary has no state that means "registered as a placeholder, offered but not implemented"**, so there is no correct row to write. Fixing the rows without fixing the vocabulary would just move the lie.

#### Acceptance Criteria

1. WHEN a diagram type is registered but has no parser THEN the catalog SHALL have a state that says exactly that, and every such row SHALL carry it.
2. WHEN a row states a state THEN it SHALL be derivable from the tree, and a test SHOULD derive it — a catalog checked by hand drifts again the week after it is corrected.
3. WHERE placeholders are offered in the Add dialog THEN this spec SHALL record whether that is intended, because a user choosing one of ~50 unimplemented types is a separate question this scan does not answer.

### Requirement 7 — Publish paths do not depend on one platform's separator

**User Story:** As a maintainer, I want the build to describe paths the same way everywhere, because the exception is what hides.

`src/backend/EtAlii.Adp.Backend.Service/EtAlii.Adp.Backend.Service.csproj` uses backslashes at `:20`, `:21` (two `Exec … WorkingDirectory`) and `:26` (`ClientDistFile Include`), one line above the `RelativePath` at `:30` that was changed to a forward slash precisely because a backslash there decided where files landed on the Linux release runner. The three that remain **do** work — MSBuild normalises those, and the release job has built the client on Linux — so this is consistency rather than a break.

#### Acceptance Criteria

1. WHEN a path appears in a project file THEN it SHALL use forward slashes, which both platforms accept.
2. WHEN this is changed THEN a publish SHALL be run and its output directory inspected, not merely built — that is how the `wwwroot/dist/` duplication was found rather than reasoned about.

### Requirement 8 — Compiler warnings are not invisible

**User Story:** As a maintainer, I want to know whether a warning-free build is a fact or an assumption.

No `TreatWarningsAsErrors`, `WarningsAsErrors` or `AnalysisLevel` is set anywhere in `src/*.props`, `*.targets` or any `.csproj`. The style gate (`dotnet format style`) judges formatting and naming; it does not fail on a compiler warning. So nothing in the four gates would stop a warning from accumulating.

#### Acceptance Criteria

1. WHEN this is decided THEN the decision SHALL be recorded either way — warnings promoted to errors, or explicitly tolerated with the reason.
2. IF warnings are promoted THEN the existing warning count SHALL be measured first, because promoting an unknown number of warnings turns one decision into an unbounded task.

### Requirement 9 — Screenshots match the UI they claim to show

**User Story:** As a reader of the docs, I want a screenshot to be current, or to know that it is not.

All seven images under `docs/screenshots/` were last written 2026-09-03; `src/client/src` changed on 2026-09-04. That is suggestive, not proof — a client change need not alter these views — and it cannot be settled by reading. `docs/screenshots/capture.mjs` and its readme exist to settle it.

#### Acceptance Criteria

1. WHEN a UI change lands that alters a captured view THEN the screenshot SHALL be retaken by the recorded procedure.
2. WHEN this spec runs THEN each of the seven images SHALL be compared against the running app once, and the result recorded — including "unchanged", which is the likely answer for most.

## Non-Functional Requirements

### Reliability

- A release either names its own commit or does not publish. There is no third acceptable outcome, because a misattributed tag is worse than a missing one: it looks like an answer.

### Code Architecture and Modularity

- One concept, one definition. The `C4ElementKindProto` / `C4ElementKind` pair is the instance found; the requirement is the rule.

### Documentation

- A document that describes the tree should be checked against the tree by a test wherever that is possible. `docs/dependencies.md` already is, and is deliberately untouched by this spec; the catalog is the obvious next candidate.

## Out of Scope

- Backend source, client source, `src/diagrams/**` implementation and the test projects — other agents' lanes. Where this scan touched them it was to read, not to judge.
- `docs/dependencies.md`, which has its own guard.
- Editing CLAUDE.md. The contradiction in Requirement 5 is reported, not resolved.
