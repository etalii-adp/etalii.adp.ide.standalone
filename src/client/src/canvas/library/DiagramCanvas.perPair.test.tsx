import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { Cardinality, DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "./testing/canvasHarness";

/**
 * One connection per pair, refused under the pointer.
 *
 * The check is a fourth one beside the type, cardinality and cycle checks, and like them it refuses
 * on its own - so the last test here drives a refusal each of the others alone explains, with
 * `perPair` declared, to show declaring it took none of them away.
 */

/** Three nodes at centres 0, 300 and 600, 100 wide: every east anchor 50 right of centre, every west 50 left. */
function modelOf(connections: readonly DiagramModelConnection[]): DiagramModel {
  return {
    elements: [
      { id: "a", type: "node", x: 0, y: 0, width: 100, height: 40, label: "A", payload: { count: 2 } },
      { id: "b", type: "node", x: 300, y: 0, width: 100, height: 40, label: "B", payload: { count: 2 } },
      { id: "c", type: "other", x: 600, y: 0, width: 100, height: 40, label: "C" },
    ],
    connections: [...connections],
  };
}

function definitionOf(cardinality: Cardinality, acyclic = false): DiagramDefinition {
  return {
    elementTypes: [
      {
        id: "node",
        shape: "box",
        anchors: { kind: "compass", positions: ["e", "w"] },
        sizing: "model",
        segments: { count: { path: "payload.count" }, max: 4 },
      },
      { id: "other", shape: "box", anchors: { kind: "compass", positions: ["e", "w"] }, sizing: "model" },
    ],
    relationTypes: [
      {
        id: "flows",
        route: "straight",
        endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false, cardinality },
        hideWhenAttachmentHidden: true,
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    ...(acyclic ? { acyclic: [{ relationTypes: ["flows"] }] } : {}),
  };
}

function renderCanvas(definition: DiagramDefinition, model: DiagramModel) {
  const onConnectionDrawn = vi.fn();
  const result = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{ onConnectionDrawn }} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  return { ...result, onConnectionDrawn };
}

const anchorOn = (container: HTMLElement, id: string, name: string) =>
  container.querySelector(`[data-element-id="${id}"] [data-anchor="${name}"]`)!;
const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;

/** From one element's anchor to over another's centre, mid-drag: whether the target is offered. */
function hover(container: HTMLElement, fromId: string, fromAnchor: string, fromX: number, toX: number) {
  const anchor = anchorOn(container, fromId, fromAnchor);
  fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: fromX, clientY: 0 }));
  fireEvent(anchor, pointer("pointermove", { clientX: toX, clientY: 0 }));
  return anchor;
}

function release(anchor: Element, x: number) {
  fireEvent(anchor, pointer("pointerup", { clientX: x, clientY: 0 }));
}

const flows = (id: string, sourceId: string, targetId: string, extra: Partial<DiagramModelConnection> = {}): DiagramModelConnection =>
  ({ id, type: "flows", sourceId, targetId, ...extra });

