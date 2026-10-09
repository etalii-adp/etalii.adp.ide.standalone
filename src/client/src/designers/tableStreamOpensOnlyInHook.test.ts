import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { sourceFiles } from "@client/sourceFiles";

/**
 * One place opens a table's stream: `useTableStream`. A designer module wraps it.
 *
 * This is `diagramStreamOpensOnlyInHook.test.ts` for the designer family, written with the hook
 * rather than after the copies: nine diagram modules once copied the open loop, and the copies
 * drifted in exactly the ways the hook exists to prevent - a stream that ended cleanly re-opened
 * at once, and kept its stale model. A table's loop has more to get wrong than a diagram's - the
 * window is sent again after a reconnect, and the edits that were waiting die with the session -
 * so the second copy would be wrong in more ways.
 *
 * **Its limits, stated plainly.** It reads text. It recognises the open by its shape: a call to
 * `openTableStream(`, which `useWorkspaceStreams` hands out, or a call to `.openTable(`, the
 * unary call underneath it. That catches a copy of the loop and not a rewrite under other names.
 * The control below proves the pattern still recognises the call that is allowed, so a pattern
 * that stopped matching fails here rather than reporting a clean tree.
 */

/** A table stream opened: through the tab's one stream, or by the unary call underneath it. */
const TABLE_STREAM_OPEN = /\bopenTableStream\(|\.openTable\(/;

/** The one file allowed to open a table stream, relative to src/. */
const THE_HOOK = "client/src/designers/useTableStream.ts";

/**
 * The file that carries the open for the hook and opens nothing for itself: the provider that
 * hands `openTableStream` out, and makes the unary call when the hook asks.
 */
const THE_CARRIERS = ["client/src/shell/context/ContextConnectionProvider.tsx"];

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

/** The client itself plus the client folder of every module of every family. */
function clientDirectories(root: string): string[] {
  const moduleClients = (family: string) => {
    const base = join(root, family);
    if (statSync(base, { throwIfNoEntry: false })?.isDirectory() !== true) {
      return [];
    }
    return readdirSync(base, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => join(base, entry.name, "client"))
      .filter((client) => statSync(client, { throwIfNoEntry: false })?.isDirectory() === true);
  };
  return [join(root, "client", "src"), ...moduleClients("designers"), ...moduleClients("diagrams"), ...moduleClients("editors")];
}

/** Every non-test TypeScript source of the clients, relative to src/, generated code left out. */
function clientSources(root: string): string[] {
  return clientDirectories(root)
    .flatMap((directory) => sourceFiles(directory))
    .map((file) => relative(root, file).replaceAll("\\", "/"))
    .filter((file) => /\.tsx?$/.test(file) && !/\.test\.tsx?$/.test(file) && !file.includes("/generated/"));
}

describe("a table stream is opened only by useTableStream", () => {
  it("recognises the one allowed call - the pattern still matches what it polices", () => {
    // Arrange.
    const root = sourceRoot();

    // Act and assert: a control, so an empty offender list below means "none", not "blind".
    expect(TABLE_STREAM_OPEN.test(readFileSync(join(root, THE_HOOK), "utf8"))).toBe(true);
    for (const carrier of THE_CARRIERS) {
      expect(TABLE_STREAM_OPEN.test(readFileSync(join(root, carrier), "utf8")), carrier).toBe(true);
    }
  });

  it("walks the client and the module clients of every family", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const walked = clientSources(root);

    // Assert: the completeness canaries - a floor, and members known by name from two trees.
    expect(walked.length).toBeGreaterThan(200);
    expect(walked).toContain(THE_HOOK);
    expect(walked.some((file) => file.startsWith("diagrams/timeline/client/"))).toBe(true);
  });

  it("finds no other file opening a table stream", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const offenders = clientSources(root)
      .filter((file) => file !== THE_HOOK && !THE_CARRIERS.includes(file))
      .filter((file) => TABLE_STREAM_OPEN.test(readFileSync(join(root, file), "utf8")));

    // Assert: every offender named at once. The fix is a call to useTableStream, never a copy of its loop.
    expect(offenders).toEqual([]);
  });
});
