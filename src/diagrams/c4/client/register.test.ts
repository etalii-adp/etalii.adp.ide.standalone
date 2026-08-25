import { describe, expect, it } from "vitest";
import { registrations } from "./register";

/** Which registration claims a MIME type, in the same order the shell's registry resolves. */
function claim(mimeType: string) {
  return registrations.find((registration) => registration.matches(mimeType));
}

describe("the C4 module's client registrations", () => {
  it.each([
    "c4/context",
    "c4/container",
    "c4/component",
    "c4/system-landscape",
    "c4/dynamic",
    "c4/deployment",
  ])("claims %s for the shared canvas", (mimeType) => {
    // Act.
    // Six views of one notation, so one canvas - not six.
    const registration = claim(mimeType);

    // Assert.
    expect(registration?.Canvas).toBeDefined();
    expect(registration?.unsupported).toBeUndefined();
  });

  it("sends c4/code to its own notice rather than the canvas", () => {
    // Act.
    // C4 specifies UML class or ER notation for the code level and advises generating it
    // rather than drawing it, so this type waits on a class diagram type.
    const registration = claim("c4/code");

    // Assert.
    expect(registration?.Canvas).toBeUndefined();
    expect(registration?.unsupported?.description).toMatch(/UML class or entity-relationship notation/);
    expect(registration?.unsupported?.futureSpec).toBe("c4-diagrams");
  });

  it("claims nothing outside its own vendor", () => {
    // Act and assert.
    // The canvas entry matches on a `c4/` prefix, which is the kind of predicate that quietly
    // grows teeth - `c4x/` or another vendor's `c4`-ish type must not fall into it.
    expect(claim("freeplane/mindmap")).toBeUndefined();
    expect(claim("mermaid/c4")).toBeUndefined();
    expect(claim("")).toBeUndefined();
  });
});
