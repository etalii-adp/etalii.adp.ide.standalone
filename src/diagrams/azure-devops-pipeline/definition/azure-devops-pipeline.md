# Azure DevOps pipeline

The DISL specification of this diagram type is [azure-devops-pipeline.dis](azure-devops-pipeline.dis), written at DISL 0.3. This page holds what DISL 0.3 and FBL 0.2 cannot express about the tool, each gap with the construct it concerns, the document that shows it and what would resolve it. Everything either language can say is said in the `.dis` and is not repeated here.

The tool is the Azure DevOps pipeline diagram of the standalone IDE (origin `azure-devops/pipeline`). Every sentence, label, code and id in the `.dis` is the standalone tool's, as its parity transcript records them: `Parity/azure-pipeline.transcript.json` under [`src/diagrams/azure-devops-pipeline/backend/EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/src/diagrams/azure-devops-pipeline/backend/EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests) in etalii.adp.ide.standalone. The documents named below are that test project's fixtures; `findings/` is its `Fixtures/findings/` folder. The conversion is specified in that repository's `.spec-workflow/specs/azure-devops-pipeline-disl-fbl/`, whose findings this page cites by number.

## What the diagram is for

A CI/CD pipeline's stages, jobs and steps, and which of them wait for which. Pipeline YAML nests stages, jobs and steps inside templates; opening one level at a time keeps a long pipeline readable. The diagram reads a pipeline, it does not run one, and the file stays Azure DevOps' own: only `displayName`, `dependsOn` and `enabled` are ever written, besides whole stages, jobs and steps added, removed and moved.

## What the reader reads

The reader is the standalone module's, through the FBL binding it ships; while gap 1 stands, the binding's reader is a persistence plugin. What it reads, for every document of the corpus:

- **Stages, jobs and steps**, a deployment job's steps gathered per lifecycle hook: `preDeploy`, `deploy`, `routeTraffic` and `postRouteTraffic`. **The steps of `on.failure` and `on.success` are not read**: a hook is read only where its own mapping holds `steps`, and those two hold `failure` and `success` (finding A7, `findings/deployment-on-hooks.yml`).
- **The implicit stage and job** of a pipeline without `stages`, and of one with only `steps` (gap 3).
- **Every value as the text it was written with**, expressions included, none ever evaluated (gap 2).
- **`${{ if }}` and `${{ each }}` around a stage or a job**, at any depth, with the expressions kept as the element's gate, joined with " and " (gap 4). **A step under `${{ if }}` is not unwrapped**: it is read as one step of unknown kind (finding B4, `findings/gates-nested.yml`).
- **Merge keys** expanded, the mapping's own keys winning.
- **Templates** followed inside the workspace only, never over the network, with one of six reasons for one that is not; a template's parameters by name and never by value (gap 1).
- **`dependsOn`** in each of its forms and its absence (gaps 5 and 6), each name resolved without regard to case, in its scope (gap 7).
- **Ids** are paths, never written, as `persistence.ids` states. Two stages of one name share an id and the first is the one a gesture finds (finding A8); `std.duplicateId` is switched off until reporting it is ruled on its own.

## The gaps

Ten, each a candidate change for this repository. None is worked around in the `.dis` by stating something the tool does not do.

### 1. FBL: a reader that is declared for the file and a plugin for part of it (B2)

