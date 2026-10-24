import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { pointer } from "@client/canvas/library/testing/canvasHarness";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "../HierarchySurface";
import { levelsThatFit } from "../layout";

// Made-up names throughout: the page knows only what the definition tells it.
const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [],
  noteIcons: {},
  severalNotesIcon: "mdi-comment-multiple-outline",
  operations: {
    toggleCollapsed: "fold",
    setTitle: "retitle",
    setDescription: "redescribe",
    addTopLevel: { operation: "open", label: "New section", icon: "mdi-plus" },
    addEntry: { operation: "jot", label: "New point", icon: "mdi-plus" },
    addLevel: { operation: "nest", label: "New section", icon: "mdi-plus" },
    moveLevel: "shift",
    moveEntry: "slide",
    delete: "drop",
  },
  notes: {
    operations: { add: "remark", change: "reword", resolve: "settle", remove: "strike" },
    params: { kind: "sort", text: "line", detail: "more" },
    words: { list: "Notes", add: "New note", change: "Reword", resolve: "Settle", reopen: "Unsettle", remove: "Strike" },
  },
  moveParams: { into: "section", after: "behind" },
  deletion: { threshold: 1, message: (number: string, count: number) => `Drop ${number}? ${count} things concern it.` },
  widths: { operations: { add: "widen", set: "rewiden", reset: "unwiden" }, params: { level: "depth", width: "pixels" }, min: 150, max: 900 },
  shortcutKeys: ["Enter"],
};

function level(id: string, parentId: string, title: string): HierarchySurfaceNode {
  return { id, number: `[${id}]`, role: "level", type: "Section", parentId, title, description: "", labels: [], references: [], collapsed: false };
}

/** Orders (1) holds Payment (2), which holds Cards (3); Stock (4) stands beside Orders. The second column is kept at 400. */
function page(extra: Partial<HierarchySurfaceModel> = {}): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Plan",
    nodes: [level("1", "", "Orders"), level("2", "1", "Payment"), level("3", "2", "Cards"), level("4", "", "Stock")],
    widths: [{ id: "width/2", level: 2, width: 400 }],
    ...extra,
  };
}

function show(model: HierarchySurfaceModel, onGesture = vi.fn((): unknown => Promise.resolve(""))) {
  const view = render(<HierarchySurface model={model} definition={definition} onGesture={onGesture} />);
  return { onGesture, update: (next: HierarchySurfaceModel) => view.rerender(<HierarchySurface model={next} definition={definition} onGesture={onGesture} />) };
}

/** The width the box of the level titled `title` stands in. */
function widthOf(title: string): string {
  return (screen.getByText(title).closest(".hs-level-box") as HTMLElement).style.width;
}

function edge(column: number): HTMLElement {
  return screen.getAllByRole("separator", { name: `Width of column ${column}` })[0]!;
}

function drag(target: HTMLElement, by: number, end = true) {
  fireEvent(target, pointer("pointerdown", { pointerId: 1, button: 0, clientX: 500, clientY: 5 }));
  fireEvent(target, pointer("pointermove", { pointerId: 1, clientX: 500 + by, clientY: 5 }));
  if (end) {
    fireEvent(target, pointer("pointerup", { pointerId: 1, button: 0, clientX: 500 + by, clientY: 5 }));
  }
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("the widths of the columns", () => {
  it("draws each level's box at its column's kept width, and the page's own where none is kept", () => {
    // Act.
    show(page());

    // Assert.
    expect([widthOf("Orders"), widthOf("Stock"), widthOf("Payment"), widthOf("Cards")]).toEqual(["240px", "240px", "400px", "240px"]);
  });

  it("follows a drag of a column's right edge in every box of that column, and keeps the width when it is let go, sending one gesture", () => {
    // Arrange.
    const { onGesture } = show(page());

    // Act.
    drag(edge(1), 60, false);
    const during = [widthOf("Orders"), widthOf("Stock")];
    const sentDuring = onGesture.mock.calls.length;
    fireEvent(edge(1), pointer("pointerup", { pointerId: 1, button: 0, clientX: 560, clientY: 5 }));

    // Assert: a first width names the column's depth.
    expect(during).toEqual(["300px", "300px"]);
    expect(sentDuring).toBe(0);
    expect(onGesture.mock.calls).toEqual([["widen", "document", { depth: "1", pixels: "300" }]]);
    expect(widthOf("Orders")).toBe("300px");
  });

  it("changes a kept width by the column's id, holds a width to the definition's range, and sends nothing for a press that moved nothing", () => {
    // Arrange.
    const { onGesture } = show(page());

    // Act.
    drag(edge(2), 900);
    drag(edge(1), 0);
    drag(edge(1), -500);

    // Assert.
    expect(onGesture.mock.calls).toEqual([
      ["rewiden", "width/2", { pixels: "900" }],
      ["widen", "document", { depth: "1", pixels: "150" }],
    ]);
  });

  it("gives a kept column the page's own width again on a double click, and the arrow keys move the edge by a step", () => {
    // Arrange.
    const { onGesture } = show(page());

    // Act.
    fireEvent.doubleClick(edge(1));
    fireEvent.doubleClick(edge(2));
    fireEvent.keyDown(edge(1), { key: "ArrowRight" });

    // Assert: the first column keeps no width, so a double click on it sends nothing.
    expect(onGesture.mock.calls).toEqual([
      ["unwiden", "width/2", {}],
      ["widen", "document", { depth: "1", pixels: "256" }],
    ]);
    expect(widthOf("Payment")).toBe("240px");
  });

  it("puts a dragged column back when the gesture is refused, and shows the kept width the next model brings", async () => {
    // Arrange.
    const refused = vi.fn((): unknown => Promise.resolve("No."));
    const { update } = show(page(), refused);

    // Act.
    drag(edge(1), 100);
    await Promise.resolve();
    await Promise.resolve();
    const afterRefusal = widthOf("Orders");
    update(page({ widths: [{ id: "width/1", level: 1, width: 500 }] }));

    // Assert.
    expect(afterRefusal).toBe("240px");
    expect(widthOf("Orders")).toBe("500px");
  });

  it("offers no edge on a document that cannot be changed, nor with a definition that keeps no widths", () => {
    // Act.
    show(page({ readOnlyReason: "Read only." }));
    const readOnly = screen.queryAllByRole("separator").length;
    cleanup();
    render(<HierarchySurface model={page()} definition={{ ...definition, widths: undefined }} onGesture={vi.fn()} />);

    // Assert.
    expect(readOnly).toBe(0);
    expect(screen.queryAllByRole("separator")).toHaveLength(0);
  });
});

describe("the levels that fit", () => {
  it("counts the columns by their widths from the left, leaving the entries their least width", () => {
    // Act.
    const fit = (width: number) => levelsThatFit(width, (column) => [400, 240, 240][column] ?? 240);

    // Assert: 280 for the entries, then 400, 240 and 240.
    expect([fit(679), fit(680), fit(919), fit(920), fit(1160), fit(100)]).toEqual([1, 1, 1, 2, 3, 1]);
    expect(levelsThatFit(1000)).toBe(3);
  });
});
