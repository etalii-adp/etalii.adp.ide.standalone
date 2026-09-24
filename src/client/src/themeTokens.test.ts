import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { themeTokens, tokensRead } from "./themeContrast";

/**
 * A `var(--x)` naming a token nothing defines renders its fallback in every mode - or nothing at
 * all where there is no fallback - and looks perfectly fine in review. Three such phantoms reached
 * `develop` this way, found by two agents on three separate occasions, and one of them cost a
 * canvas its selection outline entirely:
 *
 * - `--color-accent` carried ansible's selection stroke and focus outline. Undefined and with
 *   no fallback there, a selected node rendered exactly like an unselected one.
 * - `--color-error` was a phantom synonym for the theme's real `--color-danger`, used in a
 *   module *and in the theme's own stylesheet* - which is how it survived so long.
 * - `--color-border-strong` never existed; helm's edges fell through to a hardcoded grey.
 *   <b>That one was closed by deleting the reference rather than defining the token</b>
 *   (`efa25112`), and the token itself only came into existence with task 1. The sentence above
 *   is kept in its past tense on purpose - it describes a state that no longer holds, and reading
 *   it as present cost a later agent three repeated claims.
 *
 * <b>The fallback is the reason these are invisible, not a mitigation.</b> Each author picked a
 * literal correct for the one mode they had in mind, so the bug shows only in the other theme,
 * or only on the one canvas nobody was looking at. So a fallback does not excuse an undefined
 * token here: the rule is that the token must exist.
 *
 * <b>Task 2 widened this from `--color-*` to EVERY custom property, because the namespace was
 * never what made a token safe.</b> Task 1 found ten `--adp-*` and five `--vscode-*` names defined
 * nowhere in the tree, each resolving to a hard-coded literal identically in both themes - a
 * defect a `--color-*` walk could not see by construction. A private prefix is not a smaller
 * promise; it is the same promise with the guard switched off.
 *
 * <b>A module may define its own palette, and `ansible-structure` is the pattern.</b> Its
 * `--ansible-play-hue-0..5` are declared on `.ansible-canvas` and redeclared under
 * `@media (prefers-color-scheme: dark)` - locally owned AND theme-aware. That is why the rule is
 * "resolves to a theme token, or to one this stylesheet declares itself" rather than "must be a
 * theme token": a per-kind palette is the module's business, and being undefined is not.
 *
 * <b>The canaries below are planted strings rather than planted files, and that is why they work.</b>
 * On its first run the local-palette canary failed: the declaration reader anchored on line start,
 * so `.y { --mine: #0f0; }` read as declaring nothing and its own palette came back as an
 * offender. <b>Every stylesheet in this repository puts declarations on their own lines</b> - so
 * the tree is a sample from which the failing input shape is absent BY CONVENTION, not by luck.
 * No amount of looking harder at the real files finds that; only a string nobody would write does.
 * A compact or minified sheet would have had its palette reported undefined forever, and nothing
 * in the tree would ever have said so. The same holds for the both-modes detector further down,
 * which on its first run accused every correct palette in the tree including `ansible-structure`'s:
 * <b>a detector whose first failure accuses the known-good is the good failure - the one to fear
 * accuses nobody.</b>
 *
 * Scope is deliberate and covers `src/client/src` as well as the diagram and editor modules.
 * `--color-error` was used by `index.css` itself; a guard that trusted the theme to be
 * self-consistent would have missed it. TypeScript sources are walked too, with comments
 * stripped first: the library sets colours inline from `var()` strings, and a doc comment
 * mentioning `var(--x, fallback)` is prose rather than a usage.
 */
