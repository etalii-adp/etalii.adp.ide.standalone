import { describe, expect, it } from "vitest";
import { parse } from "yaml";
import type { SchemaFile } from "@client/editors/SchemaFiles";
import type { EditorFinding } from "@client/editors/editorAnalysis";
import { createYamlAnalysis, loadYamlEngine, markdownToText, type YamlAnalysisHost } from "./yamlAnalysis";

/**
 * The YAML editor's analysis, one test per criterion of Requirements 4 to 7 that names a
 * behaviour. Ranges and severities are asserted, not the service's sentences, except where a
 * sentence is what the criterion asks for (the key a missing-key finding names, the column).
 */

const serviceSchema = {
  $schema: "http://json-schema.org/draft-07/schema#",
  title: "Service",
  type: "object",
  required: ["name", "image"],
  additionalProperties: false,
  properties: {
    name: { type: "string", description: "The **name** the service is known by." },
    image: { type: "string" },
    replicas: { type: "integer", maximum: 10 },
    restart: { enum: ["always", "never"] },
    memory: { type: "string", deprecationMessage: "Use limits instead." },
    legacy: { type: "string", deprecated: true },
    defaults: {},
    limits: { type: "object", additionalProperties: false, properties: { memory: { type: "string" }, cpu: { type: "number" } } },
    ports: { type: "array", items: { type: "object", required: ["port"], additionalProperties: false, properties: { port: { type: "integer" }, protocol: { enum: ["tcp", "udp"] } } } },
  },
};

const schemaLine = "# yaml-language-server: $schema=service.schema.json\n";

interface Harness {
  host: YamlAnalysisHost;
  needed: string[][];
  files: Map<string, SchemaFile>;
  statusChanges: number;
}

function harness(files: Record<string, SchemaFile | object | string> = { "app/service.schema.json": serviceSchema }): Harness {
  const map = new Map<string, SchemaFile>();
  for (const [key, value] of Object.entries(files)) {
    map.set(key, typeof value === "object" && "state" in value ? (value as SchemaFile) : { state: "loaded", text: typeof value === "string" ? value : JSON.stringify(value), revision: 1 });
  }
  const state: Harness = {
    needed: [],
    files: map,
    statusChanges: 0,
    host: {
      documentPath: ["app", "service.yaml"],
      schemaFiles: () => state.files,
      needSchemas: (paths) => {
        state.needed = paths.map((path) => [...path]);
      },
      statusChanged: () => {
        state.statusChanges++;
      },
    },
  };
  return state;
}

function at(text: string, needle: string, delta = 0): number {
  const offset = text.indexOf(needle);
  if (offset === -1) {
    throw new Error(`"${needle}" is not in the text`);
  }
  return offset + delta;
}

/** Findings as the line and column they start at, with their severity: what the user sees. */
function placed(text: string, findings: EditorFinding[]): string[] {
  return findings
    .map((finding) => {
      const before = text.slice(0, finding.from).split("\n");
      return `${before.length}:${before[before.length - 1].length + 1} ${finding.severity} ${text.slice(finding.from, finding.to)}`;
    })
    .sort();
}