- **Construct.** `Binding.reader` is `"declared"` or one `PluginReader`. Nothing states a plugin for a part of the reading, though FBL 11.5 and 17 name this tool as "a declared binding, with a plugin for part of its reading". The whole-plugin form does not reach templates either: a plugin's `read` receives the body's bytes, and the standalone runtime's read request tells it neither where the body is nor where the workspace ends, so it cannot open `templates/build-jobs.yml`. `watch` is for folder subjects, and no host calls it.
- **Shown by.** `templates.yml`, `extends.yml`, `template-in-template.yml` with `templates/nested-jobs.yml`.
- **Would resolve it.** A plugin that expands named rules of a declared binding (the draft's `expand`), and for any plugin: the body's path, the workspace's edge, the files it read, and the watching of them.
- **Meanwhile.** The binding names a persistence plugin as its reader, standing in for the gap, and the draft declared binding ([`../../specifications/fbl/azure-devops-pipeline.fbl.draft`](../../specifications/fbl/azure-devops-pipeline.fbl.draft)) measures what is missing.

### 2. FBL: a slot that reads a YAML scalar as written (B1)

- **Construct.** A plain scalar is typed by the YAML 1.2 core schema, so a stage named `1.0`, a display name `yes` or a job named `null` would reach the model as a number, a boolean or nothing. Already reported by the timeline's conversion; this tool adds that the scalar may be an expression.
- **Shown by.** `findings/scalars-as-text.yml`.
- **Would resolve it.** A slot that reads the text as written (the draft's `"as": "text"`).

### 3. FBL: one entry, several elements, and a parent that is not an entry (B3)

- **Construct.** "An entry becomes at most one element or relation" (FBL 5.1), and `parent` needs the nearest enclosing entry a rule matched. A file with only `jobs` has a stage nobody wrote; one with only `steps` has that stage and a job: the root mapping is the diagram, a stage and a job at once.
- **Shown by.** `jobs-only.yml`, `steps-only.yml`.
- **Would resolve it.** Further elements an entry stands for, each with its condition (the draft's `also`).

### 4. FBL: selecting through a key that contains braces, at any depth, with the keys on the way (B4)

- **Construct.** A key containing `{` or `}` cannot be selected (FBL 4.2); a `{gate}` capture with a `when` reaches one level; `**` reaches every level and binds nothing, so the joined gate cannot be read.
- **Shown by.** `findings/gates-nested.yml`, `edge-expressions.yml`.
- **Would resolve it.** A selector that passes through keys matching a pattern and binds what it passed (the draft's `through`).

### 5. FBL: a value that is one scalar or a sequence of scalars (B5)

- **Construct.** A relation rule at `dependsOn/*` finds list items only, and nothing in `dependsOn: Build`; no slot means "the entry's own value". Read as a list attribute, three things are unstated: that a scalar reads as a list of one, how a list is written other than `flow` (the tool writes one name as a scalar, several as a block list and none as `[]`), and removing the key as distinct from writing `[]`.
- **Shown by.** `findings/depends-on-forms.yml`.
- **Would resolve it.** A list read from a scalar or a sequence, with its written forms (the draft's `"list": "scalar-or-sequence"`).

### 6. FBL: unsetting a slot as distinct from emptying it (B5)

- **Construct.** "(nothing)" writes `dependsOn: []` and "(the default order)" removes the key; they are different pipelines. `empty` chooses one of them for a binding, and FBL 11.2 has no model change that unsets.
- **Shown by.** `findings/depends-on-forms.yml` (`Lint` waits for nothing, `Build` follows the default order).
- **Would resolve it.** An unset model change and a slot's `unset` (the draft's `"unset": "remove"`).

### 7. FBL: a reference by a name compared without case and resolved in a scope (B6)

- **Construct.** `dependsOn` is matched without regard to case, the first element of that name wins, and a job's names are those of its own stage. `reference: {to, by}` is exact and file-wide.
- **Shown by.** `findings/depends-on-case.yml`.
- **Would resolve it.** `reference.compare` and `reference.scope` (the draft's `"compare": "ignore-case"` and `"scope": "parent"`).

### 8. FBL: the order of an entry's keys in CEL (B16)

- **Construct.** A step's kind is its first recognised key in written order; an entry reaches CEL as a map, which has no order.
- **Shown by.** `findings/step-two-kinds.yml`.
- **Would resolve it.** The entry's keys in written order, available to a slot or a `when`.

### 9. DISL: a relation with an unset end that is drawn (A6)

- **Construct.** Under `typeMap`, a relation read with an end that names nothing is kept with that end unset, "not drawn" (DISL 11.2, 6.1). The tool draws it as a broken edge, `edge:?<name>-><to>`, in the danger colour with a `2 4` dash, and marks the stage that waits indeterminate.
- **Shown by.** `findings/dangling-dependency.yml`, `edge-broken-graph.yml`.
- **Would resolve it.** A notation for a relation with an unset end: drawn from a stub, with the name it was written with.
- **Meanwhile.** The module draws it, as code named as standing in for this gap (Q4).

### 10. DISL: the standard `layered` algorithm (A10)

- **Construct.** DISL 0.3 defines `rowPacked`, `rows` and `tidyTree`, and still only names `layered` (D.2, open question 9). The `.dis` states the tool's rules as `adp.*` options.
- **Shown by.** Every document of the corpus; the payloads of the transcript hold every position.
- **The rules, in numbers.** A stage's column is the length of the longest chain of dependencies before it, ignoring dependencies that name nothing and the edge that closes a cycle. A column is as wide as its widest element, columns 80 apart, each filled top-down in written order 32 apart. A closed stage is 220 × 88, a closed job and a step 180 × 56. An open stage is its jobs' width + 2 × 24 wide and their height + 40 + 2 × 24 tall, never smaller than closed, its jobs offset by 24 and 40 + 24. An open job is 228 wide and 40 + 2 × 24 + n × 56 + (n − 1) × 32 tall. An unfollowed template goes below everything else, at the x of the element whose list names it. Positions are deterministic and never written.
- **Would resolve it.** A definition of `layered`.

## Found while writing the definition

- **The order of the job findings.** The tool judges each stage's jobs in turn, a dangling dependency and then a cycle for one stage before the next stage's. The `.dis` declares one rule for each, so every job's dangling dependency comes before every job's cycle; `constraints.order` orders by code and then by line, not by stage. No document of the corpus has a dangling job dependency in one stage and a job cycle in another, so the transcript does not show the difference yet.

## Outside the definition, and not a gap

- **The client's payloads**, `PipelineElementPayload` in `api/azure-pipeline.proto`, and the deltas a session sends when a stage opens or the file changes on disk are the host's (finding A11).
- **Undo** is the host's history; a change made to the file outside the editor reaches every open view and is not put on it.
- **Security**: a template outside the workspace is never read, a repository resource never fetched, and a parameter's value never held in the model. These are today's rules and FBL 16's.

## Sources

- Code: [`src/diagrams/azure-devops-pipeline/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/src/diagrams/azure-devops-pipeline) in etalii.adp.ide.standalone, and its parity transcript.
- The conversion's specification, `azure-devops-pipeline-disl-fbl`, in that repository's `.spec-workflow/specs/`: its findings A6 to A11 and B1 to B17, and section C, the ten gaps.
- Microsoft: [YAML schema reference](https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/), [key concepts](https://learn.microsoft.com/en-us/azure/devops/pipelines/get-started/key-pipelines-concepts), [stages, dependencies and conditions](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/stages).
