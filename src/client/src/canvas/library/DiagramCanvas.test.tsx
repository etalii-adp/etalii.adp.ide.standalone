import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { DiagramEventHandlers } from "./api/diagramEvents";
import { DiagramViewProvider, useDiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider, useDiagramToolbox, TOOLBOX_DRAG_TYPE } from "@client/shell/panels/DiagramToolboxContext";

/**
 * A pointer event jsdom can actually carry: jsdom implements no PointerEvent, and
 * `fireEvent.pointerDown` builds a bare Event whose `button` is undefined -
 * usePointerGesture.test.tsx's idiom, for the same reason.
 */
function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

/** A click in the pointer vocabulary the canvas listens to: press and release, unmoved. */
function press(target: Element, init: MouseEventInit = {}) {
  fireEvent(target, pointer("pointerdown", { button: 0, ...init }));
  fireEvent(target, pointer("pointerup", { ...init }));
}

/** A drag: press, move past the threshold, release at the destination. */
function drag(target: Element, fromX: number, fromY: number, toX: number, toY: number) {
  fireEvent(target, pointer("pointerdown", { button: 0, clientX: fromX, clientY: fromY }));
  fireEvent(target, pointer("pointermove", { clientX: toX, clientY: toY }));
  fireEvent(target, pointer("pointerup", { clientX: toX, clientY: toY }));
}

// jsdom implements no pointer capture on SVG elements; the arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * A definition with the enforcement the tests probe: services connect to services and only
 * services - a `store` can be a target of nothing and a source of nothing, which is the
 * forbidden pair the gesture-time tests drive at.
 */
