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

**Added since, from the same folder at commit `b73f15f`** (`develop`, 2026-10-12, the merge of etalii.adp #132 that introduced FBL 0.6): the fixture pair `fixtures/insert-empty-yaml` and `fixtures/insert-empty-json`, each with its own binding `cards.fbl` beside it, its body (`deck.yaml`, `deck.json`) and its `fixture.json`. FBL 0.6 settles what an insert does with an empty value (section 6.2: the attribute binding's `empty` rule applies, with a set's defaults), and spec 025 task T038 vendored the pair to show this library's yaml and json families disagreeing about it (D9, behaviour-changes B-09). T216 corrected both families (B-09, and B-12 for the layout of a new json entry), and `ConformanceFixtures.Tests.cs` runs the pair as plain fixtures; a refused step there may leave out `expect`, since the document is the one before it. `.gitattributes` keeps their `*.fbl` and `*.yaml` from conversion as well, by extension under `fixtures/`, because both are CRLF in the source.

**Copied again from the same folder at commit `5c62e79`** (`develop`, 2026-10-10, after the merge of etalii.adp #115, `42da8ee`, which introduced FBL 0.5), for fbl-written-text task 6: every binding there, `registrations/`, and every fixture there except the `knowledge-*` ones, which are kept in `etalii.adp/` below. Against the copies above, that changed `databricks-job.fbl`, `databricks-pipeline.fbl` and `fixtures/databricks-pipeline-json`, brought `timeline.fbl` to FBL 0.5, and added `work-board.fbl` (FBL 0.3) and the fixture `fixtures/timeline-written-text`, whose labels, ids and times a YAML reader would type otherwise; the rest was byte for byte the same. `LICENSE.md` was compared with that repository's own `LICENSE` at the commit and is the same text. `agent-activity-diagram.fbl`, which lives under `definitions/diagrams/` there, and the `insert-empty-*` pair, from a later commit, are kept as they were. With them came `etalii.adp/definitions/diagrams/timeline.dis` from the same commit, read for its attribute types only.

Two more things the runner does since, neither an edit to a fixture:

- **It compares the attribute values a fixture's reading names** (FBL 0.5 section 15.3): each attribute an element lists has that value and that JSON type, a string for a text, and one it does not list is not compared. `TheRunnerFailsOnAnAttributeValueOfAnotherType` changes an expected label of `timeline-written-text` from the string `"yes"` to `true` in a copy and sees the runner fail on it.
- **It hands the timeline's attribute types to the library**, from `timeline.dis` through `Support/TimelineTypes.cs`, following `extends`, because a binding does not carry them and FBL 0.5 section 5.2 reads by them.

**Copied again from the same folder for causal-loop-disl-fbl task 6:** `causal-loop-diagram.fbl` at commit `9c4fea9` (`develop`, 2026-10-14, the merge of etalii.adp #177), the binding written for FBL 0.2 that types a link `Link`, reads the header and every unread statement as entries of their own, and stores no id, leaving every id to DISL; and `fixtures/causal-loop-rename/fixture.json` at commit `6329a5e`, which brings that fixture to the same binding (`variable:births` for the variable, `Link` for the links) with its splices and bytes unchanged, merged into `develop` as `f321932` (etalii.adp #179). `growth.cld` is unchanged. The test stand-in for DISL's ids, `Support/NaturalIds.cs`, now derives the causal loop's three kinds as `causal-loop-diagram.dis` does: `variable:<name>`, `link:<causeName>|<effectName>` and `loop:<identifier>`.

**Copied again from the same folder for helm-chart-disl-fbl task 9:** `helm-chart.fbl` at commit `234e3df` (`develop`, 2026-10-14, the rewrite of the Helm chart definition at DISL 0.3 over its FBL binding, helm-chart-disl-fbl task 2), the binding written for FBL 0.2 with the file rules of finding B3 repaired: `values*` with either YAML extension in any case, the files under a subchart's `templates/`, a subchart's own `charts/` entries, what `charts/` holds directly, and `requirements.lock`; no `suggest` (gap 6) and no `registration` block. It is byte for byte the binding the Helm module embeds (`src/diagrams/helm-chart/backend/EtAlii.Adp.Diagram.HelmChart/helm-chart.fbl`). `Loading.Tests.cs` asserts its file rules' families, its FBL version and its claims as this copy declares them.

**Added since, from the same folder at commit `a58749e`** (`develop`, 2026-10-17, the merge of etalii.adp #268, for dependency-graph-disl-fbl task 9): the binding `dependency-graph.fbl` (FBL 0.5), byte for byte the binding the dependency graph module embeds but for its `doc`, and the fixture `fixtures/dependency-graph-empty-flow-sequence` with its body `graph.dgr`, which opens an empty flow sequence with a comment after it (finding B6, gap 4 of the definition's companion): the first node removes ` []` and keeps the comment on the key's line, and the undo puts it back. `.gitattributes` keeps `*.dgr` from line-ending conversion, as everywhere in this repository.

## `etalii.adp/`: the Knowledge designer's files

The folder `etalii.adp/` holds a second vendored set, copied unchanged from the same repository at commit `5893e7d` (`develop`, 2026-10-12, after etalii.adp #124, which gave a view's column a `calculate` and a view a `freeze`), **in that repository's own layout**: `definitions/designers/knowledge.fbl` (the bindings `yaml`, `json` and `xml`), `definitions/designers/knowledge.des` (the specification, read here for its attribute types only), `definitions/designers/examples/`, and `specifications/fbl/fixtures/knowledge-*`. The layout is kept because those fixtures name their binding and their inputs by paths relative to that tree, and the files are never edited here. The examples and the `knowledge-equivalence` fixtures were copied again from commit `5ec503d` (etalii.adp #120, 2026-10-11), which added Peter's extra cities.

`.gitattributes` keeps the whole of `etalii.adp/` from line-ending conversion by its path. A rule by extension would exempt this repository's ordinary `.yaml`, `.json` and `.xml` files too.

`KnowledgeFixtures.Tests.cs` runs each fixture's `read`, an unchanged save, the byte-coverage invariant, and that the one example reads as the same model in all three formats. `KnowledgeFixtures.Steps.Tests.cs` runs their edit steps: all 138 of each of the three large fixtures, and the 11 of `knowledge-kept`, go through. The reading is told the attribute types of `knowledge.des`, which a binding does not carry: that is what makes the cell of `knowledge-kept` whose number is not a number an unreadable entry.

A fixture's refusal sentence is not compared, where it is the message of the designer's own rule and not the binding's.

## What this corpus does not demonstrate

- **No persistence plugin runs here but the Helm chart folder's.** `helm-chart.fbl` and `w3c-turtle.fbl` are plugin-read bindings; for `w3c-turtle.fbl` only what the host declares (routing, recognition, the registration, templates) is exercised. `RealFiles/ModuleCrossCheck.Tests.cs` reads every chart folder of this repository through `helm-chart.fbl` with the Helm module's plugin, and no fixture names a folder as its input (FBL gap 4).
- **Only the timeline's fixtures are read with their tool type's attribute types**, and only the attribute values `timeline-written-text` lists are compared; the other fixtures name no values.
- **No fixture has a folder subject**, and none exercises several readings of one body, `id.sidecar`, a legacy sidecar or `undo: "snapshot"` byte for byte.
- **The fixtures name DISL-derived ids** (`task:ingest`, `link:population|births`), which FBL leaves to DISL. The tests supply those derivations from the tool types' specifications in `etalii.adp/definitions/diagrams/`; the library itself does not compute them.
