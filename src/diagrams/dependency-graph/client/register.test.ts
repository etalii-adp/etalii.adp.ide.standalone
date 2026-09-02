import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the dependency graph registration", () => {
  it("claims generic/dependencies and nothing else", () => {
    // Assert.
    const registration = registrations[0];
    expect(registrations).toHaveLength(1);
    expect(registration.matches("generic/dependencies")).toBe(true);
    // generic/timeline in particular: this module is that one's fork, and a registration that
    // claimed it would put this canvas in front of every timeline in the workspace.
    expect(registration.matches("generic/timeline")).toBe(false);
    expect(registration.matches("wardley/map")).toBe(false);
    expect(registration.Canvas).toBeDefined();
  });
});
