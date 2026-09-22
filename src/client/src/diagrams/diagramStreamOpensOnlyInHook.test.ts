import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * One place opens a diagram's delta stream: `useDiagramStream`. Every module wraps it.
 *
 * Nine modules once copied the open loop instead, and the copies drifted from it in exactly
 * the two ways the hook exists to prevent - a cleanly-ended stream re-opened at once (a hot
 * loop, against technical-debt-cleanup R3.4) and kept its stale model. Fixing nine copies
 * would have left the tenth to be written wrong; this guard makes a new copy fail instead.
 * The behavioural half, which proves the wrappers actually reconnect correctly, is
 * `diagramStreamHooks.reconnect.test.ts`.
 *
 * **Its limits, stated plainly.** It reads text: it recognises the `Open` request by its
 * shape, a call to `.open(` whose request begins with `projectId:`. That catches a copy of
 * the loop, which is how every offender arose, and not a from-scratch rewrite that builds the
 * request under other names. The positive control below proves the pattern still recognises
 * the one call that is allowed, so a pattern that silently stopped matching fails here rather
 * than passing with nothing found.
 */

/** A `DiagramService.Open` call: `.open(` with a request object that starts with projectId. */
const DIAGRAM_STREAM_OPEN = /\.open\(\s*\{\s*projectId\s*:/;

/** The one file allowed to open the stream, relative to src/. */
const THE_HOOK = "client/src/diagrams/useDiagramStream.ts";

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

/** Every non-test TypeScript source under the given directories, skipping node_modules. */
function sourcesUnder(directories: readonly string[]): string[] {
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
  for (const directory of directories) {
    if (statSync(directory, { throwIfNoEntry: false })?.isDirectory() === true) {
      walk(directory);
    }
  }
  return files;
}

/** The client itself plus every diagram and editor module's client folder. */
function clientDirectories(root: string): string[] {
  const moduleClients = (family: string) => {
    const base = join(root, family);
    if (statSync(base, { throwIfNoEntry: false })?.isDirectory() !== true) {
      return [];
    }
    return readdirSync(base, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => join(base, entry.name, "client"));
  };
  return [join(root, "client", "src"), ...moduleClients("diagrams"), ...moduleClients("editors")];
}

describe("the diagram stream is opened only by useDiagramStream", () => {
  it("recognises the one allowed call - the pattern still matches what it polices", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const hookSource = readFileSync(join(root, THE_HOOK), "utf8");

    // Assert: a control, so an empty offender list below means "none", not "blind".
    expect(DIAGRAM_STREAM_OPEN.test(hookSource)).toBe(true);
  });

  it("walks the modules that used to hand-roll the loop", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const walked = sourcesUnder(clientDirectories(root)).map((file) => relative(root, file).replaceAll("\\", "/"));

    // Assert: the completeness canary - the former offenders' folders are in the walk.
    for (const module of ["databricks", "dependency-graph", "helm-charts", "rdf", "sparql", "timeline"]) {
      expect(walked.some((file) => file.startsWith(`diagrams/${module}/client/`))).toBe(true);
    }
  });

  it("finds no other file opening the stream", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const offenders = sourcesUnder(clientDirectories(root))
      .map((file) => relative(root, file).replaceAll("\\", "/"))
      .filter((file) => file !== THE_HOOK)
      .filter((file) => DIAGRAM_STREAM_OPEN.test(readFileSync(join(root, file), "utf8")));

    // Assert: every offender named at once. The fix is a thin wrapper over useDiagramStream,
    // shaped like useCausalLoopStream - never a copy of the loop.
    expect(offenders).toEqual([]);
  });
});
