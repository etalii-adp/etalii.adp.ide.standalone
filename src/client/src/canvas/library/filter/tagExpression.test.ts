import { describe, expect, it } from "vitest";
import { matchesTags, parseTagExpression, type TagExpression } from "./tagExpression";

/** The expression a text must parse to, or the test fails saying why it did not. */
function parsed(text: string): TagExpression | null {
  const result = parseTagExpression(text);
  if (!result.ok) {
    throw new Error(`"${text}" did not parse: ${result.message}`);
  }
  return result.expression;
}

const matches = (text: string, tags: readonly string[]) => matchesTags(parsed(text), tags);

describe("a tag expression", () => {
  it("matches with and, or and parentheses", () => {
    expect(matches("energy and (transport or industry)", ["energy", "industry"])).toBe(true);
    expect(matches("energy and (transport or industry)", ["energy"])).toBe(false);
    expect(matches("transport or energy", ["transport"])).toBe(true);
    expect(matches("communication and computing", ["communication"])).toBe(false);
  });

  it("binds and tighter than or: a or b and c is a or (b and c)", () => {
    // `a` alone satisfies a or (b and c), and does not satisfy (a or b) and c.
    expect(matches("a or b and c", ["a"])).toBe(true);
    // `b` alone satisfies neither reading of the first, and it is the second that says so.
    expect(matches("a or b and c", ["b"])).toBe(false);
    expect(parsed("a or b and c")).toEqual({
      kind: "or",
      left: { kind: "tag", tag: "a" },
      right: { kind: "and", left: { kind: "tag", tag: "b" }, right: { kind: "tag", tag: "c" } },
    });
  });

  it("ignores case in keywords and tags alike", () => {
    expect(matches("Energy AND Transport", ["energy", "TRANSPORT"])).toBe(true);
  });

  it("treats an empty box as matching everything", () => {
    expect(parsed("   ")).toBeNull();
    expect(matchesTags(null, [])).toBe(true);
  });

  it("reports an unclosed parenthesis at its own position", () => {
    const result = parseTagExpression("energy and (transport or industry");
    expect(result).toEqual({ ok: false, message: "The parenthesis at 12 is never closed.", position: 11 });
  });

  it("reports a parenthesis that closes nothing at its position", () => {
    const result = parseTagExpression("energy) or transport");
    expect(result).toMatchObject({ ok: false, position: 6 });
  });

  it("reports a dangling keyword rather than guessing", () => {
    expect(parseTagExpression("energy and")).toMatchObject({ ok: false, position: 10 });
    expect(parseTagExpression("or energy")).toMatchObject({ ok: false, position: 0 });
  });
});