describe("YAML's own mistakes (Requirement 4)", () => {
  it("marks each parser error at its place, with a sentence (4.1)", async () => {
    const text = "items: [1, 2\nnext: 3\n";
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(findings.length).toBeGreaterThan(0);
    expect(findings.every((finding) => finding.severity === "error" && finding.message.length > 0)).toBe(true);
    expect(findings.some((finding) => finding.from >= at(text, "next"))).toBe(true);
  });

  it("marks a tab used for indentation as an error on the tab (4.2)", async () => {
    const text = "a:\n\tb: 1\n";
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(placed(text, findings)).toEqual(["2:1 error \t"]);
    expect(findings[0].message).toContain("indent with spaces");
  });

  it("marks the second of two equal keys as an error (4.3)", async () => {
    const text = "name: a\nname: b\n";
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(placed(text, findings)).toEqual(["2:1 error n"]);
  });

  it("marks an alias that names no anchor as an error on the alias (4.4)", async () => {
    const text = "a: &one 1\nb: *one\nc: *two\n";
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(placed(text, findings)).toEqual(["3:4 error *two"]);
  });

  it("reads each document on its own: a mistake in one leaves the others' findings in place (4.5)", async () => {
    const text = `${schemaLine}name: web\nimage: x\nreplicas: 50\n---\nbroken: [\n---\n${schemaLine}name: worker\nimage: y\nrestart: sometimes\n`;
    const findings = await createYamlAnalysis(harness().host).findings(text);
    const lines = placed(text, findings);
    expect(lines.filter((line) => line.startsWith("4:"))).toEqual(["4:11 error 50"]);
    expect(lines.some((line) => line.startsWith("6:") || line.startsWith("7:"))).toBe(true);
    expect(lines.filter((line) => line.startsWith("11:"))).toEqual(["11:10 error sometimes"]);
  });

  it("answers for the text it is given, so an edit is read afresh (4.6)", async () => {
    const analysis = createYamlAnalysis(harness().host);
    expect(await analysis.findings("a: 1\na: 2\n")).toHaveLength(1);
    expect(await analysis.findings("a: 1\nb: 2\n")).toEqual([]);
  });

  it("names the column that was expected in a sentence about indentation (Usability)", async () => {
    const text = "a:\n    b: 1\n  c: 2\n";
    const [finding] = await createYamlAnalysis(harness().host).findings(text);
    expect(finding.severity).toBe("error");
    expect(finding.message).toContain("column 5 was expected");
  });

  it("constructs nothing from a tag, and reads an unknown tag as a plain value (Security)", async () => {
    const text = "run: !!python/object/apply:os.system [ls]\ncustom: !Ref other\n";
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(findings).toEqual([]);
    expect(parse(text, { version: "1.2" })).toEqual({ run: ["ls"], custom: "other" });
  });

  it("stops at the parser's bound on alias expansion, says so, and returns in under a second (Security)", async () => {
    const levels = ["a: &a [x, x, x, x, x, x, x, x, x]"];
    for (const name of "bcdefghi") {
      const previous = String.fromCharCode(name.charCodeAt(0) - 1);
      levels.push(`${name}: &${name} [${Array(9).fill(`*${previous}`).join(", ")}]`);
    }
    const text = `${schemaLine}${levels.join("\n")}\n`;
    const started = performance.now();
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(performance.now() - started).toBeLessThan(1000);
    expect(findings.filter((finding) => finding.severity === "error" && /alias/i.test(finding.message))).toHaveLength(1);
  });
});

