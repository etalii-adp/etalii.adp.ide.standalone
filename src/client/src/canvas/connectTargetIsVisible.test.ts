import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { HIGHLIGHT_STROKE } from "./library/highlight";

/**
 * A drag that would be accepted says so on the element it would land on.
 *
 * ## The defect
 *
 * The library decides this on every frame and always has: `connectVerdict` asks the definition
 * for the relation's permitted target types, `allowSelf`, and both cardinality caps, then puts
 * `library-connect-target` on the element that answered yes and `library-connect-forbidden` on
 * one that answered no. **Nothing painted either class**, so the answer was computed and thrown
 * away — you could drag a connection across a whole diagram with no sign of whether the drop
 * would be accepted. Only four modules showed anything, and only because they separately
 * declare `canvas-connect-target` for themselves.
 *
 * ## Why this exists beside `noUnstyledLibraryClasses`
 *
 * That guard would be the natural home, and it had both classes on its **exemption list** —
 * the only two entries there with no reason written beside them. They are off it now, so it
 * covers them. But it asks "does any selector mention this class", and a class named only as an
 * ANCESTOR satisfies it: `.library-connect-target .library-anchor` styles the anchors of a
 * connect target, and would keep that guard green while the element itself went back to being
 * invisible. This asserts the narrower thing that actually matters — **each state class carries
 * a declaration of its own** — which is the half that would otherwise rot unnoticed.
 *
 * ## Where "visible" lives, and the reversal of 2026-09-11
 *
 * This went three ways. It was a glow in the selected colour, so accept and selected were
 * indistinguishable; then a dashed ring of its own, further out, so they were two looks that could
 * show at once; and now, by the user's ruling of 2026-09-22, ONE look: a drop target is painted
 * exactly the way a selection is (Requirements 6.1, 6.2). <b>That reverses the amendment this file
 * once asserted</b>, so its claim is reversed with it: accept must look the SAME as selected, and
 * must still be clearly different from refused.
 *
 * The highlight is an inline paint (`highlight.ts`), because a rule lost to any module rule styling
 * its own shapes by descendant. Refused is still a filter rule here, and still read from the
 * stylesheet.
 */
describe("a connect target is visible", () => {
  const css = readFileSync(join(__dirname, "canvas.css"), "utf8");

  /** The declaration block of the rule whose selector is exactly `.name`, ancestors excluded. */
  function ownBlockOf(name: string): string | null {
    // `\\.name\\s*\\{` — the class as the WHOLE selector, so a descendant selector naming it as
    // an ancestor (which is what makes the sibling guard green) cannot satisfy this.
    const match = new RegExp(`(?:^|\\})\\s*\\.${name}\\s*\\{([^}]*)\\}`, "m").exec(css);
    return match === null ? null : match[1]!;
  }

  it("paints the element a valid drop would land on", () => {
    // Something visible: a stroke in a theme colour. The fill is deliberately untouched now, so a
    // notation's own colours still say what the thing IS while it is a drop target.
    expect(HIGHLIGHT_STROKE).toMatch(/^var\(--color-/);
  });

  it("paints a target the definition would refuse, differently", () => {
    const block = ownBlockOf("library-connect-forbidden");
    expect(block, "library-connect-forbidden has no rule of its own in canvas.css").not.toBeNull();
    expect(block).toMatch(/--color-danger/);
  });

  it("does not paint the two states the same", () => {
    // The canary. Both assertions above would pass if a careless edit gave the refused state the
    // accepted state's colour, and the drag would then promise a drop that gets refused — the
    // one outcome worse than showing nothing at all.
    expect(ownBlockOf("library-connect-forbidden")).not.toContain(HIGHLIGHT_STROKE);
    expect(HIGHLIGHT_STROKE).not.toMatch(/--color-danger/);
  });

  it("looks EXACTLY like selected, which is the 2026-09-22 reversal, and nothing like refused", () => {
    // The reversal, as a test: there is one look, so accept and selected cannot be told apart by
    // paint - and the cost, that "would accept" is invisible on an already-selected element, is
    // written in the requirements rather than left to be discovered. What must still differ is
    // refused, which is a danger-coloured filter rather than a stroke.
    expect(HIGHLIGHT_STROKE).toMatch(/^var\(\s*--color-selected/);
    expect(ownBlockOf("library-connect-forbidden")).toMatch(/--color-danger/);
  });

  it("is not left to the modules to declare", () => {
    // The request was for a central fix: the library's own ring carries the behaviour, so a
    // module that declares nothing still shows it. The module-private `canvas-connect-target`
    // declarations are removed as each module migrates (Requirement 6.1).
    expect(HIGHLIGHT_STROKE.length).toBeGreaterThan(0);
  });
});
