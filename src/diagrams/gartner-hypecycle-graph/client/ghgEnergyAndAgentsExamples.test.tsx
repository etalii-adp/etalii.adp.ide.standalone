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
  { name: "energy-breakthroughs", trends: 33, triggers: 7, notes: 0, influences: 57 },
  { name: "llms-and-agents", trends: 35, triggers: 7, notes: 0, influences: 67 },
  { name: "coal-technologies", trends: 34, triggers: 6, notes: 1, influences: 54 },
  { name: "electric-vehicles", trends: 28, triggers: 6, notes: 0, influences: 48 },
  { name: "internet-evolution", trends: 34, triggers: 7, notes: 0, influences: 56 },
  { name: "warfare-in-ukraine", trends: 29, triggers: 7, notes: 0, influences: 53 },
])("the $name example, as this definition reads it", ({ name, trends, triggers, notes, influences }) => {
  it(`is read in full: ${trends} trends, ${triggers} triggers, ${notes} notes and ${influences} influences, none of them hidden`, () => {
    const example = readExample(name);

    expect(example.trends).toHaveLength(trends);
    expect(example.triggers).toHaveLength(triggers);
    expect(example.notes).toHaveLength(notes);
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

    expect(container.querySelectorAll("[data-element-id]")).toHaveLength(trends + triggers + notes);
    expect(container.querySelectorAll("[data-connection-id]")).toHaveLength(influences);
  });
});
