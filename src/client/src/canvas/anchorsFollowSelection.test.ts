import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";

/**
 * An anchor is coloured like the element it belongs to: at rest, and when selected.
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
    const selectedBox = variableOf(blockFor(".canvas-selected .canvas-node"), "stroke");
    expect(selectedBox).not.toBeNull();

    for (const anchor of ANCHORS) {
      expect(variableOf(blockFor(`.canvas-selected ${anchor}`), "stroke"), `${anchor} when selected`).toBe(selectedBox);
    }
  });

  it("reads the two states as different, whatever the theme calls them", () => {
    // The canary. Both assertions above compare an anchor against a box, so a stylesheet that
    // gave BOTH states one colour would satisfy them and still show nothing. This is the only
    // claim here that does not depend on the other rules being right.
    expect(variableOf(blockFor(".canvas-node"), "stroke")).not.toBe(variableOf(blockFor(".canvas-selected .canvas-node"), "stroke"));
  });
});
