# Databricks job: what DISL 0.3 does not say

This is the companion to [databricks-job.dis](databricks-job.dis). The definition states the job diagram as the standalone tool implements it today, in `src/diagrams/databricks/` of [etalii.adp.ide.standalone](https://github.com/etalii-adp/etalii.adp.ide.standalone): its metamodel, notation, toolbox, menus, rows, rules, operations, simulated run, layout choice and persistence, and, in its `x-databricks` block, the wire ids the tool uses for each of them. That module serves three tools from one engine: this one, [databricks-bundle](databricks-bundle.dis) and [databricks-pipeline](databricks-pipeline.dis).

This page lists only what DISL 0.3 cannot express about this tool. Each item is a finding of the standalone specification [databricks-disl-fbl](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/.spec-workflow/specs/databricks-disl-fbl), whose `requirements.md` gives its evidence; every sentence, code and severity quoted here is taken from that specification's transcript of the tool.

## A8: the order of findings, and whose findings they are

**What the tool does.** The tool judges the whole file, not the reading that asked. Whichever of the three tool types opened the file, and whatever job its `.adp` registration's `resource:` names, it reports the findings of every job of the file, then the bundle's, then every pipeline's. Within a job it goes task by task in file order and reports, for each task, `databricks.task-key-missing`, then one `databricks.missing-task` per `depends_on` entry naming a key no task has, then `databricks.dangling-cluster`. Only after the last task does it report every `databricks.duplicate-task-key`, and then every `databricks.cycle`.

**Why DISL 0.3 cannot say it.** A definition evaluates its constraints over its own reading: the job definition sees one job, and it does not see the bundle's targets or a pipeline's libraries at all. DISL 8.6 orders findings by constraint and then by element, so it cannot interleave three constraints per task. `constraints.order` orders by code and then by the line of the element, which does not give the tool's order either: the key and cluster findings stand on the task's line and a missing upstream on its `depends_on` entry's later line, so the cluster finding would come before the missing upstream.

**What the definition does instead.** It declares each rule with its code, severity, sentence and location, and leaves the order and the other readings' findings to the host, which joins the three definitions' findings in the tool's order.

## A9: a missing upstream task is a drawn node

**What the tool does.** A `depends_on` entry naming a key no task of the job has draws a node `task:<key>` with the badge `serverless`, one node per key however many entries name it, with an edge from it to each waiting task. The node can be moved, and its position is stored in the registration like any task's. It has no menu and no rows. A simulated run treats it as a task that never finishes, so the task waiting on it stays pending.

**Why DISL 0.3 cannot say it.** DISL keeps such an entry as a relation without a source and reports it through `std.references`; it is not drawn. A stub (6.10) is drawn for a relation without a *target*, and as a line, not a node. The written key reaches the finding as `detail.missingId` and the relation's own attribute, and no derived node type can be built from a relation's missing end.

**What the definition does instead.** The `Dependency` relation keeps the key as written in `upstreamKey`, so the finding's sentence, the "Disconnect from" entry and the edge's id `edge:<from>-><to>` are all stated, and the simulated run's `upstreamSettled` holds the waiting task back. The stub node itself is the host's.

## A11: a fresh key

**What the tool does.** A task added from the canvas menu or by a drop is named `<type>_<n>`, with the first `n`, counting from 1, that no task of the job uses: in a job whose only notebook task is `notebook_2`, the next notebook task is `notebook_1`.

**Why DISL 0.3 cannot say it.** CEL has no range to search, so no expression finds the first free number; counting the tasks of a type and adding one collides after a removal. `persistence.ids.suffix` appends to an id that is already taken and does not make the first one `_1`.

**What the definition does instead.** The add operations take the key as a parameter, and the canvas menu and the toolbox drop pass `freeTaskKey(keys, type)`, a function of the plugin `net.etalii.adp.databricks.taskKeys` that the host implements.
