import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import path from "node:path";
import { contrastRatio, resolveToken, themeTokens } from "./themeContrast";

/**
 * The element-fill tokens, against the theme's own text, in both modes.
 *
 * <b>Two lines with different subjects, and both are taken unrounded.</b> Every fill the contract
 * admits must clear **4.5:1**, which is WCAG 2.x AA for normal-size text. The five fills shipped
 * for the dark theme must clear **5.0:1** as well - the margin that exists so a value cannot fall
 * below AA on the next small edit, or on somebody else's rounding. A failure says which of the two
 * lines it crossed, because "contrast too low" sends a reader to the wrong question when the colour
 * is legible and merely has no margin left.
 *
 * <b>The reference colour is `--color-text`</b>, resolved per mode from `index.css` - `#0f172a` in
 * light and `#f1f5f9` in dark. It is not white, and that distinction is load-bearing: `#7c7203`
 * reads 4.4942 against `#f1f5f9` and 4.92 against white, which is how two sessions computed
 * different numbers for the same colour and one of them adopted it.
 *
 * <b>The formula</b> is WCAG 2.x: each sRGB channel is divided by 255 and linearised as
 * `c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4`, the relative luminance is
 * `0.2126 R + 0.7152 G + 0.0722 B`, and the ratio is `(lighter + 0.05) / (darker + 0.05)`. It is
 * imported from `themeContrast.ts` rather than restated here, deliberately: an acceptance number
 * given as a ratio is only meaningful if everyone computes it the same way, and two
 * implementations of `contrastRatio` are two acceptance criteria wearing one name.
 *
 * <b>Why the tokens rather than something rendered.</b> jsdom applies no CSS, so a contrast
 * assertion over a mounted component measures nothing and passes green - the worst shape a guard
 * can have. Both modes declare literal hex, so the number a browser would compute is computable
 * from the source.
 */
describe("a diagram fill stays readable under the theme's text", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const css = readFileSync(path.join(here, "index.css"), "utf8");
  const tokens = themeTokens(css);

  /** WCAG 2.x AA for normal-size text: the line any admitted fill must clear. */
  const AA = 4.5;

  /** The margin the specification's own requirement asks the dark values to be chosen for. */
  const MARGIN = 5;

  /**
   * The fill tokens, taken from the stylesheet by their prefix rather than from a list written
   * here. A sixth fill added to the family is then checked the day it is declared, and a renamed
   * one makes this guard say "found none" loudly instead of passing over an empty set - which is
   * why the count is asserted before any ratio is taken.
   *
   * `--color-diagram-potential` is deliberately outside the family: it paints TEXT on the surface
   * rather than a fill behind text, so a ratio against `--color-text` is not the question about it.
   */
  const PREFIX = "--color-diagram-fdg-";

  function fillsOf(mode: "light" | "dark"): readonly [string, string][] {
    const declared = [...tokens[mode].keys()].filter((name) => name.startsWith(PREFIX));
    const resolved = declared.map((name) => [name, resolveToken(tokens[mode], name)!] as [string, string]);

    // The liveness proof. An instrument reporting "nothing wrong" has to show it was looking.
    expect(resolved.length, `${mode} declares the ${PREFIX}* fills`).toBe(5);
    return resolved;
  }

  function textOf(mode: "light" | "dark"): string {
    const text = resolveToken(tokens[mode], "--color-text");
    expect(text, `${mode} declares --color-text`).toBeDefined();
    return text!;
  }

  it("clears the WCAG AA line of 4.5:1 in both modes, for every fill the contract admits", () => {
    for (const mode of ["light", "dark"] as const) {
      const text = textOf(mode);
      for (const [name, fill] of fillsOf(mode)) {
        const ratio = contrastRatio(fill, text);
        expect(
          ratio,
          `${name} is ${fill} in ${mode} mode, at ${ratio.toFixed(4)}:1 against --color-text ${text} - below the WCAG AA line of ${AA}:1`,
        ).toBeGreaterThanOrEqual(AA);
      }
    }
  });

  it("clears the 5.0:1 margin for the five dark fills, which is what the margin is for", () => {
    // The light fills are the user's own hex and are not held to this line: they run 11.96 to
    // 15.44 to 1 as given, so there is nothing to choose. The dark values ARE chosen, and they are
    // chosen to this number - the lightest same-hue variants reaching only 4.5 were measured at
    // 4.4942 to 4.5327, with two of them not reaching 4.5 at all.
    const text = textOf("dark");
    for (const [name, fill] of fillsOf("dark")) {
      const ratio = contrastRatio(fill, text);
      expect(
        ratio,
        `${name} is ${fill}, at ${ratio.toFixed(4)}:1 against --color-text ${text} - above WCAG AA but below the ${MARGIN}:1 margin this specification requires`,
      ).toBeGreaterThanOrEqual(MARGIN);
    }
  });

  describe("the hype cycle's trigger and note", () => {
    /** WCAG 2.x's line for a graphical object - a circle, not text - against what it is drawn on. */
    const GRAPHICAL = 3;

    function resolved(mode: "light" | "dark", name: string): string {
      const colour = resolveToken(tokens[mode], name);
      expect(colour, `${mode} declares ${name}`).toBeDefined();
      return colour!;
    }

    for (const mode of ["light", "dark"] as const) {
      it(`draws a trigger that stands out from the canvas in ${mode} mode`, () => {
        const trigger = resolved(mode, "--color-diagram-hype-trigger");
        for (const ground of ["--color-bg", "--color-surface"]) {
          const ratio = contrastRatio(trigger, resolved(mode, ground));
          expect(ratio, `the trigger (${trigger}) on ${ground} in ${mode} mode is ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(GRAPHICAL);
        }
      });

      it(`writes a note's text readably on its box in ${mode} mode`, () => {
        const text = resolved(mode, "--color-diagram-hype-note-text");
        const box = resolved(mode, "--color-diagram-hype-note");
        const ratio = contrastRatio(text, box);
        expect(ratio, `the note's text (${text}) on its box (${box}) in ${mode} mode is ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(AA);
      });
    }
  });

  it("declares the user's light hex exactly, none of it adjusted for contrast", () => {
    // The user ruled the light values are theirs verbatim. Pinning them is the point: a later
    // reader tuning all ten for contrast would be undoing a decision rather than fixing a defect.
    expect(Object.fromEntries(fillsOf("light"))).toEqual({
      "--color-diagram-fdg-ui-element": "#aaed92",
      "--color-diagram-fdg-data-element": "#ededed",
      "--color-diagram-fdg-action": "#9edcfa",
      "--color-diagram-fdg-function": "#86e6d9",
      "--color-diagram-fdg-comment": "#fcf281",
    });
  });
});
