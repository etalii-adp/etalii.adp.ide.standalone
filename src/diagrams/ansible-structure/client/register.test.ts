import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the ansible-structure module's client registrations", () => {
  it("claims ansible/structure for its canvas", () => {
    // Act.
    const registration = registrations.find((candidate) => candidate.matches("ansible/structure"));

    // Assert.
    expect(registration?.Canvas).toBeDefined();
    expect(registration?.unsupported).toBeUndefined();
  });

  it("claims nothing else", () => {
    // Act and assert.
    expect(registrations.find((candidate) => candidate.matches("freeplane/mindmap"))).toBeUndefined();
    expect(registrations.find((candidate) => candidate.matches("ansible/other"))).toBeUndefined();
    expect(registrations.find((candidate) => candidate.matches(""))).toBeUndefined();
  });
});

describe("the shell's discovery", () => {
  it("finds this module through its own glob, with nothing added to the shell", async () => {
    // Act.
    // The real claim: register.ts exporting the right thing is only half of it - the shell has
    // to pick the file up without anyone editing a list in it.
    const { canvasFor } = await import("@client/shell/panels/diagramCanvases");

    // Assert.
    expect(canvasFor("ansible/structure")?.Canvas).toBeDefined();
  });
});
