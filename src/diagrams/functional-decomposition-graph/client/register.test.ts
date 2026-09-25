import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the functional decomposition graph registration", () => {
  it("claims etalii/functional-decomposition-graph and nothing else", () => {
    // Assert.
    const registration = registrations[0];
    expect(registrations).toHaveLength(1);
    expect(registration.matches("etalii/functional-decomposition-graph")).toBe(true);
    // A near miss: the element types travel as `<mime>+<type>`, and a prefix match would claim them.
    expect(registration.matches("etalii/functional-decomposition-graph+ui-element")).toBe(false);
    expect(registration.matches("generic/dependencies")).toBe(false);
    expect(registration.Canvas).toBeDefined();
  });
});
