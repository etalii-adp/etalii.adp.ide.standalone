import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * No module sends the backend a keystroke; the library sends the one a declaration names
 * (client-centralization Requirement 5).
 *
 * Eleven modules each kept a `BACKEND_KEYS` map from action id to key and built a `ContextShortcut`
 * out of it in their own `onActionInvoked`. The keys now live on the declarations as `backendKey`,
 * and `useLibrarySelection` sends them.
 *
 * <b>Why this guards the SEND rather than the map.</b> Requirement 5.4 asks for a guard that fails on
 * "a module that maps an action id to a key". A map by that name is a text pattern, and a rename walks
 * straight past it. But a map of keys only matters if a module can send one - and sending takes
 * `executeShortcut`, or a `ContextShortcut` built to pass to it. A module that can do neither cannot
 * put a key on the wire whatever it keeps, so forbidding those two is the property the requirement is
 * after, and it survives a rename the name-check would not.
 *
 * <b>Its limits, stated plainly.</b> It reads text, so it recognises the three names and not a
 * from-scratch reimplementation of the shortcut request under other names. The positive control
 * proves the patterns still see the one place allowed to use them, so a pattern that silently stopped
 * matching fails here rather than reporting a clean tree.
 */

/** Sending a keystroke: calling the connection's send, or building the request it takes. */
const SENDS_A_KEYSTROKE = /\b(executeShortcut|contextShortcutOf|ContextShortcut)\b/;

/** The one file allowed to send a declared keystroke, relative to src/. */
const THE_LIBRARY = "client/src/canvas/library/librarySelection.ts";

/**
 * A source with its comments blanked. <b>A comment naming these is prose, not a send</b> - a module
 * explaining that it no longer builds a `ContextShortcut` must not be reported as building one. A
 * guard that reads comments makes the narrative unwritable and fires on the one sentence most likely
 * to be telling the truth.
 */
function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/(^|[^:])\/\/[^\n]*/g, "$1 ");
}

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

/** Every non-test TypeScript source in each diagram and editor module's client folder. */
function moduleSources(root: string): string[] {
  const files: string[] = [];
  const walk = (directory: string) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== "node_modules" && entry.name !== "generated") {
          walk(path);
        }
      } else if (/\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
        files.push(path);
      }
    }
  };
  for (const family of ["diagrams", "editors"]) {
    const base = join(root, family);
    if (statSync(base, { throwIfNoEntry: false })?.isDirectory() !== true) {
      continue;
    }
    for (const module of readdirSync(base, { withFileTypes: true }).filter((entry) => entry.isDirectory())) {
      const client = join(base, module.name, "client");
      if (statSync(client, { throwIfNoEntry: false })?.isDirectory() === true) {
        walk(client);
      }
    }
  }
  return files;
}

describe("no module sends the backend a keystroke", () => {
  it("recognises the one place allowed to - the pattern still matches what it polices", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const librarySource = readFileSync(join(root, THE_LIBRARY), "utf8");

    // Assert: the control. Without it, an offender list of [] could mean a pattern gone blind.
    expect(SENDS_A_KEYSTROKE.test(librarySource)).toBe(true);
  });

  it("walks the modules that used to send their own", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const walked = moduleSources(root).map((file) => relative(root, file).replaceAll("\\", "/"));

    // Assert: the completeness canary - the eleven former senders' folders are all in the walk.
    for (const module of ["azure-pipeline", "c4", "databricks", "dependency-graph", "mindmap", "rdf", "timeline", "wardley-map"]) {
      expect(walked.some((file) => file.startsWith(`diagrams/${module}/client/`)), module).toBe(true);
    }
  });

  it("finds no module that can send one", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const offenders = moduleSources(root)
      .map((file) => relative(root, file).replaceAll("\\", "/"))
      .filter((file) => SENDS_A_KEYSTROKE.test(withoutComments(readFileSync(join(root, file), "utf8"))));

    // Assert: every offender named at once. The fix is `backendKey` on the action's declaration.
    expect(offenders).toEqual([]);
  });
});
