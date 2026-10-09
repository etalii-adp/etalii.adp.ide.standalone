import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { sourceFiles } from "@client/sourceFiles";

/**
 * A designer module's client holds declarations and handlers, and renders nothing itself.
 *
 * ## Why this is asserted before the first designer module exists
 *
 * The diagram family learned it afterwards. Its canvases were written by copying the one before,
 * and the copy carried its markup with it: twenty-eight element types drew themselves through an
 * escape hatch while the declarative vocabulary beside it had no user at all, and
 * `declarativeModules.test.ts` exists because of that. A table has far more markup to copy than a
 * shape has - headers, rows, cells, editors, menus - and a module that draws one cell itself has
 * a table that looks and behaves unlike every other.
 *
 * So the rule is here first: a module client mounts the library's components and writes no
 * element of its own.
 *
 * ## What it can and cannot see
 *
 * **It reads text.** It finds an element written as JSX - a tag whose name starts with a lower
 * case letter - and an element made by `createElement("...")` with a string for its name. A
 * module that built markup some third way would pass. That limit is acceptable for the reason
 * the diagram guard gives: what happens is a copy, and a copy carries the shape it was copied
 * from.
 *
 * **The rule is tested on text of its own**, below, because the tree it walks may hold no
 * designer module at all - and a guard that has never been seen to find anything is not one.
 */

const DESIGNERS = join(__dirname, "..", "..", "..", "..", "designers");

/**
 * How many designer module clients there are today. Raise it in the change that adds one: a walk
 * that finds fewer than there are has stopped looking, and passes.
 */
const KNOWN_MODULE_CLIENTS = 0;

/** What a module client may not write, and how each is recognised on one line. */
const RULES: readonly { rule: string; matches: (line: string) => boolean }[] = [
  { rule: "an element written as JSX", matches: (line) => /<[a-z][a-z0-9]*(\s|>|\/>)/.test(line) },
  { rule: "an element made by createElement", matches: (line) => /\bcreateElement\(\s*["'`][a-z]/.test(line) },
];

export interface Offence {
  rule: string;
  line: number;
  text: string;
}

/** Every line of a source that writes an element, comments left out. */
export function offencesIn(source: string): Offence[] {
  const found: Offence[] = [];
  source.split(/\r?\n/).forEach((text, index) => {
    // A line that is only a comment describes the rule rather than breaking it.
    if (/^\s*(\/\/|\*|\/\*)/.test(text)) {
      return;
    }
    for (const { rule, matches } of RULES) {
      if (matches(text)) {
        found.push({ rule, line: index + 1, text: text.trim() });
      }
    }
  });
  return found;
}

/** The client folders of the designer family's modules, for those that have one. */
function moduleClients(): string[] {
  if (statSync(DESIGNERS, { throwIfNoEntry: false })?.isDirectory() !== true) {
    return [];
  }
  return readdirSync(DESIGNERS, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => join(DESIGNERS, entry.name, "client"))
    .filter((client) => statSync(client, { throwIfNoEntry: false })?.isDirectory() === true);
}

describe("a designer module's client renders nothing itself", () => {
  it("sees an element however a copy would write it", () => {
    // Assert: the positive controls. Each is a line a copied table would carry.
    expect(offencesIn("  return <table className=\"knowledge\">;").map((offence) => offence.rule)).toEqual(["an element written as JSX"]);
    expect(offencesIn("      <td>{value}</td>").map((offence) => offence.rule)).toEqual(["an element written as JSX"]);
    expect(offencesIn("  <input type=\"text\" />").map((offence) => offence.rule)).toEqual(["an element written as JSX"]);
    expect(offencesIn("  <br/>").map((offence) => offence.rule)).toEqual(["an element written as JSX"]);
    expect(offencesIn("  const cell = createElement(\"div\", null, value);").map((offence) => offence.rule)).toEqual(["an element made by createElement"]);
  });

  it("lets a module mount the library and say what it is", () => {
    // Assert: the negative controls. Mounting a component, a generic, a comparison and a comment are not elements.
    expect(offencesIn("  return <TableSurface model={model} definition={knowledgeTable} onGesture={edit} />;")).toEqual([]);
    expect(offencesIn("  const kinds = new Map<string, TableKindDefinition>();")).toEqual([]);
    expect(offencesIn("  if (count < limit) {")).toEqual([]);
    expect(offencesIn("  // never a <table> of its own")).toEqual([]);
    expect(offencesIn("   * the cells are <div> elements the library draws")).toEqual([]);
    expect(offencesIn("  const panel = createElement(TableSurface, props);")).toEqual([]);
  });

  it("walks all the module clients the designer family has", () => {
    // Assert: the canary. No module exists until the first is added; this number moves with it.
    expect(moduleClients().length).toBe(KNOWN_MODULE_CLIENTS);
  });

  it("finds no designer module writing an element", () => {
    // Act.
    const offenders = moduleClients().flatMap((client) =>
      sourceFiles(client)
        .filter((file) => /\.tsx?$/.test(file) && !/\.test\.tsx?$/.test(file))
        .flatMap((file) => offencesIn(readFileSync(file, "utf8")).map((offence) => `${file.slice(file.indexOf("designers"))}:${offence.line} ${offence.rule}: ${offence.text}`)),
    );

    // Assert: every offender named at once.
    expect(offenders).toEqual([]);
  });
});
