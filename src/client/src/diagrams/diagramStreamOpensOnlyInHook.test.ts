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
 * **Its limits, stated plainly.** It reads text: it recognises the open by its shape, a call to
 * `openDiagramStream(` - the tab's one stream, handed out by `useWorkspaceStreams` since
 * two-tab-connection-wedge task 5 - or to `.open(` whose request begins with `projectId:`, the
 * `DiagramService.Open` call it replaced and the shape every past copy took. That catches a copy of
 * the loop, which is how every offender arose, and not a from-scratch rewrite that builds the
 * request under other names. The positive control below proves the pattern still recognises
 * the one call that is allowed, so a pattern that silently stopped matching fails here rather
 * than passing with nothing found.
 */

/** A diagram stream opened: on the tab's one stream, or the `DiagramService.Open` call it replaced. */
const DIAGRAM_STREAM_OPEN = /\bopenDiagramStream\(\s*\{|\.open\(\s*\{\s*projectId\s*:/;

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
    for (const module of ["databricks", "dependency-graph", "helm-chart", "rdf", "sparql", "timeline"]) {
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

/**
 * The move, by the same argument and the same instrument (client-centralization Requirement 7).
 *
 * Fourteen modules each built their own `moveElementTo` on the stream's client - kept per module
 * deliberately by technical-debt-cleanup R3.2, reversed by the user on 2026-09-20. Thirteen bodies
 * were identical; the fourteenth had drifted in the way copies do, catching the failure and
 * returning a fixed "The position could not be saved." in place of the backend's own reason. That
 * is the drift this guard exists to stop recurring: one call, so there is nothing to drift from.
 *
 * Same limits as the open guard above, stated for the same reason: it recognises the `MoveElement`
 * request by its shape, and the positive control proves the pattern still sees the one call that
 * is allowed, so a pattern that stopped matching fails rather than reporting a clean tree.
 *
 * <b>It polices the ARRANGEMENT move only, and that distinction is the backend's, not this file's.</b>
 * One `MoveElement` request carries two operations: with a `position` it arranges an element where it
 * was dropped, and with a `newParentId` it re-parents it - "the backend routes on its presence", as the
 * shared body says. Task 8 consolidated the arrangement move, which fourteen modules each built.
 * `mindmap`'s `useMindmapStream` builds the other one, a drag-to-reparent with `newParentId` and an
 * index, and no position at all - its own operation, outside task 8. <b>The first version of this
 * guard matched any `MoveElement` request</b> and so fired on mindmap's legitimate call; it was red on
 * its own branch from the commit that introduced it, and unnoticed because nothing had run it yet. A
 * request is therefore an arrangement only when its object carries `position`.
 */
const DIAGRAM_MOVE_CALL = /\.moveElement\(\s*\{/g;

/** The request objects passed to `.moveElement({...})` in a source, braces balanced. */
function moveRequestsIn(source: string): string[] {
  const requests: string[] = [];
  for (const call of source.matchAll(DIAGRAM_MOVE_CALL)) {
    const open = (call.index ?? 0) + call[0].length - 1;
    let depth = 0;
    for (let at = open; at < source.length; at++) {
      if (source[at] === "{") {
        depth++;
      } else if (source[at] === "}" && --depth === 0) {
        requests.push(source.slice(open, at + 1));
        break;
      }
    }
  }
  return requests;
}

/** Whether a source builds an ARRANGEMENT move: a `MoveElement` request carrying a `position`. */
function buildsAnArrangementMove(source: string): boolean {
  return moveRequestsIn(source).some((request) => /\bposition\s*:/.test(request) && /\bprojectId\s*:/.test(request));
}

describe("an element is moved only by useDiagramStream", () => {
  it("tells an arrangement from a re-parenting - the distinction this guard was narrowed to", () => {
    // Arrange: the two operations one request can carry, as text, so the narrowing is exercised
    // directly rather than inferred from whatever the tree happens to hold today.
    const arrangement = `await client.moveElement({ projectId: { value: p }, elementId, position: { x, y } });`;
    const reparenting = `await client.moveElement({
      projectId: { value: p },
      elementId,
      newParentId,
      index: -1,
    });`;

    // Act & assert: the one it must catch, and the one it was narrowed to leave alone.
    expect(buildsAnArrangementMove(arrangement)).toBe(true);
    expect(buildsAnArrangementMove(reparenting)).toBe(false);
  });

  it("recognises the one allowed call - the pattern still matches what it polices", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const hookSource = readFileSync(join(root, THE_HOOK), "utf8");

    // Assert: the control. Without it, an offender list of [] could mean a pattern gone blind.
    expect(buildsAnArrangementMove(hookSource)).toBe(true);
  });

  it("finds no other file building its own move", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const offenders = sourcesUnder(clientDirectories(root))
      .map((file) => relative(root, file).replaceAll("\\", "/"))
      .filter((file) => file !== THE_HOOK)
      .filter((file) => buildsAnArrangementMove(readFileSync(join(root, file), "utf8")));

    // Assert: a module takes `moveElementTo` from useDiagramStream's result and passes it on.
    expect(offenders).toEqual([]);
  });
});
