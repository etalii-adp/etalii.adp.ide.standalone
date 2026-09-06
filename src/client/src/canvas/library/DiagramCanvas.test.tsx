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

  // ---- drawing a relation with the right button (connectOnRightDrag) ----------------------

  /**
   * A right-button draw: press button 2 on an element BODY (not an anchor handle), move over a
   * target, release. It maps its move exactly as the anchor drag does - press point plus pixel
   * delta in canvas units - so element "a" at (0,0) reaches "b"'s box by a delta of 260 px.
   */
  function rightDraw(source: Element, fromX: number, fromY: number, toX: number, toY: number) {
    fireEvent(source, pointer("pointerdown", { button: 2, clientX: fromX, clientY: fromY }));
    fireEvent(source, pointer("pointermove", { clientX: toX, clientY: toY }));
    fireEvent(source, pointer("pointerup", { clientX: toX, clientY: toY }));
  }

  it("a right-button drag from an element body raises connection-drawn, where the definition opts in", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCanvas({ onConnectionDrawn }, definitionOf({ connectOnRightDrag: true }));

    // From Alpha's body into Beta's box, released nearer its west anchor (250,0) - no source
    // anchor, because the draw started on the body rather than on a handle.
    rightDraw(elementOn(container, "a"), 0, 0, 260, 0);

    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith({
      kind: "connection-drawn",
      relationType: "calls",
      sourceElementId: "a",
      targetElementId: "b",
      sourceAnchor: undefined,
      targetAnchor: "w",
    });
  });

  it("a right-button drag raises nothing where the definition does not opt in", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCanvas({ onConnectionDrawn }); // the default definition omits connectOnRightDrag

    rightDraw(elementOn(container, "a"), 0, 0, 260, 0);

    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("a right-button draw onto a forbidden target raises nothing", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCanvas({ onConnectionDrawn }, definitionOf({ connectOnRightDrag: true }));

    // The store is no legal target of `calls`; the release over it states nothing.
    rightDraw(elementOn(container, "a"), 0, 0, 150, 200);

    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("a right press that does not move raises nothing, leaving the body press to the menu", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCanvas({ onConnectionDrawn }, definitionOf({ connectOnRightDrag: true }));

    const body = elementOn(container, "a");
    fireEvent(body, pointer("pointerdown", { button: 2, clientX: 0, clientY: 0 }));
    fireEvent(body, pointer("pointerup", { button: 2, clientX: 0, clientY: 0 }));

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

  it("a beside-placed editor opens at the model's own label origin where one is carried", () => {
    // A wardley label sits at an AUTHORED pixel offset from its mark - left of it included -
    // and the editor must open where the drawn text begins, not at the shape's default gap.
    // The model carries the origin (labelAt); the definition still decides editability.
    const definition = definitionOf();
    (definition.elementTypes[0] as { label?: unknown }).label = { placement: "beside", editable: true };
    const model = modelOf();
    (model.elements[0] as { labelAt?: unknown }).labelAt = { x: -57, y: 48 };

    const { container } = renderCanvas({}, definition, model, {
      editing: {
        editingId: "a",
        onPropose: async (revision: number) => ({ revision, valid: true, reason: "" }),
        onSubmit: async () => ({ completed: true, error: "" }),
        onCancel: () => {},
      },
    });

    const editor = container.querySelector("foreignObject")!;
    expect(editor).not.toBeNull();
    expect(Number(editor.getAttribute("x"))).toBe(-57);
  });

  it("a drag is clamped to the definition's drag bounds, preview and raised position alike", () => {
    // An intrinsic space is a hard edge: a wardley component must not be draggable off the
    // map while the pointer is down, and the raised position must already respect it.
    const onElementMoved = vi.fn();
    const definition = definitionOf({ dragBounds: { x: -100, y: -100, width: 200, height: 200 } });
    const { container } = renderCanvas({ onElementMoved }, definition);

    drag(elementOn(container, "a"), 10, 10, 160, 40);

    expect(onElementMoved).toHaveBeenCalledExactlyOnceWith({
      kind: "element-moved",
      elementId: "a",
      position: { x: 100, y: 30 },
    });
  });

  it("a connection carrying a title renders it as the hover tooltip", () => {
    // A wardley link's `context` is a tooltip on the whole connection; a migration must not
    // silently drop it.
    const model = modelOf();
    (model.connections[0] as { title?: string }).title = "verifies tokens with";

    const { container } = renderCanvas({}, definitionOf(), model);

    expect(container.querySelector('[data-connection-id="a->b"] title')?.textContent).toBe("verifies tokens with");
  });

  it("a frame element draws at its centre, not displaced by half its size", () => {
    // FrameElement is centre-based like every shared element; feeding it the corner-based
    // library bounds shifted every boundary by half its box - the third instance of the
    // corner/centre class, caught before c4 mounted a single frame.
    const definition = definitionOf({
      elementTypes: [{ id: "boundary", shape: "frame", anchors: { kind: "edge" }, sizing: "model" }],
      relationTypes: [],
    });
    const model: DiagramModel = {
      elements: [{ id: "b1", type: "boundary", x: 100, y: 50, width: 200, height: 120, label: "Payments" }],
      connections: [],
    };

    const { container } = renderCanvas({}, definition, model);

    const rect = container.querySelector('[data-element-id="b1"] rect')!;
    // Centre (100,50), 200x120: the group is translated to the centre and the rect spans
    // from minus half - so its absolute left edge is at 0, not at 100.
    const group = rect.closest("g[transform], g")!;
    expect(rect.getAttribute("x")).toBe("-100");
    expect(group.getAttribute("transform") ?? container.innerHTML).toContain("100");
  });

  it("an inset label rule opens the editor over the named line, not the whole card", () => {
    // A c4 element is three lines of text; its editor covers the NAME line alone. The inset
    // placement carries the module's own offsets as definition data.
    const definition = definitionOf();
    (definition.elementTypes[0] as { label?: unknown }).label = {
      placement: "inset",
      editable: true,
      insetTop: 6,
      insetHeight: 20,
      insetX: 4,
    };

    const { container } = renderCanvas({}, definitionOf() && definition, modelOf(), {
      editing: {
        editingId: "a",
        onPropose: async (revision: number) => ({ revision, valid: true, reason: "" }),
        onSubmit: async () => ({ completed: true, error: "" }),
        onCancel: () => {},
      },
    });

    const editor = container.querySelector("foreignObject")!;
    expect(editor).not.toBeNull();
    // Element a: centre (0,0), 100x40 - the name line: x = -50+4, y = -20+6, height 20.
    expect(Number(editor.getAttribute("x"))).toBe(-46);
    expect(Number(editor.getAttribute("y"))).toBe(-14);
    expect(Number(editor.getAttribute("height"))).toBe(20);
  });

  it("a connection's editor opens with its authored edit value, not the drawn label", () => {
    // c4 draws "description [technology]", sometimes numbered; the editor replaces that whole
    // string on screen while editing the one authored value - the description.
    const definition = definitionOf();
    (definition.relationTypes[0] as { label?: unknown }).label = { placement: "midpoint", editable: true };
    const model = modelOf();
    (model.connections[0] as { label?: string; editValue?: string }).label = "2. Verifies tokens [HTTPS]";
    (model.connections[0] as { editValue?: string }).editValue = "Verifies tokens";

    const { container } = renderCanvas({}, definition, model, {
      editing: {
        editingId: "a->b",
        onPropose: async (revision: number) => ({ revision, valid: true, reason: "" }),
        onSubmit: async () => ({ completed: true, error: "" }),
        onCancel: () => {},
      },
    });

    const field = container.querySelector("foreignObject input") as HTMLInputElement;
    expect(field).not.toBeNull();
    expect(field.value).toBe("Verifies tokens");
  });

  it("a custom route is handed its endpoint bounds, for geometry that anchors on boxes", () => {
    // causal-loop's arc picks its own anchors from the two end BOXES - bowed to the side of
    // travel, an ellipse pair for a self-loop - which resolved points alone cannot express.
    const seen: unknown[] = [];
    const definition = definitionOf();
    (definition.relationTypes[0] as { route: unknown }).route = {
      customRoute: "probe",
      path: (_from: unknown, _to: unknown, _waypoints: unknown, ends?: unknown) => {
        seen.push(ends);
        return "M 0 0 L 10 10";
      },
    };

    renderCanvas({}, definition, modelOf());

    expect(seen.length).toBeGreaterThan(0);
    const ends = seen[0] as { source: { width: number }; target: { width: number } };
    expect(ends).toBeDefined();
    expect(ends.source.width).toBe(100);
    expect(ends.target.width).toBe(100);
  });

  it("a relation's adornment renders inside the connection group, where selection styling reaches it", () => {
    // Polarity signs and delay strokes ride the connection: drawn inside its group so the
    // shared .canvas-selected cascade colours them with the line they describe.
    const definition = definitionOf();
    (definition.relationTypes[0] as { adorn?: unknown }).adorn = () => (
      <text className="probe-adornment" data-testid="probe-adornment">
        +
      </text>
    );

    const { container } = renderCanvas({}, definition, modelOf());

    const adornment = container.querySelector('[data-connection-id="a->b"] [data-testid="probe-adornment"]');
    expect(adornment).not.toBeNull();
  });

  it("the anchor a connect starts from selects the relation whose source allows it", () => {
    // skos files a concept under another from its TOP anchor and cross-links from its SIDE -
    // two relations told apart by where the drag began. The source constraint's anchor list
    // is that meaning; a connect from a named anchor must pick the relation that names it.
    const onConnectionDrawn = vi.fn();
    const definition = definitionOf({
      elementTypes: [
        { id: "service", shape: "box", anchors: { kind: "compass", positions: ["n", "e"] }, sizing: "model" },
      ],
      relationTypes: [
        {
          id: "files-under",
          route: "straight",
          endpoints: { source: { elementTypes: ["service"], anchors: ["n"] }, target: { elementTypes: ["service"] }, allowSelf: false },
        },
        {
          id: "relates",
          route: "straight",
          endpoints: { source: { elementTypes: ["service"], anchors: ["e"] }, target: { elementTypes: ["service"] }, allowSelf: false },
        },
      ],
    });
    const model: DiagramModel = {
      elements: [
        { id: "a", type: "service", x: 0, y: 0, width: 100, height: 40, label: "Alpha" },
        { id: "b", type: "service", x: 300, y: 0, width: 100, height: 40, label: "Beta" },
      ],
      connections: [],
    };

    const { container } = renderCanvas({ onConnectionDrawn }, definition, model);

    // A drag from the EAST anchor must draw the side relation, not the first declared.
    const anchor = anchorOn(container, "a", "e");
    fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 50, clientY: 0 }));
    fireEvent(anchor, pointer("pointermove", { clientX: 300, clientY: 0 }));
    fireEvent(anchor, pointer("pointerup", { clientX: 300, clientY: 0 }));

    expect(onConnectionDrawn).toHaveBeenCalledTimes(1);
    expect(onConnectionDrawn.mock.calls[0][0].relationType).toBe("relates");
  });

  it("a connection's own class names join its group, for kinds one relation type cannot enumerate", () => {
    // A shacl edge's kind is an open string from the document; the connection carries the
    // kind class itself rather than the definition declaring one relation type per kind.
    const model = modelOf();
    (model.connections[0] as { className?: string }).className = "shacl-edge shacl-edge-node";

    const { container } = renderCanvas({}, definitionOf(), model);

    const group = container.querySelector('[data-connection-id="a->b"]')!;
    expect(group.getAttribute("class")).toContain("shacl-edge-node");
  });

  it("a type marked beneathConnections paints its elements under the connections", () => {
    // An opaque container whose members' edges must stay visible over it - azure-pipeline's
    // stage cards. The default stays connections-first, so only the marked type moves down.
    const definition = definitionOf();
    (definition.elementTypes as unknown[]).push({
      id: "zone",
      shape: "box",
      anchors: { kind: "edge" },
      sizing: "model",
      beneathConnections: true,
    });
    const model = modelOf();
    (model.elements as unknown[]).push({ id: "z", type: "zone", x: 150, y: 0, width: 500, height: 200 });

    const { container } = renderCanvas({}, definition, model);

    const drawn = [...container.querySelectorAll("[data-element-id], [data-connection-id]")].map(
      (node) => node.getAttribute("data-element-id") ?? node.getAttribute("data-connection-id"),
    );
    expect(drawn.indexOf("z")).toBeLessThan(drawn.indexOf("a->b"));
    expect(drawn.indexOf("a->b")).toBeLessThan(drawn.indexOf("a"));
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
