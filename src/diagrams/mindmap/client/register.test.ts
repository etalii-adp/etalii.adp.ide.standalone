import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the mindmap module's client registrations", () => {
  it("claims freeplane/mindmap for its canvas", () => {
    // Act.
    const registration = registrations.find((candidate) => candidate.matches("freeplane/mindmap"));

    // Assert.
    expect(registration?.Canvas).toBeDefined();
    expect(registration?.unsupported).toBeUndefined();
  });

  it("claims nothing else", () => {
    // Act and assert.
    expect(registrations.find((candidate) => candidate.matches("c4/context"))).toBeUndefined();
    expect(registrations.find((candidate) => candidate.matches("freeplane/other"))).toBeUndefined();
    expect(registrations.find((candidate) => candidate.matches(""))).toBeUndefined();
  });
});
