import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { StraightConnection } from "./straight/StraightConnection";
import { BezierConnection } from "./bezier/BezierConnection";
import { FixedBezierConnection } from "./fixed-bezier/FixedBezierConnection";
import { InteractiveBezierConnection } from "./interactive-bezier/InteractiveBezierConnection";

const left = { x: 0, y: 0, width: 40, height: 20 };
const right = { x: 100, y: 0, width: 40, height: 20 };

function renderSvg(children: React.ReactNode) {
  return render(<svg>{children}</svg>);
}

describe("the shared connection implementations", () => {
  it("draws a straight connection with its label at the midpoint and a marker at the end", () => {
    // Act.
    const { container } = renderSvg(
      <StraightConnection from={left} to={right} className="g" pathClassName="line" markerEnd="url(#arrow)" label="uses" labelClassName="lbl" />,
    );

    // Assert.
    const path = container.querySelector("path.line")!;
    expect(path.getAttribute("d")).toMatch(/^M /);
    expect(path.getAttribute("marker-end")).toBe("url(#arrow)");
    const label = container.querySelector("text.lbl")!;
    expect(label.textContent).toBe("uses");
    expect(Number(label.getAttribute("x"))).toBe(50);
  });

  it("draws a bezier connection between the facing branch sides", () => {
    // Act.
    const { container } = renderSvg(<BezierConnection from={left} to={right} className="edge" />);

    // Assert.
    // From the parent's right side (x=20) to the child's left side (x=80), curving through the
    // corridor between them.
    const d = container.querySelector("path.edge")!.getAttribute("d")!;
    expect(d.startsWith("M 20 0")).toBe(true);
    expect(d.endsWith("80 0")).toBe(true);
  });

  it("draws a fixed-reach bezier between two precomputed points", () => {
    // Act.
    const { container } = renderSvg(
      <FixedBezierConnection from={{ x: 10, y: 5 }} to={{ x: 90, y: 25 }} className="edge" data-testid="edge-1" />,
    );

    // Assert.
    expect(container.querySelector('[data-testid="edge-1"]')!.getAttribute("d")).toBe(
      "M 10 5 C 40 5, 60 25, 90 25",
    );
  });

  it("gives the interactive connection a fat hit twin of its visible line", () => {
    // Act.
    const { container } = renderSvg(
      <InteractiveBezierConnection from={left} to={right} id="ccc" className="conn" hitClassName="hit" lineClassName="line" label="gates" />,
    );

    // Assert.
    const hit = container.querySelector("path.hit")!;
    const line = container.querySelector("path.line")!;
    expect(hit.getAttribute("d")).toBe(line.getAttribute("d"));
    expect(container.querySelector('[data-connection-id="ccc"]')).not.toBeNull();
    expect(container.textContent).toContain("gates");
  });

  it("loops the interactive connection forward-and-back when told the target starts early", () => {
    // Arrange: the target sits to the LEFT, as an overlapping timeline element's box does.
    const behind = { x: -60, y: 40, width: 40, height: 20 };

    // Act.
    const { container } = renderSvg(
      <InteractiveBezierConnection from={left} to={behind} loopsBack lineClassName="line" />,
    );

    // Assert.
    // Departs the source's right side rightward and arrives at the target's left side from
    // the left: the first control point lies beyond the start, the second before the end.
    const numbers = container.querySelector("path.line")!.getAttribute("d")!.match(/-?[\d.]+/g)!.map(Number);
    const [startX, , control1X, , control2X, , endX] = numbers;
    expect(endX).toBeLessThan(startX);
    expect(control1X).toBeGreaterThan(startX);
    expect(control2X).toBeLessThan(endX);
  });

  it("marks the selected interactive connection with the selected class", () => {
    // Act.
    const { container } = renderSvg(
      <InteractiveBezierConnection from={left} to={right} className="conn" selectedClassName="sel" selected />,
    );

    // Assert.
    expect(container.querySelector("g.conn.sel")).not.toBeNull();
  });
});
