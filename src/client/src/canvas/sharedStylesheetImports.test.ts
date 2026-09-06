import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * Every diagram module that registers a canvas imports the shared stylesheet from its own
 * `register.ts` - the convention `docs/creating-a-diagram-module.md` states, checked rather
 * than remembered. Five modules named `canvas-*` classes while importing nothing: they
 * styled correctly only because `diagramCanvases.ts` globs every register file eagerly, so
 * the sheet was always loaded on some OTHER module's behalf - and deleting or lazy-loading
 * any importer would have silently unstyled them.
 *
 * **Its limits, stated plainly.** jsdom applies no CSS, so no mounted test can see the
 * appearance; the convention is per-module and textual - `register.ts` imports the sheet -
 * so this guard asserts the IMPORT, which is the only assertable thing. It keys on the
 * register file rather than on any canvas file, so composition (databricks' three readings
 * over one inner canvas) cannot misreport: one module, one register, one import.
 */
const SHARED_STYLESHEET_IMPORT = /import\s+"@client\/canvas\/canvas\.css";/;

/** A register that mounts a canvas component - stub modules without one are out of scope. */
const REGISTERS_A_CANVAS = /import\s+\{[^}]*Canvas[^}]*\}\s+from\s+"\.\//;

function sourceRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
  for (let depth = 0; depth < 12; depth++) {
    const hasModules = statSync(join(directory, "diagrams"), { throwIfNoEntry: false })?.isDirectory() === true;
    const hasStyleRules = statSync(join(directory, ".editorconfig"), { throwIfNoEntry: false })?.isFile() === true;
    if (hasModules && hasStyleRules) {
      return directory;
    }
    directory = dirname(directory);
  }
  throw new Error("The src folder was not found above this test file.");
}

describe("the shared canvas stylesheet", () => {
  it("is imported by every module register that mounts a canvas", () => {
    const root = sourceRoot();
    const diagrams = join(root, "diagrams");
    const offenders: string[] = [];
    let walked = 0;

    for (const entry of readdirSync(diagrams, { withFileTypes: true })) {
      if (!entry.isDirectory()) {
        continue;
      }

      const register = join(diagrams, entry.name, "client", "register.ts");
      if (statSync(register, { throwIfNoEntry: false })?.isFile() !== true) {
        continue;
      }

      const source = readFileSync(register, "utf-8");
      if (!REGISTERS_A_CANVAS.test(source)) {
        continue;
      }

      walked += 1;
      if (!SHARED_STYLESHEET_IMPORT.test(source)) {
        offenders.push(`${entry.name}/client/register.ts: mounts a canvas but never imports @client/canvas/canvas.css`);
      }
    }

    // The canary: the walk found the implemented modules, so an empty offender list means
    // compliance rather than a walk that matched nothing.
    expect(walked).toBeGreaterThanOrEqual(12);
    expect(offenders, offenders.join("\n")).toEqual([]);
  });
});
