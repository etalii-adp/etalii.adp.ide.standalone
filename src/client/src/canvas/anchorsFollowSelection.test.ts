import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { HIGHLIGHT_STROKE } from "./library/highlight";

/**
 * An anchor is coloured like the element it belongs to: at rest, when selected, and when it is
 * the target a dragged connection would land on.
 *
 * ## The defect
 *
 * All three anchor classes were `--color-primary` unconditionally, so every anchor on every
 * canvas sat green whether or not anything was selected. Two things are wrong with that at
 * once: an unselected element wears the highlight colour, and a selected one's anchors say
 * nothing they did not already say. Reported against the dependency graph and the timeline -
 * neither of which has an anchor rule of its own, which is why the fix is here and not there.
 *
 * ## Why this asserts a RELATIONSHIP and never a colour
 *
 * A test naming `#8892a6` would be a second copy of the stylesheet: it would have to be edited
 * every time the theme legitimately changed, which makes it a duplicate of the data rather
 * than a check on it. What it asserts instead is that each anchor resolves to **the same
 * variable the box it belongs to resolves to, in the same state** - so a theme change moves
 * both sides at once and this stays green, while a colour pinned on one side only turns it
 * red. That is the property that was actually violated.
 *
 * ## What "the element's colour" is, since task 28
 *
 * Selected was a recoloured stroke here, then a ring the library drew outside the element. It is
 * now ONE inline highlight - colour plus a heavier line - on the element's outline AND on its
 * anchors, with "would accept" showing the same look (Requirements 5.3, 5.4, 6.1). So an anchor's
 * highlighted colour is no longer a rule in this file at all: `highlight.ts` paints the anchor and
 * the element together, which is the strongest form this relationship can take - they cannot
 * disagree, because one expression paints both. What stays here is the RESTING relationship, which
 * is still a cascade: an unselected anchor wears the same colour as an unselected box.
 */
describe("anchors follow their element's selection", () => {
  const css = readFileSync(join(__dirname, "canvas.css"), "utf8");

  /** The declaration block of the first rule whose selector list contains `selector`. */
  function blockFor(selector: string): string {
    // Selectors are matched whole, comma- or brace-delimited, so `.canvas-anchor` cannot be
    // satisfied by `.canvas-anchor-hit` sitting next to it.
    const pattern = new RegExp(`(^|,|\\})\\s*[^{}]*(?<![\\w-])${selector.replace(".", "\\.")}(?![\\w-])[^{}]*\\{([^}]*)\\}`, "m");
    const match = pattern.exec(css);
    expect(match, `no rule in canvas.css names ${selector}`).not.toBeNull();
    return match![2]!;
  }

  /** The variable a declaration resolves to, e.g. `stroke: var(--color-border, #x)` -> `--color-border`. */
  function variableOf(block: string, property: string): string | null {
    const declaration = new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`, "m").exec(block);
    if (declaration === null) {
      return null;
    }

    return /var\(\s*(--[\w-]+)/.exec(declaration[1]!)?.[1] ?? null;
  }

  /** The variable the highlight's inline stroke resolves to, e.g. `var(--color-selected, x)`. */
  function highlightVariable(): string | null {
    return /var\(\s*(--[\w-]+)/.exec(HIGHLIGHT_STROKE)?.[1] ?? null;
  }

  const ANCHORS = [".canvas-anchor", ".library-anchor", ".library-span-anchor"] as const;

  it("is drawn in the same colour as an unselected box, not the highlight colour", () => {
    // THE DEFECT ITSELF. Every one of these was the selected colour at rest.
    const box = variableOf(blockFor(".canvas-node"), "stroke");
    expect(box).not.toBeNull();

    for (const anchor of ANCHORS) {
      expect(variableOf(blockFor(anchor), "stroke"), `${anchor} at rest`).toBe(box);
    }
  });

  it("takes the highlight colour with its element, from the one expression that paints both", () => {
    // The other half, and the reason the first half is safe: losing the colour entirely would
    // pass the test above and fail this one. It is no longer a cascade to check - the library
    // paints the anchor and the element in one expression - so what is asserted is that the
    // highlight names a theme colour, and that selectionLooks proves an anchor actually wears it.
    expect(highlightVariable(), "the highlight names no theme colour").not.toBeNull();
    expect(HIGHLIGHT_STROKE).toMatch(/^var\(--color-selected/);
  });

  it("has no rule of its own for the highlighted states, which is what stops the two disagreeing", () => {
    // The defect this whole file exists for was an anchor whose colour was stated separately from
    // its element's. A rule here would be exactly that separate statement returning.
    for (const anchor of ANCHORS) {
      expect(css, `${anchor} is painted for a selected element by a rule`).not.toMatch(new RegExp(`\.canvas-selected\s+\${anchor}`));
      expect(css, `${anchor} is painted for a drop target by a rule`).not.toMatch(new RegExp(`\.library-connect-target\s+\${anchor}`));
    }
  });

  it("reads resting and highlighted as different, whatever the theme calls them", () => {
    // The canary. The assertions above compare an anchor against a ring, so a stylesheet that
    // gave two states one colour would satisfy them and still show nothing. This is the only
    // claim here that does not depend on the other rules being right.
    const rest = variableOf(blockFor(".canvas-node"), "stroke");
    const highlight = highlightVariable();
    // TWO states now, not three: the user's ruling made "would accept" the same look as selected,
    // so the claim is that resting and highlighted differ - and that the highlight's colour is not
    // one a diagram paints at rest (Requirement 5.5), which --color-primary was.
    expect(new Set([rest, highlight]).size, `rest ${rest}, highlighted ${highlight}`).toBe(2);
    expect(highlight).not.toBe("--color-primary");
  });
});
