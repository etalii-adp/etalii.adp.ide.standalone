import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import path from "node:path";
import { contrastRatio, resolveToken, themeTokens, tokensRead } from "./themeContrast";

/**
 * Two modules painted themselves out of the theme, and a user could see both.
 *
 * `azure-devops-pipeline` read ten `--adp-*` names and `causal-loop` five `--vscode-*` names -
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
 * <b>Why a label's colour is ruled out structurally before any ratio is taken - and the story
 * is worth keeping, because the wrong answer here was very convincing.</b>
 *
 * The tasks document recorded the light-theme label at 1.37:1, which is pure `#000000` on the
 * pill. Reading the source said `#0f172a` and 1.17:1: the label declares `canvas-node-label`,
 * the library paints that `var(--color-text)`, and nothing sets an inline fill when the
 * declaration names no colour. Two instruments, both working, disagreeing - so the guard was
 * written to forbid black rather than to measure it, because <b>no choice of pill colour
 * rescues a black label</b>: mapping the fill to `--color-surface` puts black at 1.44:1 in the
 * dark theme, relocating the defect into the theme nobody is looking at.
 *
 * <b>The disagreement then resolved, and neither instrument was faulty.</b> Re-measured live
 * and grouped by class, the canvas holds EIGHT unclassed `<text>` nodes computing
 * `rgb(0,0,0)` - and all eight are EMPTY. They are the empty label at `DiagramCanvas.tsx:2449`,
 * rendered for every element whose type declares `labels`. The original probe took a text node
 * without scoping to the label's class and measured one of those. <b>The label was never
 * black; the real figure was 1.17:1, and the defect was slightly worse than recorded.</b>
 *
 * So the black candidate is gone, and the assertion that forbids it stays anyway. Eight
 * unclassed black text nodes per canvas is a live trap for anyone measuring by eye or by
 * probe - it caught the author of the specification, and then it caught the first mounted
 * assertion written against it. <b>A guard that catches the trap is worth more than one that
 * catches only the fill.</b> Every label class a module declares must be painted by a rule,
 * and only then is the ratio worth taking.
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
    const css = moduleFile("causal-loop-diagram", "causal-loop.css");
    const canvas = moduleFile("causal-loop-diagram", "CausalLoopCanvas.tsx");
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

  describe("azure-devops-pipeline's stage carries a readable name", () => {
    const sheets = [moduleFile("azure-devops-pipeline", "azure-pipeline.css"), libraryCss];

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
   * gartner-hype-cycle-graph's phase fills against the chevron dividers drawn over them.
   *
   * A chevron is a stroke, not text, so the bar is WCAG's 3:1 for graphical objects rather than
   * the 4.5:1 above. Each fill is its own pair, because a divider sits between two phases and a
   * chevron that reads against three of four fills still vanishes into the fourth.
   */
  describe("gartner-hype-cycle-graph's chevrons read against every phase", () => {
    const GRAPHICAL = 3;

    for (const theme of ["light", "dark"] as const) {
      for (const phase of ["peak", "trough", "slope", "plateau"]) {
        it(`separates the ${phase} phase in the ${theme} theme`, () => {
          const fill = resolvedIn(theme, `--color-diagram-hype-${phase}`);
          const chevron = resolvedIn(theme, "--color-diagram-hype-chevron");
          const ratio = contrastRatio(chevron, fill);

          expect(ratio, `the chevron (${chevron}) on the ${phase} fill (${fill}) in the ${theme} theme is ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(GRAPHICAL);
        });
      }
    }
  });

  /**
   * supply-chain's seven stage hues: the header each card wears, and the band its flows run in.
   *
   * The module shipped its own palette and nobody looked at it in a browser. Manufacturer and
   * assembler were two violets 13 apart (CIE76), the assembler violet sat beside the selection
   * colour a selected card is outlined in, and supplier and raw material were two browns. A stage
   * is told apart by its hue alone, so the hues must be far apart - from each other and from the
   * selection - and the white stage title must read on every one. They are theme tokens, as every
   * diagram's per-kind hues are, so they sit in index.css beside the colours they must not be.
   */
  describe("supply-chain's stages are seven distinct, readable theme hues", () => {
    const css = moduleFile("supply-chain", "supply-chain.css");
    const stages = [...css.matchAll(/\.supply-chain-stage-([a-z-]+)\s*\{\s*--supply-chain-stage:\s*var\(\s*(--[A-Za-z0-9_.-]+)/g)]
      .map((match) => ({ stage: match[1], token: match[2] }));

    /** Far enough apart that two stages never read as one, by eye, at a glance (CIE76). */
    const DISTINCT = 20;

    function lab(colour: string): [number, number, number] {
      const hex = colour.trim().replace(/^#/, "");
      const linear = [0, 2, 4].map((at) => {
        const c = Number.parseInt(hex.slice(at, at + 2), 16) / 255;
        return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
      });
      const [r, g, b] = linear;
      const xyz = [
        (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047,
        0.2126 * r + 0.7152 * g + 0.0722 * b,
        (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883,
      ].map((t) => (t > 216 / 24389 ? Math.cbrt(t) : (24389 / 27 * t + 16) / 116));

      return [116 * xyz[1] - 16, 500 * (xyz[0] - xyz[1]), 200 * (xyz[1] - xyz[2])];
    }

    function deltaE(one: string, other: string): number {
      const [a, b] = [lab(one), lab(other)];
      return Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
    }

    it("maps every stage to a theme token", () => {
      expect(stages.map((found) => found.stage)).toEqual([
        "source", "processor", "producer", "integrator", "hub", "outlet", "consumer",
      ]);
      for (const { stage, token } of stages) {
        expect(token.startsWith("--color-diagram-supply-chain-"), `${stage} is painted from the theme`).toBe(true);
      }
    });

    for (const theme of ["light", "dark"] as const) {
      it(`keeps the stages apart, and away from the selection, in the ${theme} theme`, () => {
        const hues = stages.map(({ stage, token }) => ({ stage, colour: resolvedIn(theme, token) }));
        const selected = resolvedIn(theme, "--color-selected");
        const close: string[] = [];
        hues.forEach((one, at) => {
          for (const other of hues.slice(at + 1)) {
            const distance = deltaE(one.colour, other.colour);
            if (distance < DISTINCT) close.push(`${one.stage} and ${other.stage} are ${distance.toFixed(1)} apart`);
          }
          const distance = deltaE(one.colour, selected);
          if (distance < DISTINCT) close.push(`${one.stage} is ${distance.toFixed(1)} from the selection`);
        });

        expect(close).toEqual([]);
      });

      it(`reads the stage title on every stage in the ${theme} theme`, () => {
        const title = resolvedIn(theme, paintedBy([css], ".supply-chain-stage-title", "fill"));
        for (const { stage, token } of stages) {
          const ratio = contrastRatio(title, resolvedIn(theme, token));
          expect(ratio, `the title (${title}) on ${stage} in the ${theme} theme is ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(READABLE);
        }
      });
    }
  });

  /**
   * sankey's twelve palette words: each is the bar, the band and the LABEL of what it colours, so
   * every one must read as text on the surface, and two words must never read as one colour - an
   * author picks "slate" and "grey" to tell two things apart. The words are the backend's
   * `SankeyColors.Palette` and the client's `SANKEY_COLORS`, which the module's own test holds equal.
   */
  describe("sankey's palette is twelve distinct, readable theme colours", () => {
    const ids = moduleFile("sankey", "sankeyIds.ts");
    const words = JSON.parse(/SANKEY_COLORS[^=]*=\s*(\[[^\]]*\])/.exec(ids)![1].replace(/,\s*\]/, "]")) as string[];
    const DISTINCT = 20;

    function lab(colour: string): [number, number, number] {
      const hex = colour.trim().replace(/^#/, "");
      const [r, g, b] = [0, 2, 4].map((at) => {
        const c = Number.parseInt(hex.slice(at, at + 2), 16) / 255;
        return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
      });
      const xyz = [
        (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047,
        0.2126 * r + 0.7152 * g + 0.0722 * b,
        (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883,
      ].map((t) => (t > 216 / 24389 ? Math.cbrt(t) : (24389 / 27 * t + 16) / 116));
      return [116 * xyz[1] - 16, 500 * (xyz[0] - xyz[1]), 200 * (xyz[1] - xyz[2])];
    }

    function deltaE(one: string, other: string): number {
      const [a, b] = [lab(one), lab(other)];
      return Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
    }

    it("names twelve words", () => {
      expect(words).toHaveLength(12);
    });

    for (const theme of ["light", "dark"] as const) {
      it(`reads every word as text on the surface in the ${theme} theme`, () => {
        const surface = resolvedIn(theme, "--color-surface");
        for (const word of words) {
          const colour = resolvedIn(theme, `--color-diagram-sankey-${word}`);
          const ratio = contrastRatio(colour, surface);
          expect(ratio, `${word} (${colour}) on the surface (${surface}) in the ${theme} theme is ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(READABLE);
        }
      });

      it(`keeps the words apart, and away from the selection, in the ${theme} theme`, () => {
        const colours = words.map((word) => ({ word, colour: resolvedIn(theme, `--color-diagram-sankey-${word}`) }));
        const selected = resolvedIn(theme, "--color-selected");
        const close: string[] = [];
        colours.forEach((one, at) => {
          for (const other of colours.slice(at + 1)) {
            const distance = deltaE(one.colour, other.colour);
            if (distance < DISTINCT) close.push(`${one.word} and ${other.word} are ${distance.toFixed(1)} apart`);
          }
          const distance = deltaE(one.colour, selected);
          if (distance < DISTINCT) close.push(`${one.word} is ${distance.toFixed(1)} from the selection`);
        });

        expect(close).toEqual([]);
      });
    }
  });

  /**
   * The cause behind both looks, stated directly rather than through a ratio: a private
   * namespace the theme never defines.
   *
   * <b>This used to say `themeTokens.test.ts` walks `--color-*` "today", and that task 2 would
   * widen it "until then".</b> Task 2 has landed and that walk now covers every custom property,
   * so the sentence was three hours from describing a state that no longer held - the same shape
   * as the phantom line it was written beside. A "today" in a comment is a promise to come back,
   * and nobody does. This assertion stays because it names the two namespaces that put a defect
   * in front of a user, which the general walk cannot say.
   */
  it("neither module reads a token the theme does not declare", () => {
    const offenders = [
      ["causal-loop-diagram", moduleFile("causal-loop-diagram", "causal-loop.css")],
      ["azure-devops-pipeline", moduleFile("azure-devops-pipeline", "azure-pipeline.css")],
    ].flatMap(([module, css]) =>
      [...new Set(tokensRead(css))]
        .filter((token) => resolveToken(tokens.light, token) === undefined)
        .map((token) => `${module} reads ${token}`),
    );

    expect(offenders).toEqual([]);
  });
});
