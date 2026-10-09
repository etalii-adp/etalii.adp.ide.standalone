# The FBL conformance corpus

Everything in this folder except this readme is copied, unchanged, from [`etalii-adp/etalii.adp`](https://github.com/etalii-adp/etalii.adp), folder `specifications/fbl/`, at commit `4590c56` (`develop`, 2026-10-01, the merge of etalii.adp #43 that introduced FBL 0.1):

- the eight example bindings, `*.fbl`;
- `fixtures/`, the round-trip fixtures FBL §15.3 makes the shared test of every host;
- `registrations/`, the example `.adp` registrations;
- `LICENSE.md`, that repository's `LICENSE`, verbatim.

**These files are never edited here** (fbl-implementation Requirement 10.2). A difference from the source is fixed by copying them again at a newer commit and changing the commit above. A binding that does not fit a real file of this repository is recorded in `RealFiles/divergences.json`, not corrected here.

`.gitattributes` keeps the fixture inputs and registrations (`*.yml`, `*.json`, `*.cld`, `*.adp`, and `*.tml`, `*.mm`, `*.dsl` through the repository-wide rules) from line-ending conversion, because the fixtures' offsets are byte offsets into them.

**Added since, from the same folder and from `definitions/diagrams/` at commit `2d5c11d`** (`develop`, 2026-10-09, the merge of etalii.adp #109): the binding `agent-activity-diagram.fbl` and the three fixtures `fixtures/agent-activity-read`, `-edits` and `-new`, written against FBL 0.4, whose nested levels (sections 5.2 and 6.2) this library creates and removes in yaml only: a json or xml binding that asks for them is refused or keeps the level, and `Yaml/YamlLevels.Tests.cs` holds the cases the fixtures do not reach. The rest of the corpus is still the FBL 0.1 copy named above; the bindings and fixtures FBL 0.2 and 0.3 added for other tool types are not vendored here yet.

Three things the runner does for those fixtures, each because FBL leaves it to the host or to the tool type's specification and none of them is an edit to a fixture:

- **The binding is found beside the others.** A tool type's fixture names its binding under `definitions/`, a folder this corpus does not have.
- **A refusal's text is compared only where the binding gives it.** FBL words a refusal only there; elsewhere the reason is the host's own (etalii.adp #107 says so in section 15.3).
- **A removal also clears the keys that named the removed element**, which is the default deletion policy of the tool type's specification, not FBL's; and the attributes that specification types as a date-time are named to the library, which FBL 6.3 needs to write them plain.

Splices are compared in body order, those at one offset as listed: FBL orders only splices at the same offset, and the 0.3 fixtures list a cascaded removal bottom-up where this library lists every edit in body order.

## What this corpus does not demonstrate

- **No persistence plugin runs.** `helm-chart.fbl` and `w3c-turtle.fbl` are plugin-read bindings; only what the host declares for them (routing, recognition, the registration, templates) is exercised.
- **No fixture has a folder subject**, and none exercises several readings of one body, `id.sidecar`, a legacy sidecar or `undo: "snapshot"` byte for byte.
- **The fixtures name DISL-derived ids** (`task:ingest`, `link:population|births`), which FBL leaves to DISL. The tests supply those derivations from the tool types' specifications in `etalii.adp/definitions/diagrams/`; the library itself does not compute them.
