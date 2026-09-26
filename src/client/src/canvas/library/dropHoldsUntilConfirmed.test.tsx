import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * A dropped element stays where it was dropped until the model says where it is.
 *
 * ## The defect
 *
 * The release used to undo the drag's live offset at once, assuming the module writes the new
 * position in the same React batch. Most modules ask their backend and wait instead, so between
 * the release and the confirming delta the element was drawn at the model's position - the OLD
 * one. Reported from two modules: ansible flashed the element back to where the drag began before
 * it moved on; dotnet-dependency-graph, whose backend also never confirmed, kept it there until a
 * zoom forced a redraw.
 *
 * ## What these can and cannot see
 *
 * They read the element's drawn position from its transform, and they drive the model the way a
 * module's delta would - by re-rendering with a new one. That makes "where is it drawn between the
 * release and the answer" checkable in jsdom, which is the whole defect. What they cannot see is
 * the frame the browser paints, so a flash shorter than one render is out of their reach; the
 * browser check in the commit covers that half.
 */

const definitionWith = (extra: Partial<DiagramDefinition> = {}): DiagramDefinition => ({
  elementTypes: [{ id: "node", shape: "box", sizing: "model", anchors: { kind: "edge" } }],
  relationTypes: [],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  ...extra,
});

const at = (x: number, y: number): DiagramModel => ({
  elements: [{ id: "a", type: "node", x, y }],
  connections: [],
});

/** The box's drawn top-left, which moves one-for-one with the element's centre. */
function drawnAt(container: HTMLElement): { x: number; y: number } {
  const group = container.querySelector('[data-element-id="a"] g[transform]');
  const translate = /translate\(\s*(-?[\d.]+)[\s,]+(-?[\d.]+)\s*\)/.exec(group?.getAttribute("transform") ?? "");
  expect(translate, "the element drew no transform to read a position from").not.toBeNull();
  return { x: Number(translate![1]), y: Number(translate![2]) };
}

function mount(definition: DiagramDefinition, model: DiagramModel, moves: { x: number; y: number }[]) {
  const tree = (m: DiagramModel) => (
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={m} events={{ onElementMoved: ({ position }) => void moves.push(position) }} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>
  );
  const view = render(tree(model));
  return { ...view, answer: (m: DiagramModel) => view.rerender(tree(m)) };
}

/** Drag `a` by (dx, dy) and let go. */
function dragAndDrop(container: HTMLElement, dx: number, dy: number) {
  const target = container.querySelector('[data-element-id="a"]')!;
  fireEvent(target, pointer("pointerdown", { clientX: 0, clientY: 0 }));
  fireEvent(target, pointer("pointermove", { clientX: dx, clientY: dy }));
  fireEvent(target, pointer("pointerup", { clientX: dx, clientY: dy }));
}

describe("a positional drop is held until the model answers", () => {
  it("stays at the drop between the release and the model's answer", () => {
    // THE DEFECT. Nothing about the model has changed yet - exactly the window a backend round
    // trip opens - and the element must already be where it was let go, not where it began.
    const moves: { x: number; y: number }[] = [];
    const { container, unmount } = mount(definitionWith(), at(0, 0), moves);
    const before = drawnAt(container);

    dragAndDrop(container, 50, 30);

    expect(moves, "the module was never told about the move, so this test cannot say anything").toHaveLength(1);
    const after = drawnAt(container);
    expect(after.x).toBeCloseTo(before.x + 50, 5);
    expect(after.y).toBeCloseTo(before.y + 30, 5);
    unmount();
  });

  it("is drawn once at the confirmed position, not shifted a second time", () => {
    // The trap in holding: once the model moves the element, a held offset still in place would
    // be added ON TOP of the confirmed position and throw it the same distance again.
    const moves: { x: number; y: number }[] = [];
    const { container, answer, unmount } = mount(definitionWith(), at(0, 0), moves);
    const before = drawnAt(container);
    dragAndDrop(container, 50, 30);

    answer(at(moves[0]!.x, moves[0]!.y)); // the confirming delta, as a module would apply it

    const confirmed = drawnAt(container);
    expect(confirmed.x).toBeCloseTo(before.x + 50, 5);
    expect(confirmed.y).toBeCloseTo(before.y + 30, 5);
    unmount();
  });

  it("follows the model when the model puts it somewhere else", () => {
    // The backend has the last word. A position it corrected - snapped, clamped, re-laid out -
    // must win over where the pointer let go, not be overridden by a lingering hold.
    const moves: { x: number; y: number }[] = [];
    const { container, answer, unmount } = mount(definitionWith(), at(0, 0), moves);
    const before = drawnAt(container);
    dragAndDrop(container, 50, 30);

    answer(at(200, 100));

    const placed = drawnAt(container);
    expect(placed.x).toBeCloseTo(before.x + 200, 5);
    expect(placed.y).toBeCloseTo(before.y + 100, 5);
    unmount();
  });

  it("no longer looks dragged once it is let go", () => {
    // A held offset is drawn but is not a drag. Keeping the dragging class would leave the
    // element styled mid-gesture for as long as the backend takes to answer.
    const moves: { x: number; y: number }[] = [];
    const { container, unmount } = mount(definitionWith(), at(0, 0), moves);
    dragAndDrop(container, 50, 30);

    expect(container.querySelector('[data-element-id="a"]')!.getAttribute("class")).not.toContain("library-element-dragging");
    unmount();
  });
});

describe("a drop that is a proposal is not held", () => {
  it("returns to the model's position when the definition declares a drop target", () => {
    // The mindmap's drop re-parents: the node's resulting place is the LAYOUT's, not the
    // cursor's. Holding it at the cursor would draw it somewhere it will never be.
    const moves: { x: number; y: number }[] = [];
    const definition = definitionWith({ dropTarget: { parentPath: "payload.parentId" } });
    const { container, unmount } = mount(definition, at(0, 0), moves);
    const before = drawnAt(container);

    dragAndDrop(container, 50, 30);

    const after = drawnAt(container);
    expect(after.x).toBeCloseTo(before.x, 5);
    expect(after.y).toBeCloseTo(before.y, 5);
    unmount();
  });
});
