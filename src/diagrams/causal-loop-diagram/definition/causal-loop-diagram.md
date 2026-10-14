# Causal loop diagram: what DISL does not express

This document accompanies [causal-loop-diagram.dis](causal-loop-diagram.dis), the DISL 0.3 specification of the causal loop diagram (`systems/causal-loop-diagram`, extension `.cld`), and its FBL binding, [`specifications/fbl/causal-loop-diagram.fbl#cld`](../../specifications/fbl/causal-loop-diagram.fbl). The specification holds everything DISL 0.3 can state: the metamodel, the notation with its curved links, the toolbox with its drops, the menus with their counts and reasons, the forms with their parsing, the seven findings with the tool's own codes, sentences and order, the operations and their confirmations, and the body read and written through the binding. What DISL 0.3 and FBL 0.2 cannot state is listed here in [Language gaps](#language-gaps), each with its evidence and what would resolve it. The wire ids, which DISL does not carry by ruling, are declared under `x-cld` in the specification.

The tool it describes is the standalone module `src/diagrams/causal-loop-diagram/` in [etalii-adp/etalii.adp.ide.standalone](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/src/diagrams/causal-loop-diagram). Every sentence, label and code in the specification is taken from that module's parity transcript, `backend/EtAlii.Adp.Diagram.CausalLoopDiagram.Tests/Parity/causal-loop.transcript.json`, recorded over the fixtures beside it (`backend/EtAlii.Adp.Diagram.CausalLoopDiagram.Tests/Fixtures/*.cld`) and the two examples. The conversion that brings the module onto this specification is the standalone repository's spec-workflow specification `causal-loop-disl-fbl`; finding numbers below (A3, B1 and so on) are those of its requirements document. Paths are relative to the module unless they say otherwise.

## Contents

