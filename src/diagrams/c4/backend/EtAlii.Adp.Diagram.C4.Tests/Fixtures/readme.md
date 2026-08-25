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

### Realistic documents  hand-written, then certified by the real Structurizr CLI

Task 1 asks for documents that would catch a parser written against a plausible-but-wrong
mental model of the DSL, which the small hand-made files above cannot do, and names two: the
canonical **Big Bank plc** workspace and a real **AWS-style deployment** example.

| File | What it guards |
|---|---|
| `big-bank-plc.dsl` | The C4 worked example: all four static levels nested three deep, a dynamic view with ordered interactions, a live deployment environment with instance counts, and a styles block  combined in one document, which is where a parser tested one construct at a time comes apart. |
| `aws-deployment.dsl` | An AWS deployment: infrastructure nodes, deployment nodes nested four deep with an instance count on a node that also has a body, and relationships declared at environment level between an infrastructure node and a container instance. |

**Both were written by hand here. Neither is a copy of an upstream file, and neither may be
described as canonical**  task 1's restriction is explicit about that, and it still holds.

What makes them worth more than the small fixtures is not their provenance but their
certification: each is validated by the **real Structurizr CLI** (`validate -workspace <file>`,
which parses with the same `structurizr-dsl` library Structurizr itself uses). So the DSL in
them is not ADP's opinion of the DSL. A disagreement between one of these files and ADP's
parser is ADP's to fix, which is exactly the property the task wanted, and it is how nine real
defects were found:

| Found by | Defect |
|---|---|
| `big-bank-plc.dsl` | `deploymentNode`'s instance count was read as a tag, because the count sits *after* the tags. |
| `big-bank-plc.dsl` | `c4.missing-description` fired on deployment nodes; C4 names those by what they are and leaves them undescribed. |
| `big-bank-plc.dsl` | `c4.missing-protocol` fired on component-to-component calls, which are in-process and have no protocol. |
| `big-bank-plc.dsl` | `c4.mixed-abstraction-levels` was removed outright: C4's own sign-in view mixes containers and components, so Requirement 7.7 was wrong, not the example. |
| `big-bank-plc.dsl` | The layout drew two elements on top of each other  outsiders leaving a boundary each took their own shortest route and collided. |
| `comments-everywhere.dsl` | The fixture itself was invalid: a comment trailing a declaration is rejected by the real parser ("Too many tokens"). It had asserted something the format does not support. |
| `aws-deployment.dsl` | A `tags` line inside an element's block was ignored - tags were only read from the declaration's arguments. Every element in that document tags itself that way, so all of them came out untagged, and tags are what styles key off. |
| `C4InteropTests` | ADP's own component template scoped its view as `system.container`; identifiers are flat unless a document says `!identifiers hierarchical`, so the dotted form named nothing. |
| `C4InteropTests` | ADP's own deployment template wrote an empty `deploymentEnvironment`; an environment is a property of the nodes deployed into it, so with no nodes it does not exist and the view bound to nothing. |

The two files also close a claim that was previously left open: an ADP-authored document is
fed back through the same CLI in `C4Interop.Tests`, so "another tool can still read what ADP
wrote" is checked rather than assumed.

#### On the earlier "unreachable" note

This section used to record these files as outstanding, on the grounds that the `structurizr`
GitHub organisation was unreachable from the sandbox. **That was a wrong diagnosis.** The
organisation resolves fine (`structurizr/lite` returns 200); the repositories task 1 names 
`structurizr/dsl`, `structurizr/examples`  simply do not exist under those names. A 404 was
read as a filter. Maven Central and GitHub releases were reachable the whole time, which is
where the CLI used above came from. Recorded here rather than deleted, because the mistake
was to treat one failed fetch as proof of a policy.
