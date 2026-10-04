import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the supply chain registration", () => {
  it("claims etalii/supply-chain and nothing else", () => {
    // Assert.
    const registration = registrations[0];
    expect(registrations).toHaveLength(1);
    expect(registration.matches("etalii/supply-chain")).toBe(true);
    // A near miss: the element types travel as `<mime>+<type>`, and a prefix match would claim them.
    expect(registration.matches("etalii/supply-chain+flow")).toBe(false);
    expect(registration.Panel).toBeDefined();
  });
});
