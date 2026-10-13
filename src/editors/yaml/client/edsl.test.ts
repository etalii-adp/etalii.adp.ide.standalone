import { readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { describe, expect, it } from "vitest";
import { sourceFiles } from "@client/sourceFiles";

/**
 * EDSL is a placeholder, so no `.eds` file is invented for this editor and no code reads one
 * (yaml-editor Requirement 10.3). What it would state is the list in the module's readme.
 */
const module = join(__dirname, "..");

describe("EDSL", () => {
  it("has no .eds file in the module, and no code that names one", () => {
    const files = sourceFiles(module);
    expect(files.length).toBeGreaterThan(10);
    expect(files.filter((file) => file.toLowerCase().endsWith(".eds")).map((file) => relative(module, file))).toEqual([]);

    const code = files.filter((file) => /\.(ts|tsx|cs)$/.test(file) && !file.endsWith("edsl.test.ts"));
    expect(code.filter((file) => /\.eds\b/i.test(readFileSync(file, "utf8"))).map((file) => relative(module, file))).toEqual([]);
  });
});