describe("theme colour tokens", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const clientSrc = here;
  const repoSrc = path.resolve(here, "../..");

  /** Every custom property the theme defines, in either mode. */
  const themeCss = readFileSync(path.join(clientSrc, "index.css"), "utf8");
  const theme = themeTokens(themeCss);
  const defined = new Set([...theme.light.keys(), ...theme.dark.keys()]);

  /** Every source a running client pulls in: the shell's own, and every module's. */
  function sources(extensions: string[]): string[] {
    const roots = [clientSrc, path.join(repoSrc, "diagrams"), path.join(repoSrc, "editors")];
    const found: string[] = [];

    function walk(directory: string) {
      let entries: string[];
      try {
        entries = readdirSync(directory);
      } catch {
        return; // a root that does not exist yet - editors/ predates nothing, but be tolerant
      }

      for (const entry of entries) {
        if (entry === "node_modules" || entry === "dist" || entry === "bin" || entry === "obj") {
          continue;
        }

        const full = path.join(directory, entry);
        if (statSync(full).isDirectory()) {
          walk(full);
        } else if (extensions.some((extension) => entry.endsWith(extension))) {
          found.push(full);
        }
      }
    }

    roots.forEach(walk);
    return found;
  }

  const stylesheets = () => sources([".css"]);

  const DARK_SCHEME = "@media (prefers-color-scheme: dark)";

  /**
   * TypeScript that paints. Tests are excluded - a guard naming a deliberately undefined token
   * in a canary is not an offence - and comments are stripped, because this file and
   * `themeContrast.ts` both discuss `var(--x, fallback)` in prose.
   */
  const painters = () =>
    sources([".ts", ".tsx"]).filter((file) => !file.includes(".test.") && !file.includes(`${path.sep}generated${path.sep}`));

  function withoutComments(source: string): string {
    return source.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/(^|[^:])\/\/[^\n]*/g, "$1 ");
  }

  /**
   * The same for a stylesheet, but keeping the line structure so an offence can still name its
   * line. <b>A comment naming a token is prose, not a usage</b> - this file's own fix quotes the
   * names it removed, and `dotnet-dependency-graph.css` explains in a comment which phantom it
   * closed. A guard that reads those is a guard over prose: it makes the narrative unwritable
   * and fires on the one document most likely to be telling the truth.
   */
  function withoutCssComments(css: string): string {
    return css.replace(/\/\*[\s\S]*?\*\//g, (comment) => comment.replace(/[^\r\n]/g, " "));
  }

  /** The custom properties a source declares itself, in any block or mode. */
  function declaredIn(source: string): Set<string> {
    const own = themeTokens(source);
    return new Set([...own.light.keys(), ...own.dark.keys()]);
  }

  /**
   * The rule, in one function so the canaries below can exercise it on text rather than on the
   * tree: a `var(--x)` must name something the theme defines or something this source declares.
   */
  function undefinedTokensIn(source: string): string[] {
    const own = declaredIn(source);
    return [...new Set(tokensRead(source))].filter((token) => !defined.has(token) && !own.has(token));
  }

  it("finds the sources it is meant to be guarding, and reads names out of them", () => {
    // Arrange & act: a walk that silently found nothing would pass every assertion below, and a
    // `tokensRead` that stopped matching would report the same clean zero as a clean tree.
    const sheets = stylesheets();
    const code = painters();
    const everyNameRead = new Set(sheets.flatMap((sheet) => tokensRead(readFileSync(sheet, "utf8"))));

    // Assert: the walk reaches both trees, the theme is non-empty, and the reader returns names
    // we know are there - including one of each namespace this guard exists to cover.
    expect(sheets.length).toBeGreaterThan(5);
    expect(sheets.some((sheet) => sheet.endsWith("index.css"))).toBe(true);
    expect(sheets.some((sheet) => sheet.includes(`diagrams${path.sep}`))).toBe(true);
    expect(code.some((file) => file.endsWith("DiagramCanvas.tsx"))).toBe(true);
    expect(defined.size).toBeGreaterThan(5);
    expect(everyNameRead.size).toBeGreaterThan(20);
    expect(everyNameRead).toContain("--color-text");
    expect(everyNameRead).toContain("--ansible-play-hue-0");
  });

  it("catches an undefined token, and leaves a locally defined palette alone", () => {
    // Arrange: the two cases the rule has to tell apart, as text rather than as planted files.
    const phantom = `.x { color: var(--totally-made-up, #f00); }`;
    const localPalette = `.y { --mine: #0f0; } .z { color: var(--mine); }`;
    const themed = `.w { color: var(--color-text); }`;

    // Act & assert: seen to fail for the right reason before any clean run below is believed.
    expect(undefinedTokensIn(phantom)).toEqual(["--totally-made-up"]);
    expect(undefinedTokensIn(localPalette)).toEqual([]);
    expect(undefinedTokensIn(themed)).toEqual([]);

    // And the prose half: a stylesheet explaining which phantom it closed must not be
    // reported as still using it.
    const narrative = `/* it used to read var(--long-gone, #f00) and now does not */
.a { color: var(--color-text); }`;
    expect(undefinedTokensIn(withoutCssComments(narrative))).toEqual([]);
    expect(undefinedTokensIn(narrative)).toEqual(["--long-gone"]);
  });

  it("defines every token any stylesheet uses", () => {
    // Arrange.
    const offences: string[] = [];

    // Act: every var(--…) in every sheet, with or without a fallback.
    for (const sheet of stylesheets()) {
      const css = withoutCssComments(readFileSync(sheet, "utf8"));
      const own = declaredIn(css);
      const lines = css.split(/\r?\n/);
      lines.forEach((line, index) => {
        for (const token of tokensRead(line)) {
          if (!defined.has(token) && !own.has(token)) {
            // Name the file, the token and the line: a guard that says only "an undefined
            // token exists" sends the next person searching.
            offences.push(
              `${path.relative(repoSrc, sheet)}:${index + 1} uses ${token}, which neither index.css nor this stylesheet defines`,
            );
          }
        }
      });
    }

    // Assert.
    expect(offences).toEqual([]);
  });

  /**
   * A colour a source declares for itself, and whether it says so twice.
   *
   * <b>The guard above catches an undeclared name and cannot catch a palette that forgot a
   * mode.</b> That distinction is the whole of `dotnet-dependency-graph`'s defect: its names were
   * undefined AND its literals were dark, so it rendered dark-looking in the light theme.
   * Declaring them in one mode only would have satisfied the rule and moved the phantom rather
   * than closed it - a local palette is allowed precisely because `ansible-structure` declares
   * both, and allowing it on any weaker terms gives back what task 2 was for.
   *
   * Only colour-valued properties are held to this. `--font-sans` is the same stack in both
   * modes and saying so twice would be noise.
   */
  function colourOnlyInOneMode(css: string): string[] {
    const stripped = withoutCssComments(css);
    const darkAt = stripped.indexOf(DARK_SCHEME);
    if (darkAt === -1) {
      const everywhere = themeTokens(stripped).light;
      return [...everywhere].filter(([, value]) => isColour(value)).map(([name]) => name);
    }

    const light = themeTokens(stripped.slice(0, darkAt)).light;
    // `.dark` rather than `.light`: this slice BEGINS with the media marker, so themeTokens
    // splits it with an empty light half and everything in the dark one. Reading `.light`
    // here returned an empty set and reported every correct two-mode palette in the tree as
    // a one-mode offender - which is how this detector was caught being wrong.
    const dark = themeTokens(stripped.slice(darkAt)).dark;

    return [...light]
      .filter(([name, value]) => isColour(value) && !dark.has(name))
      .map(([name]) => name);
  }

  function isColour(value: string): boolean {
    return /^(#|rgb|hsl|color\()/i.test(value.trim());
  }

  it("declares a local colour palette in both modes, or not at all", () => {
    // Arrange.
    const offences: string[] = [];

    // Act: every sheet that owns colours, excluding the theme itself - index.css IS the modes.
    for (const sheet of stylesheets().filter((file) => !file.endsWith("index.css"))) {
      for (const name of colourOnlyInOneMode(readFileSync(sheet, "utf8"))) {
        offences.push(
          `${path.relative(repoSrc, sheet)} declares ${name} for one mode only, so it paints a light-theme colour on the dark surface or the reverse`,
        );
      }
    }

    // Assert.
    expect(offences).toEqual([]);
  });

  it("sees a one-mode palette, and a two-mode one as fine", () => {
    // Arrange: the distinction as text, so the assertion above is believed only once its
    // detector has been watched failing.
    const oneMode = `.a { --mine: #0f0; }`;
    const bothModes = `.a { --mine: #0f0; }
${DARK_SCHEME} {
  .a { --mine: #7f7; }
}`;
    const notAColour = `.a { --stack: system-ui, sans-serif; }`;

    // Act & assert.
    expect(colourOnlyInOneMode(oneMode)).toEqual(["--mine"]);
    expect(colourOnlyInOneMode(bothModes)).toEqual([]);
    expect(colourOnlyInOneMode(notAColour)).toEqual([]);
  });

  it("defines every token the painting TypeScript uses", () => {
    // Arrange.
    const offences: string[] = [];

    // Act: the library writes colours as inline `var()` strings, which no stylesheet walk sees.
    for (const file of painters()) {
      for (const token of undefinedTokensIn(withoutComments(readFileSync(file, "utf8")))) {
        offences.push(`${path.relative(repoSrc, file)} uses ${token}, which index.css never defines`);
      }
    }

    // Assert.
    expect(offences).toEqual([]);
  });
});
