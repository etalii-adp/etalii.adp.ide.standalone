# Databricks bundle: what DISL 0.3 does not say

This is the companion to [databricks-bundle.dis](databricks-bundle.dis). The definition states the bundle diagram as the standalone tool implements it today, in `src/diagrams/databricks/` of [etalii.adp.ide.standalone](https://github.com/etalii-adp/etalii.adp.ide.standalone): its metamodel, notation, toolbox, menus, rows, rules, operations, simulated deploy, layout choice and persistence, and, in its `x-databricks` block, the wire ids the tool uses for each of them. That module serves three tools from one engine: this one, [databricks-job](databricks-job.dis) and [databricks-pipeline](databricks-pipeline.dis).

This page lists only what DISL 0.3 cannot express about this tool. Each item is a finding of the standalone specification [databricks-disl-fbl](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/.spec-workflow/specs/databricks-disl-fbl), whose `requirements.md` gives its evidence; every sentence, code and severity quoted here is taken from that specification's transcript of the tool.

## A8: the order of findings, and whose findings they are

**What the tool does.** The tool judges the whole file, not the reading that asked. Opened as a bundle, a `databricks.yml` reports the findings of every job it declares, then the bundle's own, then those of every pipeline it declares; opened as a job or a pipeline, the same file reports the bundle's `databricks.no-default-target`, `databricks.multiple-default-targets` and `databricks.override-of-undeclared` too. The bundle's own findings come in that order: the missing default target, then each second default target, then each override of an undeclared resource, target by target.

**Why DISL 0.3 cannot say it.** A definition evaluates its constraints over its own reading: the bundle definition does not see a job's tasks or a pipeline's libraries, and the job and pipeline definitions do not see the bundle's targets. DISL 8.6 orders the findings of one definition; it has no way to join those of three.

**What the definition does instead.** It declares each of the bundle's rules with its code, severity, sentence and location, in the tool's order, and leaves the other readings' findings and their place to the host, which joins the three definitions' findings in the tool's order.

## A10: the bundle node exists without an entry

**What the tool does.** The bundle reading always draws one node with the id `bundle`, labelled with the bundle's name, or `bundle` when it has none. It is drawn when the file has no `bundle:` key, when the file is empty, and when the file cannot be read at all. It can be moved, and its position is stored in the registration under that fixed id. Renaming it in a file without a `bundle:` section is refused: "The file has no bundle: section to name."

**Why DISL 0.3 cannot say it.** DISL creates an element from what the reader reads, or derives one from other elements (4.11). Whether a derived element may exist while the entry it would be derived from does not, and keep a stored position, is not stated, and nothing else makes an element that no entry of the file stands for.

**What the definition does instead.** `Bundle` has a multiplicity of exactly one, an optional `name` and the fixed id `bundle`; the node's presence without an entry is the host's.
