# Measurements

Tests that take a measurement rather than guard a behaviour. Each asserts only that the work it times really happened; the times go to the test output and are recorded in the task that asked for them, because a bound that holds on one machine is not a bound on another.

## `LargeTable.Tests.cs`

A table of ten thousand rows and eight properties, one entry per cell, opened and edited through `OpenBody` (knowledge-designer task 2, Requirement 9.6). It generates its file in memory and commits none.

## `knowledge-draft.fbl`

The **draft** YAML binding of the Knowledge designer's file, copied unchanged from `etalii-adp/etalii.adp`, branch `claude/project-thread-rmr2na`, commit `bb2b103` (`definitions/designers/knowledge.fbl`). It is here only so the measurement has a binding before the definition is merged. Knowledge-designer task 8 vendors the merged bindings and their fixtures into `Conformance/`; this copy is removed, and the measurement pointed at the vendored one, in that change.

It is written in FBL 0.2, which this library reads with a warning.
