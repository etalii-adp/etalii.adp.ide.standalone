import { describe, expect, it } from "vitest";
import {
  getLanguageService,
  DiagnosticSeverity,
  MarkupKind,
  TextDocument,
  type Diagnostic,
  type LanguageService,
} from "vscode-json-languageservice";

/**
 * The spike of json-editor Requirement 1, kept as tests: `vscode-json-languageservice` loads in
 * this client and answers findings, completions and hover text for one fixed document and one
 * fixed schema, under this repository's own test runner. The same calls ran once in a production
 * build opened in a browser; that half is recorded in the design, not here, because jsdom is not
 * a browser.
 *
 * The schema is never fetched: the document lives under a URI of ADP's own scheme, its relative
 * `$schema` is resolved against that URI through the workspace context the module supplies, and
 * the request comes back through the callback below. That is the seam json-editor's design builds on (every schema request is seen
 * and decided by the module), so it is held here too.
 */

const documentUri = "adp-project:/settings/app.json";
const schemaUri = "adp-project:/settings/app.schema.json";

const schema = {
  $schema: "https://json-schema.org/draft/2020-12/schema",
  title: "Application settings",
  type: "object",
  required: ["name", "port"],
  additionalProperties: false,
  properties: {
    $schema: { type: "string" },
    name: { type: "string", description: "The name the application shows in its title bar." },
    port: { type: "integer", minimum: 1, maximum: 65535, description: "The port it listens on." },
    mode: { enum: ["development", "production"], description: "How it runs." },
  },
};

const text = `{
  "$schema": "./app.schema.json",
  "name": "ADP",
  "port": 70000,
  "colour": "green",
  "mode": ""
}`;

function serviceFor(schemas: Record<string, unknown>): { service: LanguageService; requested: string[] } {
  const requested: string[] = [];
  const service = getLanguageService({
    schemaRequestService: (uri) => {
      requested.push(uri);
      const found = schemas[uri];
      return found === undefined ? Promise.reject(`${uri} is not in the project`) : Promise.resolve(JSON.stringify(found));
    },
    // Without this the service resolves a relative `$schema` against nothing and asks for
    // `file:///./...`; with it, against the document's own URI.
    workspaceContext: { resolveRelativePath: (relativePath, resource) => new URL(relativePath, resource).href },
    clientCapabilities: {
      textDocument: {
        hover: { contentFormat: [MarkupKind.PlainText] },
        completion: { completionItem: { documentationFormat: [MarkupKind.PlainText] } },
      },
    },
  });
  return { service, requested };
}

async function validate(service: LanguageService, uri: string, content: string): Promise<Diagnostic[]> {
  const document = TextDocument.create(uri, "json", 1, content);
  return service.doValidation(document, service.parseJSONDocument(document), {
    comments: "error",
    trailingCommas: "error",
    schemaValidation: "error",
  });
}

function positionOf(content: string, needle: string, delta = 0) {
  const offset = content.indexOf(needle) + delta;
  const before = content.slice(0, offset).split("\n");
  return { line: before.length - 1, character: before[before.length - 1].length };
}

