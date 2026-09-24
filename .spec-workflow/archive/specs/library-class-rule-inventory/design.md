# Design Document

## Overview

**The guard this specification asks for already exists**, as `src/client/src/canvas/library/noUnstyledLibraryClasses.test.ts`, landed 2026-09-06. It collects the classes the library emits, collects the selectors the stylesheets define, and requires each emitted class to be ruled or listed with a reason. That is R1.1, R1.2 and R4.1 already standing.

So this design is not a new guard. It is **four additions, one method correction, and one ruling** — and it arrives with **two findings about its own approved requirements**, which R1.4 and R4.3 anticipate and which are stated here rather than worked around.

The additions: the stale-entry walk (R1.3), the inverse walk (R5), a liveness assertion on the ruled side (R3.2), and the namespace walk the boundary ruling needs (R6.2). The correction: how the emitted set is obtained (R2). The ruling: what the boundary is (R6.1).

## Steering Document Alignment

### Technical Standards (tech.md)

Styling is centralised rather than inline, and this guard is the instrument that says whether that is true of the canvas library's own surface. It runs in the client suite with the other client guards, parses rather than pattern-matches, and asserts that its extractors are alive before it reports — the three habits `processes.md` requires of a check that reports absence.

### Project Structure (structure.md)

The work stays inside one existing file under `src/client/src/canvas/library/`, plus one paragraph in `docs/creating-a-diagram-module.md`. No new project, no new dependency.

## Finding 1: the motivating defect was already inside the list, with a reason

This is the finding that shapes the whole design, so it comes before the components.

`library-canvas-surface` entered the deliberately-unruled list in **95fbb6cb (2026-09-06)**, which is the commit that created the list, and the class began being emitted in **9769cfd4**, the same day. It has never been ruled: `grep -n 'library-canvas-surface' src/client/src/canvas/canvas.css` returns nothing today.

So during the entire life of the second-scrollbar defect, **the inventory this specification's introduction asks for existed, contained that class, carried a reason for it, and was green.**

**And the reason written was true.** The list's own heading says the entries are "structural or text, carrying no fill of their own", and that is a correct statement about `library-canvas-surface`: nothing paints it, and nothing should. The defect was not a fill. It was `display` — an `<svg>` left inline reserves descender space below the baseline — and the class sharing the attribute with it, `canvas-drawing`, **does** have a rule: `width: 100%; height: 100%`, silent about `display`.

**The consequence is a limit that must be stated rather than discovered:**

> **This inventory accounts for the EXISTENCE of a rule, never its ADEQUACY.** A class can be ruled, or truthfully exempted, and still be wrong on a property no rule mentions. The second scrollbar is that case, and this guard would not have caught it in any of the six requirements' shapes.

The requirements' introduction says "nobody could have written its reason truthfully." Measurement says otherwise: somebody did, and it was true about the question the list asks. **The list was not the missing guard. The missing guard was one that asks about layout**, which is out of scope here and is `canvas-single-scrollbar`'s own subject.

This does not make the inventory worthless — R1.3, R5 and the namespace walk all catch things nothing else catches, and thirty-odd exempted classes are a real contract surface that nothing currently reviews. It makes one sentence of its motivation false, and a guard whose motivation is false is the guard people delete in a year. The limit above goes in the test file's own docstring and in the failure message, so the next reader meets it where they are standing.

## Finding 2: R2.2's worked example is emitted, and R2.1 read literally loses five classes

R2.2 names `library-element` as a class that "appears only inside a doc comment showing a CSS rule". **It is emitted**, at `DiagramCanvas.tsx:1885`, as the first entry of a `classes` array that is `.filter(Boolean).join(" ")`-ed into a `className`. The requirements' own warning applies to their own example: every count in that document came from a method caught being wrong three times, and this is a fourth instance of it.

The worked example R2.2 needs is still available and is better, because it survives the quoted-literal method too: `DiagramCanvas.tsx:2302` is a `//` comment containing a backticked `.library-element-label { text-anchor: middle }`. The existing extractor matches backtick-delimited strings, so **it matches that comment today**; the false positive is masked only because the class is genuinely emitted twelve lines above. Remove the emission and the guard would keep insisting the class was emitted, from a comment.

A second, unmasked example sits in the module tree: `DependencyGraphCanvas.tsx:162` and `TimelineCanvas.tsx:131` both contain the word **`library-internal`** in prose. Any raw-text scan reports that as an emitted class named `library-internal`. The quoted-literal scan does not, because the phrase is not inside a delimiter pair on one line. Naming which method each example defeats is the point; an example that defeats nothing teaches nothing.

**And R2.1 read literally is less complete than the regular expression it forbids.** "Parsing `className` attributes" collects only what appears inside the attribute. Five classes do not: `library-element`, `library-element-dragging`, `library-connect-target` and `library-connect-forbidden` reach `className` through the `classes` array at line 1885, and `library-element-label` through another array at line 2292. An attribute-only parse loses all five and reports a smaller, cleaner, wronger inventory.

