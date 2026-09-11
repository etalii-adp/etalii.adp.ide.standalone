import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";

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
 * ## What "the element's colour" is, since centralized-selection
 *
 * Selected used to be a recoloured stroke on the box, `.canvas-selected .canvas-node`. It is now
 * a ring the library draws outside the element, `library-selected-outline`, and accept is a
 * second ring, `library-accept-outline` (that specification's Requirements 5 and 6). So the
 * anchor's selected colour is compared with the selected RING's, and its accept colour with the
 * accept ring's - the same relationship, read from where each look now lives.
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

  const ANCHORS = [".canvas-anchor", ".library-anchor", ".library-span-anchor"] as const;

  it("is drawn in the same colour as an unselected box, not the highlight colour", () => {
    // THE DEFECT ITSELF. Every one of these was the selected colour at rest.
    const box = variableOf(blockFor(".canvas-node"), "stroke");
    expect(box).not.toBeNull();

    for (const anchor of ANCHORS) {
      expect(variableOf(blockFor(anchor), "stroke"), `${anchor} at rest`).toBe(box);
    }
  });

  it("takes the highlight colour when its element is selected", () => {
    // The other half, and the reason the first half is safe: losing the green entirely would
    // pass the test above and fail this one.
    const selectedRing = variableOf(blockFor(".library-selected-outline"), "stroke");
    expect(selectedRing).not.toBeNull();

    for (const anchor of ANCHORS) {
      expect(variableOf(blockFor(`.canvas-selected ${anchor}`), "stroke"), `${anchor} when selected`).toBe(selectedRing);
    }
  });

  it("takes the accept colour when its element is a drop target", () => {
    const acceptRing = variableOf(blockFor(".library-accept-outline"), "stroke");
    expect(acceptRing).not.toBeNull();

    for (const anchor of ANCHORS) {
      expect(variableOf(blockFor(`.library-connect-target ${anchor}`), "stroke"), `${anchor} under a drop target`).toBe(acceptRing);
    }
  });

  it("reads the three states as different, whatever the theme calls them", () => {
    // The canary. The assertions above compare an anchor against a ring, so a stylesheet that
    // gave two states one colour would satisfy them and still show nothing. This is the only
    // claim here that does not depend on the other rules being right.
    const rest = variableOf(blockFor(".canvas-node"), "stroke");
    const selected = variableOf(blockFor(".library-selected-outline"), "stroke");
    const accept = variableOf(blockFor(".library-accept-outline"), "stroke");
    expect(new Set([rest, selected, accept]).size, `rest ${rest}, selected ${selected}, accept ${accept}`).toBe(3);
  });
});