describe("which schema applies (Requirement 5)", () => {
  it("reads the schema line before the first content, resolved against the document's folder (5.1)", async () => {
    const state = harness();
    const analysis = createYamlAnalysis(state.host);
    await analysis.findings(`${schemaLine}name: web\nimage: x\n`);
    expect(state.needed).toEqual([["app", "service.schema.json"]]);
    expect(analysis.status!("")).toBe("Service");
  });

  it("applies a line to its own document only, and a document without one has no schema (5.2)", async () => {
    const text = `${schemaLine}name: web\nimage: x\n---\nname: 5\nreplicas: lots\n`;
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(findings).toEqual([]);
  });

  it("says once, on the line, why a reference cannot be followed (5.3)", async () => {
    const text = "# yaml-language-server: $schema=https://example.org/s.json\nname: web\n";
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(placed(text, findings)).toEqual([`1:1 info ${text.split("\n")[0]}`]);
    expect(findings[0].message).toContain("web address");

    const missing = harness({ "app/service.schema.json": { state: "failed", reason: "was not found in the project" } });
    const notThere = await createYamlAnalysis(missing.host).findings(`${schemaLine}name: web\n`);
    expect(notThere.map((finding) => finding.severity)).toEqual(["info"]);

    const above = "# yaml-language-server: $schema=../../outside.json\nname: web\n";
    expect((await createYamlAnalysis(harness().host).findings(above))[0].message).toContain("leaves the project");
  });

  it("names the schema in the status line, or says No schema (5.4)", async () => {
    const analysis = createYamlAnalysis(harness().host);
    await analysis.findings("name: web\n");
    expect(analysis.status!("name: web\n")).toBe("No schema");
    await analysis.findings(`${schemaLine}name: web\nimage: x\n`);
    expect(analysis.status!("")).toBe("Service");
  });

  it("reads a schema written in YAML (5.5)", async () => {
    const yamlSchema = "title: Written in YAML\ntype: object\nproperties:\n  port:\n    type: integer\n";
    const state = harness({ "app/port.schema.yaml": yamlSchema });
    const text = "# yaml-language-server: $schema=port.schema.yaml\nport: eighty\n";
    const findings = await createYamlAnalysis(state.host).findings(text);
    expect(placed(text, findings)).toEqual(["2:7 error eighty"]);
  });

  it("validates again against a new reading of the schema, with no reopening", async () => {
    const state = harness();
    const analysis = createYamlAnalysis(state.host);
    const text = `${schemaLine}name: web\nimage: x\nreplicas: 50\n`;
    expect(await analysis.findings(text)).toHaveLength(1);

    const relaxed = { ...serviceSchema, properties: { ...serviceSchema.properties, replicas: { type: "integer" } } };
    state.files = new Map([["app/service.schema.json", { state: "loaded", text: JSON.stringify(relaxed), revision: 2 }]]);
    expect(await analysis.findings(text)).toEqual([]);
  });

  it("follows a $ref by the same rules, reading the schema it names from the project", async () => {
    const parent = { title: "Parent", type: "object", properties: { child: { $ref: "parts/child.schema.json" } } };
    const child = { type: "object", properties: { size: { type: "integer" } } };
    const state = harness({ "app/parent.schema.json": parent, "app/parts/child.schema.json": child });
    const text = "# yaml-language-server: $schema=parent.schema.json\nchild:\n  size: big\n";
    const findings = await createYamlAnalysis(state.host).findings(text);
    expect(state.needed).toEqual([["app", "parent.schema.json"], ["app", "parts", "child.schema.json"]]);
    expect(placed(text, findings)).toEqual(["3:9 error big"]);
  });

  it("refuses a $ref that leaves the project, and says so once on the schema line", async () => {
    const parent = { title: "Parent", type: "object", properties: { away: { $ref: "../../away.json" } } };
    const state = harness({ "app/parent.schema.json": parent });
    const text = "# yaml-language-server: $schema=parent.schema.json\naway: 1\n";
    const findings = await createYamlAnalysis(state.host).findings(text);
    expect(placed(text, findings)).toEqual([`1:1 info ${text.split("\n")[0]}`]);
    expect(findings[0].message).toContain("leaves the project");
  });
});