**The method this design adopts**, which satisfies R2.1's prohibition and its intent:

- Parse each non-test `.tsx`/`.ts` file under the library with the **TypeScript compiler API** (`typescript ^5.7.2`, already a devDependency of `src/client`).
- Collect every `StringLiteral` and `NoSubstitutionTemplateLiteral` **node**, wherever it sits, and split each on whitespace.
- **Comments are not nodes.** That is the AST's real advantage over the regular expression here, and it is what satisfies R2.2 — not the following of attributes.

The prohibition in R2.1 is on a regular expression over source text, and there is none: the input is a syntax tree. **The adopted method is broader than the criterion's literal wording, and that is disclosed rather than assumed.** If the narrower reading was intended, it is a finding about R2.1 and the five lost classes are its cost.

## The boundary (R6)

**The boundary is the `library-` prefix, and it is a namespace the canvas library owns.** It is a property of the class name, not of the file the class is written in, which is what R6.2 asks for in those words.

| class | in the emitted set | in the inverse walk | if a module emits it |
|---|---|---|---|
| `library-*` from the library's own sources | yes | yes | — |
| `library-*` from anywhere else | no | yes | **the guard fails: namespace violation** |
| any other class, from anywhere | no | no | out of scope |

Three consequences worth stating, because each is a decision rather than a deduction:

**A module emitting a `library-*` class fails the guard rather than being absorbed into the inventory.** The alternative — treat it as emitted, and require a rule or a list entry — would let a module grow the library's contract surface without touching the library, and the list's reasons would then be written by somebody who cannot see the module. The prefix is the library's to spend.

**The inverse walk spans the whole client tree, but only `.library-*` selectors.** If it were "every rule minus what the library emits", it would fire on every module class ruled in a shared stylesheet — hundreds of them. A rule named `.library-foo` that nothing emits is an orphan whoever wrote it did not want; a rule named `.timeline-foo` is none of this guard's business.

**Nothing has to change for the namespace walk to pass.** No module emits a `library-*` class today: the only two occurrences of the string in `src/diagrams` are the prose word `library-internal` in two comments, which is Finding 2's unmasked example. The walk lands green and stays cheap, which is the correct shape for a rule about a namespace — it exists so the first person to cross the line meets a message rather than a reviewer.

R6.3 is met twice over: a paragraph in `docs/creating-a-diagram-module.md` saying the prefix is reserved and what to use instead, and the namespace walk's own failure message saying it at the moment it applies.

## The components

All of it lives in `noUnstyledLibraryClasses.test.ts`, whose name stops being accurate and becomes `libraryClassInventory.test.ts`. The four existing paint-specific cases in that file — the connect preview's `fill: none`, the anchors' pressability, the anchors' fill against the shared ones — are untouched and keep their own names.

### The two sets

`emitted` — the AST walk of Finding 2, over non-test `.ts`/`.tsx` under `src/client/src/canvas/library/`, filtered to class-shaped `library-` names.

`ruled` — selector tokens matching `.library-*` from every `.css` under `src/client/src`, **with CSS comments stripped first** (R2.4). No CSS parser is added: a comment strip plus a selector-token match is the whole requirement, and a dependency bought for one regular expression is a dependency somebody has to justify at the next audit. If the strip ever proves insufficient, `postcss` is the escalation and the reason to take it will be a failure rather than a preference.

### The four walks

1. **Forward (R1.1, R1.2).** Every emitted class is ruled or on the list. Fails naming the classes, and says both remedies.
2. **Stale entries (R1.3).** Every list entry is still emitted. This is the walk that keeps the list evidence rather than sediment, and it is the one the existing guard has no form of.
3. **Inverse (R5.1).** Every `.library-*` selector is emitted. **Empty at the time of writing** — the ruled classes are all emitted — and R5.2 requires that emptiness to be in the test's own text, which it is, with the reason it is kept: a rule orphaned by a rename costs nothing to detect and is invisible otherwise.
4. **Namespace (R6.2).** No source outside the library emits a `library-*` class.

### The liveness assertions (R3)

The existing guard has half of one: a floor of twenty on the emitted set. A floor is a weaker instrument than a named set — it survives an extractor that has lost a whole file — so both sides get a named set instead, and both are separate (R3.1, R3.2):

- **emitted side:** `library-anchor`, `library-connect-preview`, `library-element` and `library-canvas-surface` must all be found. The last two are deliberate: `library-element` reaches `className` through an array and `library-canvas-surface` is second in its attribute, so the pair fails if either of Finding 2's two collection hazards regresses.
- **ruled side:** `library-anchor` and `library-connect-preview` must be found among the selectors.

**R3.4 is the constraint that shapes them.** They must be sensitive to the extractor breaking and insensitive to the inventory being legitimately empty in one direction — which is exactly why they are assertions about **named classes being found**, and not about either walk's result being non-empty. The inverse walk finds nothing today and must be allowed to.

