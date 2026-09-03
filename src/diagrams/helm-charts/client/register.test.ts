import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("register", () => {
  it("answers for helm/chart and nothing else", () => {
    // Arrange.
    const registration = registrations[0];

    // Act & assert.
    expect(registrations).toHaveLength(1);
    expect(registration.matches("helm/chart")).toBe(true);
    expect(registration.matches("ansible/structure")).toBe(false);
    expect(registration.Canvas).toBeDefined();
  });
});
