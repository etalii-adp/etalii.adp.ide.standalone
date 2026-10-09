# The FBL conformance corpus

Everything in this folder except this readme is copied, unchanged, from [`etalii-adp/etalii.adp`](https://github.com/etalii-adp/etalii.adp), folder `specifications/fbl/`, at commit `4590c56` (`develop`, 2026-10-01, the merge of etalii.adp #43 that introduced FBL 0.1):

- the eight example bindings, `*.fbl`;
- `fixtures/`, the round-trip fixtures FBL §15.3 makes the shared test of every host;
- `registrations/`, the example `.adp` registrations;
- `LICENSE.md`, that repository's `LICENSE`, verbatim.

**These files are never edited here** (fbl-implementation Requirement 10.2). A difference from the source is fixed by copying them again at a newer commit and changing the commit above. A binding that does not fit a real file of this repository is recorded in `RealFiles/divergences.json`, not corrected here.

`.gitattributes` keeps the fixture inputs and registrations (`*.yml`, `*.json`, `*.cld`, `*.adp`, and `*.tml`, `*.mm`, `*.dsl` through the repository-wide rules) from line-ending conversion, because the fixtures' offsets are byte offsets into them.

## `etalii.adp/`: the Knowledge designer's files

The folder `etalii.adp/` holds a second vendored set, copied unchanged from the same repository at commit `2d5c11d` (`develop`, 2026-10-09), **in that repository's own layout**: `definitions/designers/knowledge.fbl` (the bindings `yaml`, `json` and `xml`), `definitions/designers/knowledge.des` (the specification, read here for its attribute types only), `definitions/designers/examples/`, and `specifications/fbl/fixtures/knowledge-*`. The layout is kept because those fixtures name their binding and their inputs by paths relative to that tree, and the files are never edited here.

`.gitattributes` keeps the whole of `etalii.adp/` from line-ending conversion by its path. A rule by extension would exempt this repository's ordinary `.yaml`, `.json` and `.xml` files too.

`KnowledgeFixtures.Tests.cs` runs each fixture's `read`, an unchanged save, the byte-coverage invariant, and that the one example reads as the same model in all three formats. `KnowledgeFixtures.Steps.Tests.cs` runs their edit steps: all 132 of each of the three large fixtures, and the 11 of `knowledge-kept`, go through. The reading is told the attribute types of `knowledge.des`, which a binding does not carry: that is what makes the cell of `knowledge-kept` whose number is not a number an unreadable entry. One thing is not this library's doing, and the test names it:

- **One step of each large fixture is taken from the fixture, not planned.** Duplicating a view is written there as an add carrying `x-copyOf`, expecting one new entry with the copy's settings in the original's order. Neither FBL nor `knowledge.des` says how that entry is planned, and the `duplicateView` operation, a transaction of creates, would write the settings in another order under this binding. The step applies the fixture's own splices so the steps after it can run.

A fixture's refusal sentence is not compared either, where it is the message of the designer's own rule and not the binding's.

## What this corpus does not demonstrate

- **No persistence plugin runs.** `helm-chart.fbl` and `w3c-turtle.fbl` are plugin-read bindings; only what the host declares for them (routing, recognition, the registration, templates) is exercised.
- **No fixture has a folder subject**, and none exercises several readings of one body, `id.sidecar`, a legacy sidecar or `undo: "snapshot"` byte for byte.
- **The fixtures name DISL-derived ids** (`task:ingest`, `link:population|births`), which FBL leaves to DISL. The tests supply those derivations from the tool types' specifications in `etalii.adp/definitions/diagrams/`; the library itself does not compute them.
