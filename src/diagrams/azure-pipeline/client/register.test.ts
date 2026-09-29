import { describe, expect, it } from "vitest";
import { registrations } from "./register";

/** Which registration claims a MIME type, in the same order the shell's registry resolves. */
function claim(mimeType: string) {
  return registrations.find((registration) => registration.matches(mimeType));
}

describe("the Azure Pipelines module's client registrations", () => {
  it("claims its own type for the canvas", () => {
    // Act.
    const registration = claim("azure-devops/pipeline");

    // Assert.
    expect(registration?.Panel).toBeDefined();
    expect(registration?.unsupported).toBeUndefined();
  });

  it("claims nothing else", () => {
    // Act and assert.
    // The predicate is an exact match rather than a prefix, so there is nothing here to grow
    // teeth later - a second azure-devops type would be a decision, not an accident.
    expect(claim("azure-devops/pipeline+stage")).toBeUndefined();
    expect(claim("azure-devops/release")).toBeUndefined();
    expect(claim("c4/context")).toBeUndefined();
    expect(claim("")).toBeUndefined();
  });

  it("matches the origin the backend declares", () => {
    // Assert.
    // The vendor/type pair here and the DiagramOrigin in the module's Diagram.cs are the same
    // string in two places, and a diagram that opens to a blank panel is how they disagree.
    expect(registrations.map((registration) => registration.matches("azure-devops/pipeline"))).toEqual([true]);
  });
});
