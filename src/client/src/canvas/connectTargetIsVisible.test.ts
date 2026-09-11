import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";

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
 * ## Where "visible" lives, since centralized-selection
 *
 * The accept look was a glow on `library-connect-target`, in the selected colour — so accept and
 * selected looked the same, and an element that was both could not show both. It is now a ring
 * the library draws for that state, `library-accept-outline`: dashed, further out than the
 * selected ring, in a colour of its own (that specification's Requirements 6.1 and 6.2). So this
 * asks the ring for its paint, and adds the claim the amendment made: accept is not selected.
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
    const block = ownBlockOf("library-accept-outline");
    expect(block, "library-accept-outline has no rule of its own in canvas.css").not.toBeNull();
    // Something visible: a stroke in a theme colour, on a ring that is not filled over the element.
    expect(block).toMatch(/stroke:\s*var\(--color-/);
    expect(block).toMatch(/fill:\s*none/);
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
    expect(ownBlockOf("library-accept-outline")).not.toEqual(ownBlockOf("library-connect-forbidden"));
    expect(ownBlockOf("library-accept-outline")).not.toMatch(/--color-danger/);
  });

  it("does not look like selected - a different colour, and dashed where selected is not", () => {
    // Requirement 6.1: two meanings, two looks. Until centralized-selection both were the
    // primary colour on the same element, so "would accept" read as "is selected".
    const colourOf = (block: string | null) => /stroke:\s*var\(\s*(--[\w-]+)/.exec(block ?? "")?.[1];
    const accept = ownBlockOf("library-accept-outline");
    const selected = ownBlockOf("library-selected-outline");
    expect(selected, "library-selected-outline has no rule of its own in canvas.css").not.toBeNull();
    expect(colourOf(accept)).not.toBe(colourOf(selected));
    expect(accept).toMatch(/stroke-dasharray/);
    expect(selected).not.toMatch(/stroke-dasharray/);
  });

  it("is not left to the modules to declare", () => {
    // The request was for a central fix: the library's own ring carries the behaviour, so a
    // module that declares nothing still shows it. The module-private `canvas-connect-target`
    // declarations are removed as each module migrates (Requirement 6.1).
    expect(ownBlockOf("library-accept-outline")).not.toBeNull();
  });
});