describe("validation against the schema (Requirement 6)", () => {
  it("marks every place the data breaks the schema as an error, on the key or value concerned (6.1)", async () => {
    const text = `${schemaLine}name: web\nimage: x\nreplicas: 50\nrestart: sometimes\ncolour: green\n`;
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(placed(text, findings)).toEqual(["4:11 error 50", "5:10 error sometimes", "6:1 error colour"]);
  });

  it("puts a missing key on the mapping that lacks it, naming the key (6.2)", async () => {
    const text = `${schemaLine}name: web\nports:\n  - protocol: tcp\n`;
    const findings = await createYamlAnalysis(harness().host).findings(text);
    const image = findings.find((finding) => finding.message.includes('"image"'));
    const port = findings.find((finding) => finding.message.includes('"port"'));
    expect(image?.severity).toBe("error");
    expect(image!.from).toBeGreaterThanOrEqual(at(text, "name"));
    expect(port!.from).toBe(at(text, "protocol"));
  });

  it("validates a value reached through an alias or a merge key, with the finding at the alias too (6.3)", async () => {
    const text = `${schemaLine}name: web\nimage: x\ndefaults: &bad\n  protocol: http\nports:\n  - port: 1\n  - *bad\n`;
    const findings = await createYamlAnalysis(harness().host).findings(text);
    const lines = placed(text, findings);
    expect(lines).toContain("8:5 error *bad");

    const merged = `${schemaLine}name: web\nimage: x\ndefaults: &limits\n  cpu: lots\nlimits:\n  <<: *limits\n`;
    const mergedLines = placed(merged, await createYamlAnalysis(harness().host).findings(merged));
    expect(mergedLines).toContain("7:7 error *limits");
  });

  it("marks a deprecated key as a warning, by deprecationMessage and by deprecated (6.4)", async () => {
    const text = `${schemaLine}name: web\nimage: x\nmemory: 1Gi\nlegacy: yes\n`;
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(findings.map((finding) => ({ line: placed(text, [finding])[0], message: finding.message }))).toEqual(
      expect.arrayContaining([
        { line: "4:1 warning memory: 1Gi", message: "Use limits instead." },
        { line: "5:1 warning legacy: yes", message: "This key is deprecated." },
      ]),
    );
    expect(findings.filter((finding) => finding.severity === "error")).toEqual([]);
  });

  it("invents no schema finding for a document that could not be read (6.5)", async () => {
    const text = `${schemaLine}name: web\nreplicas: [50\n`;
    const findings = await createYamlAnalysis(harness().host).findings(text);
    expect(findings.length).toBeGreaterThan(0);
    expect(findings.every((finding) => finding.source === "yaml")).toBe(true);
  });

  it("says once that a dialect is not supported (6.6)", async () => {
    const state = harness({ "app/service.schema.json": { ...serviceSchema, $schema: "http://json-schema.org/draft-03/schema#" } });
    const text = `${schemaLine}name: 5\n`;
    const findings = await createYamlAnalysis(state.host).findings(text);
    expect(findings.map((finding) => finding.severity)).toEqual(["info"]);
    expect(findings[0].message).toContain("draft-03");
  });

  it("checks nothing above the size bound, and says so", async () => {
    const analysis = createYamlAnalysis(harness().host);
    const text = `${schemaLine}${"a: 1\n".repeat(250_000)}`;
    expect(await analysis.findings(text)).toEqual([]);
    expect(analysis.status!(text)).toBe("Too large to check");
  });

  it("keeps the parser's findings when the service throws, and says schema support stopped (Reliability)", async () => {
    const throwing = async () => {
      const engine = await loadYamlEngine();
      return {
        ...engine,
        getLanguageService: ((parameters: Parameters<typeof engine.getLanguageService>[0]) => ({
          ...engine.getLanguageService(parameters),
          doValidation: () => Promise.reject(new Error("the service broke")),
        })) as typeof engine.getLanguageService,
      };
    };
    const analysis = createYamlAnalysis(harness().host, throwing);
    const text = `${schemaLine}name: a\nname: b\n`;
    const findings = await analysis.findings(text);
    expect(placed(text, findings)).toEqual(["3:1 error n"]);
    expect(analysis.status!(text)).toBe("Schema support stopped: the service broke");
  });

  it("says checking is unavailable when the service cannot be loaded", async () => {
    const analysis = createYamlAnalysis(harness().host, () => Promise.reject(new Error("the chunk did not arrive")));
    expect(await analysis.findings("a: 1\n")).toEqual([]);
    expect(analysis.status!("a: 1\n")).toBe("Checking is unavailable: the chunk did not arrive");
  });
});

