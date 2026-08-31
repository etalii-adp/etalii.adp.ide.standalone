import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the timeline registration", () => {
  it("claims generic/timeline and nothing else", () => {
    // Assert.
    const registration = registrations[0];
    expect(registrations).toHaveLength(1);
    expect(registration.matches("generic/timeline")).toBe(true);
    expect(registration.matches("wardley/map")).toBe(false);
    expect(registration.matches("generic/swimlane")).toBe(false);
    expect(registration.Canvas).toBeDefined();
  });
});
