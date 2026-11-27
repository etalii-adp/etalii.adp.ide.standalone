import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import path from "node:path";
import { contrastRatio, resolveToken, themeTokens } from "./themeContrast";

/**
 * The explorer tells a registered tool document from a file a tool could open by the colour of its icon alone
 * (small-refinements Requirement 3): registered wears `--color-primary`, potential `--color-diagram-potential`.
 * The spec 093 host test met the two as nearly one green in the dark theme (limegreen against #3f9142, 1.86:1),
 * so a new user could not see which files were already tools. Both modes are held to two lines: the two states
 * at least {@link APART}:1 from each other, and potential at least {@link VISIBLE}:1 on the surface it is drawn on,
 * the WCAG 2.x line for a graphical object.
 */
describe("the explorer's two tool states are told apart by colour", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const tokens = themeTokens(readFileSync(path.join(here, "index.css"), "utf8"));

  /** How far apart the two states' colours are, at the least. */
  const APART = 2;

  /** WCAG 2.x 1.4.11: a graphical object against what is next to it. */
  const VISIBLE = 3;

  /** The CSS colour names the theme uses for these tokens, as hex: the ratio is computed over hex alone. */
  const NAMED: Readonly<Record<string, string>> = { limegreen: "#32cd32" };

  it("keeps registered and potential apart, and potential visible on the surface, in both modes", () => {
    for (const mode of ["light", "dark"] as const) {
      const [registered, potential, surface] = ["--color-primary", "--color-diagram-potential", "--color-surface"].map((name) => {
        const value = resolveToken(tokens[mode], name);
        expect(value, `${mode} declares ${name}`).toBeDefined();
        return NAMED[value!] ?? value!;
      });

      expect(contrastRatio(registered!, potential!), `${mode}: registered ${registered} against potential ${potential}`).toBeGreaterThanOrEqual(APART);
      expect(contrastRatio(potential!, surface!), `${mode}: potential ${potential} on the surface ${surface}`).toBeGreaterThanOrEqual(VISIBLE);
    }
  });
});
