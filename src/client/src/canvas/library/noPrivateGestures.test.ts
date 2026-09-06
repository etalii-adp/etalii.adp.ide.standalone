import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

import { NOT_YET_MIGRATED, NOT_YET_MIGRATED_FILES } from "./adoptionStatus";

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
  it("classifies every module: migrated and guarded, or named as not yet migrated", () => {
    // The completeness half: a module added to the tree lands in the walk by default, and a
    // NOT_YET_MIGRATED name that no longer matches a module on disk is a stale entry.
    const modules = moduleNames(sourceRoot());
    const stale = [...NOT_YET_MIGRATED].filter((name) => !modules.includes(name));
    expect(stale, "NOT_YET_MIGRATED names modules that do not exist").toEqual([]);

    // The canary: the two reference migrations are guarded members, by name.
    const migrated = modules.filter((name) => !NOT_YET_MIGRATED.has(name));
    expect(migrated).toContain("rdf");
    expect(migrated).toContain("timeline");
  });

  it("finds no module-owned gesture state in any migrated module's client sources", () => {
    const root = sourceRoot();
    const offenders: string[] = [];

    for (const module of moduleNames(root).filter((name) => !NOT_YET_MIGRATED.has(name))) {
      for (const path of clientSources(root, module)) {
        if (NOT_YET_MIGRATED_FILES.has(path.split(sep).pop() ?? "")) {
          continue;
        }

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
