# Timeline round-trip corpus

These documents exist so that Requirement 2's byte-identical claim has something real to fail
against. Every one of them was **written before the parser and the writer existed**, so the
implementation had to meet the documents rather than the documents being trimmed to match an
implementation. A fixture written to match the code it tests proves only that the code agrees
with itself.

## Provenance, stated honestly

`.tml` is ADP's own format (Requirement 1.4), so unlike the C4 and Azure Pipelines corpora there
is **no external corpus to draw from** — nobody else writes these files yet. That is a real
weakness and worth naming rather than glossing: a hand-written fixture embodies its author's
assumptions about the format, and those assumptions are exactly what a corpus is supposed to be
independent of.

Two things mitigate it. The documents were written first, before any code could shape them. And
each one is built around a property that is **about text rather than about timelines** — line
endings, comments, indentation, unknown keys — so the interesting cases come from how files
behave in general, not from anybody's idea of what a timeline should contain.

When real `.tml` files exist, written by someone other than this module's author, they belong
here and these should be reduced to the edge cases they encode.

## What each file guards

| File | Exists to prove |
| --- | --- |
| `simple.tml` | The ordinary case: a period, a moment, and one connection between them. If this fails, nothing else matters. |
| `shared-rows.tml` | A row is a placement grid, not a lane (Requirement 3.6). Several elements share row 0, the indices are neither contiguous nor ascending, and row 12 exists with nothing between. |
| `mixed-precision.tml` | Date-only and date-time both appear, but never within one element (Requirement 3.2). A date-only value must not come back as a midnight date-time — the failure this file exists to catch is silent, because both render identically on a canvas. |
| `connections.tml` | Two connections between the same pair are legal (Requirement 8.8), and a connection need not carry a label. |
| `comments.tml` | Comments in every position a YAML document allows: leading, trailing, between keys, between elements, and on the end of a line. Plus blank lines. None of it is the module's to reformat. |
| `lf-line-endings.tml` / `crlf-line-endings.tml` | The same document under both conventions. These are a **pair**: if line-ending normalisation is ever in play, they collapse into one file and stop testing anything — which is what the `.gitattributes` entry below prevents. |
| `no-trailing-newline.tml` | The last line has no terminator. Appending must not add one, and removing the last line must move its ending to the new last line. A sibling module shipped both bugs before finding them. |
| `indentation.tml` | Valid YAML in an indentation no generator would choose — four spaces at the sequence level, dashes indented under their key. A writer that "tidies" this has broken Requirement 2.2 even though the model is unchanged. |
| `scalars.tml` | Quoting is the author's choice: double-quoted, single-quoted with an embedded apostrophe, plain with trailing whitespace, escaped quotes, and non-ASCII. Requoting is a rewrite. |
| `unmodelled-keys.tml` | Keys this module does not model, at the document, element and connection level, including a nested sequence. Requirement 2.3 says every one survives verbatim. |

## Line endings are load-bearing here

`*.tml -text` is registered in the repository's root `.gitattributes`, beside `*.mm` and `*.dsl`.
Without it, git's `text=auto eol=crlf` policy would rewrite these files on checkout, the
LF/CRLF pair would become the same file, and `no-trailing-newline.tml` would quietly gain one —
at which point these tests would be measuring git rather than the writer.

That entry is a prerequisite for this corpus, not a detail. If a round-trip test starts failing
in a way that makes no sense, check that the attribute is still there before suspecting the code.

## Documents that exist to be judged: `findings/`

The files above are all clean; nothing in them raises a finding. `findings/` holds the opposite: documents written to raise every finding the rules know, so the parity transcript (`Parity/timeline.transcript.json`) pins each message, its location and the order the findings arrive in. They sit in a folder of their own because the round-trip tests above enumerate `Fixtures/*.tml` and expect every document there to parse.

| File | Exists to prove |
| --- | --- |
| `identity.tml` | Missing ids (absent and empty), an id held three times, an id shared by an element and a relation, and a relation without an id. |
| `times.tml` | Unreadable begins and ends (an empty `end:` included), ends before begins, mixed precision, and forms .NET reads that ISO 8601 does not (`2026/07/01`, `July 5, 2026`) - one of them containing a `T`, which counts as a time of day. |
| `dangling.tml` | Relations naming elements that are not there, at either end or both, and ends left empty, which are not reported. |
| `combined.tml` | Every kind at once, interleaved, plus a sequence entry that is not a mapping, so the order of the findings is pinned. |
| `unparseable.tml` | A document that is not YAML: the one error finding. |
| `empty.tml` | No elements: nothing to arrange. |

`unparseable.tml` breaks the YAML with a mapping value where none is allowed. An unclosed flow sequence (`label: [unclosed` at the end of the file) would be the more obvious choice, and is not used because YamlDotNet throws an `InvalidOperationException` for it rather than a `YamlException`, which neither the store nor the validator catches.
