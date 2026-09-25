import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { FDG_MIME } from "./fdgIds";
import { registrations } from "./register";

/**
 * Task 16's second guard: the catalog row, the example's `.adp` registration and the client's
 * registration name ONE origin - asserted from the three files, not read off the page by eye.
 *
 * An `.adp` names its origin on its first line, the shell opens a canvas by matching that line, and
 * `docs/diagrams.md` tells a reader which origin a type has. Any two of them can drift without the
 * third noticing: a renamed mime would still register, and the example would stop opening.
 */

const MODULE = join(__dirname, "..");
const REPOSITORY = join(MODULE, "..", "..", "..");

/** The catalog's row for this type: the one line whose origin cell names it. */
function catalogRow(): string {
  const rows = readFileSync(join(REPOSITORY, "docs", "diagrams.md"), "utf8")
    .split(/\r?\n/)
    .filter((line) => line.includes("functional-decomposition-graph</code>"));
  expect(rows, "the catalog has exactly one row naming this type's origin").toHaveLength(1);
  return rows[0];
}

/** What the example's registration says it is: its first line. */
function adpOrigin(): string {
  return readFileSync(join(MODULE, "examples", "field-service", "field-service.adp"), "utf8").split(/\r?\n/)[0];
}

describe("the functional decomposition graph's origin, as the catalog, the example and the client state it", () => {
  it("is one string in all three places", () => {
    // Act.
    const origin = adpOrigin();

    // Assert: the example's registration is the client's mime ...
    expect(origin).toBe(FDG_MIME);
    // ... which the client's registration actually claims ...
    expect(registrations.some((registration) => registration.matches(origin))).toBe(true);
    // ... and which the catalog's origin cell carries exactly.
    expect(catalogRow()).toContain(`<code>${origin}</code>`);
  });
});
