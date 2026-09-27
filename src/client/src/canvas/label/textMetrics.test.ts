import { readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { sourceFiles } from "../../sourceFiles";
import { capacityOf, fitToCapacity, fitToWidth, widthOf } from "./textMetrics";

/**
 * One character metric and one fit (client-centralization Requirement 4).
 *
 * <b>The sample fact</b> is the requirement's own sentence made executable: a label sized by the
 * metric is not then trimmed by the fit. It held nowhere before - the library sized content boxes
 * at 8 per character and trimmed them at 7.
 *
 * <b>The guard</b> fails on a second metric or a second fit anywhere in the client or a module's
 * client. It reads text, so its limit is the usual one: it finds the copy somebody makes by
 * starting from an existing canvas, which is how the five copies this replaced arose, and not a
 * reinvention written in a shape none of its patterns know.
 */

describe("the metric and the fit agree", () => {
  const texts = ["", "a", "Order", "Reconcile the ledger", "dct:contributor: mailto:chris.little@metoffice.gov.uk"];
  const fontSizes = [9, 10, 11, 12, 14, 15];

  it("never trims a text in the box the metric sized for it", () => {
    for (const fontSize of fontSizes) {
      for (const text of texts) {
        // The fit's padding is the one thing a box adds; the content sizing adds at least it.
        expect(fitToWidth(text, widthOf(text, fontSize) + 8, fontSize), `${text} at ${fontSize}`).toBe(text);
        expect(fitToWidth(text, widthOf(text, fontSize) + 16, fontSize), `${text} at ${fontSize}`).toBe(text);
      }
    }
  });

  it("trims a text one character too wide, keeping the result inside the box", () => {
    for (const fontSize of fontSizes) {
      for (const text of texts.filter((candidate) => candidate.length > 2)) {
        const width = widthOf(text.slice(1), fontSize) + 8;
        const fitted = fitToWidth(text, width, fontSize);

        expect(fitted.endsWith("…"), `${text} at ${fontSize}`).toBe(true);
        expect(widthOf(fitted, fontSize), `${text} at ${fontSize}`).toBeLessThanOrEqual(width - 8);
      }
    }
  });

  it("gives a wrapped line as many characters as the metric says fit", () => {
    for (const fontSize of fontSizes) {
      for (const count of [1, 3, 17, 40]) {
        expect(capacityOf(widthOf("x".repeat(count), fontSize), fontSize)).toBe(count);
      }
    }
  });

  it("cuts to a capacity with the ellipsis inside it", () => {
    expect(fitToCapacity("abcdef", 6)).toBe("abcdef");
    expect(fitToCapacity("abcdef", 4)).toBe("abc…");
    expect(fitToCapacity("abcdef", 1)).toBe("…");
  });
});

/** Code, with its comments blanked so prose about a metric is not mistaken for one. */
function codeOf(source: string): string {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, (comment) => comment.replace(/[^\n]/g, " "))
    .replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
}

/** What a second metric or a second fit reads like, each with the copy it was written from. */
const COPIES: readonly { name: string; reads: (line: string) => boolean }[] = [
  // `${text.slice(0, capacity - 1)}…` - the fit written again.
  { name: "an ellipsis cut by slice", reads: (line) => /\.slice\([^\n]*…/.test(line) },
  // `text.length * CHAR_WIDTH`, `(label?.length ?? 0) * 8 + 16` - a width from a character count.
  { name: "a width from a character count", reads: (line) => /\.length(?:\s*\?\?\s*0\))?\s*\*/.test(line) && /width/i.test(line) },
  // `/ (10 * 0.55)` - characters from a width by a per-character advance.
  { name: "a per-character advance", reads: (line) => /\/\s*\(\s*\d+(?:\.\d+)?\s*\*\s*0?\.\d+\s*\)/.test(line) || /\*\s*0\.55\b|\b0\.55\s*\*/.test(line) },
];

/** Every line of client code that reads as a copy, as `path:line  pattern`. */
function copiesIn(files: readonly string[], root: string): string[] {
  const found: string[] = [];
  for (const file of files) {
    codeOf(readFileSync(file, "utf8")).split("\n").forEach((line, index) => {
      for (const copy of COPIES) {
        if (copy.reads(line)) {
          found.push(`${relative(root, file).split(sep).join("/")}:${index + 1}  ${copy.name}`);
        }
      }
    });
  }
  return found;
}

/** The repository's `src`, found from this file: the folder holding both `diagrams/` and `.editorconfig`. */
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

describe("no text metric or fit outside textMetrics.ts", () => {
  it("reads each copy it exists to catch", () => {
    // The five copies this replaced, as they were written. A pattern that misses one of them
    // would report a clean tree for ever.
    const planted = [
      "  return text.length * CHAR_WIDTH;",
      "  const width = element.width ?? Math.max(DEFAULT_WIDTH, (element.label?.length ?? 0) * 8 + 16);",
      "  return capacity <= 1 ? \"…\" : `${label.slice(0, capacity - 1)}…`;",
      "  return Math.max(1, Math.floor((width - 2 * 12) / (10 * 0.55)));",
      "  return text.length <= budget ? text : `${text.slice(0, budget - 1).trimEnd()}…`;",
    ];
    for (const line of planted) {
      expect(COPIES.some((copy) => copy.reads(line)), line).toBe(true);
    }
    // And a comment about the metric is not code.
    expect(codeOf("// ~0.55em per character, `${text.slice(0, 3)}…`").trim()).toBe("");
  });

  it("finds none in the client or any module's client", () => {
    const root = sourceRoot();
    const files = [...sourceFiles(join(root, "client", "src")), ...sourceFiles(join(root, "diagrams"))]
      .filter((file) => /\.tsx?$/.test(file) && !/\.test\.tsx?$/.test(file))
      .filter((file) => !file.split(sep).includes("generated") && !file.split(sep).includes("backend"))
      .filter((file) => !file.endsWith(`${sep}textMetrics.ts`));

    // Enough files that a walk from the wrong folder cannot pass by finding nothing.
    expect(files.length).toBeGreaterThan(150);
    expect(copiesIn(files, root)).toEqual([]);
  });
});
