# The FBL conformance corpus

Everything in this folder except this readme is copied, unchanged, from [`etalii-adp/etalii.adp`](https://github.com/etalii-adp/etalii.adp), folder `specifications/fbl/`, at commit `4590c56` (`develop`, 2026-10-01, the merge of etalii.adp #43 that introduced FBL 0.1):

- the eight example bindings, `*.fbl`;
- `fixtures/`, the round-trip fixtures FBL §15.3 makes the shared test of every host;
- `registrations/`, the example `.adp` registrations;
- `LICENSE.md`, that repository's `LICENSE`, verbatim.

**These files are never edited here** (fbl-implementation Requirement 10.2). A difference from the source is fixed by copying them again at a newer commit and changing the commit above. A binding that does not fit a real file of this repository is recorded in `RealFiles/divergences.json`, not corrected here.

`.gitattributes` keeps the fixture inputs and registrations (`*.yml`, `*.json`, `*.cld`, `*.adp`, and `*.tml`, `*.mm`, `*.dsl` through the repository-wide rules) from line-ending conversion, because the fixtures' offsets are byte offsets into them.

## What this corpus does not demonstrate

- **No persistence plugin runs.** `helm-chart.fbl` and `w3c-turtle.fbl` are plugin-read bindings; only what the host declares for them (routing, recognition, the registration, templates) is exercised.
- **No fixture has a folder subject**, and none exercises several readings of one body, `id.sidecar`, a legacy sidecar or `undo: "snapshot"` byte for byte.
- **The fixtures name DISL-derived ids** (`task:ingest`, `link:population|births`), which FBL leaves to DISL. The tests supply those derivations from the tool types' specifications in `etalii.adp/definitions/diagrams/`; the library itself does not compute them.
