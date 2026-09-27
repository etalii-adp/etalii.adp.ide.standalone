import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { GHG_MIME } from "./ghgIds";
import { registrations } from "./register";

/**
 * Task 22's guard: the catalog row, the example's `.adp` registration and the client's registration
 * name ONE origin - asserted from the three files, not read off the page by eye.
 */

const MODULE = join(__dirname, "..");
const REPOSITORY = join(MODULE, "..", "..", "..");

function catalogRow(): string {
  const rows = readFileSync(join(REPOSITORY, "docs", "diagrams.md"), "utf8")
    .split(/\r?\n/)
    .filter((line) => line.includes("gartner/hypecycle-graph</code>"));
  expect(rows, "the catalog has exactly one row naming this type's origin").toHaveLength(1);
  return rows[0];
}

function adpOrigin(): string {
  return readFileSync(join(MODULE, "examples", "technology-trends", "technology-trends.adp"), "utf8").split(/\r?\n/)[0];
}

describe("the hype cycle graph's origin, as the catalog, the example and the client state it", () => {
  it("is one string in all three places", () => {
    const origin = adpOrigin();

    expect(origin).toBe(GHG_MIME);
    expect(registrations.some((registration) => registration.matches(origin))).toBe(true);
    expect(catalogRow()).toContain(`<code>${origin}</code>`);
    expect(catalogRow()).toContain("gartner-hype-cycle-graph");
  });
});
