import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { ghgDefinitionFor } from "./GhgCanvas";
import { exampleModel, isHidden, readExample } from "./ghgExample";

/**
 * The eras-of-innovation example, as the decade's definition reads and draws it: every trend and
 * every influence the readme counts reaches the canvas, and none of the influences is hidden.
 */

const EXAMPLE = readExample("eras-of-innovation");
const RECT = { width: 1000, height: 600 };
let restore: (() => void) | undefined;

beforeEach(() => {
  const original = SVGSVGElement.prototype.getBoundingClientRect;
  SVGSVGElement.prototype.getBoundingClientRect = () =>
    ({ left: 0, top: 0, x: 0, y: 0, width: RECT.width, height: RECT.height, right: RECT.width, bottom: RECT.height, toJSON: () => ({}) }) as DOMRect;
  restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
});

afterEach(() => restore?.());

describe("the eras-of-innovation example, as this definition reads it", () => {
  it("is read in full, in decades: forty-two trends and fifty-five influences, none of them hidden", () => {
    expect(EXAMPLE.unit).toBe("decade");
    expect(EXAMPLE.trends).toHaveLength(42);
    expect(EXAMPLE.triggers).toHaveLength(9);
    expect(EXAMPLE.influences).toHaveLength(66);
    expect(EXAMPLE.influences.filter((influence) => isHidden(influence, EXAMPLE.trends))).toEqual([]);
  });

  it("draws every trend and every influence", () => {
    const model = exampleModel(undefined, "eras-of-innovation");

    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={ghgDefinitionFor(EXAMPLE.unit)} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    expect(container.querySelectorAll("[data-element-id]")).toHaveLength(42 + 9);
    expect(container.querySelectorAll("[data-connection-id]")).toHaveLength(66);
  });
});