describe("completion and hover from the schema (Requirement 7)", () => {
  async function labels(text: string, offset: number) {
    const list = await createYamlAnalysis(harness().host).completions!(text, offset, true);
    return list?.items ?? [];
  }

  it("offers the keys not yet present in the caret's mapping, required ones first (7.1)", async () => {
    const text = `${schemaLine}replicas: 2\n\n`;
    const items = await labels(text, text.length - 1);
    const keys = items.filter((item) => !item.detail);
    expect(keys.map((item) => item.label)).not.toContain("replicas");
    expect(keys.map((item) => item.label)).toEqual(expect.arrayContaining(["name", "image", "restart"]));
    const boosted = keys.filter((item) => (item.boost ?? 0) > 0).map((item) => item.label).sort();
    expect(boosted).toEqual(["image", "name"]);

    const nested = `${schemaLine}name: web\nlimits:\n  \n`;
    const inner = await labels(nested, nested.length - 1);
    expect(inner.map((item) => item.label).sort()).toEqual(["cpu", "memory"]);
  });

  it("offers the values the schema limits a value to (7.2)", async () => {
    const text = `${schemaLine}restart: \n`;
    expect((await labels(text, text.length - 1)).map((item) => item.label).sort()).toEqual(["always", "never"]);
  });

  it("offers a new item on an empty line of a described sequence (7.3)", async () => {
    const text = `${schemaLine}ports:\n  - port: 1\n  \n`;
    const items = await labels(text, text.length - 1);
    expect(items.some((item) => item.insert.startsWith("- "))).toBe(true);
  });

  it("inserts well-formed YAML at the caret's depth, in spaces, for every kind of item (7.4)", async () => {
    for (const [text, caretFromEnd] of [
      [`${schemaLine}\n`, 1],
      [`${schemaLine}name: web\nlimits:\n  \n`, 1],
      [`${schemaLine}ports:\n  - port: 1\n  \n`, 1],
      [`${schemaLine}restart: \n`, 1],
    ] as [string, number][]) {
      const offset = text.length - caretFromEnd;
      for (const item of await labels(text, offset)) {
        // Applied as the shared presentation applies it, tab stops dropped as an accept leaves them.
        const inserted = item.insert.replace(/\$\{\d+(?::([^}]*))?\}/g, (_match, value: string | undefined) => value ?? "x");
        const result = text.slice(0, item.from) + inserted + text.slice(Math.max(item.to, offset));
        expect(result, `${item.label} into ${JSON.stringify(text)}`).not.toContain("\t");
        expect(() => parse(result, { version: "1.2", uniqueKeys: false }), `${item.label} gives ${JSON.stringify(result)}`).not.toThrow();
      }
    }
  });

  it("keeps a multi-line key at the caret's depth: its later lines go under it, not to the margin (7.4)", async () => {
    const nested = {
      type: "object",
      properties: { outer: { type: "object", properties: { inner: { type: "object", properties: { a: { type: "string" }, b: { type: "object", required: ["c"], properties: { c: { type: "integer" } } } } } } } },
    };
    const text = "# yaml-language-server: $schema=nested.schema.json\nouter:\n  inner:\n    \n";
    const offset = text.length - 1;
    const list = await createYamlAnalysis(harness({ "app/nested.schema.json": nested }).host).completions!(text, offset, true);
    const b = list!.items.find((item) => item.label === "b" && !item.insert.includes("${"))!;
    expect(b.insert).toContain("\n");

    const result = text.slice(0, b.from) + b.insert + "1" + text.slice(Math.max(b.to, offset));
    expect(parse(result)).toEqual({ outer: { inner: { b: { c: 1 } } } });
  });

  it("shows an item once when two would insert the same text (7.5's list)", async () => {
    const items = await labels(`${schemaLine}\n`, schemaLine.length);
    const inserts = items.map((item) => item.insert);
    expect(new Set(inserts).size).toBe(inserts.length);

    // The service answers twice with one insert; the list shows it once.
    const twice = async () => {
      const engine = await loadYamlEngine();
      return {
        ...engine,
        getLanguageService: ((parameters: Parameters<typeof engine.getLanguageService>[0]) => {
          const service = engine.getLanguageService(parameters);
          return {
            ...service,
            doComplete: async (...args: Parameters<typeof service.doComplete>) => {
              const answer = await service.doComplete(...args);
              return { ...answer, items: [...answer.items, ...answer.items] };
            },
          };
        }) as typeof engine.getLanguageService,
      };
    };
    const doubled = await createYamlAnalysis(harness().host, twice).completions!(`${schemaLine}\n`, schemaLine.length, true);
    expect(doubled!.items.map((item) => item.insert)).toEqual(inserts);
  });

  it("answers hover with the schema's description as text, without the source line (7.6)", async () => {
    const text = `${schemaLine}name: web\n`;
    const hover = await createYamlAnalysis(harness().host).hover!(text, at(text, "name", 1));
    // The service heads the text with the title of the schema it found the key in.
    expect(hover?.text).toBe("Service\n\nThe name the service is known by.");
    expect(markdownToText("#### Title\n\nThe \\*\\*name\\*\\*.\n\nSource: [s.json](https://adp.invalid/s.json)")).toBe("Title\n\nThe name.");
  });

  it("offers nothing without a schema (7.7)", async () => {
    const analysis = createYamlAnalysis(harness().host);
    expect(await analysis.completions!("name: web\n\n", 11, true)).toBeNull();
    expect(await analysis.hover!("name: web\n", 1)).toBeNull();
  });
});
