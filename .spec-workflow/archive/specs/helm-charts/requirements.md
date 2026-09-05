# Requirements Document

## Introduction

This spec covers **one diagram type over a Helm chart** — the folder of files (`Chart.yaml`, `values.yaml`, `templates/`, `charts/`) that Helm's own tooling packages and deploys. Like the Ansible structure diagram before it, the subject is not a single file but a directory whose *layout is the notation*: Helm prescribes where metadata, values, templates and vendored dependencies live, and the diagram makes that prescribed structure — and the relationships threaded through it — visible at a glance.

| Origin | Subject visualized | What it shows |
| --- | --- | --- |
| `helm/chart` | A chart directory (the folder holding `Chart.yaml`) | The chart's anatomy: metadata, the values layering, the templates and what they render, declared dependencies and how (whether) they resolve to vendored subcharts |

**The chart format, as researched.** A chart is a directory named after the chart, carrying a `Chart.yaml` (`apiVersion: v2` for Helm 3; `name` and `version` required; `type` is `application` or `library`; `dependencies[]` entries carry `name`, `version`, `repository`, optional `condition`, `tags`, `alias` and `import-values`), a `values.yaml` of defaults, an optional `values.schema.json`, a `templates/` directory of Go-templated manifests (files starting with `_` are partials that render nothing themselves; `NOTES.txt` renders to install notes; `templates/tests/` holds test hooks), an optional `crds/` directory of plain, untemplated YAML, and a `charts/` directory where dependencies land when vendored — as unpacked chart directories or as `.tgz` archives. `helm dependency update` pins resolved versions in `Chart.lock`. Helm 2 charts (`apiVersion: v1`) declare dependencies in a separate `requirements.yaml`/`requirements.lock` pair instead. Two relationship systems ride on top: a `dependencies[].condition` names a path into the parent's values (`alertmanager.enabled`) that switches the subchart on and off, and a top-level key in the parent's `values.yaml` matching a dependency's name or alias configures that subchart (`global:` is visible to all of them).

