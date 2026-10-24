import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import type { CustomRouteRef } from "../definition/diagramDefinition";
import type { DiagramModelConnection } from "../api/diagramModel";
import { ribbonAdornment, ribbonPath } from "./ribbonLine";

/**
 * A relation drawn as a filled band along its route, as thick as a number the connection carries,
 * measured vertically: its top and bottom edges are the route moved up and down by half of it.
 */

const S: CustomRouteRef = {
  customRoute: "test-s",
  path: (from, to) => `M ${from.x} ${from.y} C ${from.x + 50} ${from.y} ${to.x - 50} ${to.y} ${to.x} ${to.y}`,
};

describe("ribbonPath", () => {
  it("runs the route out along its top edge and back along its bottom edge, the thickness apart vertically", () => {
    expect(ribbonPath("M 0 10 C 50 10 150 90 200 90", 20)).toBe("M 0 0 C 50 0 150 80 200 80 L 200 100 C 150 100 50 20 0 20 Z");
  });

  it("follows every segment of a route of several, and a straight one", () => {
    expect(ribbonPath("M 0 0 L 10 0 C 20 0 30 10 40 10", 2)).toBe("M 0 -1 L 10 -1 C 20 -1 30 9 40 9 L 40 11 C 30 11 20 1 10 1 L 0 1 Z");
  });

  it("refuses a route it cannot move, naming the command", () => {
    expect(() => ribbonPath("M 0 0 Q 5 5 10 0", 2)).toThrow(/"Q"/);
  });
});

describe("ribbonAdornment", () => {
  const connection = (payload: Record<string, unknown>) => ({ id: "f", type: "flow", sourceId: "a", targetId: "b", payload }) as DiagramModelConnection;
  const draw = (highlighted: boolean, payload: Record<string, unknown>) => {
    const adorn = ribbonAdornment({ thickness: { path: "payload.thickness" }, fill: { path: "payload.paint" }, className: "t-band", highlightWidth: 2 }, S);
    const { container } = render(<svg>{adorn({ from: { x: 0, y: 10 }, to: { x: 200, y: 90 }, waypoints: [], highlighted }, connection(payload)) as never}</svg>);
    return container.querySelector("path")!;
  };

  it("fills the band with the connection's paint, a token as its custom property", () => {
    const band = draw(false, { thickness: 20, paint: "--color-a" });
    expect(band.getAttribute("class")).toBe("t-band");
    expect(band.getAttribute("d")).toBe(ribbonPath(S.path({ x: 0, y: 10 }, { x: 200, y: 90 }, []), 20));
    expect(band.style.fill).toBe("var(--color-a)");
    expect(band.style.stroke).toBe("");
    expect(draw(false, { thickness: 20, paint: "#336699" }).style.fill).toBe("#336699");
  });

  it("outlines the band in the selection colour while the connection is highlighted", () => {
    const band = draw(true, { thickness: 20, paint: "--color-a" });
    expect(band.style.stroke).toContain("--color-selected");
    expect(band.style.strokeWidth).toBe("2");
  });

  it("draws nothing for a connection that carries no thickness", () => {
    const adorn = ribbonAdornment({ thickness: { path: "payload.thickness" } }, S);
    expect(adorn({ from: { x: 0, y: 0 }, to: { x: 1, y: 1 }, waypoints: [] }, connection({}))).toBeNull();
  });
});
