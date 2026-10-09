import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { DiagramSelection, LibraryEventHandlers } from "./api/diagramEvents";
import { resolveSelection } from "./librarySelection";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * Lists inside an element, as the canvas draws them (agent-activity-diagram Requirements 4.1 to
 * 4.3 and 7.6).
 *
 * `compartments.test.ts` holds the arithmetic. What only a mounted canvas can say is here: that
 * the element's drawn box grows with its rows, that a folded group's rows are not in the document
 * at all, that a heading is a button a keyboard reaches, and that pressing one asks the module
 * rather than folding anything itself - the library holds no collapse state.
 */

const definition: DiagramDefinition = {
  elementTypes: [
    {
      id: "specification",
      shape: "box",
      anchors: { kind: "edge" },
      sizing: "model",
      compartments: [
        {
          id: "tasks",
          rows: "payload.tasks",
          rowId: "id",
          text: { path: "title" },
          groupBy: {
            path: "status",
            groups: [
              { value: "progressing", title: "Progressing" },
              { value: "pending", title: "Pending" },
            ],
            otherTitle: "Other",
          },
          collapsed: "payload.collapsed",
          top: 40,
          headingHeight: 20,
          rowHeight: 18,
          bottom: 8,
          insetX: 10,
          rowIndent: 12,
        },
      ],
    },
  ],
  relationTypes: [],
  layout: { modes: ["manual"] },
  dragging: "enabled",
};

function modelOf(collapsed: readonly string[]): DiagramModel {
  return {
    elements: [
      {
        id: "s1",
        type: "specification",
        x: 200,
        y: 200,
        width: 220,
        height: 40,
        label: "Knowledge designer",
        payload: {
          collapsed,
          tasks: [
            { id: "t1", title: "Trial the binding", status: "progressing" },
            { id: "t2", title: "Table component", status: "progressing" },
            { id: "t3", title: "Examples", status: "pending" },
          ],
        },
      },
    ],
    connections: [],
  };
}

function canvasOf(collapsed: readonly string[], events: LibraryEventHandlers = {}, selection: DiagramSelection = []) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={modelOf(collapsed)} events={events} selection={selection} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const heading = (container: HTMLElement, key: string) => container.querySelector(`[data-element-id="s1"] [data-heading="${key}"]`);
const rowIds = (container: HTMLElement) => [...container.querySelectorAll('[data-element-id="s1"] [data-row-id]')].map((row) => row.getAttribute("data-row-id"));
const bodyHeight = (container: HTMLElement) => Number(container.querySelector('[data-element-id="s1"] rect.library-shape')?.getAttribute("height"));

describe("lists inside an element", () => {
  it("draws a heading per group with its count, and the rows of the groups that are not folded", () => {
    const { container } = canvasOf(["pending"]);

    expect(heading(container, "progressing")?.textContent).toContain("Progressing (2)");
    expect(heading(container, "pending")?.textContent).toContain("Pending (1)");
    expect(heading(container, "progressing")?.getAttribute("aria-expanded")).toBe("true");
    expect(heading(container, "pending")?.getAttribute("aria-expanded")).toBe("false");
    // The folded group's row is not drawn at all - not hidden, absent.
    expect(rowIds(container)).toEqual(["t1", "t2"]);
  });

  it("makes the element as tall as its visible lines need, over the height the model gave", () => {
    // The model says 40. Two headings and two visible rows need 40 + 2 × 20 + 2 × 18 + 8.
    expect(bodyHeight(canvasOf(["pending"]).container)).toBe(124);
    // Unfold the other group and its one row adds a line.
    expect(bodyHeight(canvasOf([]).container)).toBe(142);
  });

  it("asks the module to fold a heading that is pressed, and folds nothing itself", () => {
    const onCompartmentToggled = vi.fn();
    const onElementMoved = vi.fn();
    const { container } = canvasOf([], { onCompartmentToggled, onElementMoved });
    const target = heading(container, "progressing")!;

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 120, clientY: 170 }));
    fireEvent(target, pointer("pointerup", { clientX: 120, clientY: 170 }));
    fireEvent.click(target);

    expect(onCompartmentToggled).toHaveBeenCalledTimes(1);
    expect(onCompartmentToggled).toHaveBeenCalledWith({
      kind: "compartment-toggled",
      elementId: "s1",
      compartmentId: "tasks",
      key: "progressing",
      collapsed: true,
    });
    // The model did not change, so neither did the drawing: the rows are still there.
    expect(rowIds(container)).toEqual(["t1", "t2", "t3"]);
    expect(onElementMoved).not.toHaveBeenCalled();
  });

  it("asks to unfold a folded heading from the keyboard", () => {
    const onCompartmentToggled = vi.fn();
    const { container } = canvasOf(["pending"], { onCompartmentToggled });
    const target = heading(container, "pending")!;

    expect(target.getAttribute("role")).toBe("button");
    expect(target.getAttribute("tabindex")).toBe("0");
    fireEvent.keyDown(target, { key: "Enter" });

    expect(onCompartmentToggled).toHaveBeenCalledWith(expect.objectContaining({ key: "pending", collapsed: false }));
  });
});

