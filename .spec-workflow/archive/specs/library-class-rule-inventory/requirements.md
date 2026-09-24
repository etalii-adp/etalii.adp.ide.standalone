# Requirements Document

## Introduction

**A class the canvas library emits, with no rule behind it, cost a user a visible defect.** `library-canvas-surface` was rendered on every canvas and styled by nothing, so its `<svg>` stayed an inline box and reserved four pixels of descender space, which spilled into the diagram pane and produced a second vertical scrollbar. `canvas.css` records that this had already happened once before, to `.library-canvas`.

**This specification adds the guard that would have caught it: an inventory in which every class the library emits is either ruled or listed as deliberately unruled with a reason.** `library-canvas-surface` would have had to appear in that list, and **nobody could have written its reason truthfully.**

### Why this is its own specification rather than an amendment

`canvas-single-scrollbar`'s approved requirements do not cover this direction, and its design says so in a section of its own. **Adding it there would have claimed work the user never approved**, and would have required amending three approved documents in lockstep — requirements, design and tasks — one of which is under a live approval card. **This specification costs the same three cards and touches no approved text.** The cross-reference exists so a later reader does not meet this as a duplicate or a leftover.

### What measuring it before writing it destroyed

**The idea began as a symmetry: one walk over both lists, finding a rule nothing emits and a class no rule claims. Measurement killed half of it.**

- **The inverse direction — a rule whose class is never emitted — finds nothing.** It is the direction the guard was named for, and it is empty. It is kept because it is free and catches a rule orphaned by a rename, and **R5 requires its emptiness to be stated so that its silence is never read as coverage.**
- **The forward direction would be red on arrival.** A guard that failed on any unruled class would fail from the moment it landed, on a scale that would get it deleted rather than fixed.

**Every count in this document is an upper bound from a method that was caught being wrong three times**, and no criterion below depends on one. A quote-anchored pattern missed every class not first in its attribute and reported `library-canvas-surface` as a *dead rule* while the line emitting it had already been read. Correcting that found ten more. Spot-checking then showed `library-element` counted as emitted **only because a documentation string inside a `.tsx` file contains `.library-element-label { text-anchor: middle; }`** — a comment, not an emission. **The implementation reports what the parse finds; the greps' numbers appear here as history, not as targets.**

## Alignment with Product Vision

`tech.md` requires styling to be centralised rather than inline, and the canvas library exists so that many diagram types share one drawing surface. **A class the library emits is part of that surface's contract**, and a contract nobody has read is how the same defect arrives twice.

## Requirements

### Requirement 1: Every emitted class is accounted for

**User Story:** As a maintainer, I want a class the library emits to be either styled or deliberately listed, so that a class with nothing behind it cannot ship unnoticed.

#### Acceptance Criteria

1. WHEN the library emits a class THEN the guard SHALL require it to be either matched by a rule in the tree's stylesheets or present in a checked-in list of deliberately unruled classes.
2. WHEN a class is neither ruled nor listed THEN the guard SHALL fail, naming that class.
3. WHEN a class is removed from the library THEN a list entry that no longer applies SHALL fail, so the list cannot accumulate entries for classes that no longer exist.
4. The guard SHALL be green when this specification's work is complete, and **a guard that could not be green would be a finding about this design rather than about the code.**

### Requirement 2: The two sets are parsed, never pattern-matched

**User Story:** As the next implementer, I want the extraction method fixed by a criterion, so that the cheap method cannot quietly replace it and make the guard confidently wrong.

#### Acceptance Criteria

1. The emitted set SHALL be obtained by **parsing `className` attributes** in the library's components, and SHALL NOT be obtained by a regular expression over source text.
2. A class name appearing in a comment, a documentation example or any other string that is not a `className` attribute SHALL NOT count as emitted. **`library-element` is the worked example: it appears only inside a doc comment showing a CSS rule.**
3. A class not first in its attribute SHALL be extracted. **`canvas-drawing library-canvas-surface` is the worked example**, and the pattern that missed it reported a live class as a dead rule.
4. The ruled set SHALL be obtained from the stylesheets' selectors with comments excluded, so that a class named in a CSS comment does not count as ruled.

