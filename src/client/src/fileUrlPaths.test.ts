import { describe, expect, it } from "vitest";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

/**
 * A file URL's pathname always begins with a slash. Stripping that slash by hand looks
 * right on Windows, where the drive letter carries the root, and silently yields a
 * relative path on Linux, where the slash is the root. That shipped in
 * viteFsAllow.test.ts and cost a red pipeline: the config loader reported
 * UNRESOLVED_ENTRY for home/runner/... with its leading slash gone, while every Windows
 * desktop stayed green.
 *
 * node:url's fileURLToPath converts correctly on both. This guards the class of mistake
 * across the client sources rather than the one instance that was fixed.
 */
describe("file URL to path conversion", () => {
  const self = fileURLToPath(import.meta.url);
  const here = path.dirname(self);
  const roots = [path.resolve(here, ".."), path.resolve(here, "../../diagrams")];
  const skipped = new Set(["node_modules", "dist", "coverage", ".vite", ".turbo"]);

  /** Every TypeScript/JavaScript source below `directory`, build output excluded. */
  function sources(directory: string): string[] {
    return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
      if (entry.isDirectory()) {
        return skipped.has(entry.name) ? [] : sources(path.join(directory, entry.name));
      }
      return /\.(ts|tsx|js|jsx|mjs|cjs)$/.test(entry.name) ? [path.join(directory, entry.name)] : [];
    });
  }

  it("no client source strips a file URL root by hand", () => {
    // Arrange: the pattern is assembled from fragments so this guard cannot match itself,
    // and its own file is excluded so the sentence below may name the mistake plainly.
    const slicedPathname = new RegExp("pathname" + "\\s*\\.\\s*(?:slice|substring|substr)\\s*\\(");
    const files = roots.flatMap(sources).filter((file) => file !== self);

    // Act.
    const offenders = files.filter((file) => slicedPathname.test(readFileSync(file, "utf8")));

    // Assert.
    expect(
      offenders,
      "convert a file URL with fileURLToPath from node:url - slicing the pathname drops the root on Linux",
    ).toEqual([]);
  });
});
