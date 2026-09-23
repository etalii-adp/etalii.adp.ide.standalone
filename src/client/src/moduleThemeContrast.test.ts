import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import path from "node:path";
import { contrastRatio, resolveToken, themeTokens, tokensRead } from "./themeContrast";

/**
 * Two modules painted themselves out of the theme, and a user could see both.
 *
 * `azure-pipeline` read ten `--adp-*` names and `causal-loop` five `--vscode-*` names -
 * <b>and neither namespace was defined anywhere in the tree</b>, so every one of those
 * `var()`s had always resolved to its hard-coded fallback, identically in both themes. The
 * modules were not mis-themed; they were theme-blind. azure painted a light stage on the dark
 * app, and causal-loop painted a near-black variable pill under the light theme's dark label.
 *
 * <b>Why this is a token-level check and not a rendered one.</b> jsdom applies no CSS, so a
 * contrast assertion over a mounted canvas measures nothing and passes - it would have been
 * green against the very defect it was written for. Both themes declare literal hex, so the
 * ratio a browser would compute is computable from the source, which is the only instrument
 * here that can be wrong in a way anybody notices.
 *
 * <b>Why a label's colour is ruled out structurally before any ratio is taken.</b> The tasks
 * document records the light-theme label at 1.37:1, which is pure `#000000` on the pill - and
 * SVG `fill` inherits, so a label with no rule of its own takes black from the canvas
 * `<svg>`. The route to that black was never found: the variable label declares
 * `canvas-node-label`, the library paints that `var(--color-text)`, and the badge's three
 * polarity words each have a fill. <b>But black cannot be made to pass by any choice of pill
 * colour</b> - mapping the fill to `--color-surface` puts black at 1.44:1 in the dark theme,
 * which is the defect relocated rather than removed, into the theme nobody is looking at.
 * So black is not a colour to measure here; it is a state to forbid. Every label class a
 * module declares must be painted by a rule, and only then is the ratio worth taking.
 */
describe("module colours honour the theme", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const repoSrc = path.resolve(here, "../..");
  const tokens = themeTokens(readFileSync(path.join(here, "index.css"), "utf8"));
  const libraryCss = readFileSync(path.join(here, "canvas", "canvas.css"), "utf8");

  /** WCAG AA for text. The acceptance number task 1 states. */
  const READABLE = 4.5;

  function moduleFile(module: string, file: string): string {
    return readFileSync(path.join(repoSrc, "diagrams", module, "client", file), "utf8");
  }

  /** The token a named rule paints a property from, looked for in the module then the library. */
  function paintedBy(sheets: string[], selector: string, property: string): string {
    const rule = new RegExp(`${selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\s*\\{[^}]*\\}`);
    const blocks = sheets.map((css) => rule.exec(css)).filter((found) => found !== null);
    expect(blocks.length, `a rule declares ${selector}`).toBeGreaterThan(0);

    const painted = blocks
      .map((block) => new RegExp(`${property}\\s*:\\s*var\\(\\s*(--[A-Za-z0-9_.-]+)`).exec(block![0]))
      .find((found) => found !== null);
    expect(painted, `${selector} paints its ${property} from a token, rather than inheriting`).not.toBeUndefined();

    return painted![1];
  }

  function resolvedIn(theme: "light" | "dark", token: string): string {
    const colour = resolveToken(tokens[theme], token);
    expect(colour, `${token} is declared for the ${theme} theme`).toBeDefined();

    return colour!;
  }

  describe("causal-loop's variable pill carries a readable label", () => {
    const css = moduleFile("causal-loop", "causal-loop.css");
    const canvas = moduleFile("causal-loop", "CausalLoopCanvas.tsx");
    const sheets = [css, libraryCss];

    it("declares the label with a class some stylesheet paints, so it cannot inherit black", () => {
      const declared = [...canvas.matchAll(/className:\s*"([a-z-]+)"/g)].map((match) => match[1]);
      expect(declared, "the variable label names a class").toContain("canvas-node-label");

      const token = paintedBy(sheets, ".canvas-node-label", "fill");
      expect(token.startsWith("--color-"), `${token} is a theme token`).toBe(true);
    });

    it("paints every polarity word the badge template can produce", () => {
      const words = [...canvas.matchAll(/return\s+"([a-z]+)";/g)].map((match) => match[1]);
      expect(words.length, "polarityWord returns a closed set of words").toBeGreaterThan(0);

      for (const word of words) {
        const token = paintedBy([css], `.causal-loop-${word}`, "fill");
        expect(token.startsWith("--color-"), `.causal-loop-${word} is painted from a theme token`).toBe(true);
      }
    });

    for (const theme of ["light", "dark"] as const) {
      it(`reads in the ${theme} theme`, () => {
        const fill = resolvedIn(theme, paintedBy(sheets, ".causal-loop-variable .canvas-node", "fill"));
        const label = resolvedIn(theme, paintedBy(sheets, ".canvas-node-label", "fill"));

        const ratio = contrastRatio(label, fill);
        expect(
          ratio,
          `the label (${label}) on the pill (${fill}) in the ${theme} theme is ${ratio.toFixed(2)}:1`,
        ).toBeGreaterThanOrEqual(READABLE);
      });
    }
  });

  describe("azure-pipeline's stage carries a readable name", () => {
    const sheets = [moduleFile("azure-pipeline", "azure-pipeline.css"), libraryCss];

    for (const theme of ["light", "dark"] as const) {
      it(`reads in the ${theme} theme`, () => {
        const fill = resolvedIn(theme, paintedBy(sheets, ".pipeline-stage-box", "fill"));
        const label = resolvedIn(theme, paintedBy(sheets, ".pipeline-stage-name", "fill"));

        const ratio = contrastRatio(label, fill);
        expect(
          ratio,
          `the stage name (${label}) on the stage box (${fill}) in the ${theme} theme is ${ratio.toFixed(2)}:1`,
        ).toBeGreaterThanOrEqual(READABLE);
      });
    }
  });

  /**
   * The cause behind both looks, stated directly rather than through a ratio: a private
   * namespace the theme never defines. `themeTokens.test.ts` walks `--color-*` today and task
   * 2 widens it to every custom property - until then this names the two namespaces that put
   * a defect in front of a user.
   */
  it("neither module reads a token the theme does not declare", () => {
    const offenders = [
      ["causal-loop", moduleFile("causal-loop", "causal-loop.css")],
      ["azure-pipeline", moduleFile("azure-pipeline", "azure-pipeline.css")],
    ].flatMap(([module, css]) =>
      [...new Set(tokensRead(css))]
        .filter((token) => resolveToken(tokens.light, token) === undefined)
        .map((token) => `${module} reads ${token}`),
    );

    expect(offenders).toEqual([]);
  });
});
