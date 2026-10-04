import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the agent behavior modelling registration", () => {
  it("claims etalii/agent-behavior-modelling and nothing else", () => {
    // Assert.
    const registration = registrations[0];
    expect(registrations).toHaveLength(1);
    expect(registration.matches("etalii/agent-behavior-modelling")).toBe(true);
    // A near miss: the element types travel as `<mime>+<type>`, and a prefix match would claim them.
    expect(registration.matches("etalii/agent-behavior-modelling+sequence")).toBe(false);
    expect(registration.matches("etalii/functional-decomposition-graph")).toBe(false);
    expect(registration.Panel).toBeDefined();
  });
});
