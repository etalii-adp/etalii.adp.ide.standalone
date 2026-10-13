import { describe, expect, it, vi } from "vitest";
import { appendFileSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import type { EditorCompletion, EditorFinding } from "@client/editors/editorAnalysis";
import type { SchemaFile } from "@client/editors/SchemaFiles";
import { sourceFiles } from "@client/sourceFiles";
import { createJsonAnalysis, grammarFindings, largestCheckedText, toEditorSnippet, type JsonAnalysisOptions } from "./jsonAnalysis";

/**
 * The analysis over small documents and schemas (json-editor Requirements 4 to 7). Findings are
 * asserted by the text they cover and their severity, never by the library's sentence, except
 * where the sentence is ADP's own: the reason a schema was refused.
 */

type Files = Record<string, string | "failed" | "loading">;

/** An analysis of `settings/app.json` over the given schema files, as the panel would make it. */
function analyse(files: Files = {}, extra: Partial<JsonAnalysisOptions> = {}) {
  const map = new Map<string, SchemaFile>(
    Object.entries(files).map(([key, value]) => [
      key,
      value === "failed"
        ? { state: "failed", reason: "was not found in the project" }
        : value === "loading"
          ? { state: "loading" }
          : { state: "loaded", text: value, revision: 1 },
    ]),
  );
  const needs: string[][][] = [];
  let current: ReadonlyMap<string, SchemaFile> = map;
  const analysis = createJsonAnalysis({
    documentPath: ["settings", "app.json"],
    schemaFiles: () => current,
    onNeeds: (paths) => needs.push(paths),
    ...extra,
  });
  // A file arriving is a new map, as SchemaFiles reports one.
  const arrive = (key: string, file: SchemaFile) => {
    current = new Map(current).set(key, file);
  };
  return { analysis, needs, arrive };
}

/** Each finding as the text it covers and its severity. */
function covered(text: string, findings: EditorFinding[]) {
  return findings.map((finding) => [text.slice(finding.from, finding.to), finding.severity]);
}

const serviceSchema = JSON.stringify({
  title: "Service settings",
  type: "object",
  required: ["name", "port"],
  additionalProperties: false,
  properties: {
    name: { type: "string", description: "The service's name." },
    port: { type: "integer", minimum: 1 },
    logLevel: { enum: ["error", "warning", "debug"], description: "How much is logged." },
    secure: { type: "boolean" },
    verbose: { type: "boolean", deprecated: true },
    limits: { type: "object", properties: { timeoutSeconds: { type: "number" } } },
    tags: { type: "array", items: { type: "string" } },
    mode: { const: "fast" },
  },
});

describe("jsonAnalysis: the mistakes of JSON itself", () => {
  it("marks a syntax error at its place, as an error (4.1)", async () => {
    // Arrange: a missing comma between two properties.
    const text = '{\n  "a": 1\n  "b": 2\n}\n';

    // Act.
    const findings = await analyse().analysis.findings(text);

    // Assert: on the property that follows the missing comma.
    expect(covered(text, findings)).toEqual([['"b"', "error"]]);
  });

  it("marks a repeated property name as a warning (4.2)", async () => {
    // Arrange.
    const text = '{ "a": 1, "a": 2 }';

    // Act.
    const findings = await analyse().analysis.findings(text);

    // Assert: the second is marked, as a warning, and nothing is an error.
    const second = text.lastIndexOf('"a"');
    expect(findings.some((finding) => finding.from === second && finding.severity === "warning")).toBe(true);
    expect(findings.every((finding) => finding.severity === "warning")).toBe(true);
  });

  it("marks a comment as an error (4.3)", async () => {
    // Arrange.
    const text = '{\n  // a note\n  "a": 1\n}';

    // Act.
    const findings = await analyse().analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([["// a note", "error"]]);
  });

  it("marks a trailing comma as an error (4.3)", async () => {
    // Arrange.
    const text = '{ "a": 1, }';

    // Act.
    const findings = await analyse().analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([[",", "error"]]);
  });

  it("marks a comment as an error even when the schema allows comments (4.3)", async () => {
    // Arrange: a schema that would switch the service's own comment check off.
    const text = '{ "$schema": "./app.schema.json", /* a note */ "a": 1 }';
    const { analysis } = analyse({ "settings/app.schema.json": JSON.stringify({ allowComments: true }) });

    // Act.
    const findings = await analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([["/* a note */", "error"]]);
  });

  it("finds nothing in well-formed JSON with no schema, and says No schema (5.6)", async () => {
    // Arrange.
    const { analysis } = analyse();

    // Act.
    const findings = await analysis.findings('{ "a": [1, 2.5, true, false, null, "x"] }');

    // Assert.
    expect(findings).toEqual([]);
    expect(analysis.status!("")).toBe("No schema");
  });
});

describe("jsonAnalysis: loading the service", () => {
  it("loads the service on first use and once, not when it is made", async () => {
    // Arrange.
    const load = vi.fn(() => import("vscode-json-languageservice"));
    const { analysis } = analyse({}, { loadService: load });
    expect(load).not.toHaveBeenCalled();

    // Act.
    await analysis.findings("{}");
    await analysis.findings("[]");

    // Assert.
    expect(load).toHaveBeenCalledTimes(1);
  });

  it("is the only file that imports the service, and imports it with import() alone", () => {
    // Arrange: every source of the module and of the client that could import it.
    const here = path.dirname(fileURLToPath(import.meta.url));
    const client = path.join(here, "../../../client/src");
    const sources = [...sourceFiles(here), ...sourceFiles(client)].filter((file) => /\.(ts|tsx)$/.test(file) && !/\.test\.tsx?$/.test(file));

    // Act.
    const importers = sources.filter((file) => readFileSync(file, "utf8").includes('"vscode-json-languageservice"'));
    const own = readFileSync(path.join(here, "jsonAnalysis.ts"), "utf8");

    // Assert: one importer, and no static import in it - the service is a chunk of its own.
    expect(importers.map((file) => path.basename(file))).toEqual(["jsonAnalysis.ts"]);
    expect(own).not.toMatch(/^import\s[^;]*from\s+"vscode-json-languageservice"/m);
    expect(own).toContain('import("vscode-json-languageservice")');
  });

  it("keeps colour and says checking is unavailable when the service does not load", async () => {
    // Arrange.
    const { analysis } = analyse({}, { loadService: () => Promise.reject(new Error("the chunk did not arrive")) });

    // Act.
    const findings = await analysis.findings("{}");

    // Assert.
    expect(findings).toEqual([]);
    expect(analysis.status!("")).toBe("Checking is unavailable: the chunk did not arrive");
  });

  it("catches a throw inside the service once, keeps the grammar's syntax findings and says support stopped", async () => {
    // Arrange: a service whose every use throws.
    const real = await import("vscode-json-languageservice");
    const getLanguageService = vi.fn(() => {
      throw new Error("boom");
    });
    const { analysis } = analyse({}, { loadService: () => Promise.resolve({ ...real, getLanguageService }) });

    // Act.
    const first = await analysis.findings('{ "a": 1 "b": 2 }');
    const second = await analysis.findings('{ "a": 1 "b": 2 }');

    // Assert: the grammar's finding stands in, and the service was not asked again.
    expect(first.length).toBeGreaterThan(0);
    expect(first.every((finding) => finding.severity === "error")).toBe(true);
    expect(second).toEqual(first);
    expect(getLanguageService).toHaveBeenCalledTimes(1);
    expect(analysis.status!("")).toBe("Schema support stopped: boom");
  });

  it("finds the grammar's errors where the text stops being JSON", () => {
    // Arrange and act.
    const text = '{ "a": 1 "b": 2 }';
    const findings = grammarFindings(text);

    // Assert.
    expect(findings.length).toBeGreaterThan(0);
    expect(findings[0].from).toBeGreaterThanOrEqual(text.indexOf("1"));
  });

  it("validates nothing above the size bound and says so", async () => {
    // Arrange.
    const text = `[${"1,".repeat(largestCheckedText / 2)}1]`;
    const { analysis } = analyse();

    // Act.
    const findings = await analysis.findings(text);

    // Assert.
    expect(text.length).toBeGreaterThan(largestCheckedText);
    expect(findings).toEqual([]);
    expect(analysis.status!("")).toBe("Too large to check");
  });
});

describe("jsonAnalysis: which schema applies", () => {
  it("asks for the document's schema file, and waits for it without schema findings", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "port": "x" }';
    const { analysis, needs } = analyse();

    // Act.
    const findings = await analysis.findings(text);

    // Assert.
    expect(needs.at(-1)).toEqual([["settings", "app.schema.json"]]);
    expect(findings).toEqual([]);
    expect(analysis.status!("")).toBe("Reading ./app.schema.json");
  });

  it("names the schema by its title, and by its reference when it has none (5.7)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json" }';
    const titled = analyse({ "settings/app.schema.json": serviceSchema }).analysis;
    const untitled = analyse({ "settings/app.schema.json": "{}" }).analysis;

    // Act.
    await titled.findings(text);
    await untitled.findings(text);

    // Assert.
    expect(titled.status!("")).toBe("Service settings");
    expect(untitled.status!("")).toBe("./app.schema.json");
  });

  it.each([
    ["a file that is not there", "./missing.schema.json", { "settings/missing.schema.json": "failed" as const }, "was not found in the project"],
    ["a web address", "https://example.com/app.schema.json", {}, "is a web address; ADP reads schemas from the project"],
    ["a path that leaves the project", "../../outside.schema.json", {}, "leaves the project"],
    ["a dialect the service does not carry", "http://json-schema.org/draft-06/schema#", {}, "is a JSON Schema dialect ADP cannot check against"],
  ])("says once, on the $schema line, why %s gives no schema (5.2, 5.5, 6.5)", async (_name, reference, files, reason) => {
    // Arrange.
    const text = `{ "$schema": "${reference}", "port": "x" }`;
    const { analysis } = analyse(files);

    // Act.
    const findings = await analysis.findings(text);

    // Assert: one information finding over the $schema property, naming the reference and the reason.
    expect(covered(text, findings)).toEqual([[`"$schema": "${reference}"`, "info"]]);
    expect(findings[0].message).toBe(`${reference} ${reason}`);
    expect(await analysis.completions!(text, text.indexOf('"port"'), true)).toBeNull();
  });

  it("says once that a schema which is not valid JSON cannot be used, with no schema findings (5.5)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "port": "x" }';
    const { analysis } = analyse({ "settings/app.schema.json": '{ "type": "object", ' });

    // Act.
    const findings = await analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([['"$schema": "./app.schema.json"', "info"]]);
    expect(await analysis.completions!(text, text.indexOf("}"), true)).toBeNull();
  });

  it("says once that a schema written in a dialect the service does not support cannot be used (6.5)", async () => {
    // Arrange: a schema in draft-03.
    const text = '{ "$schema": "./app.schema.json", "port": "x" }';
    const { analysis } = analyse({ "settings/app.schema.json": JSON.stringify({ $schema: "http://json-schema.org/draft-03/schema#", type: "object" }) });

    // Act.
    const findings = await analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([['"$schema": "./app.schema.json"', "info"]]);
  });

  it("validates a document that is itself a schema against its dialect (5.4)", async () => {
    // Arrange: `type` must be a name or a list of names.
    const text = '{ "$schema": "https://json-schema.org/draft/2020-12/schema", "type": 5 }';
    const { analysis } = analyse();

    // Act.
    const findings = await analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([["5", "error"]]);
    expect(analysis.status!("")).toBe("https://json-schema.org/draft/2020-12/schema");
  });

  it("follows a $ref to another file of the project, asking for it (5.9)", async () => {
    // Arrange: the schema keeps its definitions in a second file.
    const text = '{ "$schema": "./app.schema.json", "port": "x" }';
    const schema = JSON.stringify({ type: "object", properties: { port: { $ref: "./common/port.schema.json" } } });
    const { analysis, needs, arrive } = analyse({ "settings/app.schema.json": schema });

    // Act: first without the referred file, then with it.
    const before = await analysis.findings(text);
    arrive("settings/common/port.schema.json", { state: "loaded", text: '{ "type": "integer" }', revision: 1 });
    const after = await analysis.findings(text);

    // Assert.
    expect(needs.at(-1)).toContainEqual(["settings", "common", "port.schema.json"]);
    expect(before).toEqual([]);
    expect(covered(text, after)).toEqual([['"x"', "error"]]);
  });

  it("refuses a $ref that leaves the project, with the reason (5.9)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "port": "x" }';
    const schema = JSON.stringify({ type: "object", properties: { port: { $ref: "../../outside.schema.json" } } });
    const { analysis, needs } = analyse({ "settings/app.schema.json": schema });

    // Act.
    const findings = await analysis.findings(text);

    // Assert: never asked for, and said once.
    expect(needs.flat().some((segments) => segments.includes("outside.schema.json"))).toBe(false);
    expect(covered(text, findings)).toEqual([['"$schema": "./app.schema.json"', "info"]]);
    expect(findings[0].message).toBe("../../outside.schema.json leaves the project");
  });

  it("finishes on a schema that refers to itself, within a time limit", async () => {
    // Arrange: a schema that is its own item, through itself and through its own file.
    const text = '{ "$schema": "./app.schema.json", "child": { "child": { "child": 1 } } }';
    const schema = JSON.stringify({
      type: "object",
      properties: { child: { anyOf: [{ $ref: "#" }, { $ref: "./app.schema.json" }, { type: "string" }] } },
    });
    const { analysis } = analyse({ "settings/app.schema.json": schema });

    // Act.
    const started = performance.now();
    const findings = await analysis.findings(text);

    // Assert.
    expect(performance.now() - started).toBeLessThan(2000);
    expect(findings.some((finding) => text.slice(finding.from, finding.to) === "1")).toBe(true);
  });
});

