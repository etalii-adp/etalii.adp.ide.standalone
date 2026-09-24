# Archived: library-class-rule-inventory

**Archived 2026-09-24. Its design was REJECTED, and that is the correct record rather than a mishap.**

The design is what discovered that the specification should not exist. The user read it and ended the specification; rejecting the document is what ending it looks like from the dashboard. Nothing here was refused for being wrong.

## What it was for, and why that reason was false

The specification proposed an inventory in which every class the canvas library emits is either ruled by a stylesheet or listed as deliberately unruled with a reason. Its motivating story was the second vertical scrollbar in the diagram pane: `library-canvas-surface` was emitted, styled by nothing, and its `<svg>` reserved four pixels of descender space.

**Writing the design meant measuring that story, and the story did not survive.**

- **The guard already existed** — `src/client/src/canvas/library/noUnstyledLibraryClasses.test.ts`, landed 2026-09-06.
- **`library-canvas-surface` was already on its exemption list**, from `95fbb6cb`, the commit that created the list, on the same day the class began being emitted in `9769cfd4`.
- **And the reason written beside it was TRUE.** The entry says the class carries no fill; it carries none, and none should be given to it. The defect was `display`, and `canvas-drawing` beside it in the same attribute is ruled and silent about `display`.

So the inventory this specification asked for **existed throughout the life of the defect, contained the class, carried a reason, and was green.** The requirements' claim that nobody could have written its reason truthfully was wrong as a matter of record.

**The inventory accounts for the EXISTENCE of a rule, never its ADEQUACY**, and no arrangement of the six requirements changes that. Strengthening the wrong question does not make it the right one.

## What survived, and where it went

Everything worth keeping went into the guard itself rather than into tasks, which is why there is no `tasks.md` here. It landed at **`b25d3919`**, with a follow-up at **`118ec355`** removing the list's one stale entry:

- **The adequacy limit** is now the largest paragraph in that file's docstring, and in the forward walk's failure message. It ends by saying not to motivate work from a layout defect by pointing at the file.
- **The extraction method** became a TypeScript AST walk. Measured against the quoted-literal pattern it replaced: 33 names before, 35 after. The two it could never see are `library-connect-preview` and `library-connect-preview-invalid`, emitted from a template literal carrying a substitution.
- **R6's boundary ruling** — the `library-` prefix is a namespace the library owns, and a module emitting one fails rather than joining the inventory — is in the docstring with a walk behind it.
- **R2.2's worked example** is a test rather than a note: two module canvases contain the word `library-internal` in prose, which a raw-text scan reports as an emitted class.

## Two corrections this specification owes the record

**R2.2 named `library-element` as a class appearing only inside a doc comment. It is emitted**, at `DiagramCanvas.tsx:1885`.

**R2.1's own method was the weaker one.** Obtaining the emitted set by parsing `className` attributes, read literally, loses **24 of the 35 names**, because most reach the attribute through an array or a ternary — fewer than the regular expression the criterion forbids.

Both were measured after the requirements were approved. They are recorded here rather than corrected in place, because the approved text is the record of what was approved.

## Two consequences worth knowing about

**The approval records under `.spec-workflow/approvals/library-class-rule-inventory/` still name `.spec-workflow/specs/library-class-rule-inventory/…`**, which no longer exists. Approvals are not archived alongside specifications — the archive holds `specs` and nothing else — so a stale `filePath` is the ordinary cost of archiving anything that was ever carded. Bookkeeping, not evidence that the move went wrong.

**And the rejection is recorded in the request file alone.** A rejection writes no snapshot and no new `trigger`; the snapshot metadata here still reads `trigger: "initial"` at `currentVersion: 1`. Anyone reconstructing this verdict from the snapshots will find nothing, and should read `approval_1790212423569_bxc4okncr.json`, whose `status` is `rejected` with the user's own response beside it.

## Nothing links here, and that was measured

Zero markdown links anywhere in the repository point at `library-class-rule-inventory`, and zero markdown links inside either document point out of it. So the move needed no repathing — checked rather than assumed, because the link guard would otherwise have found what this note claims.
