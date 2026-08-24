# C4 test fixtures

`.dsl` documents the C4 module's parser, writer and layout are tested against.

`.gitattributes` marks `*.dsl` as `-text`, so these bytes survive checkout on every platform.
Requirement 3.1 says a document ADP did not change comes back byte-identical, and the tests
check that by comparing serialized bytes against the file on disk — with `core.autocrlf` on,
git would otherwise rewrite the line endings and the test would be measuring git rather than
the writer. **Replace a fixture only by committing what an authoring tool actually wrote.**

## Provenance

Two kinds of file live here, and the difference matters.

### Hand-made edge cases — deliberate, and correct to hand-write

These exist to pin one specific property each. Task 1 asks for them explicitly; they are not
approximations of anything, so writing them by hand is the right way to get them.

| File | What it guards |
|---|---|
| `minimal.dsl` | The smallest document that must round-trip: one person, one system, one relationship, one view. |
| `comments-everywhere.dsl` | `//`, `#` and `/* */` comments before the workspace, trailing an opening brace, between elements, trailing an element, and after the closing brace. Requirement 3.3 — none may be dropped or moved. |
| `crlf-line-endings.dsl` | Identical content to `lf-line-endings.dsl`, written CRLF throughout. |
| `lf-line-endings.dsl` | The same content written LF throughout. The pair proves the writer preserves whichever style it was given rather than normalising. |
| `no-trailing-newline.dsl` | A final line with no newline at all — the case a naive line-splitter silently "fixes". |
| `unusual-indentation.dsl` | Tabs, mixed widths, a closing brace under-indented, and a blank line inside a block. Requirement 3.2 — unaffected lines are not re-indented. |
| `unmodelled-constructs.dsl` | `!identifiers`, `!docs`, `!adrs`, `styles`, `theme` and `configuration`. Requirement 3.3 — parsed or not, all of it survives verbatim. |
| `deployment-nested.dsl` | Deployment nodes nested four deep, infrastructure nodes, and one container with two instances. Requirements 9.2–9.4. |
| `dynamic-interactions.dsl` | An ordered interaction sequence, for the numbering Requirements 7.5–7.6 maintain. |

### Real-world documents — **outstanding**

Task 1 also calls for documents produced by Structurizr's own tooling rather than by us: the
canonical **Big Bank plc** workspace (all four static levels plus dynamic and deployment views)
and a real **AWS-style deployment** example. They are the ones that would catch a parser
written against a plausible-but-wrong mental model of the DSL, which is exactly what the
hand-made files above cannot do.

**They are not here yet, and none of the files above is a stand-in for them.** The `structurizr`
GitHub organisation is not reachable from the sandbox this repository is developed in — a
control fetch of an unrelated public repository succeeds while every `structurizr/*` path
returns 404, including the repositories' own `README.md`. Task 1's restriction is explicit —
*do not hand-write a file and call it canonical* — so nothing was fabricated to fill the gap.

To finish this task, drop these two files into this folder and add them to the table above with
their source and version:

* `big-bank-plc.dsl` — from `structurizr/examples`, the `dsl/big-bank-plc` workspace.
* `aws-deployment.dsl` — any real AWS deployment example from the same repository.

Until then, treat the round-trip guarantee as **proved for the constructs the hand-made corpus
covers and unproved beyond them**. `C4Document.RoundTrip.Tests` (task 12) enumerates this
folder, so both files are picked up automatically once they are added — no test change needed.
