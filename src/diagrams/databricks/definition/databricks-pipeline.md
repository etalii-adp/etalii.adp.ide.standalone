# Databricks pipeline: what DISL 0.3 does not say

This is the companion to [databricks-pipeline.dis](databricks-pipeline.dis). The definition states the pipeline diagram as the standalone tool implements it today, in `src/diagrams/databricks/` of [etalii.adp.ide.standalone](https://github.com/etalii-adp/etalii.adp.ide.standalone): its metamodel, notation, toolbox, menus, rows, rules, operations, simulated update, layout choice and persistence, and, in its `x-databricks` block, the wire ids the tool uses for each of them. That module serves three tools from one engine: this one, [databricks-job](databricks-job.dis) and [databricks-bundle](databricks-bundle.dis).

This page lists only what DISL 0.3 cannot express about this tool. Each item is a finding of the standalone specification [databricks-disl-fbl](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/.spec-workflow/specs/databricks-disl-fbl), whose `requirements.md` gives its evidence; every sentence, code and severity quoted here is taken from that specification's transcript of the tool.

## A8: the order of findings, and whose findings they are

**What the tool does.** The tool judges the whole file, not the reading that asked. Opened as a pipeline, a bundle's YAML reports the findings of every job it declares, then the bundle's, then those of every pipeline it declares, whatever pipeline the `.adp` registration's `resource:` names. For each pipeline it reports `databricks.no-libraries`, then `databricks.schema-without-catalog`.

**Why DISL 0.3 cannot say it.** A definition evaluates its constraints over its own reading: the pipeline definition sees one pipeline, and it does not see a job's tasks or the bundle's targets. DISL 8.6 orders the findings of one definition; it has no way to join those of three.

**What the definition does instead.** It declares each of the pipeline's rules with its code, severity, sentence and location, in the tool's order, and leaves the other readings' findings and their place to the host, which joins the three definitions' findings in the tool's order.

## A10: target and compute exist without an entry

**What the tool does.** The pipeline reading always draws a node `target`, labelled with as much of `catalog.schema` as the settings name, and a node `compute`, labelled `serverless` or `cluster` as `serverless` says. Neither has an entry of its own in the settings; the one `notifications` node, drawn only when the pipeline has notifications, stands for the whole list. Each can be moved, and its position is stored in the registration under its fixed id.

**Why DISL 0.3 cannot say it.** DISL creates an element from what the reader reads, or derives one from other elements (4.11). Whether a derived element may exist while the entry it would be derived from does not, and keep a stored position, is not stated, and nothing else makes an element that no entry of the file stands for.

**What the definition does instead.** `Destination` and `Compute` have a multiplicity of exactly one and `Notifications` of at most one, each with its fixed id; that the first two are there without an entry is the host's.
