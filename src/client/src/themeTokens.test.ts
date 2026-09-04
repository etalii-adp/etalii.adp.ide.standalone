import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";

/**
 * A `var(--color-…)` naming a token the theme never defines renders its fallback in every
 * mode - or nothing at all where there is no fallback - and looks perfectly fine in review.
 * Three such phantoms reached `develop` this way, found by two agents on three separate
 * occasions, and one of them cost a canvas its selection outline entirely:
 *
 * - `--color-accent` carried ansible's selection stroke and focus outline. Undefined and with
 *   no fallback there, a selected node rendered exactly like an unselected one.
 * - `--color-error` was a phantom synonym for the theme's real `--color-danger`, used in a
 *   module *and in the theme's own stylesheet* - which is how it survived so long.
 * - `--color-border-strong` never existed; helm's edges fell through to a hardcoded grey.
 *
 * <b>The fallback is the reason these are invisible, not a mitigation.</b> Each author picked a
 * literal correct for the one mode they had in mind, so the bug shows only in the other theme,
 * or only on the one canvas nobody was looking at. So a fallback does not excuse an undefined
 * token here: the rule is that the token must exist.
 *
 * Scope is deliberate and covers `src/client/src` as well as the diagram and editor modules.
 * `--color-error` was used by `index.css` itself; a guard that trusted the theme to be
 * self-consistent would have missed it.
 */
describe("theme colour tokens", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const clientSrc = here;
  const repoSrc = path.resolve(here, "../..");

  /** Every `--color-*` the theme actually defines, in either mode. */
  const defined = new Set(
    [...readFileSync(path.join(clientSrc, "index.css"), "utf8").matchAll(/^\s*(--color-[a-z0-9-]+)\s*:/gm)]
      .map((match) => match[1]),
  );

  /** Every stylesheet a running client pulls in: the shell's own, and every module's. */
  function stylesheets(): string[] {
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
        } else if (entry.endsWith(".css")) {
          found.push(full);
        }
      }
    }

    roots.forEach(walk);
    return found;
  }

  it("finds the stylesheets it is meant to be guarding", () => {
    // Arrange & act: a walk that silently found nothing would pass every assertion below.
    const sheets = stylesheets();

    // Assert.
    expect(sheets.length).toBeGreaterThan(5);
    expect(sheets.some((sheet) => sheet.endsWith("index.css"))).toBe(true);
    expect(sheets.some((sheet) => sheet.includes(`diagrams${path.sep}`))).toBe(true);
    expect(defined.size).toBeGreaterThan(5);
  });

  it("defines every colour token any stylesheet uses", () => {
    // Arrange.
    const offences: string[] = [];

    // Act: every var(--color-…) in every sheet, with or without a fallback.
    for (const sheet of stylesheets()) {
      const lines = readFileSync(sheet, "utf8").split(/\r?\n/);
      lines.forEach((line, index) => {
        for (const match of line.matchAll(/var\(\s*(--color-[a-z0-9-]+)/g)) {
          const token = match[1];
          if (!defined.has(token)) {
            // Name the file, the token and the line: a guard that says only "an undefined
            // token exists" sends the next person searching.
            offences.push(
              `${path.relative(repoSrc, sheet)}:${index + 1} uses ${token}, which index.css never defines`,
            );
          }
        }
      });
    }

    // Assert.
    expect(offences).toEqual([]);
  });
});
