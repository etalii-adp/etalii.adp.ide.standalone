import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { build, loadConfigFromFile, type Rolldown } from "vite";

/**
 * A module of Node that Vite cannot find for a browser is replaced by an empty placeholder, and
 * the build says so in a warning and carries on. The YAML editor's language service imports
 * Node's `path` in six files, and with the placeholder in its place the built bundle threw on its
 * first hover (`basename is not a function`) while every test under Node passed. `vite.config.ts`
 * aliases `path` to `path-browserify` for it (yaml-editor ruling A1).
 *
 * This builds the service's chunk with the resolve settings of `vite.config.ts` and fails when the
 * placeholder is in it. The build's warning is not what is asserted: it reaches the console from
 * inside the bundler and not through Vite's logger, so a test collecting it saw none either way.
 * Fails, that is, on any module left out for the browser, so the alias cannot be lost, and a library added later that
 * needs another replacement is found here and not in a user's browser.
 */
describe("the production build", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));

  it("leaves no module of Node out of the YAML editor's chunk", { timeout: 120_000 }, async () => {
    const loaded = await loadConfigFromFile({ command: "build", mode: "production" }, path.resolve(here, "../vite.config.ts"));
    const entry = "\0yaml-service-entry";
    const output = (await build({
      configFile: false,
      root: path.resolve(here, ".."),
      resolve: loaded?.config.resolve,
      logLevel: "error",
      plugins: [
        {
          name: "yaml-service-entry",
          resolveId: (id) => (id === entry ? id : null),
          load: (id) => (id === entry ? `import("yaml-language-server/lib/esm/languageservice/yamlLanguageService").then((m) => { globalThis.service = m.getLanguageService; });` : null),
        },
      ],
      build: { write: false, minify: false, rolldownOptions: { input: entry } },
    })) as Rolldown.RolldownOutput | Rolldown.RolldownOutput[];

    const chunks = (Array.isArray(output) ? output : [output]).flatMap((result) => result.output).filter((file) => file.type === "chunk");
    expect(chunks.some((chunk) => chunk.code.includes("getLanguageService"))).toBe(true);
    // Unminified, so the placeholder keeps its name: `__vite-browser-external` as a module id,
    // `__vite_browser_external` in the variable that imports it.
    expect(chunks.filter((chunk) => /__vite[-_]browser[-_]external/.test(chunk.code)).map((chunk) => chunk.fileName)).toEqual([]);
  });
});
