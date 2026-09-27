import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";
import { GHG_DEFINITION } from "./GhgCanvas";
import { exampleModel, isHidden, readExample } from "./ghgExample";

/**
 * Task 20's connect guard, over the technology-trends example itself rather than a toy: A to B is
 * offered, a second A to B is not, B to A is, a trend to itself is not, and an influence hidden by
 * a phase still blocks its duplicate.
 *
 * Every one of these is the canvas's verdict under the pointer. The backend refuses the same
 * influences on write (task 18), because a request is never trusted to have come from this canvas.
 *
 * jsdom measures nothing, so the surface is given a size and the pointer is placed by reading the
 * view the canvas chose back from its viewBox.
 */

const EXAMPLE = readExample();
const RECT = { width: 1000, height: 600 };
let restore: (() => void) | undefined;

beforeEach(() => {
  const original = SVGSVGElement.prototype.getBoundingClientRect;
  SVGSVGElement.prototype.getBoundingClientRect = () =>
    ({ left: 0, top: 0, x: 0, y: 0, width: RECT.width, height: RECT.height, right: RECT.width, bottom: RECT.height, toJSON: () => ({}) }) as DOMRect;
  restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
});

afterEach(() => restore?.());

function renderModel(model: DiagramModel) {
  const onConnectionDrawn = vi.fn();
  const result = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={GHG_DEFINITION} model={model} events={{ onConnectionDrawn }} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  return { ...result, onConnectionDrawn, model };
}

/** The client point over a canvas point, through the view the canvas is showing. */
function clientOf(container: HTMLElement, x: number, y: number) {
  const [vx, vy, vw, vh] = container.querySelector("svg")!.getAttribute("viewBox")!.split(/\s+/).map(Number);
  return { clientX: ((x - vx) / vw) * RECT.width, clientY: ((y - vy) / vh) * RECT.height };
}

/** Pressed on the source's first phase, bottom edge; moved over the target's middle, mid-gesture. */
function drawBetween(rendered: ReturnType<typeof renderModel>, fromId: string, toId: string) {
  const { container, model } = rendered;
  const from = model.elements.find((element) => element.id === fromId)!;
  const to = model.elements.find((element) => element.id === toId)!;
  const strip = container.querySelector(`[data-element-id="${fromId}"] .library-edge-strip[data-edge="bottom"][data-region="0"]`)!;
  expect(strip, `${fromId} offers its peak's bottom edge`).not.toBeNull();
  const press = clientOf(container, from.x - from.width! / 2 + 2, from.y + from.height! / 2);
  const over = clientOf(container, to.x, to.y);
  fireEvent(strip, pointer("pointerdown", { button: 0, ...press }));
  fireEvent(strip, pointer("pointermove", over));
  const target = container.querySelector(`[data-element-id="${toId}"]`)!;
  return { target, release: () => fireEvent(strip, pointer("pointerup", over)) };
}

const influenceBetween = (from: string, to: string) => EXAMPLE.influences.find((entry) => entry.from === from && entry.to === to);

describe("the technology-trends example, as this definition reads it", () => {
  it("is read in full, so a verdict below is about the whole example", () => {
    // 200 trends and 263 influences, as the example's readme states them.
    expect(EXAMPLE.trends).toHaveLength(200);
    expect(EXAMPLE.influences).toHaveLength(263);
    expect(EXAMPLE.influences.filter((influence) => isHidden(influence, EXAMPLE.trends))).toHaveLength(1);
  });
});

describe("a connect gesture over the technology-trends example", () => {
  it("offers A to B where neither way exists yet", () => {
    // Arrange: coke iron smelting and coal, with no influence either way.
    expect(influenceBetween("iron-smelting", "coal")).toBeUndefined();
    expect(influenceBetween("coal", "iron-smelting")).toBeUndefined();
    const rendered = renderModel(exampleModel(["iron-smelting", "coal"]));

    // Act.
    const { target, release } = drawBetween(rendered, "iron-smelting", "coal");

    // Assert.
    expect(target.classList.contains("library-connect-target")).toBe(true);
    release();
    expect(rendered.onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ sourceElementId: "iron-smelting", targetElementId: "coal", sourceAttachment: expect.objectContaining({ edge: "bottom", region: 0 }) }),
    );
  });

  it("does not offer a second A to B, and does offer B to A", () => {
    // Arrange: iron smelting already influences the steam engine, and not the other way.
    expect(influenceBetween("iron-smelting", "steam-engine")).toBeDefined();
    expect(influenceBetween("steam-engine", "iron-smelting")).toBeUndefined();
    const ids = ["iron-smelting", "steam-engine"];

    // Act, assert: the duplicate is refused under the pointer and raises nothing.
    const again = renderModel(exampleModel(ids));
    const duplicate = drawBetween(again, "iron-smelting", "steam-engine");
    expect(duplicate.target.classList.contains("library-connect-target")).toBe(false);
    duplicate.release();
    expect(again.onConnectionDrawn).not.toHaveBeenCalled();
    again.unmount();

    const back = renderModel(exampleModel(ids));
    const reverse = drawBetween(back, "steam-engine", "iron-smelting");
    expect(reverse.target.classList.contains("library-connect-target")).toBe(true);
    reverse.release();
    expect(back.onConnectionDrawn).toHaveBeenCalledTimes(1);
  });

  it("does not offer a trend to itself", () => {
    const rendered = renderModel(exampleModel(["coal"]));

    const { target, release } = drawBetween(rendered, "coal", "coal");

    expect(target.classList.contains("library-connect-target")).toBe(false);
    release();
    expect(rendered.onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("counts the influence a phase hides, so its duplicate is still refused", () => {
    // Arrange: the example's one hidden influence - not drawn, but in the model.
    const hidden = EXAMPLE.influences.find((influence) => isHidden(influence, EXAMPLE.trends))!;
    const rendered = renderModel(exampleModel([hidden.from, hidden.to]));
    expect(rendered.container.querySelector(`[data-connection-id="${hidden.id}"]`)).toBeNull();

    // Act.
    const { target, release } = drawBetween(rendered, hidden.from, hidden.to);

    // Assert.
    expect(target.classList.contains("library-connect-target")).toBe(false);
    release();
    expect(rendered.onConnectionDrawn).not.toHaveBeenCalled();
  });
});

describe("a vertical drag over the example", () => {
  it("lands the trend's middle on a row, whatever height it was let go at", () => {
    // Arrange: coal on its own row; a drag of 70 units down lets go between rows.
    const onElementMoved = vi.fn();
    const model = exampleModel(["coal"]);
    const coal = model.elements[0];
    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={GHG_DEFINITION} model={model} events={{ onElementMoved }} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
    const body = container.querySelector('[data-element-id="coal"] .library-segment')!;
    const from = clientOf(container, coal.x, coal.y);
    const to = clientOf(container, coal.x, coal.y + 70);

    // Act.
    fireEvent(body, pointer("pointerdown", { button: 0, ...from }));
    fireEvent(body, pointer("pointermove", to));
    fireEvent(body, pointer("pointerup", to));

    // Assert: one row down (56), never the 70 it was let go at - the middle sits 16 below the row's top.
    expect(onElementMoved).toHaveBeenCalledTimes(1);
    const { y } = onElementMoved.mock.calls[0][0].position;
    expect((y - 16) % 56).toBe(0);
    expect(y).toBe(coal.y + 56);
  });
});
