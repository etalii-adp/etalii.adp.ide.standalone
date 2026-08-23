# Mindmap test fixtures

Real `.mm` files the mindmap module's parser, serializer and layout are tested against.

## Provenance — read this before trusting a round-trip test

`architecture.mm` was **written by hand against the FreeMind/Freeplane schema**. It was not
saved by Freeplane.

That distinction matters more than it looks. `mindmap-diagram` Requirement 3.3 requires that
saving a map ADP did not change produces **no diff**, and Requirement 3.2 requires that
attributes and elements ADP does not understand survive a round trip. A hand-written fixture
cannot prove either: it proves the parser round-trips *this file*, which was written from the
same understanding of the format the parser will be. If that understanding is wrong, the test
passes and the bug ships.

**Before Requirement 3 is implemented, replace this file with a genuine Freeplane save** —
open it in Freeplane, change nothing, save, and commit whatever Freeplane wrote. Any
difference between the two is exactly the knowledge this fixture is missing. Keep the same
file name so the tests do not move.

The same applies to Requirement 12: whether a code-artifact link belongs in the node's `LINK`
attribute, and whether Freeplane resolves it relative to the map file, is asserted by the spec
on the strength of the same unverified reading. A real save with a file link in it settles it.

## What `architecture.mm` deliberately contains

Each of these exists to break a naive parser or a naive serializer:

| | Feature | Why it is here |
|---|---|---|
| Structure | 25 nodes, 5 levels deep, both `POSITION` values | Layout and tree handling (Requirements 4, 5) |
| Identity | two nodes with **no** `ID` attribute | Id assignment on load, persisted on next save (Requirement 3.4) |
| Links | a relative file path, a folder path, and an `https` URL | Requirement 12.1's storage form |
| Folding | `FOLDED="true"` on a branch with children | Fold state seeded from the file (Requirement 9.3) |
| Notes | `richcontent TYPE="NOTE"`, with and without `CONTENT-TYPE` | Requirement 7.7, and attribute variance across Freeplane versions |
| Rich text | `richcontent TYPE="NODE"` — a node whose text is markup, not a `TEXT` attribute | A node's text is not always where you expect it |
| Empty text | a node with `TEXT=""` | Requirement 7.6: empty node text is valid |
| Unknown to ADP | `hook`, `MapStyle`, `map_styles`, `stylenode`, `properties`, `font`, `edge`, `icon`, `cloud`, `arrowlink`, `attribute` | Requirement 3.2: preserved untouched, never regenerated |
| Escaping | `&amp;`, `&lt;`, `&gt;` in attributes; non-ASCII and an emoji written literally, as Freeplane writes them | Requirement 3.3: byte-stable serialization |

A parser that models only "node with TEXT and children" will read this file and lose most of
it. That is the point.
