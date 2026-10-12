import { describe, expect, it } from "vitest";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import path from "node:path";
import { resolveSchemaReference, type SchemaTarget } from "./schemaReference";

interface ReferenceCase {
  name: string;
  reference: string;
  document: string[];
  bundled: string[];
  expected: SchemaTarget;
}

const here = path.dirname(fileURLToPath(import.meta.url));
const { cases } = JSON.parse(readFileSync(path.join(here, "../../../fixtures/schema-reference-cases.json"), "utf8")) as { cases: ReferenceCase[] };

describe("resolveSchemaReference, held to the shared cases", () => {
  it("reads the case file the XML editor's twin reads too", () => {
    // The liveness proof: an empty or renamed list must fail here, not pass over nothing.
    expect(cases.length).toBeGreaterThanOrEqual(20);
  });

  for (const testCase of cases) {
    it(testCase.name, () => {
      // Arrange and act.
      const target = resolveSchemaReference(testCase.reference, testCase.document, new Set(testCase.bundled));

      // Assert.
      expect(target).toEqual(testCase.expected);
    });
  }

  it("refuses rather than clamps where URL arithmetic would answer a file", () => {
    // Arrange: the case URL arithmetic gets wrong, worked both ways.
    const reference = "../../settings/app.schema.json";
    const clamped = new URL(reference, "adp-project:/settings/app.json").pathname;

    // Act.
    const target = resolveSchemaReference(reference, ["settings", "app.json"], new Set());

    // Assert. URL arithmetic reads /settings/app.schema.json, a file the document never named.
    expect(clamped).toBe("/settings/app.schema.json");
    expect(target).toEqual({ kind: "none", reason: "leaves the project" });
  });
});