### The list (R4)

`paintedElsewhere` is renamed `deliberatelyUnruled`, because its entries are no longer only about paint — a name that says "paint" is part of why Finding 1's entry read as adequate. Each entry keeps a one-line reason (R4.1). **Reasons are written by hand and never generated** (R4.2): the file's own docstring says so, and says why, because the fastest way to make this guard green is the one that destroys it.

The existing list already carries the precedent R4.3 needs, in its own words: two classes once sat on it with no reason beside them, could not be justified, and were styled instead. The seeding pass follows that — a class whose reason cannot be written truthfully becomes a finding in the implementation log, not an entry.

R4.4's report — how many entries written, how many findings — goes in the implementation log, since the seeding is a one-time pass and the log is where a one-time pass stays visible afterwards.

## What this design deliberately does not carry

**No count is an acceptance figure.** The requirements say their numbers are upper bounds from a discredited method; the same is true of this document's. What greps report today is ten-ish ruled classes and thirty-odd emitted, and the implementation reports what the parse finds. **If the parse disagrees with those numbers, the parse is right and neither document is evidence against it.**

**No adequacy check.** Finding 1 says why, and `canvas-single-scrollbar` owns that question.

**No custom-property inventory**, and **no change to any `canvas-single-scrollbar` document** — both out of scope in the requirements, and the second is approved text that stays untouched.

**No browser.** The whole guard is filesystem reads and a parse, which is what makes it affordable on every gate (NFR). The self-test's twenty-one minutes are the counter-example this project has just paid for, and a guard that reads a few dozen files is not near that line.

## Requirement Coverage

| Criterion | Where |
|---|---|
| R1.1, R1.2 | Walk 1, already standing; message names the classes and both remedies |
| R1.3 | Walk 2, new |
| R1.4 | Green on arrival for walks 2-4; walk 1's greenness rests on the seeded list. **Finding 1 is this design's answer to R1.4's second half** |
| R2.1 | AST literal walk, with the criterion's literal reading and its five-class cost disclosed |
| R2.2 | Comments are not AST nodes; worked examples corrected — the comment at `DiagramCanvas.tsx:2302`, and `library-internal` in two module files |
| R2.3 | `canvas-drawing library-canvas-surface` split on whitespace; `library-canvas-surface` is in the emitted-side liveness set so the hazard cannot regress silently |
| R2.4 | CSS comments stripped before selector extraction |
| R3.1, R3.2 | Named-set assertions, separate per side |
| R3.3 | Perturbation, recorded in the implementation log — see Testing |
| R3.4 | Named-set form rather than non-empty-result form, precisely so walk 3 may be empty |
| R4.1, R4.2 | One-line reason per entry; hand-written, with the prohibition in the file's docstring |
| R4.3 | A reason that cannot be written truthfully becomes a log finding; the existing list's two removed entries are the precedent |
| R4.4 | Counts in the implementation log |
| R5.1 | Walk 3 |
| R5.2 | Stated in this document and in the test's docstring, with the rename case as the reason it is kept |
| R5.3 | The failure message says a green inverse walk means only that no orphaned rule was found |
| R6.1 | The boundary table; the walks enforce exactly the `library-` prefix |
| R6.2 | Walk 4; the rule follows from the prefix rather than from where the class is declared |
| R6.3 | `docs/creating-a-diagram-module.md` paragraph, and walk 4's failure message |

## Testing

**Each of the two liveness assertions is perturbed and seen to fail** (R3.3): the emitted extractor made to return an empty set, then the ruled one, each run separately, and **each message recorded verbatim in the implementation log**. A liveness assertion never seen to fail is the thing it exists to prevent, one level up.

**Each of the four walks is perturbed too**, which R3.3 does not require and which costs one run each: add an emitted class with no rule and no entry (walk 1), leave an entry for a class that has been removed (walk 2), add a `.library-nothing` rule (walk 3), add a `library-x` class to a module canvas (walk 4). A walk that has never been red is a walk nobody has evidence about.

**The full client suite runs, not this file alone** — a filtered run cannot reproduce the suite's conditions, and this repository has paid for that distinction twice.

## Sources

- `src/client/src/canvas/library/noUnstyledLibraryClasses.test.ts` — the existing guard, its list and its reasons
- `src/client/src/canvas/canvas.css:44` — `.canvas-drawing`, and `:65` `.library-canvas`, whose comment records the earlier instance
- `src/client/src/canvas/library/DiagramCanvas.tsx:1513` — `canvas-drawing library-canvas-surface`; `:1885` — the `classes` array; `:2302` — the comment that defeats the quoted-literal scan
- `src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx:162`, `src/diagrams/timeline/client/TimelineCanvas.tsx:131` — `library-internal` in prose
- `95fbb6cb`, `9769cfd4` — the list's creation and the class's first emission, both 2026-09-06
