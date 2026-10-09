import { describe, expect, it } from "vitest";
import { registrations } from "./register";

describe("the agent activity diagram registration", () => {
  it("claims etalii/agent-activity-diagram and nothing else", () => {
    const registration = registrations[0];
    expect(registrations).toHaveLength(1);
    expect(registration.matches("etalii/agent-activity-diagram")).toBe(true);
    // A near miss: the element types travel as `<mime>+<type>`, and a prefix match would claim them.
    expect(registration.matches("etalii/agent-activity-diagram+project")).toBe(false);
    expect(registration.matches("etalii/agent-behavior-modelling")).toBe(false);
    expect(registration.Panel).toBeDefined();
  });
});
