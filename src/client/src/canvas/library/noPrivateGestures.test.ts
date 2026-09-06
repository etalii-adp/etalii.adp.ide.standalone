import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";


/**
 * A migrated module holds no private gesture state. There is one gesture layer, inside
 * `DiagramCanvas`, and a module on the library reaches it only by declaring a definition -
 * the drag/pan/connect refs that `drag-and-drop-centralization` counted eight, ten and four
 * of are exactly what adoption deletes, and this guard is what keeps them deleted
 * (diagram-library-adoption Requirement 3.2).
 *
 * It follows the family shape beside `noPrivateLabelEditors` and `noPrivateScrollbars`:
 * walk the tree, collect every offender, fail once naming all of them.
 *
 * **Its limits, stated plainly.** It reads text, so it catches the copy - somebody starting
 * from a canvas that already rolled its own gestures - and not a from-scratch reinvention
 * under fresh names. And it deliberately does NOT police `@client/canvas/connectors` imports:
 * a custom shape or route legitimately builds geometry from the shared primitives (rdf's
 * `edgePointOf`, timeline's loop-aware path builder), and the mounted route guard in
 * `libraryGuards` is what proves built-in routes stay on the shared geometry.
 */

/** Anything that reads as a module-owned gesture layer. */
const GESTURE_STATE = /\b(dragRef|panRef|connectRef)\b|setPointerCapture|releasePointerCapture/;

/** The library's own gesture layer, reached only through DiagramCanvas once migrated. */
const GESTURE_HOOK = /from\s+"@client\/canvas\/gesture\/usePointerGesture"|from\s+"\.\.\/gesture\/usePointerGesture"/;

/** The repository's src folder, found by walking up - the family's shared idiom. */
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

function moduleNames(root: string): string[] {
  const diagrams = join(root, "diagrams");
  return readdirSync(diagrams, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && statSync(join(diagrams, entry.name, "client"), { throwIfNoEntry: false })?.isDirectory() === true)
    .map((entry) => entry.name);
}

function clientSources(root: string, module: string): string[] {
  const files: string[] = [];
  const walk = (directory: string) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== "node_modules") {
          walk(path);
        }
      } else if (/\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
        files.push(path);
      }
    }
  };
  walk(join(root, "diagrams", module, "client"));
  return files;
}

describe("no private gestures in a migrated module", () => {
  it("walks every module: adoption is complete and the exclusion mechanism is retired", () => {
    // The completeness half: a module added to the tree lands in the walk by default. The
    // NOT_YET_MIGRATED set emptied with diagram-library-adoption task 11 and was deleted
    // with its mechanism, so every module is a guarded member unconditionally.
    const modules = moduleNames(sourceRoot());

    // The canary: the two reference migrations are guarded members, by name.
    expect(modules).toContain("rdf");
    expect(modules).toContain("timeline");
  });

  it("finds no module-owned gesture state in any migrated module's client sources", () => {
    const root = sourceRoot();
    const offenders: string[] = [];

    for (const module of moduleNames(root)) {
      for (const path of clientSources(root, module)) {
        const content = readFileSync(path, "utf-8");
        if (GESTURE_STATE.test(content)) {
          offenders.push(`${relative(root, path)}: holds private drag/pan/connect state or raw pointer capture`);
        }
        if (GESTURE_HOOK.test(content)) {
          offenders.push(`${relative(root, path)}: reaches usePointerGesture directly instead of through DiagramCanvas`);
        }
      }
    }

    expect(offenders, offenders.join("\n")).toEqual([]);
  });
});