describe("jsonAnalysis: validation against the schema", () => {
  const files = { "settings/app.schema.json": serviceSchema };

  it("marks each break of the schema on the value or property concerned, as an error (6.1)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "name": "orders", "port": "8080", "logLevel": "chatty", "region": "west" }';

    // Act.
    const findings = await analyse(files).analysis.findings(text);

    // Assert: the $schema property's own "not allowed" is dropped; region's stays.
    expect(covered(text, findings)).toEqual(
      expect.arrayContaining([
        ['"8080"', "error"],
        ['"chatty"', "error"],
        ['"region"', "error"],
      ]),
    );
    expect(findings).toHaveLength(3);
  });

  it("marks a missing required property on the object that lacks it (6.2)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "name": "orders" }';

    // Act.
    const findings = await analyse(files).analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([[text, "error"]]);
    expect(findings[0].message).toContain("port");
  });

  it("marks a deprecated property as a warning (6.3)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "name": "orders", "port": 1, "verbose": true }';

    // Act.
    const findings = await analyse(files).analysis.findings(text);

    // Assert.
    expect(covered(text, findings)).toEqual([['"verbose": true', "warning"]]);
  });

  it("drops the not-allowed finding on $schema by where it sits, not by its sentence", async () => {
    // Arrange: a second, data property literally called "$schema" deeper down is still reported.
    const schema = JSON.stringify({ type: "object", additionalProperties: false, properties: { inner: { type: "object", additionalProperties: false } } });
    const text = '{ "$schema": "./app.schema.json", "inner": { "$schema": "x" } }';

    // Act.
    const findings = await analyse({ "settings/app.schema.json": schema }).analysis.findings(text);

    // Assert.
    expect(findings.map((finding) => finding.from)).toEqual([text.lastIndexOf('"$schema"')]);
  });

  it("gives the schema findings it can still read in a broken document, beside the syntax ones (6.4, 6.6)", async () => {
    // Arrange: a wrong type, then the text breaks off; and a property both repeated and not allowed.
    const text = '{ "$schema": "./app.schema.json", "name": "orders", "port": "x", "region": 1, "region": 2, ';

    // Act.
    const findings = await analyse(files).analysis.findings(text);

    // Assert: the type error, both readings of the repeated region, and the syntax error at the end.
    const seen = covered(text, findings);
    expect(seen).toContainEqual(['"x"', "error"]);
    expect(seen).toContainEqual(['"region"', "warning"]);
    expect(seen).toContainEqual(['"region"', "error"]);
    expect(findings.some((finding) => finding.source === "json" && finding.from >= text.lastIndexOf(","))).toBe(true);
  });
});