function definitionOf(overrides: Partial<DiagramDefinition> = {}): DiagramDefinition {
  return {
    elementTypes: [
      { id: "service", shape: "box", anchors: { kind: "compass", positions: ["e", "w"] }, sizing: "model" },
      { id: "store", shape: "cylinder", anchors: { kind: "edge" }, sizing: "model", deletable: false },
    ],
    relationTypes: [
      {
        id: "calls",
        route: "straight",
        endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false },
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    ...overrides,
  };
}

function modelOf(): DiagramModel {
  return {
    elements: [
      { id: "a", type: "service", x: 0, y: 0, width: 100, height: 40, label: "Alpha" },
      { id: "b", type: "service", x: 300, y: 0, width: 100, height: 40, label: "Beta" },
      { id: "s", type: "store", x: 150, y: 200, width: 100, height: 60, label: "Store" },
    ],
    connections: [{ id: "a->b", type: "calls", sourceId: "a", targetId: "b", label: "calls" }],
  };
}

function renderCanvas(events: DiagramEventHandlers = {}, definition = definitionOf(), model = modelOf(), extra: Partial<React.ComponentProps<typeof DiagramCanvas>> = {}) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={model} events={events} {...extra} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;
const anchorOn = (container: HTMLElement, id: string, name: string) =>
  container.querySelector(`[data-element-id="${id}"] [data-anchor="${name}"]`)!;

describe("DiagramCanvas", () => {
  it("renders every element and connection the model states, through the definition's shapes", () => {
    const { container } = renderCanvas();

    expect(container.querySelectorAll("[data-element-id]")).toHaveLength(3);
    expect(container.querySelector('[data-connection-id="a->b"] path.canvas-connection-line')).not.toBeNull();
    expect(container.textContent).toContain("Alpha");
    expect(container.textContent).toContain("calls");
  });

  it("an edge-attached connection leaves the source's edge, not its corner or centre", () => {
    // ConnectorBox is centre-based and the library's bounds are corner-based; feeding one to
    // the other silently drew every edge from the wrong point. Alpha is 100 wide at (0,0),
    // so a line toward Beta at (300,0) must leave at (50,0) - the east edge, exactly.
    const definition = definitionOf();
    (definition.elementTypes[0] as { anchors: unknown }).anchors = { kind: "edge" };
    const { container } = renderCanvas({}, definition);

    const d = container.querySelector('[data-connection-id="a->b"] path.canvas-connection-line')!.getAttribute("d")!;

    expect(d).toMatch(/^M 50 0/);
  });

  it("renders a visible fallback for an element naming no declared type, and raises nothing", () => {
    const onSelectionChanged = vi.fn();
    const model = modelOf();
    (model.elements as unknown[]).push({ id: "ghost", type: "phantom", x: 500, y: 500 });

    const { container } = renderCanvas({ onSelectionChanged }, definitionOf(), model);

    expect(container.querySelector('[data-element-id="ghost"] [data-testid="library-fallback"]')).not.toBeNull();
    expect(onSelectionChanged).not.toHaveBeenCalled();
  });

  it("a press selects and raises selection-changed; a background press clears it", () => {
    const onSelectionChanged = vi.fn();
    const { container } = renderCanvas({ onSelectionChanged });

    press(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    expect(onSelectionChanged).toHaveBeenLastCalledWith({
      kind: "selection-changed",
      selection: [{ kind: "element", id: "a" }],
    });
    expect(elementOn(container, "a").classList.contains("canvas-selected")).toBe(true);

    press(container.querySelector("svg.library-canvas-surface")!, { clientX: 400, clientY: 300 });
    expect(onSelectionChanged).toHaveBeenLastCalledWith({ kind: "selection-changed", selection: [] });
  });

  it("a controlled selection renders, and presses still raise the event for the module to answer", () => {
    const onSelectionChanged = vi.fn();
    const { container } = renderCanvas({ onSelectionChanged }, definitionOf(), modelOf(), {
      selection: [{ kind: "element", id: "b" }],
    });

    expect(elementOn(container, "b").classList.contains("canvas-selected")).toBe(true);

    press(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    // The module was asked; the highlight stays the module's until it answers.
    expect(onSelectionChanged).toHaveBeenCalled();
    expect(elementOn(container, "a").classList.contains("canvas-selected")).toBe(false);
  });

  it("a drag raises element-moved with the carried position", () => {
    const onElementMoved = vi.fn();
    const { container } = renderCanvas({ onElementMoved });

    drag(elementOn(container, "a"), 10, 10, 60, 40);

    // jsdom's zero-size rect makes one pixel one canvas unit, so the delta is 50,30 verbatim.
    expect(onElementMoved).toHaveBeenCalledExactlyOnceWith({
      kind: "element-moved",
      elementId: "a",
      position: { x: 50, y: 30 },
    });
  });

  it("disabled dragging keeps the gesture a press: no move preview, no move event", () => {
    const onElementMoved = vi.fn();
    const onSelectionChanged = vi.fn();
    const { container } = renderCanvas({ onElementMoved, onSelectionChanged }, definitionOf(), modelOf(), {
      config: { dragging: "disabled" },
    });

    drag(elementOn(container, "a"), 10, 10, 60, 40);

    expect(onElementMoved).not.toHaveBeenCalled();
    expect(container.querySelector(".library-element-dragging")).toBeNull();
  });

  it("a permitted connect raises connection-drawn with the anchors involved", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCanvas({ onConnectionDrawn });

    // From Alpha's east anchor (at 50,0 in canvas units) into Beta's box, released nearer
    // its west anchor (250,0) than its east one - so the anchor choice is unambiguous.
    drag(anchorOn(container, "a", "e"), 50, 0, 260, 0);

    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith({
      kind: "connection-drawn",
      relationType: "calls",
      sourceElementId: "a",
      targetElementId: "b",
      sourceAnchor: "e",
      targetAnchor: "w",
    });
  });

  it("a forbidden connect cannot complete, and the refusal renders under the pointer", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCanvas({ onConnectionDrawn });

    // The store is no legal target of `calls`. Mid-drag the candidate renders forbidden -
    // the rule taught where the user's hand is (Requirement 4.3) - and release raises nothing.
    const anchor = anchorOn(container, "a", "e");
    fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 50, clientY: 0 }));
    fireEvent(anchor, pointer("pointermove", { clientX: 150, clientY: 200 }));
    expect(elementOn(container, "s").classList.contains("library-connect-forbidden")).toBe(true);
    fireEvent(anchor, pointer("pointerup", { clientX: 150, clientY: 200 }));

    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("a self-connect is refused where the definition forbids it", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCanvas({ onConnectionDrawn });

    drag(anchorOn(container, "a", "e"), 50, 0, 0, 0);

    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("a toolbox drop of a declared type raises element-dropped at the drop point", () => {
    const onElementDropped = vi.fn();
    const { container } = renderCanvas({ onElementDropped });
    const surface = container.querySelector("svg.library-canvas-surface")!;

    fireEvent.drop(surface, {
      clientX: 120,
      clientY: 80,
      dataTransfer: { types: [TOOLBOX_DRAG_TYPE], getData: () => "service", dropEffect: "" },
    });

    expect(onElementDropped).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ kind: "element-dropped", elementType: "service" }),
    );
  });

  it("a drop the definition does not offer refuses: no event", () => {
    const onElementDropped = vi.fn();
    const { container } = renderCanvas({ onElementDropped });
    const surface = container.querySelector("svg.library-canvas-surface")!;

    fireEvent.drop(surface, {
      dataTransfer: { types: [TOOLBOX_DRAG_TYPE], getData: () => "phantom", dropEffect: "" },
    });

    expect(onElementDropped).not.toHaveBeenCalled();
  });

  it("Delete raises element-deleted for a deletable selection and nothing for an undeletable one", () => {
    const onElementDeleted = vi.fn();
    const { container } = renderCanvas({ onElementDeleted });
    const surface = container.querySelector("svg.library-canvas-surface")!;

    press(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    fireEvent.keyDown(surface, { key: "Delete" });
    expect(onElementDeleted).toHaveBeenCalledExactlyOnceWith({ kind: "element-deleted", elementId: "a" });

    press(elementOn(container, "s"), { clientX: 150, clientY: 200 });
    fireEvent.keyDown(surface, { key: "Delete" });
    // The store's type says deletable: false; the second press must add no second event.
    expect(onElementDeleted).toHaveBeenCalledTimes(1);
  });

  it("registers view controls and toolbox as a pair, the toolbox derived from the element types", () => {
    // The half-registration class of defect cannot occur: mounting the canvas is what
    // registers BOTH, so a probe reading the two registries sees both or neither.
    let controls: unknown = null;
    let items: unknown = null;
    function Probe() {
      controls = useDiagramViewControls();
      items = useDiagramToolbox();
      return null;
    }

    render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvas definition={definitionOf()} model={modelOf()} events={{}} />
          <Probe />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    expect(controls).not.toBeNull();
    expect((items as { id: string; label: string }[]).map((item) => item.id)).toEqual(["service", "store"]);
  });

  it("an adjustable relation offers its handle only when selected, and dragging it raises connection-adjusted", () => {
    const onConnectionAdjusted = vi.fn();
    const definition = definitionOf();
    (definition.relationTypes[0] as { adjustable?: boolean }).adjustable = true;
    renderCanvas({ onConnectionAdjusted }, definition, modelOf(), {
      selection: [{ kind: "connection", id: "a->b" }],
    });

    const handle = screen.getByTestId("adjust-a->b");
    drag(handle, 175, 0, 175, 60);

    expect(onConnectionAdjusted).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ kind: "connection-adjusted", connectionId: "a->b" }),
    );
  });

  it("a definition that never marks adjustability renders no handle at all", () => {
    const { container } = renderCanvas({}, definitionOf(), modelOf(), {
      selection: [{ kind: "connection", id: "a->b" }],
    });

    expect(container.querySelector('[data-testid="adjust-a->b"]')).toBeNull();
  });

  it("gives its surface the full pane, so nothing is clipped down a vertical line", () => {
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;

    // An svg clips to its own viewport, so the surface has to be told to fill the pane in both
    // directions. `canvas-host` is the *container's* class: it sets a height and no width, and
    // an svg with a height, no width and a viewBox sizes its width from the viewBox's aspect
    // ratio. The surface then came out narrower than the pane and cut the diagram off down a
    // vertical line that moved as the view's proportions changed - visible while panning and
    // zooming. `canvas-drawing` is the shared width:100%/height:100% surface class every
    // hand-written canvas in this repository already wears.
    //
    // jsdom computes no layout, so this asserts the mechanism rather than the pixels; the
    // measurement that found the bug was 303px of surface inside a 469px pane.
    expect(surface.classList.contains("canvas-drawing")).toBe(true);
    expect(surface.classList.contains("canvas-host")).toBe(false);
  });

  it("a declared background draws behind the elements, and a declared extent is what fit shows", () => {
    const definition = definitionOf({
      extent: { x: 0, y: 0, width: 1000, height: 500 },
      background: { background: "axis", render: (view) => <line data-testid="axis" x1={view.x} y1={0} x2={view.x + view.width} y2={0} /> },
    });
    const { container } = renderCanvas({}, definition);

    const svg = container.querySelector("svg.library-canvas-surface")!;
    expect(svg.getAttribute("viewBox")).toBe("0 0 1000 500");
    const background = container.querySelector('[data-testid="canvas-background"]')!;
    expect(background.querySelector('[data-testid="axis"]')).not.toBeNull();
    // Behind the elements: the background group precedes every element in document order.
    expect(background.compareDocumentPosition(elementOn(container, "a")) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("an empty release completes only where the relation declares it, carrying the drop point", () => {
    const onConnectionReleasedOnEmpty = vi.fn();
    const declared = definitionOf();
    (declared.relationTypes[0] as { emptyRelease?: string }).emptyRelease = "complete";
    const { container, unmount } = renderCanvas({ onConnectionReleasedOnEmpty }, declared);

    drag(anchorOn(container, "a", "e"), 50, 0, 400, 320);
    expect(onConnectionReleasedOnEmpty).toHaveBeenCalledExactlyOnceWith({
      kind: "connection-released-on-empty",
      relationType: "calls",
      sourceElementId: "a",
      sourceAnchor: "e",
      position: { x: 400, y: 320 },
    });
    unmount();

    // The default stays the enforcement rule: released over nothing, nothing is raised.
    onConnectionReleasedOnEmpty.mockClear();
    const { container: second } = renderCanvas({ onConnectionReleasedOnEmpty });
    drag(anchorOn(second, "a", "e"), 50, 0, 400, 320);
    expect(onConnectionReleasedOnEmpty).not.toHaveBeenCalled();
  });

  it("a user-sized element earns resize handles when selected, and dragging one raises element-resized", () => {
    const onElementResized = vi.fn();
    const definition = definitionOf();
    (definition.elementTypes[0] as { sizing: string }).sizing = "user";
    const { container } = renderCanvas({ onElementResized }, definition, modelOf(), {
      selection: [{ kind: "element", id: "a" }],
    });

    const handle = container.querySelector('[data-element-id="a"] [data-resize="right"]')!;
    expect(handle).not.toBeNull();
    drag(handle, 50, 0, 90, 0);

    // Alpha spans x -50..50; carrying the right edge 40 further makes the box 140 wide.
    expect(onElementResized).toHaveBeenCalledExactlyOnceWith({
      kind: "element-resized",
      elementId: "a",
      side: "right",
      bounds: { x: -50, y: -20, width: 140, height: 40 },
    });
  });

  it("a model-sized element offers no resize handles at all", () => {
    const { container } = renderCanvas({}, definitionOf(), modelOf(), {
      selection: [{ kind: "element", id: "a" }],
    });

    expect(container.querySelector("[data-resize]")).toBeNull();
  });

  it("an editable label edits through the shared editor and commits as an event first", async () => {
    const onLabelCommitRequested = vi.fn();
    const definition = definitionOf();
    (definition.elementTypes[0] as { label?: unknown }).label = { placement: "inside", editable: true };
    const onSubmit = vi.fn(async () => ({ completed: true, error: "" }));

    const { container } = renderCanvas({ onLabelCommitRequested }, definition, modelOf(), {
      editing: {
        editingId: "a",
        onPropose: async (revision: number) => ({ revision, valid: true, reason: "" }),
        onSubmit,
        onCancel: () => {},
      },
    });

    const field = container.querySelector("foreignObject input") as HTMLInputElement;
    expect(field).not.toBeNull();
    fireEvent.change(field, { target: { value: "Alpha Prime" } });
    fireEvent.keyDown(field, { key: "Enter" });

    expect(onLabelCommitRequested).toHaveBeenCalledExactlyOnceWith({
      kind: "label-commit-requested",
      target: { kind: "element", id: "a" },
      value: "Alpha Prime",
    });
    expect(onSubmit).toHaveBeenCalledWith("Alpha Prime");
  });

  it("a label the definition does not mark editable opens no editor", () => {
    const { container } = renderCanvas({}, definitionOf(), modelOf(), {
      editing: {
        editingId: "a",
        onPropose: async (revision: number) => ({ revision, valid: true, reason: "" }),
        onSubmit: async () => ({ completed: true, error: "" }),
        onCancel: () => {},
      },
    });

    expect(container.querySelector("foreignObject input")).toBeNull();
  });
});
