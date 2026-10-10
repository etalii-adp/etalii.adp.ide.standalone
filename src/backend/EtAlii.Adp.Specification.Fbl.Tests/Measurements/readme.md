# Measurements

Tests that take a measurement rather than guard a behaviour. Each asserts only that the work it times really happened; the times go to the test output and are recorded in the task that asked for them, because a bound that holds on one machine is not a bound on another.

## `LargeTable.Tests.cs`

A table of ten thousand rows and eight properties, one entry per cell, opened and edited through `OpenBody` (knowledge-designer task 2, Requirement 9.6). It generates its file in memory and commits none.

It reads the Knowledge designer's YAML binding from the vendored conformance tree, `Conformance/etalii.adp/definitions/designers/knowledge.fbl`. The first measurement, recorded in task 2's implementation log, was taken with a draft of that binding from before the definition was merged.
