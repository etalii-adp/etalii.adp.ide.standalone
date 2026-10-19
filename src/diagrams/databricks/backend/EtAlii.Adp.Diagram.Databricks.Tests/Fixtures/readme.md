# The databricks round-trip corpus

Documents whose bytes are the test subject: the parsers read them, the round-trip tests prove an
untouched document saves byte-identically, and the writers' smallest-diff tests splice them.

- `bundle.yml` - a `databricks.yml`: bundle name, includes, variables, two modelled resource
  kinds plus an unmodelled one (`experiments`), an unmodelled root key (`sync`), and two targets
  of which `prod` overrides a job.
- `job.yml` - a job resource file: five tasks across four types, a condition task with outcome
  edges, a non-default `run_if`, a job cluster and a schedule.
- `pipeline.json` - pipeline settings JSON, read through the same YAML door (JSON is a subset of
  YAML 1.2): catalog and schema, three library kinds, notifications, and an unmodelled key
  (`edition`).
- `crlf-line-endings.yml` / `lf-line-endings.yml` - the same small bundle under each convention;
  they exist to prove the writer preserves whichever the file has.
- `tied-line-endings.yml` - as many LF endings as CRLF, so neither is the majority and the tie
  rule alone decides which ending a newly inserted line takes: CRLF, core's rule
  (backend-centralization Requirement 1.2).
- `no-trailing-newline.yml` - the same bundle without a final newline, which an append must not
  silently add.
- `broken.yml` - not YAML; carried as an error, opened as unavailable, refused on save.

These files are `-text` in the repository root's `.gitattributes` - by a glob naming this
folder's `.yml`/`.yaml`/`.json` specifically, because the extensions are shared repository-wide
and cannot take the by-extension rule the other modules use. This readme is deliberately outside
that glob: it is ordinary text, and the house line-ending style should simply apply.

## The parity corpus (databricks-disl-fbl task 1)

The files below were added for the parity transcript, `../Parity/databricks.transcript.json`, which freezes what today's module does with every file of this folder and every example before any of it moves to DISL definitions and an FBL binding. Each is written for one or more of the Read findings of the databricks-disl-fbl requirements; the transcript records what the module does with it, and `src/backend/EtAlii.Adp.Specification.Fbl.Tests/RealFiles/divergences.json` records where the vendored bindings read it differently. They are byte-compared like the rest of the folder, so the same `-text` glob covers them.

| Fixture | Finding | What it holds |
| --- | --- | --- |
| `two-jobs-two-pipelines.yml` | A7, A13, B2, B3 | Two jobs that share a task key, two pipelines, each with a finding of its own, and a bundle key: which job and pipeline a reading, a property row and an edit reach, and that the findings are the file's whatever the reading. |
| `cycle-downstream.yml` | A4 | A circle and tasks downstream of it: the cycle rule reports both. |
| `duplicate-key-on-a-circle.yml` | A2, A4 | A key written twice on a circle: the duplicate is reported, the circle is not. |
| `finding-order.yml` | A8 | Missing keys, missing upstreams, a dangling cluster and a duplicate interleaved, so the order of the findings shows. |
| `missing-upstream.yml` | A9 | Two tasks naming one missing upstream and one naming another: one stub per key, drawn and movable. |
| `no-bundle-key.yml` | A10 | No `bundle:` key, and a pipeline with neither catalog, schema nor notifications. |
| `empty.yml` | A10 | Zero bytes. |
| `fresh-key.yml` | A11 | Task keys that already take the pattern `<type>_<n>` with a gap. |
| `typed-scalars.yml` | B4 | Keys and values that a typed reading changes: `01`, `2024`, `null`, `123`, `true`, `True`, `yes`. |
| `variables-in-typed-places.yml` | B4 | `${var...}` in places the module reads as a number or a flag. |
| `anchors-and-merge.yml` | B5 | A merge key in a cluster and a task list shared by an alias. |
| `key-written-twice.yml` | B6 | A key written twice in one mapping. |
| `unreadable.json` | B7 | A settings file that is not JSON. |
| `comments-and-quotes.yml` | B8 | Trailing comments and quoted values on every line an edit rewrites. |
| `raw-values.yml` | B9 | A job whose edits write values that need quoting. |
| `for-each-before-key.yml` | B10 | A `for_each_task` written before its task's own `task_key`, and a task whose first key is a nested mapping. |
| `new-key-placement.yml` | B11 | A task without and a task with `depends_on`, a cluster, and no `pipelines:`, for where a new key and a new resource kind go. |
| `flow-collections.yml` | B12 | `tasks: []`, a flow mapping, a flow `depends_on` behind another job, and a one-line `libraries` array. |
| `flow-dependencies.yml` | B12 | A flow `depends_on` on the first task of the only job, so the transcript's disconnect and connect reach it. |
| `settings-one-line.json` | B12, Requirement 1.2 | A pipeline settings file written on one line. |
| `leading-comments.yml` | B13 | Comment lines above tasks, which a removal leaves or takes. |
| `every-task-type.yml` | A2, B14 | Every task type the module knows, an `existing_cluster_id` task, a task without `task_key` and a dependency on an undeclared task. |
| `library-ids.json` | B15 | Two libraries of one path and different kinds, a glob, a path with `$`, `{` and `}`, and two libraries of kind `other`. |
| `no-resources.yml` | B17 | A bundle without `resources:`, so adding a resource creates two containers at once. |
| `legacy-target.yml`, `legacy-target.json` | B18 | A pipeline whose schema is written as the legacy `target`, in a bundle and in a settings file. |
| `bundle.json` | B22 | A bundle written as JSON, read under the YAML door and edited by the YAML writers. |

Findings B1, B16, B20 and B21 need no fixture of their own: B1 and B16 show on the examples and on every task of the corpus (two readings of the lakehouse body; a move, a rename and two undos), and B20 and B21 are the transcript's `newDocuments` and `resourceHeaders` sections. A1, A3, A5, A6, A12, A14, A15 and B19 are about the definitions, the runtime libraries, the tooling or the sentences of a library that does not exist yet, and no file of today's module can show them; A14 no longer occurs, as `bundle-disl.sh` now takes several named definitions.

What the corpus does not demonstrate:

- A bundle spread over several files through `include:`: every document is read alone, as the module reads it, so nothing here shows a job declared in one file and referenced from another.
- Target overrides merged into resources, variables resolved per target, or any value Databricks' own tooling computes: the module draws what is written, and so does the transcript.
- A multi-document YAML stream, a YAML tag, a tab, a byte order mark or a non-UTF-8 encoding.
- A pipeline settings file written in YAML rather than JSON, and a settings file holding a job.
- The tasks inside a `for_each_task` as tasks of their own: the module draws only the outer task, and no fixture asks otherwise.
- A body that an edit leaves unreadable: since databricks-disl-fbl task 2 every value is written so that it reads back as the same text (finding B9), so the transcript's `*nothing` and `[x` are written quoted, where before the first left the file unreadable and the second threw.
- Real published Databricks bundles: every file here is written for this repository, as the examples are.
