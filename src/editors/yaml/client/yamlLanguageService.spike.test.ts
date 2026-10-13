import { describe, expect, it } from "vitest";
import { getLanguageService, type LanguageService } from "yaml-language-server/lib/esm/languageservice/yamlLanguageService";
import { TextDocument } from "vscode-languageserver-textdocument";

/**
 * The spike of yaml-editor Requirement 1, kept as tests: the language service of
 * `yaml-language-server` loads in this client and answers findings, completions and hover text
 * for one fixed file of two documents and one fixed schema, under this repository's own test
 * runner. The same calls ran once in a production build opened in a browser; that half is
 * recorded in the design, not here, because jsdom is not a browser.
 *
 * The schema is never found by the service: the module hands it over through the custom schema
 * provider under an identifier of its own, and the request for that identifier comes back through
 * the callback below. That is the seam yaml-editor's design builds on, so it is held here too.
 *
 * <b>The service caches a parsed document by its URI and version.</b> Two texts under one URI and
 * one version answer for whichever was parsed first, so every call below gets a version of its own.
 */

const documentUri = "adp-project:/pipelines/build.yaml";
const schemaId = "https://adp.invalid/pipelines/pipeline.schema.json";

const schema = {
  $schema: "http://json-schema.org/draft-07/schema#",
  title: "Pipeline",
  type: "object",
  required: ["name", "stages"],
  additionalProperties: false,
  properties: {
    name: { type: "string", description: "The name the pipeline shows in its runs." },
    retries: { type: "integer", maximum: 3 },
    mode: { enum: ["fast", "thorough"] },
    trigger: { type: "string", deprecationMessage: "Use triggers instead." },
    stages: { type: "array", items: { type: "object", required: ["id"], properties: { id: { type: "string" } } } },
  },
};

// Two documents, each breaking the schema once: the second's finding sits on its own line.
const text = `name: build
retries: 5
stages:
  - id: compile
---
name: deploy
mode: quickly
stages:
  - id: ship
`;

let version = 0;

function documentOf(content: string): TextDocument {
  return TextDocument.create(documentUri, "yaml", ++version, content);
}

function serviceFor(schemas: Record<string, unknown>): { service: LanguageService; requested: string[] } {
  const requested: string[] = [];
  const service = getLanguageService({
    schemaRequestService: (uri) => {
      requested.push(uri);
      const found = schemas[uri];
      return found === undefined ? Promise.reject(`${uri} is not in the project`) : Promise.resolve(JSON.stringify(found));
    },
    workspaceContext: { resolveRelativePath: (relativePath, resource) => new URL(relativePath, resource).href },
  });
  service.configure({ validate: true, hover: true, completion: true, format: false, hoverSchemaSource: false, yamlVersion: "1.2", customTags: [] });
  service.registerCustomSchemaProvider((uri) => Promise.resolve(uri === documentUri ? Object.keys(schemas) : []));
  return { service, requested };
}

function positionOf(content: string, needle: string, delta = 0) {
  const offset = content.indexOf(needle) + delta;
  const before = content.slice(0, offset).split("\n");
  return { line: before.length - 1, character: before[before.length - 1].length };
}

