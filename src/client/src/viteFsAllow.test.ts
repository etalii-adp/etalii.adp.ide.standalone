import { beforeAll, describe, expect, it } from "vitest";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { realpathSync } from "node:fs";
import path from "node:path";
import { loadConfigFromFile } from "vite";

/**
 * The dev server refuses to serve anything outside `server.fs.allow`. Setting that option
 * replaces Vite's default (the workspace root) rather than adding to it, so an allow list
 * written in terms of this project and the diagram modules quietly stopped covering
 * `src/node_modules` - where the npm workspace hoists everything the two share. Every icon
 * 403'd, with only a dev-server console line to say why.
 *
 * These assert the property that failure violated, rather than the spelling of the fix.
 */
describe("vite server.fs.allow", () => {
  // fileURLToPath, not a hand-sliced URL pathname: the slice drops the leading slash every
  // file URL carries, which Windows survives because its drive letter keeps the root but
  // Linux does not, so /home/runner/... becomes home/runner/... - a relative path the config
  // loader cannot resolve. vite.config.ts, the file loaded below, converts the same way for
  // the same reason.
  const here = path.dirname(fileURLToPath(import.meta.url));

  // Loaded through Vite own loader rather than imported: the config uses import.meta.url,
  // which only resolves to a file URL when Vite resolves it.
  let allow: string[] = [];

  beforeAll(async () => {
    const loaded = await loadConfigFromFile(
      { command: "serve", mode: "development" },
      path.resolve(here, "../vite.config.ts"),
    );
    allow = (loaded?.config.server?.fs?.allow ?? []).map((entry) => realpathSync(entry as string));
  });

  /** Whether `target` sits inside any allowed root. */
  function isAllowed(target: string): boolean {
    const resolved = realpathSync(target);
    return allow.some((root) => resolved === root || resolved.startsWith(root.endsWith(path.sep) ? root : root + path.sep));
  }

  it("resolves its own directory to an absolute path", () => {
    // The whole allow list is read relative to this directory, so a conversion that loses
    // the root turns every assertion below into UNRESOLVED_ENTRY - which is what slicing the
    // URL pathname by hand did on Linux while Windows stayed green. fileUrlPaths.test.ts
    // guards against that idiom returning anywhere in the client.
    expect(path.isAbsolute(here)).toBe(true);
    expect(realpathSync(path.join(here, "viteFsAllow.test.ts"))).toContain("viteFsAllow.test.ts");
  });

  it("allows the icon font, wherever the workspace hoisted it", () => {
    // Arrange: resolve it the way the browser's request does - through the package, not a
    // guessed path, so a hoist to a different node_modules is still what gets checked.
    const require = createRequire(import.meta.url);
    const fontCss = require.resolve("@mdi/font/css/materialdesignicons.min.css");

    // Act.
    const allowed = isAllowed(fontCss);

    // Assert.
    expect(allowed, `${fontCss} is outside ${JSON.stringify(allow)}`).toBe(true);
  });

  it("allows this project and the diagram modules beside it", () => {
    // Arrange.
    const clientRoot = path.resolve(here, "..");
    const diagrams = path.resolve(clientRoot, "../diagrams");

    // Act & Assert: the two roots the previous allow list named explicitly must still be
    // covered, so widening to the workspace root did not trade one gap for another.
    expect(isAllowed(clientRoot)).toBe(true);
    expect(isAllowed(diagrams)).toBe(true);
  });

  it("does not allow the whole repository", () => {
    // Arrange: fs.allow is a dev-server boundary, so the fix must not open everything above
    // the workspace - the backend solution and the repo root stay out.
    const repoRoot = path.resolve(here, "../../..");

    // Act.
    const allowed = allow.some((root) => realpathSync(repoRoot) === root);

    // Assert.
    expect(allowed).toBe(false);
  });
});
