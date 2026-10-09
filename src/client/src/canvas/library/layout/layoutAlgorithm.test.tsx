import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { LAYOUT_ALGORITHMS, layoutAlgorithmFor, manualLayout, treeLayout } from "./layoutAlgorithm";
import { rowPackedLayout } from "./rowPackedLayout";
import { tieredForceLayout } from "./tieredForceLayout";
import { DiagramCanvas } from "../DiagramCanvas";
import type { DiagramDefinition } from "../definition/diagramDefinition";
import type { DiagramModel } from "../api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

const box = (id: string, x: number, y: number, parentId?: string) => ({ id, x, y, width: 100, height: 40, parentId });

describe("the layout seam", () => {
  it("manual/external returns the model's own positions untouched - literally: no second store", () => {
    // Null, not a copied map: there is nothing to drift from the model (Requirement 8.3).
    expect(manualLayout.place({ elements: [box("a", 7, 9)], connections: [] }, { modes: ["manual"] })).toBeNull();
  });

  it("tree places children beyond their parent along the direction, siblings apart on the cross axis", () => {
    const input = {
      elements: [box("root", 0, 0), box("one", 0, 0, "root"), box("two", 0, 0, "root")],
      connections: [],
    };

    const positions = treeLayout.place(input, { modes: ["tree"], treeDirection: "left-to-right" })!;

    const root = positions.get("root")!;
    const one = positions.get("one")!;
    const two = positions.get("two")!;
    expect(one.x).toBeGreaterThan(root.x);
    expect(two.x).toBe(one.x);
    expect(two.y).toBeGreaterThan(one.y);
    // The parent sits centred on its children's span.
    expect(root.y).toBeCloseTo((one.y + two.y) / 2, 5);
  });

  it("tree grows down when the definition says top-down, and hangs on connections when no parentId does", () => {
    const input = {
      elements: [box("root", 0, 0), box("leaf", 0, 0)],
      connections: [{ sourceId: "root", targetId: "leaf" }],
    };

    const positions = treeLayout.place(input, { modes: ["tree"], treeDirection: "top-down" })!;

    expect(positions.get("leaf")!.y).toBeGreaterThan(positions.get("root")!.y);
    expect(positions.get("leaf")!.x).toBe(positions.get("root")!.x);
  });

  it("a further algorithm is an addition: the registry answers for its members and nothing else", () => {
    // Named-member canaries (Requirement 10.2): the two built-ins this task lands.
    expect(layoutAlgorithmFor("manual")).toBe(manualLayout);
    expect(layoutAlgorithmFor("tree")).toBe(treeLayout);
    // Declared in the schema, landing with adoption: no entry yet, and the canvas falls back
    // to manual rather than inventing placements.
    expect(layoutAlgorithmFor("horizontal-flow")).toBeUndefined();
    expect(layoutAlgorithmFor("row-packed")).toBe(rowPackedLayout);
    expect(layoutAlgorithmFor("tiered-force")).toBe(tieredForceLayout);
    expect(LAYOUT_ALGORITHMS.length).toBe(4);
  });
});

// ---- the seam mounted: switching surface and what a drag means ------------------------------

function definitionOf(overrides: Partial<DiagramDefinition>): DiagramDefinition {
  return {
    elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    ...overrides,
  };
}

const model: DiagramModel = {
  elements: [
    { id: "root", type: "node", x: 0, y: 0, width: 100, height: 40, label: "Root" },
    { id: "leaf", type: "node", x: 10, y: 10, width: 100, height: 40, label: "Leaf", parentId: "root" },
  ],
  connections: [],
};

function mount(definition: DiagramDefinition, events = {}, config?: { activeLayoutMode?: "manual" | "tree" }) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={model} events={events} config={config} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

describe("the layout seam, mounted", () => {
  it("a definition allowing one mode presents no switching surface at all", () => {
    mount(definitionOf({ layout: { modes: ["manual"] } }));

    expect(screen.queryByTestId("layout-switcher")).toBeNull();
  });

  it("more than one mode presents the switcher, and switching raises layout-mode-changed", () => {
    const onLayoutModeChanged = vi.fn();
    mount(definitionOf({ layout: { modes: ["manual", "tree"] } }), { onLayoutModeChanged });

    const buttons = screen.getByTestId("layout-switcher").querySelectorAll("button");
    expect(buttons).toHaveLength(2);
    fireEvent.click(buttons[1]);

    // A request, as every event is: the module answers by changing activeLayoutMode.
    expect(onLayoutModeChanged).toHaveBeenCalledExactlyOnceWith({ kind: "layout-mode-changed", mode: "tree" });
  });

  it("an active tree mode places the elements: the leaf renders beyond its root", () => {
    const { container } = mount(
      definitionOf({ layout: { modes: ["manual", "tree"], treeDirection: "left-to-right" } }),
      {},
      { activeLayoutMode: "tree" },
    );

    const leaf = container.querySelector('[data-element-id="leaf"] g[transform]');
    // The leaf's model x is 10; the tree pushes it a full gap beyond the root's edge.
    expect(leaf?.getAttribute("transform")).toMatch(/translate\((\d{2,})/);
  });

  it("a drag under automatic layout raises the move when the definition repins, and nothing when it reclaims", () => {
    const onElementMoved = vi.fn();
    const repin = definitionOf({
      layout: { modes: ["tree"], treeDirection: "left-to-right", dragUnderAutomaticLayout: "repin-to-manual" },
    });
    const { container, unmount } = mount(repin, { onElementMoved }, { activeLayoutMode: "tree" });

    const leaf = container.querySelector('[data-element-id="leaf"]')!;
    fireEvent(leaf, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(leaf, pointer("pointermove", { clientX: 60, clientY: 40 }));
    fireEvent(leaf, pointer("pointerup", { clientX: 60, clientY: 40 }));
    expect(onElementMoved).toHaveBeenCalledTimes(1);
    unmount();

    const reclaimed = definitionOf({
      layout: { modes: ["tree"], treeDirection: "left-to-right", dragUnderAutomaticLayout: "reclaimed-displacement" },
    });
    onElementMoved.mockClear();
    const second = mount(reclaimed, { onElementMoved }, { activeLayoutMode: "tree" });

    const leafAgain = second.container.querySelector('[data-element-id="leaf"]')!;
    fireEvent(leafAgain, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(leafAgain, pointer("pointermove", { clientX: 60, clientY: 40 }));
    fireEvent(leafAgain, pointer("pointerup", { clientX: 60, clientY: 40 }));
    expect(onElementMoved).not.toHaveBeenCalled();
  });
});