1. [How the specification maps the tool](#how-the-specification-maps-the-tool)
2. [What the definition now states](#what-the-definition-now-states)
3. [Language gaps](#language-gaps)
4. [Other differences a runtime will meet](#other-differences-a-runtime-will-meet)
5. [Pending with the owner](#pending-with-the-owner)
6. [What stays code](#what-stays-code)

## How the specification maps the tool

| Tool concept | In causal-loop-diagram.dis | Notes |
| --- | --- | --- |
| Variable | node type `Variable` | Key attribute `name` (the tool's identifier; `id` is reserved in DISL), `label` its caption, derived `display`. Element id `variable:<name>`. |
| Causal link | relation `CausalLink` | Keyed by `causeName` and `effectName`, which the binding reads from the link's two ends; element id `link:<from>\|<to>`. The free text is `note`, shown as "Note". |
| Loop (a claim about a cycle) | node type `Loop` | Key attribute `identifier`; `title` is shown as "Name"; `members` lists the variables in loop order. Element id `loop:<identifier>`. It is a node because DISL has no construct for a claim about a path. |
| Link polarity | enum `Polarity` | `positive`, `negative`, `unstated`. |
| Computed loop polarity | enum `LoopPolarity`, derived `Loop.computed` | Reinforcing, balancing, undecidable, from the count of negative links around the cycle. |
| Cycle enumeration | `diagram.cycles('CausalLink', 1000)` and `diagram.cyclesTruncated` | DISL 12.4. |
| The body | `persistence.format: "fbl"`, binding `cld` | The standalone module's own copy of the binding; `typeMap` maps its entry types onto the metamodel. |
| Positions | `persistence.view.store: ["bounds"]` | Stored in the `.adp` registration's `layout:` block under the element ids, never in the body. |
| Ring layout | layout `ring`, `circular` with `adp.ring.*` options | See [gap 5](#5-disl-the-seating-order-and-radius-of-circular). |
| Arrange diagram | operation `arrange`, layout `selfOrganizing` from the plugin `net.etalii.adp.systems.cld` | The layout stays code; the plugin provides it alone and is not required to read the document. |

## What the definition now states

The previous companion, written against DISL 0.1, listed twelve aspects DISL could not express. DISL 0.2 and 0.3 express each, and the specification now states it with the construct named:

| Aspect | Construct |
| --- | --- |
| Enumerating the cycles the links form, bounded at 1000 | `diagram.cycles`, `diagram.cyclesTruncated` (the function `feedbackCycles()` and the rule `cycleBoundReached`) |
| One finding per cycle no loop claims | the rule `unlabelledLoop`, `forEach` over the unclaimed cycles, `enforcement: report` |
| The order of the findings | `constraints.order`: unreadable lines, dangling links and the cycle bound first; the rest by line; findings without a line last |
| The tool's own finding codes | `code` on every rule and on `std.unreadableEntry` and `std.references`, the latter computed per finding |
| A reason on an entry that does not apply | `unavailable` with a `reason` on Arrange diagram, Same direction (+) and Opposite direction (−) |
| Labels that change with state | CEL Message labels on Add delay / Remove delay and Curve the other way / Curve back |
| A count in a menu label | `'Remove variable (with ' + n + ' references)'`, with `n` from `removalCount` |
| A toolbox entry dropped onto a variable | `drop.targets` on the Link and Loop tools, `elsewhere: refuse` with "Drop a link or a loop onto a variable." |
| The empty-canvas sentence | `canvas.empty`: "This causal loop diagram states no variables yet." |
| The mirrored marker of a balancing loop | NodeIcon `flip: horizontal` under a condition on `computed` |
| Fields that read `+`, `s`, `-`, `o` and weights | FormItem `parse` with `accepts`, `write` and `refusal` on polarity and weight |
| Arrange's refusal when nothing is drawn | `layout` action `refusals.nothingDrawn` |

The rest of the rewrite is the same move: a link is `routing: "curved"` with `curvature: 0.2` (finding A3; DISL 0.2 made `arc` a circular arc), with a `-0.2` variant for a flipped link; the body is read through the binding with a `typeMap` instead of a plugin (A4, B3); the element ids carry a prefix per type (B10); the wire ids are in `x-cld` (A5); and `std.duplicateId`, `std.unique` and `std.endpoints` are off, with the tool's gesture rules declared in their place, so that the findings and refusals are the tool's list and no more (A7, Q4).

## Language gaps

Each gap is a candidate change to DISL or FBL. None is worked around in the specification by stating something the tool does not do; where the specification and the tool differ because of one, the tool's behaviour is the one described here.

### 1. FBL: a reason for a statement that almost matched

Finding B2. A rule says what it matches, not what a line that starts like it and fails should be told. The binding's last rule, of entry type `Unreadable`, takes every statement no other rule read, and the specification maps it to `unreadable`, so DISL reports it once on its line under `causal-loop.unreadable-line` with the reason `std.unreadableEntry` gives. The tool says why, in four sentences of its own, recorded over `unreadable-lines.cld`:

- line 9, `bogus statement`: "'bogus' is not a statement this reading knows."
- line 10, `link a b`: "A link reads 'link <from> -> <to>', optionally followed by a polarity, 'delayed', 'flipped', a weight and a label."
- line 11, `link a -> b weight=heavy`: "'weight=heavy' does not read as a weight."
- line 12, `variable ""`: "A variable needs an id of at least one character."

The lines are the same under the binding; the sentences are not. **Would resolve it:** a `fallback` on a `lines` or `blocks` rule, a looser expression with a message, tried when no rule matched.

### 2. FBL: a tolerant word grammar

Findings B1 and B12. The binding reads every spelling of `lenient-spellings.cld` that a regular expression can state: an unquoted label, a quoted identifier, leading whitespace, an unquoted loop name, and `S` and `O` as polarity. Three things the tool does are beyond it:

- **A bare word as a note.** The tool reads `link a -> indented + bareNote` with the note `bareNote`; the binding's `note` slot takes a quoted word only, so that link has no note.
- **The last candidate wins.** The tool reads `link a -> b + "first note" "second note"` with the note "second note" and `link b -> a - firstBare "second quoted"` with "second quoted" (`two-notes.cld`); a `word` slot takes the first word that matches, so the binding reads "first note" for the first.
- **Quotes are optional when the value has no space.** The tool reads `variable a Label` and `loop R1 Unquoted a indented`; the binding reads them too, but its `emit` templates quote a label and a loop name, so a new statement is written with quotes where the file's own spelling had none.

**Would resolve it:** a word grammar for `lines` that can say "any other word is the note, the last one winning" and "quote a value only when it needs it", short of regular expressions that restate the tool's tokenizer.

### 3. FBL: placing several new entries of different rules as one group

Finding B6. Drawing a link that closes cycles writes the link and its loop claims together, after the last link; the binding places each new entry after the last of its own rule, so the claims go after the last loop, and a first statement of a kind goes after the last entry rather than at the end of the file. The difference shows when a file ends in comments or blank lines, as `spellings-kept.cld` and `trailing-comments.cld` do. **Would resolve it:** an `insert` that places entries of several rules made by one change as one group. Needed only if the owner rules that the tool's present placement is kept (Q1).

### 4. DISL: a wavy underline

Finding A8. A loop whose stated letter disagrees with its arrows is underlined with a **wavy** line (`client/causal-loop.css`, `text-decoration: underline wavy`). DISL's text decoration has `underline` and no wavy form; the specification states the underline and documents the difference on the condition. **Would resolve it:** a `style` on a text decoration (`solid`, `wavy`, `dotted`, `dashed`), as CSS has.

### 5. DISL: the seating order and radius of `circular`

Finding A9; DISL D.2, open question 9. The ring a document opens on seats the variables in a depth-first walk of the links in declaration order, starting at the top and running clockwise, on a radius that grows with the widest variable and the count so that no two overlap. DISL names `circular` and defines neither; the specification carries both as `adp.ring.*` options, which no runtime is required to read. **Would resolve it:** an order and a radius rule for `circular` in DISL's standard layouts, or a way for a definition to state them.

### 6. DISL: a committed value that did not change

Finding A10. Committing a value equal to the current one succeeds and changes nothing: setting a loop's variables to the cycle it already runs through, and setting Delayed on a link that is already delayed, each answer success with no change written and nothing on the undo stack. DISL makes every committed field one transaction and does not say that an unchanged value lands none. **Would resolve it:** a sentence in DISL that a transaction which changes no value is not recorded.

### 7. DISL: whether a size sent on the wire is a layout-time measurement

Finding A11. A variable's width is measured on the backend from its label, and the positions the layouts compute depend on it. DISL 0.3 limits `textMetric` to layout-time measurement, and does not say whether a node's `autoSize: "fitWidth"`, sent on the wire, is such a measurement. **Would resolve it:** a statement in DISL of where a fitted size is measured, and whether a host may send it.

## Other differences a runtime will meet

These are not language gaps. Each is a place the transcript shows the tool doing something a runtime of this specification might do otherwise, recorded so that a host does not have to find it again.

- **The order of cycles.** DISL 12.4 orders cycles by first member in model order and then by member order. The module's finder is Johnson's algorithm, and its order differs: over `weight-forms.cld` it reports the cycle a → b → c → d before a → b. That order decides the order of the unlabelled-loop findings and which cycle Claim a loop picks. The standalone conversion compares the two over its corpus before the module's finder is removed.
- **Numbers.** A weight is shown and written in .NET's shortest round-trip form: `1E+21`, `1E-07`, `1E-06`, `1.2345678901234568E+29` (`weight-forms.cld`). The binding's `number: "shortest"` is ECMAScript's (`1e+21`, `1e-7`). The two agree on ordinary values (finding B11).
- **One attribute, two refusals.** A variable's label is refused with the quote sentence of `std.facets` in the property grid, and with "A name needs at least one character, and cannot contain a quote or a line break." in the inline rename dialog. A label `parse` takes no `accepts` or `refusal`, so the specification states the grid's.
- **A reason sent with an entry that applies.** The menus carry a reason on entries that are enabled as well: Arrange diagram is sent "on (There is nothing to arrange until this diagram has two variables.)" with two variables present, and Opposite direction (−) "on (This link already states −.)" on a positive link. The specification states each reason with its `unavailable` condition only.
- **Reasons no definition states.** Arrange diagram on a document that cannot be read answers "This causal loop diagram could not be read, so there is nothing to arrange."; on one that is not open, "This diagram is not open on this connection, so there is nothing to arrange."; the self-organizing layout has its own budget and overlap sentences. An edit of a link's end answers "'causal-loop.link-from' cannot be edited on this selection.", an action of another tool "That action is not this diagram's.", and the empty target gets no menu. These are the host's and the plugin's, not the definition's.
- **Moving a loop or a link.** "Only a variable can be moved; a link is drawn between its ends and a loop label among its members." The specification states it as each type's `refusals.move`.
- **Adding a variable at a point.** Add variable on the canvas writes the statement and its position as one undo step (`AddVariableAtCommand`). A DISL operation's `create` and a registration write are two changes.
- **The link's names are read, not written.** `causeName` and `effectName` are read-only `value` slots of the binding that the specification keys a link by. A new link's are given by the operation's `connect` attributes; a runtime **MUST NOT** plan those as writes to the body, since the `from` and `to` reference attributes the link is written from already carry them.
- **The declared order.** `constraints.order` lists three codes. The other codes share one position after them, sorted by line, and the findings of `unlabelledLoop`, which have no line, come last. That is one reading of DISL's rule for unlisted codes and for findings without a line; the transcript records the order the tool gives, which a runtime checks itself against.
- **A loop that is not a cycle.** `std.references` reports a loop member that names no variable under `causal-loop.loop-is-not-a-cycle`; no fixture of the corpus has such a loop, so that case is not in the transcript.

## Pending with the owner

Two findings are put to the owner and are not settled here.

- **A quote in a label (B13).** The transcript shows the property grid refusing a quote in a variable's label and in a loop's name, and the rename dialog rejecting one. The specification's `pattern` on `label`, `title` and `note` forbids it.
- **Two variables of one name (A7).** `duplicate-variable.cld` declares `a` twice. The module reports no finding for it; the transcript records its loop rows in the specification's form, where the hand-written rows threw "System.ArgumentException: An item with the same key has already been added. Key: a"; an edit of those rows, the findings and the payloads still throw until the module's cycle finder and layout are removed. The specification switches `std.duplicateId` off, so the findings are the module's.

## What stays code

| Part | Why |
| --- | --- |
| The self-organizing layout and Arrange diagram's command | The specification names the layout as a plugin. |
| The ring layout | Gap 5. |
| The stand-in for unreadable lines | Gap 1. |
| Adding a variable at a point | One undo step for a body change and a position. |
| The payloads, the measured widths, the viewport filter | The host's wire (findings A11 and A12). |
| The session, the document store and reloading | The shared document lifecycle. |