/**
 * A row is a thing with an id, so it can be the selection (agent-activity-diagram Requirement
 * 9.4): the property grid and the shared menu then serve it as they serve an element, through
 * the same selection and nothing of their own.
 */
describe("a row inside an element", () => {
  const row = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="s1"] [data-row-id="${id}"]`);

  it("is selected by a press, which does not reach the element it is in", () => {
    // The planted defect this was seen to fail against: the row's own press wiring removed, so
    // the press lands on the element and it is the element that is selected.
    const onSelectionChanged = vi.fn();
    const { container } = canvasOf([], { onSelectionChanged });
    const target = row(container, "t2")!;

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 130, clientY: 210 }));
    fireEvent(target, pointer("pointerup", { clientX: 130, clientY: 210 }));
    fireEvent.click(target);

    expect(onSelectionChanged).toHaveBeenCalledTimes(1);
    expect(onSelectionChanged).toHaveBeenCalledWith({ kind: "selection-changed", selection: [{ kind: "row", id: "t2" }] });
  });

  it("shows as selected when the selection names it, and the element does not", () => {
    const { container } = canvasOf([], {}, [{ kind: "row", id: "t2" }]);

    expect(row(container, "t2")?.getAttribute("aria-selected")).toBe("true");
    expect(row(container, "t2")?.classList.contains("canvas-selected")).toBe(true);
    expect(row(container, "t1")?.getAttribute("aria-selected")).toBe("false");
    expect(container.querySelector('[data-element-id="s1"]')?.classList.contains("canvas-selected")).toBe(false);
  });

  it("is reached and selected from the keyboard", () => {
    const onSelectionChanged = vi.fn();
    const { container } = canvasOf([], { onSelectionChanged });
    const target = row(container, "t1")!;

    expect(target.getAttribute("tabindex")).toBe("0");
    fireEvent.keyDown(target, { key: "Enter" });

    expect(onSelectionChanged).toHaveBeenCalledWith({ kind: "selection-changed", selection: [{ kind: "row", id: "t1" }] });
  });

  it("is not deleted as a connection would be when Delete is pressed", () => {
    const onConnectionDeleted = vi.fn();
    const onElementDeleted = vi.fn();
    const { container } = canvasOf([], { onConnectionDeleted, onElementDeleted }, [{ kind: "row", id: "t2" }]);

    fireEvent.keyDown(container.querySelector("svg")!, { key: "Delete" });

    expect(onConnectionDeleted).not.toHaveBeenCalled();
    expect(onElementDeleted).not.toHaveBeenCalled();
  });

  it("resolves from a pushed selection by its id, folded away or not, and after an element or a connection", () => {
    expect(resolveSelection("t2", modelOf([]), definition)).toEqual([{ kind: "row", id: "t2" }]);
    // Folded away is not gone: the row is still in the model and may still be what is selected.
    expect(resolveSelection("t3", modelOf(["pending"]), definition)).toEqual([{ kind: "row", id: "t3" }]);
    expect(resolveSelection("s1", modelOf([]), definition)).toEqual([{ kind: "element", id: "s1" }]);
    expect(resolveSelection("nothing", modelOf([]), definition)).toEqual([]);
  });
});
