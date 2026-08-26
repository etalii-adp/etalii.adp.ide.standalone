import { describe, expect, it } from "vitest";
import { registrations } from "./register";

/** Which registration claims a MIME type, in the same order the shell's registry resolves. */
function claim(mimeType: string) {
  return registrations.find((registration) => registration.matches(mimeType));
}

describe("the Wardley map module's client registrations", () => {
  it("claims wardley/map for its canvas", () => {
    // Act.
    const registration = claim("wardley/map");

    // Assert.
    expect(registration?.Canvas).toBeDefined();
    expect(registration?.unsupported).toBeUndefined();
  });

  it("claims nothing else", () => {
    // Act and assert. An exact match rather than a `wardley/` prefix: this module carries one
    // notation, and a predicate broader than the thing it can draw would silently claim a type
    // it has no canvas for - the shell would then render an empty canvas instead of the
    // placeholder that explains why there is none.
    expect(claim("wardley/map+element")).toBeUndefined();
    expect(claim("wardley/value-chain")).toBeUndefined();
    expect(claim("freeplane/mindmap")).toBeUndefined();
    expect(claim("c4/context")).toBeUndefined();
    expect(claim("")).toBeUndefined();
  });
});