describe("yaml-language-server in this client", () => {
  it("asks for the schema the module names through its callback, and nothing else", async () => {
    const { service, requested } = serviceFor({ [schemaId]: schema });
    await service.doValidation(documentOf(text), false);
    expect([...new Set(requested)]).toEqual([schemaId]);
  });

  it("answers each document's findings on its own lines", async () => {
    const { service } = serviceFor({ [schemaId]: schema });
    const findings = await service.doValidation(documentOf(text), false);
    expect(findings.map((finding) => finding.range.start.line).sort()).toEqual([1, 6]);
  });

  /**
   * What tells the finding kinds apart (recorded in the design): the parser's own findings carry
   * the source `YAML`, the schema's carry `yaml-schema: ` and the schema's title, and both come
   * as warnings. A deprecation is a schema warning too, with nothing in `code` or `source` to
   * tell it from a violation; an unresolved alias is information from `YAML`.
   */
  it("tells the parser's findings from the schema's by source, and gives both no severity of error but a duplicate key", async () => {
    const { service } = serviceFor({ [schemaId]: schema });
    const content = `name: build\ntrigger: push\nretries: 5\nbase: *missing\nname: again\nstages: []\n`;
    const findings = await service.doValidation(documentOf(content), false);
    const seen = findings.map((finding) => ({ line: finding.range.start.line, severity: finding.severity, source: finding.source }));
    expect(seen).toEqual(
      expect.arrayContaining([
        { line: 1, severity: 2, source: "yaml-schema: Pipeline" },
        { line: 2, severity: 2, source: "yaml-schema: Pipeline" },
        { line: 3, severity: 3, source: "YAML" },
        { line: 4, severity: 1, source: "YAML" },
      ]),
    );
    expect(findings.find((finding) => finding.range.start.line === 1)?.message).toBe("Use triggers instead.");
  });

  it("places the finding of a value reached through an alias at the anchor, not at the alias", async () => {
    const nested = { ...schema, properties: { ...schema.properties, defaults: {}, retries: { type: "integer" } } };
    const { service } = serviceFor({ [schemaId]: nested });
    const content = `defaults: &value many\nname: build\nretries: *value\nstages: []\n`;
    const findings = await service.doValidation(documentOf(content), false);
    expect(findings.map((finding) => finding.range.start.line)).toEqual([0]);
  });

  it("offers the keys not yet present, and the values an enumeration allows", async () => {
    const { service } = serviceFor({ [schemaId]: schema });
    const keys = `name: build\n\n`;
    const keyList = await service.doComplete(documentOf(keys), { line: 1, character: 0 }, false);
    expect(keyList.items.filter((item) => item.kind === 10).map((item) => item.label).sort()).toEqual(["mode", "retries", "stages"]);

    const values = `name: build\nmode: \n`;
    const valueList = await service.doComplete(documentOf(values), positionOf(values, "mode: ", 6), false);
    expect(valueList.items.map((item) => item.label).sort()).toEqual(["fast", "thorough"]);
  });

  it("answers the schema's description as hover text, in markdown", async () => {
    const { service } = serviceFor({ [schemaId]: schema });
    // Validated first, as the editor does.
    const document = documentOf(text);
    await service.doValidation(document, false);
    const hover = await service.doHover(document, positionOf(text, "name", 1));
    const contents = hover?.contents as { kind: string; value: string };
    expect(contents.kind).toBe("markdown");
    expect(contents.value).toContain("The name the pipeline shows in its runs.");
  });
});

/**
 * One test per dialect the service's files name, each with a keyword only that dialect gives
 * meaning to. Where a keyword is new in a dialect, the same schema under the previous dialect is
 * shown to raise nothing, so the test proves the dialect was recognised and not merely tolerated.
 */
describe("the JSON Schema dialects the service supports", () => {
  async function findingLines(dialect: string, body: Record<string, unknown>, value: string): Promise<number[]> {
    const { service } = serviceFor({ [schemaId]: { $schema: dialect, ...body } });
    const findings = await service.doValidation(documentOf(`value: ${value}\n`), false);
    return findings.map((finding) => finding.range.start.line);
  }

  it("draft-04: a boolean exclusiveMaximum excludes the maximum itself", async () => {
    const body = { properties: { value: { maximum: 5, exclusiveMaximum: true } } };
    expect(await findingLines("http://json-schema.org/draft-04/schema#", body, "5")).toEqual([0]);
  });

  it("draft-07: if and then apply a schema conditionally", async () => {
    const body = { properties: { value: { if: { type: "string" }, then: { minLength: 3 } } } };
    expect(await findingLines("http://json-schema.org/draft-07/schema#", body, "ab")).toEqual([0]);
  });

  it("2019-09: dependentRequired, which draft-07 does not know", async () => {
    const body = { properties: { value: { dependentRequired: { a: ["b"] } } } };
    expect(await findingLines("https://json-schema.org/draft/2019-09/schema", body, "{ a: 1 }")).toEqual([0]);
    expect(await findingLines("http://json-schema.org/draft-07/schema#", body, "{ a: 1 }")).toEqual([]);
  });

  it("2020-12: prefixItems, which 2019-09 does not know", async () => {
    const body = { properties: { value: { prefixItems: [{ type: "string" }] } } };
    expect(await findingLines("https://json-schema.org/draft/2020-12/schema", body, "[1]")).toEqual([0]);
    expect(await findingLines("https://json-schema.org/draft/2019-09/schema", body, "[1]")).toEqual([]);
  });
});
