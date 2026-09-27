import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the hype cycle graph registration", () => {
  it("claims gartner/hypecycle-graph and nothing else", () => {
    const registration = registrations[0];
    expect(registrations).toHaveLength(1);
    expect(registration.matches("gartner/hypecycle-graph")).toBe(true);
    // A near miss: the element types travel as `<mime>+<type>`, and a prefix match would claim them.
    expect(registration.matches("gartner/hypecycle-graph+trend")).toBe(false);
    expect(registration.matches("etalii/functional-decomposition-graph")).toBe(false);
    expect(registration.Canvas).toBeDefined();
  });
});
