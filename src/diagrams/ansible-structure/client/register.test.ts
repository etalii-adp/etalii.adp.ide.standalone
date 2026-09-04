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
  // This is the only test that imports the shell's canvas registry, and the registry globs
  // every diagram module - so this one test pays to import every canvas in the repository,
  // cold, in a fresh jsdom worker. The glob is the point of the test, so the cost is not
  // avoidable; but it grows with the repository, and it had quietly reached vitest's 5000ms
  // default. Adding two imports to another module's canvases was enough to tip it over, which
  // is a strange way to find out a module elsewhere had grown. The budget is stated rather than
  // left implicit, because an undeclared one fails in a module that did not change.
  it("finds this module through its own glob, with nothing added to the shell", async () => {
    // Act.
    // The real claim: register.ts exporting the right thing is only half of it - the shell has
    // to pick the file up without anyone editing a list in it.
    const { canvasFor } = await import("@client/shell/panels/diagramCanvases");

    // Assert.
    expect(canvasFor("ansible/structure")?.Canvas).toBeDefined();
  }, 15_000);
});