### Requirement 3: The extractor proves it is alive before the inventory is believed

**User Story:** As a reviewer, I want an empty extraction to fail loudly, because a parser that finds nothing reports a clean inventory and a clean inventory is what a healthy tree looks like.

#### Acceptance Criteria

1. The guard SHALL assert that a known set of classes is found **from the emitted side** before reporting any inventory result, and SHALL fail rather than report when they are not.
2. The guard SHALL assert that a known set of classes is found **from the ruled side**, for the same reason and separately.
3. Both assertions SHALL be **seen to fail** against a deliberately emptied extraction, and the message each produced SHALL be recorded in the implementation log.
4. The liveness assertions SHALL be sensitive to the extractor breaking and insensitive to the inventory being legitimately empty in one direction, **so that R5's empty inverse walk never trips them.**

### Requirement 4: The reasons are written by a reader

**User Story:** As a maintainer meeting this list in a year, I want each entry's reason to be something a person concluded, so that the list is evidence rather than decoration.

#### Acceptance Criteria

1. Each entry in the deliberately-unruled list SHALL carry a one-line reason saying why that class needs no rule.
2. Reasons SHALL NOT be generated. **An automatically produced reason is a reason nobody read**, and a list of them would pass this guard while restoring exactly the condition it exists to detect.
3. WHEN a reason cannot be written truthfully for an emitted class THEN that SHALL be recorded as a finding rather than worked around, **because an unruled emitted class nobody can justify is this defect's sibling.**
4. The seeding pass SHALL report how many entries were written and how many became findings, so the work that produced the list is visible rather than implied.

### Requirement 5: The inverse walk is kept, and its emptiness is stated

**User Story:** As a reader, I want to know that the inverse direction currently finds nothing, so that I do not mistake its silence for coverage.

#### Acceptance Criteria

1. WHEN a stylesheet rule names a class the library never emits THEN the guard SHALL fail, naming the rule.
2. The specification and the guard SHALL state that this direction is **empty at the time of writing** and is kept for the case of a rule orphaned by a rename.
3. The inverse walk SHALL NOT be used as evidence of coverage in any report; a green inverse walk means only that no orphaned rule was found.

### Requirement 6: The boundary between library and module is stated and enforced

**User Story:** As a module author adding a class, I want to know whether I am inside this inventory, so that the first person to hit the boundary does not have to guess.

#### Acceptance Criteria

1. The design SHALL state which classes are in scope, and the guard SHALL enforce exactly that set rather than a looser or wider one.
2. WHEN a class is emitted by a diagram module rather than by the library THEN the guard's treatment of it SHALL follow from the stated boundary rather than from where the class happens to be declared.
3. The boundary SHALL be discoverable from `docs/creating-a-diagram-module.md` or from the guard's own failure message, so a module author meets it at the moment it applies to them.

## Non-Functional Requirements

### Cost and placement

- The guard SHALL run in the client suite, with the other client guards, and SHALL NOT require a browser or an example corpus.
- The guard SHALL be fast enough to run on every gate. **The self-test's twenty-one minutes are the counter-example this project has just paid for.**

### Measurement discipline

- **Counts in this document are upper bounds from a discredited method** and SHALL NOT be used as acceptance figures. The implementation reports what the parse finds.
- **The seeding is the deliverable rather than the cost.** The guard's later runs preserve what the seeding discovers; they do not discover anything themselves.

## Out of scope

Classes emitted by modules, unless R6's boundary places them in scope; the appearance or correctness of any rule, as opposed to its existence; CSS custom properties, which are a different question with a different shape; and any change to `canvas-single-scrollbar`, whose documents are approved and stay untouched.