describe("vscode-json-languageservice in this client", () => {
  it("asks for the relative $schema through the module's callback, under the module's scheme", async () => {
    const { service, requested } = serviceFor({ [schemaUri]: schema });
    await validate(service, documentUri, text);
    expect(requested).toEqual([schemaUri]);
  });

  it("answers the schema's findings as errors, on the value or property concerned", async () => {
    const { service } = serviceFor({ [schemaUri]: schema });
    const findings = await validate(service, documentUri, text);
    const lines = findings.map((finding) => ({ line: finding.range.start.line, severity: finding.severity }));

    // port above its maximum, colour not allowed, mode not one of its values - and the document's
    // own `$schema` property, which the schema above declares, raises nothing.
    expect(lines).toEqual(
      expect.arrayContaining([
        { line: 3, severity: DiagnosticSeverity.Error },
        { line: 4, severity: DiagnosticSeverity.Error },
        { line: 5, severity: DiagnosticSeverity.Error },
      ]),
    );
    expect(findings.filter((finding) => finding.range.start.line === 1)).toEqual([]);
  });

  it("offers the properties not yet present, and the values an enumeration allows", async () => {
    const { service } = serviceFor({ [schemaUri]: schema });
    const partial = `{\n  "$schema": "./app.schema.json",\n  \n}`;
    const document = TextDocument.create(documentUri, "json", 1, partial);
    const properties = await service.doComplete(document, positionOf(partial, "  \n}", 2), service.parseJSONDocument(document));
    expect(properties?.items.map((item) => item.label).sort()).toEqual(["mode", "name", "port"]);

    const values = await service.doComplete(
      TextDocument.create(documentUri, "json", 1, text),
      positionOf(text, `"mode": ""`, 9),
      service.parseJSONDocument(TextDocument.create(documentUri, "json", 1, text)),
    );
    expect(values?.items.map((item) => item.label).sort()).toEqual(['"development"', '"production"']);
  });

  it("answers the schema's description as hover text, markdown-escaped whatever format is asked", async () => {
    const { service } = serviceFor({ [schemaUri]: schema });
    const document = TextDocument.create(documentUri, "json", 1, text);
    const hover = await service.doHover(document, positionOf(text, `"name"`, 2), service.parseJSONDocument(document));

    // Plain text was asked for above and the service still answers markdown-escaped strings
    // ("bar\\."), so the module unescapes them and sets the result as text, never as HTML.
    const contents = (hover?.contents ?? []) as string[];
    expect(contents.join("\n")).toContain("The name the application shows in its title bar\\.");
  });

  it("reports a schema it cannot load as a resolve error, not as silence", async () => {
    const { service } = serviceFor({});
    const findings = await validate(service, documentUri, text);
    expect(findings).toHaveLength(1);
    expect(findings[0].range.start.line).toBe(1);
    expect(Number(findings[0].code)).toBeGreaterThanOrEqual(0x300);
  });
});

/**
 * One test per dialect the library's source names, each with a keyword that only that dialect
 * gives meaning to (json-editor Requirement 6.5: the supported list is what these show, not what
 * memory says). Where the library gates the keyword by dialect, the same schema under the
 * previous dialect is shown to raise nothing, so the test proves the dialect was recognised.
 */
describe("the JSON Schema dialects the library supports", () => {
  async function findingsFor(dialect: string, body: Record<string, unknown>, value: unknown) {
    const { service } = serviceFor({ [schemaUri]: { $schema: dialect, ...body } });
    const content = JSON.stringify({ $schema: "./app.schema.json", value }, null, 2);
    const findings = await validate(service, documentUri, content);
    return findings.map((finding) => ({ line: finding.range.start.line, severity: finding.severity }));
  }

  const valueError = [{ line: 2, severity: DiagnosticSeverity.Error }];

  it("draft-04: a boolean exclusiveMaximum excludes the maximum itself", async () => {
    const body = { properties: { value: { maximum: 5, exclusiveMaximum: true } } };
    expect(await findingsFor("http://json-schema.org/draft-04/schema#", body, 5)).toEqual(valueError);
  });

  it("draft-06: const holds a value to exactly one", async () => {
    const body = { properties: { value: { const: "fixed" } } };
    expect(await findingsFor("http://json-schema.org/draft-06/schema#", body, "other")).toEqual(valueError);
  });

  it("draft-07: if and then apply a schema conditionally", async () => {
    const body = { properties: { value: { if: { type: "string" }, then: { minLength: 3 } } } };
    expect(await findingsFor("http://json-schema.org/draft-07/schema#", body, "ab")).toEqual(valueError);
  });

  it("2019-09: dependentRequired, which draft-07 does not know", async () => {
    const body = { properties: { value: { dependentRequired: { a: ["b"] } } } };
    expect(await findingsFor("https://json-schema.org/draft/2019-09/schema", body, { a: 1 })).toEqual(valueError);
    expect(await findingsFor("http://json-schema.org/draft-07/schema#", body, { a: 1 })).toEqual([]);
  });

  it("2020-12: prefixItems, which 2019-09 does not know", async () => {
    const body = { properties: { value: { prefixItems: [{ type: "string" }] } } };
    // The array is printed one item to a line, so the finding sits on the item, a line lower.
    expect(await findingsFor("https://json-schema.org/draft/2020-12/schema", body, [1])).toEqual([{ line: 3, severity: DiagnosticSeverity.Error }]);
    expect(await findingsFor("https://json-schema.org/draft/2019-09/schema", body, [1])).toEqual([]);
  });
});