**Insight taken from Helm-Visualizer** ([unrealandychan/Helm-Visualizer](https://github.com/unrealandychan/Helm-Visualizer), Apache-2.0), read as input rather than copied as a specification: what makes a Helm visualization useful is the *relationships* — chart to subchart, values layer to values layer, template to the objects it renders — not a file listing. That project also demonstrates what full offline rendering costs: it ships a pure-TypeScript Go-template engine reimplementing 100+ Sprig functions to turn templates into Kubernetes manifests. That is a rendering product in its own right, and this spec deliberately does not buy it — see out-of-scope. (The repository carries `CLAUDE.md`/`AGENTS.md` files addressed to AI agents; those were treated as that project's internal tooling data and played no role here.)

**What is genuinely new here, and what is not.** Three things are new. First, **the central files are unparseable by design**: a Go-templated manifest is not valid YAML until rendered, so this is the first type that must model files it deliberately never parses as their nominal format — templates are read as *lines*, with only literally-written facts (`kind:`, `{{ define "…" }}`, `{{ include "…" }}`) extracted (Requirement 3). Second, **the examples vendor third-party content**: nothing under `src/examples/` carries someone else's code today, so the mechanics of license verification, attribution and provenance travel with this spec (Requirement 11) rather than being improvised at implementation time. Third, this is the **second folder-subject type**, which makes it the proof that `DiagramSubject.Folder` and its validation/staleness seams (built for `ansible/structure`) are generic rather than one module's private arrangement. Everything else — folder registration, the read-only discipline, computed layout with authored repositions, the problems panel, the property grid's honest read-only reasons — is consumed unchanged.

**Where it sits among the types already built.** [`ansible-structure-diagram`](../ansible-structure-diagram/requirements.md) proved the folder subject, the tolerant never-throwing reader, the watched-folder store and the read-only property grid; this type inherits all four. [`databricks-diagrams`](../databricks-diagrams/requirements.md) defined the `.adp` `layout:` block — authored positions in the registration, never in files another tool owns — and this type is its second consumer (Requirement 6). The unresolved-edge treatment (declared dependency with nothing vendored) mirrors Ansible's three-state resolution.

**Reused, not redefined:** the shell and canvas ([`adp-diagram-ide`](../adp-diagram-ide/requirements.md)), `Element`/`Delta` ([`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md)), tabs, the Add flow, the context service and property grid, and the problems panel. **No core changes are expected**: the folder subject exists, the `layout:` block exists. The one seam intersection to verify at design time is the two of them *together* — a folder-subject `.adp` carrying a `layout:` block has no precedent, and if it needs core work, that is a finding to report, not a silent change.

**Deliberately out of scope.**

* **Template rendering.** No Go-template evaluation, no Sprig, no rendered Kubernetes manifests, and therefore no manifest-level object graph (Deployments wired to Services wired to Ingresses). Helm-Visualizer shows the cost of that engine; a later spec can add it behind the same diagram.
* **Values resolution.** The diagram shows the *layering* (which file overrides which), not a computed merge of one environment's effective values.
* **Editing chart content.** ADP writes no chart file, ever — the chart belongs to Helm's tooling and the repository's review process. The only file the module writes is the diagram's own `.adp` (Requirement 6).
* **Registry and cluster connectivity.** No Artifact Hub or OCI fetching, no `helm dependency update`, no live release state, no `helm install`.
* **`values.schema.json` evaluation** (needs a JSON-Schema engine — a dependency this spec refuses to add for one finding) and **security scanning** of rendered output (impossible without rendering).

## Alignment with Product Vision

* [product.md](../../../steering/product.md)'s **"Files are the source of truth"** — the diagram renders the folder a repository already has, changes nothing in it, and its one authored artifact (element positions) is a diffable text block in the `.adp`.
* product.md's **"Linkage over illustration"** — every node is a real file or folder; selection and navigation lead to the artifact itself (Requirement 8).
* product.md's **"Don't reinvent, integrate"** — Helm's own directory convention *is* the notation; ADP adds no format of its own.
* [tech.md](../../../steering/tech.md)'s **Specifying a diagram type** — file format and registration (R1–2), visualization (R3–7), toolbox (stated as not applying, with the reason — R9), context actions and commands (R6, R9).
* [structure.md](../../../steering/structure.md)'s **dependency direction** — one module under `src/diagrams/helm-charts/`, depending on core only.

## Requirements

### Requirement 1 — The chart the repository already has

**User Story:** As a platform engineer, I want ADP to understand the Helm chart my repository already carries, so that visualizing it costs no conversion and touches nothing Helm reads.

#### Acceptance Criteria

1. WHEN a folder contains a `Chart.yaml` THEN the module SHALL treat that folder as a chart root and SHALL recognize, when present: `Chart.yaml`, `values.yaml`, additional values override files (any sibling `*.yaml`/`*.yml` whose name starts with `values` — `values-dev.yaml`, `values.prod.yaml`), `values.schema.json`, `templates/` and its contents (including `_*.tpl` partials, `NOTES.txt` and `templates/tests/`), `crds/`, `charts/` (unpacked chart directories and `*.tgz` archives), `Chart.lock`, `.helmignore`, and — for `apiVersion: v1` charts — `requirements.yaml` and `requirements.lock`.
2. WHEN `Chart.yaml` declares `type: library` THEN the chart SHALL be presented as a library chart, and findings that only make sense for application charts (Requirement 10.6) SHALL NOT be raised against it.
3. WHEN files or folders beyond this list exist in the chart (a `ci/` folder, a `README.md`, source code) THEN they SHALL be ignored without complaint — a chart living inside a larger repository is the normal case, not an error.
4. WHEN anything in the chart folder changes on disk — a template edited, a dependency vendored, `Chart.lock` rewritten — THEN open diagrams SHALL update without a manual refresh, per the live-update behaviour the Ansible store established.

### Requirement 2 — Making a folder a diagram, by saying so

**User Story:** As a user, I want to register a chart folder as a diagram with one small file, so that the chart itself is never modified by the act of visualizing it.

#### Acceptance Criteria

1. WHEN the module declares itself THEN it SHALL expose one `DiagramDefinition` — origin `helm/chart`, with a description and a fitting icon — whose subject is the **folder** (`DiagramSubject.Folder`): no `Extension`, no `body:` header, no sibling body file, exactly as `ansible/structure` established.
2. WHEN a `.adp` file carrying the `helm/chart` MIME line sits in a folder THEN that folder SHALL be the diagram's subject — the `.adp` lives *inside* the chart root, beside `Chart.yaml`.
3. WHEN a user invokes Add on a folder that contains a `Chart.yaml` THEN the choice tree SHALL suggest `helm/chart`; the suggestion SHALL remain a suggestion in the Add flow, never an automatic claim, and Add SHALL remain available on folders without a `Chart.yaml` (the registration is then valid but the diagram reports the folder is not a chart, per Requirement 10.1).
4. WHEN the registration is created through Add THEN the factory SHALL write only the `.adp` file — a chart is never scaffolded into existence by this module; creating charts is `helm create`'s job, not ADP's.

### Requirement 3 — Reading tolerantly, and never parsing what is not parseable

**User Story:** As a user with a half-finished chart open in another editor, I want the diagram to keep working on whatever is readable, so that one broken file degrades the picture instead of blanking it.

#### Acceptance Criteria

1. WHEN the module reads the chart's own YAML — `Chart.yaml`, values files, `Chart.lock`, `requirements.yaml`, files under `crds/` — THEN it SHALL parse them as YAML tolerantly: a file that fails to parse becomes an unreadable-marked node carrying the parse error and its line, and never aborts the read of the rest of the chart.
2. WHEN the module reads files under `templates/` THEN it SHALL NOT parse them as YAML — a Go-templated manifest is not YAML until rendered — and SHALL instead scan lines for literally-written facts only: `kind:`/`apiVersion:` values on untemplated lines (a template file's caption lists the kinds it renders, and one file may render several), `{{ define "name" }}` in partials, and `{{ include "name" … }}`/`{{ template "name" … }}` references with literal string names. A fact that is templated (`kind: {{ .Values.kind }}`) SHALL be treated as unknown, not guessed.
3. WHEN a `.tgz` archive sits in `charts/` THEN it SHALL be represented without being unpacked — its node is sealed, labeled by file name, and carries no interior.
4. WHEN an unpacked chart directory sits in `charts/` THEN it SHALL be read as a nested chart (its own `Chart.yaml`, values, templates summarized) one level deep; deeper vendored nesting SHALL be summarized by count rather than recursed without bound.
5. WHEN the module reads anything THEN it SHALL never throw out of the read path and SHALL never write: reading a chart SHALL NOT create, modify or delete any file in it (Requirement 7 makes this a guarantee, not a habit).

### Requirement 4 — The model: nodes

**User Story:** As a reader, I want every meaningful artifact of the chart to be a node I can point at, so that the diagram is an inventory I can trust.

#### Acceptance Criteria

1. WHEN a chart opens THEN the model SHALL contain: one **chart** node (name, version, `appVersion`, `type`, `apiVersion`, description, deprecation flag); one **values** node per values file, the default `values.yaml` distinguished from overrides; a **schema** node when `values.schema.json` exists; one **template** node per file under `templates/` (partials, `NOTES.txt` and test hooks each visually distinct from manifest templates); a **crds** node summarizing `crds/` when present; one **dependency** node per `dependencies[]` entry (name, alias when set, version constraint, repository, condition when set); one **subchart** node per unpacked directory and one **archive** node per `.tgz` in `charts/`; and a **lock** node when `Chart.lock` (or `requirements.lock`) exists.
2. WHEN a template node's kinds were discovered (R3.2) THEN they SHALL caption the node; a manifest template with no discoverable kind SHALL still be a node, captioned as undetermined rather than dropped.
3. WHEN the chart is `apiVersion: v1` THEN dependency nodes SHALL come from `requirements.yaml` and the whole SHALL be marked legacy (Requirement 10.7).
4. WHEN element kinds are mapped to the wire THEN each SHALL be a distinct element type under the `helm/chart` origin, following the `origin+kind` convention the Ansible module established, with no extension of the core `Element`/`Delta` vocabulary.

### Requirement 5 — The model: edges (the relationships are the diagram)

**User Story:** As a reviewer, I want the chart's wiring drawn — what depends on what, what overrides what, what includes what — so that questions that today need four files open are answered by one picture.

#### Acceptance Criteria

1. **Declares:** WHEN `Chart.yaml` declares a dependency THEN an edge SHALL run from the chart node to that dependency node, labeled with the version constraint.
2. **Resolves:** each dependency SHALL carry a three-state resolution, mirroring Ansible's treatment — **Resolved** (a matching unpacked directory or archive exists in `charts/`, edge drawn to it), **Unvendored** (nothing in `charts/` — drawn as a marked open end, and *not* a validation finding: a repository that vendors nothing and lets `helm dependency build` fetch at deploy time is the common case), or **Undeclared** (content in `charts/` with no declaring entry — drawn and reported, Requirement 10.4). Matching SHALL respect `alias`.
3. **Overrides:** WHEN override values files exist THEN each SHALL connect to the default `values.yaml` node, so the layering reads as a stack; the diagram SHALL NOT attempt to merge them (out of scope).
4. **Configures:** WHEN the default `values.yaml` carries a top-level key equal to a dependency's name or alias THEN a configures edge SHALL run from the values node to that dependency node; a `global:` key SHALL mark the values node rather than fan out one edge per dependency.
5. **Conditions:** WHEN a dependency declares a `condition` THEN the dependency node SHALL carry it as a badge, resolved against the default `values.yaml` to show its current truth where the path exists.
6. **Includes:** WHEN a template references a named template (`include`/`template` with a literal name, R3.2) that a partial in the same chart defines THEN an edge SHALL run from the template node to the partial; a reference no local partial defines SHALL be drawn as an open end (library charts provide these at render time, so it is a marked unknown, not a finding).
7. **Locks:** WHEN `Chart.lock` exists THEN each dependency node SHALL additionally carry the pinned version from the lock beside its declared constraint.

### Requirement 6 — Layout: computed by the module, arranged by the user

**User Story:** As a user, I want a sensible picture without doing anything, and my own arrangement remembered when I make one, so that a large chart stays legible.

#### Acceptance Criteria

1. WHEN a chart opens with no stored positions THEN the module SHALL compute a layout that groups the anatomy legibly — metadata (chart, lock, schema), the values stack, the templates, and the dependency/subchart column — with edges routed by the shared connection drawing.
2. WHEN the user drags an element THEN it SHALL move, and the position SHALL be stored in the diagram's own `.adp` file via the core `layout:` block that [`databricks-diagrams`](../databricks-diagrams/requirements.md) Requirement 7 defined — the chart folder SHALL NOT change by a single byte. This module is that block's second consumer and its first folder-subject consumer.
3. WHEN a diagram reopens THEN stored positions SHALL win over computed ones element by element; an element with no stored position takes its computed place, and a stored id that no longer exists is ignored on read and dropped on the next layout write.
4. A reposition SHALL be an `ICommand` with a working inverse — the module's **only** command, because it is the module's only edit. Every other mutation is out of scope by Requirement 7.

### Requirement 7 — Zero writes to the chart, guaranteed

**User Story:** As a reviewer, I want certainty that opening a diagram never dirties the repository, so that ADP can be pointed at a production chart without ceremony.

#### Acceptance Criteria

1. WHEN a chart is opened, browsed, laid out, validated and closed THEN every file in the chart folder SHALL be byte-identical to before — trivially, because the module has no writer for chart content at all.
2. WHEN this guarantee is tested THEN it SHALL be pinned by a zero-writes test over a fixture chart, the way the Ansible module pinned the same promise; the fixture bytes SHALL be protected from line-ending normalization by the repository's established `.gitattributes` mechanism.
3. BECAUSE nothing is written, the CST parse-and-splice writer discipline of the editable types SHALL be stated as **not applying** to chart content, per tech.md's rule that an inapplicable aspect is declared with its reason rather than left out. The `.adp` `layout:` writes of Requirement 6 go through the core mechanism, not a module writer.

### Requirement 8 — Navigation: every node is a door

**User Story:** As a user, I want to jump from the picture to the artifact, so that the diagram is a map of the chart and not a dead end.

#### Acceptance Criteria

1. WHEN a file-backed node (template, values file, `Chart.yaml`, lock, schema, crd file) is activated THEN ADP SHALL open that file in the matching text editor, exactly as the Ansible module's navigation behaves.
2. WHEN an unpacked subchart node is activated AND that subchart folder carries its own `helm/chart` `.adp` THEN ADP SHALL offer to open the subchart's own diagram; without one, activation SHALL fall back to revealing the folder in the explorer.
3. WHEN a sealed archive node or an unvendored dependency's open end is activated THEN nothing SHALL open, and the property grid SHALL say why (no file to open; nothing vendored).

### Requirement 9 — Toolbox, context actions and the property grid

**User Story:** As a user, I want the panels around the canvas to tell the truth about a read-only diagram, so that what I cannot do is explained rather than merely absent.

#### Acceptance Criteria

1. The toolbox SHALL contribute **nothing** for this type, stated with the reason: elements mirror what exists on disk, and disk content is created by Helm tooling and text editors, not by dropping shapes — the same position `ansible/structure` recorded.
2. Context menus SHALL offer only navigation (Requirement 8) and the standard diagram-level entries; no create, connect, rename or delete gestures SHALL be offered anywhere.
3. WHEN any element is selected THEN the property grid SHALL show its real properties (the chart node Chart.yaml's fields; a dependency its name, alias, constraint, pinned version, repository, condition and resolution state; a template its path and discovered kinds; a values file its path and role in the stack), every row read-only with a true reason naming the defining file — the "Defined in …; edit it in a text editor." convention.
4. WHEN nothing is selected THEN the diagram-level properties SHALL summarize the chart: name, version, type, apiVersion, counts of templates, dependencies and values layers.

### Requirement 10 — Telling the user what is broken (read-only does not mean silent)

**User Story:** As a chart author, I want structural mistakes surfaced in the problems panel with a file and line, so that the diagram earns its keep as a reviewer, not just a picture.

#### Acceptance Criteria

1. WHEN the registered folder holds no `Chart.yaml` THEN validation SHALL report exactly one finding saying the folder is not a chart, attributed to the folder, and the diagram SHALL present its empty state rather than an error page.
2. WHEN `Chart.yaml` fails to parse, or parses but lacks `name` or `version` THEN validation SHALL report it as an error at the file (and line, where determinable); a `version` that is not SemVer SHALL be a warning.
3. WHEN any other chart-owned YAML (values files, lock, `crds/*.yaml`, `requirements.yaml`) fails to parse THEN validation SHALL report the file and line as an error, while the rest of the diagram stays alive (R3.1).
4. WHEN `charts/` holds a subchart or archive that no dependency declares THEN validation SHALL warn (Undeclared, R5.2); the Unvendored state SHALL NOT be a finding.
5. WHEN a `Chart.lock` exists THEN a declared dependency missing from it, or a lock entry with no declaring dependency, SHALL each be a warning (the lock is stale); WHEN dependencies are declared and no lock exists THEN that SHALL NOT be a finding.
6. WHEN an application chart's `templates/` is absent or empty THEN validation SHALL warn; a library chart SHALL be exempt (R1.2). A dependency `condition` whose path does not exist in the default `values.yaml` SHALL be a warning. Two dependencies resolving to the same effective name (name or alias collision) SHALL be an error.
7. WHEN the chart is `apiVersion: v1` THEN validation SHALL report one informational finding naming it legacy Helm 2 format.
8. Every finding SHALL carry the file (or folder) and line where determinable and a sentence a person can act on, surfaced through the standard problems panel with go-to navigation — including folder-attributed findings, which the core `ProblemStamp` staleness fix already supports.
9. WHEN validation runs over the shipped examples THEN it SHALL report no findings (Requirement 11.6).

### Requirement 11 — Examples: real charts from well-known repositories, vendored honestly

**User Story:** As a user meeting this diagram type, I want to open real, recognizable charts rather than toy inventions, so that the diagram proves itself on the kind of chart I actually have.

#### Acceptance Criteria

1. WHEN the module ships THEN it SHALL carry at least three example charts under `src/diagrams/helm-charts/examples/`, taken from at least two different well-known public repositories, replicated to `src/examples/diagrams/helm-charts/` per the established convention. **The copies are no longer byte-compared:** `ExampleReplicationTests`, which enforced byte-identity when this was written, was deleted on 2026-09-03 when the two example trees were deliberately disconnected. Seeding the showcase copy remains a step of the work; what covers both trees now is the central `ExampleRegistrationTests`, which opens every registration in each of them against the deployed catalog. No second guard SHALL be built beside it.
2. WHEN a source is selected THEN its license SHALL be verified **permissive** (Apache-2.0, MIT or BSD) *before anything is copied*, and a source whose license forbids or encumbers redistribution SHALL NOT be used. The candidates verified during this spec's research (all Apache-2.0 as of 2026-09-03): the Helm project's own `hello-world` ([helm/examples](https://github.com/helm/examples)) as the simple chart; `prometheus` ([prometheus-community/helm-charts](https://github.com/prometheus-community/helm-charts)) with its four conditional dependencies as the dependency showcase; and Bitnami's `nginx` ([bitnami/charts](https://github.com/bitnami/charts)) with its OCI-repository dependency and substantial values surface. Substitution at acquisition time is allowed under the same verification.
3. WHEN an example is vendored THEN its folder SHALL carry the upstream `LICENSE` text (and `NOTICE`, where the upstream provides one) and a readme stating: the source repository URL, the chart name and version taken, the retrieval date, the verified license, and an exact list of anything added or changed locally. Unmodified upstream content SHALL stay unmodified.
4. WHEN the values layering (R5.3) needs demonstrating THEN one example SHALL gain example-authored override files (such as `values-dev.yaml` and `values-prod.yaml`), listed in that example's readme as ADP-authored additions — layering is demonstrated by honest additions, not by misattributing files to the upstream.
5. WHEN the Resolved state (R5.2) needs demonstrating THEN one example SHALL carry a dependency vendored under `charts/`, obtained from a source verified under the same rule as R11.2, with its own attribution in the readme.
6. WHEN an example is opened THEN it SHALL be complete — the `.adp` inside the chart root so a double-click opens the diagram with no setup — and validation over every shipped example SHALL be clean, making the examples the type's first fixtures; any extension whose bytes must survive checkout SHALL gain its line in `.gitattributes` per the repository's established rule.

### Requirement 12 — Pluggable registration

#### Acceptance Criteria

1. WHEN the module is added THEN it SHALL register through the standard `ServiceCollection.AddHelmCharts()`-style single registration file and be discovered like every sibling module, with no core edits.
2. WHEN the module is absent THEN nothing in core SHALL reference it — the dependency direction of structure.md holds.
3. WHEN the definition ships THEN `docs/diagrams.md` SHALL carry the `helm/chart` row at its correct state, per the catalog rule in CLAUDE.md.

## Non-Functional Requirements

### Code Architecture and Modularity

- One module at `src/diagrams/helm-charts/` (`backend/` with `EtAlii.Adp.Diagram.HelmCharts` + `.Tests`, `client/`, `examples/`; `api/` for its wire messages), depending on core abstractions only.
- The reader, graph model, layout and mapping SHALL be testable without a running server or browser, following the Ansible module's separation.
- Line-scanning of templates SHALL live in one place with fixture-pinned behaviour, so the "what counts as literal" decisions are testable facts rather than folklore.

### Performance

- Reading a chart the size of the prometheus example (dozens of templates, four dependencies) SHALL be interactive — line scanning, never template evaluation, keeps cost linear in file size.
- Disk-change bursts (a `helm dependency build` rewriting `charts/` wholesale) SHALL coalesce into one re-read, per the watched-folder pattern.

### Security

- No template execution, no hook execution, no network access: a chart is inert data to this module.
- Vendored example content SHALL be reviewed at acquisition for secrets or credentials before it is committed; a chart carrying any SHALL NOT be vendored.

### Reliability

- A broken file never blanks the diagram (R3.1); a deleted chart folder degrades to the not-a-chart state (R10.1) rather than erroring.

### Usability

- The three example charts open from the central showcase with zero setup, and each readme says what the example demonstrates.

## Sources

* [Helm: Charts guide](https://helm.sh/docs/topics/charts/) — chart structure, `Chart.yaml` fields, dependencies, library charts
* [Helm: Chart template guide](https://helm.sh/docs/chart_template_guide/) — templates, named templates and `include`
* [Helm: Chart dependencies](https://helm.sh/docs/helm/helm_dependency/) — `charts/`, `Chart.lock`, vendoring
* [unrealandychan/Helm-Visualizer](https://github.com/unrealandychan/Helm-Visualizer) — the reference visualization, Apache-2.0; read for insight, not copied
* Example sources, licenses verified 2026-09-03: [helm/examples](https://github.com/helm/examples) (Apache-2.0), [prometheus-community/helm-charts](https://github.com/prometheus-community/helm-charts) (Apache-2.0), [bitnami/charts](https://github.com/bitnami/charts) (Apache-2.0)
