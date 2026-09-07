import { describe, expect, it } from "vitest";
import { registrations } from "./register";

/**
 * What the shell discovers. Small, and worth having: the shell finds this file by glob and
 * matches on the MIME type, so a typo here makes the type register and then never draw - a
 * failure with no error anywhere.
 */
describe("register", () => {
  it("claims the origin the catalog and the backend both name", () => {
    const registration = registrations[0];

    expect(registrations).toHaveLength(1);
    expect(registration.matches("dotnet/dependency-graph")).toBe(true);
  });

  it("claims nothing else, including the authored dependency graph it sits beside", () => {
    // The two are different types with similar names: the authored generic/dependencies and
    // this derived one. A matcher that claimed both would put this canvas in front of the
    // other module's documents.
    const registration = registrations[0];

    expect(registration.matches("generic/dependencies")).toBe(false);
    expect(registration.matches("dotnet/dependency-graph+project")).toBe(false);
  });
});
