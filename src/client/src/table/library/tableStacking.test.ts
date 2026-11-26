import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * What lies on top at the foot of a table, read from its stylesheet: jsdom lays nothing out, so a sticky bar under another
 * sticky bar passes every rendering test while a person can neither see nor press it (etalii.adp spec 092, T035: the question
 * before a row is deleted lay under the line of calculations, and Delete row seemed to do nothing).
 */
const css = readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "table.css"), "utf8");

function zIndexOf(selector: string): number {
  const rule = new RegExp(`(^|\\n)${selector.replace(/[.-]/g, (character) => `\\${character}`)}\\s*\\{([^}]*)\\}`).exec(css);
  expect(rule, `${selector} has no rule of its own`).not.toBeNull();
  const value = /z-index:\s*(\d+)/.exec(rule![2]!);
  return value === null ? 0 : Number(value[1]);
}

describe("the foot of a table", () => {
  it("puts a question to its author above the line of calculations", () => {
    expect(zIndexOf(".table-ask")).toBeGreaterThan(zIndexOf(".table-calculation-row"));
  });
});
