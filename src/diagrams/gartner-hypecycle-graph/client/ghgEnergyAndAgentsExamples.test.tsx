import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { GHG_DEFINITION } from "./GhgCanvas";
import { exampleModel, isHidden, readExample } from "./ghgExample";

/**
 * The energy-breakthroughs, llms-and-agents, coal-technologies, electric-vehicles, internet-evolution and
 * warfare-in-ukraine examples, as this definition reads and draws them: every
 * trend and every influence each readme counts reaches the canvas, and none of the influences is hidden
 * by a phase count.
 */

const RECT = { width: 1000, height: 600 };
let restore: (() => void) | undefined;

beforeEach(() => {
  const original = SVGSVGElement.prototype.getBoundingClientRect;
  SVGSVGElement.prototype.getBoundingClientRect = () =>
    ({ left: 0, top: 0, x: 0, y: 0, width: RECT.width, height: RECT.height, right: RECT.width, bottom: RECT.height, toJSON: () => ({}) }) as DOMRect;
  restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
});

afterEach(() => restore?.());

describe.each([
  { name: "energy-breakthroughs", trends: 33, influences: 46 },
  { name: "llms-and-agents", trends: 35, influences: 56 },
  { name: "coal-technologies", trends: 34, influences: 45 },
  { name: "electric-vehicles", trends: 28, influences: 40 },
  { name: "internet-evolution", trends: 34, influences: 46 },
  { name: "warfare-in-ukraine", trends: 29, influences: 42 },
])("the $name example, as this definition reads it", ({ name, trends, influences }) => {
  it(`is read in full: ${trends} trends and ${influences} influences, none of them hidden`, () => {
    const example = readExample(name);

    expect(example.trends).toHaveLength(trends);
    expect(example.influences).toHaveLength(influences);
    expect(example.influences.filter((influence) => isHidden(influence, example.trends))).toEqual([]);
  });

  it("draws every trend and every influence", () => {
    const model = exampleModel(undefined, name);

    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={GHG_DEFINITION} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    expect(container.querySelectorAll("[data-element-id]")).toHaveLength(trends);
    expect(container.querySelectorAll("[data-connection-id]")).toHaveLength(influences);
  });
});