describe("one connection per pair", () => {
  it("ordered: refuses a second A to B and neither highlights nor raises it", () => {
    // Arrange: A -> B exists.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf({ perPair: "ordered" }), modelOf([flows("1", "a", "b")]));

    // Act: A's east anchor (50) dragged over B (300).
    const anchor = hover(container, "a", "e", 50, 300);

    // Assert: refused under the pointer, and nothing on release.
    expect(elementOn(container, "b").classList.contains("library-connect-target")).toBe(false);
    expect(elementOn(container, "b").classList.contains("library-connect-forbidden")).toBe(true);
    release(anchor, 300);
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("ordered: allows B to A, the other way round", () => {
    // Arrange: A -> B exists.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf({ perPair: "ordered" }), modelOf([flows("1", "a", "b")]));

    // Act: B's west anchor (250) dragged over A (0).
    const anchor = hover(container, "b", "w", 250, 0);

    // Assert.
    expect(elementOn(container, "a").classList.contains("library-connect-target")).toBe(true);
    release(anchor, 0);
    expect(onConnectionDrawn).toHaveBeenCalledTimes(1);
    expect(onConnectionDrawn.mock.calls[0][0]).toMatchObject({ sourceElementId: "b", targetElementId: "a" });
  });

  it("unordered: refuses B to A as well", () => {
    // Arrange.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf({ perPair: "unordered" }), modelOf([flows("1", "a", "b")]));

    // Act.
    release(hover(container, "b", "w", 250, 0), 0);

    // Assert.
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("counts a connection that is hidden, not only the ones drawn", () => {
    // Arrange: A -> B is attached to A's third segment, and A draws two - so it is not drawn.
    const hidden = flows("1", "a", "b", { sourceAttachment: { edge: "top", region: 2, at: 0.5 } });
    const { container, onConnectionDrawn } = renderCanvas(definitionOf({ perPair: "ordered" }), modelOf([hidden]));
    expect(container.querySelector('[data-connection-id="1"]')).toBeNull();

    // Act: a second A -> B.
    release(hover(container, "a", "e", 50, 300), 300);

    // Assert: still refused - the document holds the first one.
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("leaves the type, cardinality and cycle checks each refusing on their own", () => {
    // Type: C is not a type `flows` may reach, with no duplicate anywhere.
    const typed = renderCanvas(definitionOf({ perPair: "ordered" }), modelOf([]));
    release(hover(typed.container, "b", "e", 350, 600), 600);
    expect(typed.onConnectionDrawn).not.toHaveBeenCalled();
    typed.unmount();

    // Cardinality: B already has its one outgoing, to C, and B -> A is no duplicate.
    const capped = renderCanvas(definitionOf({ perPair: "ordered", maxFromSource: 1 }), modelOf([flows("1", "b", "c")]));
    release(hover(capped.container, "b", "w", 250, 0), 0);
    expect(capped.onConnectionDrawn).not.toHaveBeenCalled();
    capped.unmount();

    // Cycle: A -> B exists, B -> A is no duplicate under `ordered` but closes a cycle.
    const cyclic = renderCanvas(definitionOf({ perPair: "ordered" }, true), modelOf([flows("1", "a", "b")]));
    release(hover(cyclic.container, "b", "w", 250, 0), 0);
    expect(cyclic.onConnectionDrawn).not.toHaveBeenCalled();
  });
});

describe("a connection attached to a segment its element is not drawing", () => {
  const attached = flows("1", "a", "b", { sourceAttachment: { edge: "top", region: 2, at: 0.5 } });
  const withCount = (count: number): DiagramModel => {
    const model = modelOf([attached]);
    return { ...model, elements: model.elements.map((element) => (element.id === "a" ? { ...element, payload: { count } } : element)) };
  };

  it("is not drawn while the count is lowered below it, and stays in the model", () => {
    // Arrange, act: A draws two segments; the connection names the third.
    const model = withCount(2);
    const { container } = renderCanvas(definitionOf({ perPair: "ordered" }), model);

    // Assert: no path, and the model the canvas was handed still holds it.
    expect(container.querySelector('[data-connection-id="1"]')).toBeNull();
    expect(model.connections.map((connection) => connection.id)).toEqual(["1"]);
  });

  it("is drawn again when the count comes back up", () => {
    // Arrange.
    const definition = definitionOf({ perPair: "ordered" });
    const { container, rerender } = renderCanvas(definition, withCount(4));
    expect(container.querySelector('[data-connection-id="1"] path.canvas-connection-line')).not.toBeNull();

    // Act: lowered to two, then raised to four again.
    const again = (model: DiagramModel) => (
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={definition} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>
    );
    rerender(again(withCount(2)));
    expect(container.querySelector('[data-connection-id="1"]')).toBeNull();
    rerender(again(withCount(4)));

    // Assert.
    expect(container.querySelector('[data-connection-id="1"] path.canvas-connection-line')).not.toBeNull();
  });
});
