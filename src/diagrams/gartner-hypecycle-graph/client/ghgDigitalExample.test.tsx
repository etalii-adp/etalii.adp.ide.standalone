import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { GHG_DEFINITION } from "./GhgCanvas";
import { exampleModel, isHidden, readExample } from "./ghgExample";

/**
 * The digital-trends example, as this definition reads and draws it: every trend and every influence
 * the readme counts reaches the canvas, and none of the influences is hidden by a phase count.
 */

const EXAMPLE = readExample("digital-trends");
const RECT = { width: 1000, height: 600 };
let restore: (() => void) | undefined;

beforeEach(() => {
  const original = SVGSVGElement.prototype.getBoundingClientRect;
  SVGSVGElement.prototype.getBoundingClientRect = () =>
    ({ left: 0, top: 0, x: 0, y: 0, width: RECT.width, height: RECT.height, right: RECT.width, bottom: RECT.height, toJSON: () => ({}) }) as DOMRect;
  restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
});

afterEach(() => restore?.());

describe("the digital-trends example, as this definition reads it", () => {
  it("is read in full: thirty trends and forty-one influences, none of them hidden", () => {
    expect(EXAMPLE.trends).toHaveLength(30);
    expect(EXAMPLE.influences).toHaveLength(41);
    expect(EXAMPLE.influences.filter((influence) => isHidden(influence, EXAMPLE.trends))).toEqual([]);
  });

  it("draws every trend and every influence", () => {
    const model = exampleModel(undefined, "digital-trends");

    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={GHG_DEFINITION} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    expect(container.querySelectorAll("[data-element-id]")).toHaveLength(30);
    expect(container.querySelectorAll("[data-connection-id]")).toHaveLength(41);
  });
});
