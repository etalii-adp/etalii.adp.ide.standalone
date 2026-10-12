import { describe, expect, it } from "vitest";
import type { SchemaFile } from "@client/editors/SchemaFiles";
import { completionWindow, windowAbove } from "./completionWindow";
import { createYamlAnalysis } from "./yamlAnalysis";

/**
 * A long document's completion is answered from a window of its lines (Performance). What is
 * held here is that the window answers as the whole document would: the same offers, at the same
 * places, as a short document with the caret's surroundings alone.
 */

const item = {
  type: "object",
  required: ["name", "image"],
  additionalProperties: false,
  properties: {
    name: { type: "string" },
    image: { type: "string" },
    restart: { enum: ["always", "never"] },
    limits: { type: "object", additionalProperties: false, properties: { memory: { type: "string" }, cpu: { type: "number" } } },
  },
};
const schema = { title: "Services", type: "object", properties: { version: { type: "string" }, services: { type: "array", items: item } } };
const files = new Map<string, SchemaFile>([["app/services.schema.json", { state: "loaded", text: JSON.stringify(schema), revision: 1 }]]);
const analysis = () => createYamlAnalysis({ documentPath: ["app", "services.yaml"], schemaFiles: () => files, needSchemas: () => {} });

const head = "# yaml-language-server: $schema=services.schema.json\nservices:\n";
/** Enough items before the caret's own that the document is read through the window. */
const filler = Array.from({ length: windowAbove }, (_, index) => `  - name: s${index}\n    image: i${index}\n    limits:\n      cpu: 1\n`).join("");

/** The offers at `|` in a document, with the caret's place marked by `|`. */
async function offers(text: string) {
  const offset = text.indexOf("|");
  const list = await analysis().completions!(text.replace("|", ""), offset, true);
  return (list?.items ?? []).map((entry) => `${entry.label} ${text.slice(0, offset).split("\n").length}:${entry.from - text.lastIndexOf("\n", offset - 1) - 1}`).sort();
}

/** The same caret in a long and in a short document: the long one's lines are the short one's, shifted. */
async function sameOffers(tail: string) {
  const first = filler.slice(0, filler.indexOf("  - name: s1\n"));
  const short = await offers(head + first + tail);
  const long = await offers(head + filler + tail);
  const shift = filler.split("\n").length - first.split("\n").length;
  expect(short.length).toBeGreaterThan(0);
  expect(long).toEqual(short.map((entry) => entry.replace(/ (\d+):/, (_match, line: string) => ` ${Number(line) + shift}:`)).sort());
}

describe("completion on a long document", () => {
  it("offers the keys of the caret's mapping that are not present, before and after the caret", async () => {
    await sameOffers("  - name: web\n    |\n    restart: always\n");
  });

  it("offers the keys of a nested mapping", async () => {
    await sameOffers("  - name: web\n    limits:\n      |\n");
  });

  it("offers the values a key is limited to", async () => {
    await sameOffers("  - name: web\n    restart: |\n");
  });

  it("offers the keys of a new item", async () => {
    await sameOffers("  - |\n");
  });

  it("offers the keys at the top level that are not present", async () => {
    await sameOffers("|\n");
    expect((await offers(head + filler + "|\n")).map((entry) => entry.split(" ")[0])).toEqual(["version"]);
  });
});

describe("completionWindow", () => {
  it("reads a document no longer than the bound whole", () => {
    expect(completionWindow(Array.from({ length: windowAbove }, () => "a: 1"), 0, 0)).toBeNull();
  });

  it("keeps the caret's line, the keys beside it, and each line that opens a collection around it", () => {
    const lines = [...Array.from({ length: windowAbove }, (_, index) => `x${index}: 1`), "services:", "  - name: web", "    limits:", "      cpu: 1", "    ", "    image: nginx", "  - name: other", "z: 1"];
    const caret = lines.indexOf("    ");
    const window = completionWindow(lines, caret, 4)!;
    expect(window.text.split("\n")).toEqual(["services:", "  - name: web", "    limits:", "      cpu: 1", "    ", "    image: nginx", ""]);
    expect(window.lines[window.caretLine]).toBe(caret);
  });
});