/** The text after an item is accepted, with each tab stop filled as a user would. */
function accept(text: string, item: EditorCompletion): string {
  const filled = item.insert
    .replace(/\$\{\d+:([^}]*)\}/g, "$1")
    .replace(/(.)\$\{\d+\}(.)/g, (_match, before: string, after: string) => {
      const bracketed = (before === "{" && after === "}") || (before === "[" && after === "]") || (before === '"' && after === '"');
      return `${before}${bracketed ? "" : "null"}${after}`;
    })
    .replace(/\$\{\d+\}/g, "null")
    .replace(/\\([{}])/g, "$1");
  return text.slice(0, item.from) + filled + text.slice(item.to);
}

describe("jsonAnalysis: completion from the schema", () => {
  const files = { "settings/app.schema.json": serviceSchema };

  it("offers the properties not yet present, required ones first, with their descriptions (7.1, 7.3)", async () => {
    // Arrange: name is present, port is required and missing.
    const text = '{\n  "$schema": "./app.schema.json",\n  "name": "orders",\n  \n}';
    const { analysis } = analyse(files);

    // Act.
    const list = await analysis.completions!(text, text.indexOf("  \n") + 2, true);

    // Assert.
    const labels = list!.items.map((item) => item.label);
    expect(labels).toContain("port");
    expect(labels).toContain("logLevel");
    expect(labels).not.toContain("name");
    expect(list!.items.find((item) => item.label === "port")!.boost).toBeGreaterThan(0);
    expect(list!.items.find((item) => item.label === "logLevel")!.boost).toBeUndefined();
    expect(list!.items.find((item) => item.label === "logLevel")!.info).toBe("How much is logged.");
  });

  it("offers the values the schema limits a value to (7.2)", async () => {
    // Arrange.
    const values = async (property: string) => {
      const text = `{ "$schema": "./app.schema.json", "${property}":  }`;
      const list = await analyse(files).analysis.completions!(text, text.indexOf(":  ") + 2, true);
      return list!.items.map((item) => item.label);
    };

    // Act and assert.
    expect(await values("logLevel")).toEqual(expect.arrayContaining(['"error"', '"warning"', '"debug"']));
    expect(await values("secure")).toEqual(expect.arrayContaining(["true", "false"]));
    expect(await values("mode")).toEqual(['"fast"']);
  });

  it("inserts well-formed JSON for every kind it offers, a comma included where one is needed (7.4)", async () => {
    // Arrange: the caret between two properties, so an insert needs a comma after it.
    const text = '{\n  "$schema": "./app.schema.json",\n  \n  "name": "orders"\n}';
    const offset = text.indexOf("  \n") + 2;
    const { analysis } = analyse(files);

    // Act.
    const list = await analysis.completions!(text, offset, true);

    // Assert: every property inserted, its tab stops filled, leaves text that parses.
    expect(list!.items.length).toBeGreaterThan(5);
    for (const item of list!.items) {
      expect(() => JSON.parse(accept(text, item)), item.label).not.toThrow();
    }
  });

  it("inserts a value that parses where a value is asked for (7.4)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "logLevel":  }';
    const { analysis } = analyse(files);

    // Act.
    const list = await analysis.completions!(text, text.indexOf(":  ") + 2, true);

    // Assert.
    for (const item of list!.items) {
      expect(() => JSON.parse(accept(text, item)), item.label).not.toThrow();
    }
  });

  it("offers nothing without a schema, and guesses nothing (7.7)", async () => {
    // Arrange: a document with plenty to guess from.
    const text = '{ "name": "orders", "port": 1, \n }';
    const load = vi.fn(() => import("vscode-json-languageservice"));

    // Act.
    const list = await analyse({}, { loadService: load }).analysis.completions!(text, text.indexOf("\n"), true);

    // Assert.
    expect(list).toBeNull();
  });

  it("describes a property under the pointer in plain text (7.6)", async () => {
    // Arrange.
    const text = '{ "$schema": "./app.schema.json", "name": "orders" }';
    const { analysis } = analyse(files);

    // Act.
    const hover = await analysis.hover!(text, text.indexOf('"name"') + 2);

    // Assert: the service's markdown escapes are gone.
    expect(hover?.text).toBe("The service's name.");
    expect(text.slice(hover!.from, hover!.to)).toBe('"name"');
  });
});

