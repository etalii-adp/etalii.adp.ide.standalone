import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";

/**
 * The Add dialog's description of the selected type is never squeezed under its text.
 *
 * ## The defect (etalii.adp spec 092, T035, third run)
 *
 * `.dialog-body` is a flex column of a fixed height whose content scrolls (`min-height: 0`), so
 * every child it holds may shrink. The description is one of them: with the dependency graph's
 * two-line description its box was given 37px for 41px of text, and the second line was painted
 * over the "Name" label below it. A child of the body that must show all of its text has to say
 * that it does not shrink; jsdom lays nothing out, so this asserts the rule that says it.
 */
describe("the Add dialog's description", () => {
  const css = readFileSync(join(__dirname, "..", "..", "index.css"), "utf8");

  /** The declaration block of the rule whose selector is exactly `.name`. */
  function ownBlockOf(name: string): string | null {
    const match = new RegExp(
      `(?:^|\\})\\s*\\.${name}\\s*\\{([^}]*)\\}`,
      "m",
    ).exec(css);
    return match === null ? null : match[1]!;
  }

  it("sits in a body that may shrink its children", () => {
    // What makes the guard below necessary: if the body stops being a shrinking flex column,
    // this says so rather than the guard passing for a reason no longer true.
    const body = ownBlockOf("dialog-body");
    expect(body).not.toBeNull();
    expect(body).toMatch(/display:\s*flex/);
    expect(body).toMatch(/min-height:\s*0/);
  });

  it("does not shrink below its text", () => {
    const block = ownBlockOf("choice-description");
    expect(block).not.toBeNull();
    expect(block).toMatch(/flex-shrink:\s*0|flex:\s*none|flex:\s*0\s+0/);
  });
});