describe("toEditorSnippet", () => {
  it.each([
    ['"port": $1', '"port": ${1}'],
    ['"port": ${1:0}', '"port": ${1:0}'],
    ['"tags": [$1]', '"tags": [${1}]'],
    ['"a": "$1", $0', '"a": "${1}", ${0}'],
    ['"price": "\\$5"', '"price": "$5"'],
    ['"hash": "#{x}"', '"hash": "#\\{x}"'],
  ])("turns %s into %s", (input, expected) => {
    // Arrange, act and assert.
    expect(toEditorSnippet(input)).toBe(expected);
  });
});

describe("jsonAnalysis: the Performance section's bounds", () => {
  it("answers findings within a second and completions within 300 ms for 5,000 lines against a 2,000-line schema", async () => {
    // Arrange: the design's measured case - a schema of 2,000 lines and a document of 5,000.
    const { schema, document } = performanceCase();
    expect(schema.split("\n").length).toBeGreaterThanOrEqual(2000);
    expect(document.split("\n").length).toBeGreaterThanOrEqual(5000);
    const { analysis } = analyse({ "settings/app.schema.json": schema });
    await analysis.findings(document); // the service's chunk and the schema's first reading

    // Act.
    const findingsStarted = performance.now();
    const findings = await analysis.findings(document);
    const findingsTook = performance.now() - findingsStarted;
    const offset = document.indexOf('"item0":') + 1;
    const completionStarted = performance.now();
    const list = await analysis.completions!(document, document.indexOf("{", offset) + 1, true);
    const completionTook = performance.now() - completionStarted;

    // Assert.
    // JSON_EDITOR_TIMINGS names a file the measured times are added to, for the implementation log.
    const timings = process.env.JSON_EDITOR_TIMINGS;
    if (timings) {
      appendFileSync(timings, `findings ${findingsTook.toFixed(1)} ms, completion ${completionTook.toFixed(1)} ms\n`);
    }
    expect(findings.length).toBeGreaterThan(0);
    expect(list).not.toBeNull();
    expect(findingsTook).toBeLessThan(1000);
    expect(completionTook).toBeLessThan(300);
  });
});

/** A 2,000-line schema of 250 item kinds, and a 5,000-line document of 500 items breaking it once each fifty. */
function performanceCase() {
  const definitions: Record<string, unknown> = {};
  for (let index = 0; index < 250; index++) {
    definitions[`kind${index}`] = {
      type: "object",
      required: ["id"],
      properties: { id: { type: "integer" }, label: { type: "string" }, weight: { type: "number", minimum: 0 } },
    };
  }
  const schema = JSON.stringify({ type: "object", additionalProperties: { $ref: "#/$defs/kind7" }, $defs: definitions }, null, 2);
  const lines = ["{", '  "$schema": "./app.schema.json",'];
  for (let index = 0; index < 500; index++) {
    lines.push(`  "item${index}": {`, `    "id": ${index},`, `    "label": "item ${index}",`, `    "weight": ${index % 50 === 0 ? -1 : 1.5},`);
    lines.push(`    "a": 1,`, `    "b": 2,`, `    "c": 3,`, `    "d": 4,`, `    "e": 5`, `  }${index === 499 ? "" : ","}`);
  }
  lines.push("}");
  return { schema, document: lines.join("\n") };
}
