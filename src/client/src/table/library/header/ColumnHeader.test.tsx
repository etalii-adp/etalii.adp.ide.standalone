import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import type { TableGesture } from "../api/tableEvents";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableModel } from "../api/tableModel";
import type { ColumnAction, TableDefinition } from "../definition/tableDefinition";
import { pointer } from "../../../canvas/library/testing/canvasHarness";
import { TableSurface } from "../TableSurface";
import { addableKinds, columnMenuGroups, defaultKind, dropIndex, MINIMUM_COLUMN_WIDTH, resizedWidth } from "./columnActions";

const ALL_ACTIONS: readonly ColumnAction[] = ["rename", "changeType", "filter", "sortAscending", "sortDescending", "group", "hide", "wrap", "insertLeft", "insertRight", "duplicate", "delete"];

const definition: TableDefinition = {
  kinds: {
    text: { icon: "mdi-format-text", label: "Text", editor: "text" },
    number: { icon: "mdi-pound", label: "Number", editor: "number" },
    rollup: { icon: "mdi-sigma", label: "Rollup", addable: false },
  },
  columnActions: ALL_ACTIONS,
};

function column(id: string, name: string, kind: string, extra: Partial<TableColumn> = {}): TableColumn {
  return { id, name, kind, options: [], width: 100, visible: true, isTitle: false, wraps: false, settings: {}, ...extra };
}

function table(readOnlyReason = ""): TableModel {
  const structure = {
    title: "Cities",
    columns: [column("p1", "Name", "text", { isTitle: true }), column("p2", "Population", "number"), column("p3", "Country", "text")],
    views: [],
    settings: EMPTY_TABLE.settings,
    rowCount: 0,
    readOnlyReason,
  };
  return applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] });
}

function renderTable(model = table(), given: TableDefinition = definition) {
  const onGesture = vi.fn(async (_gesture: TableGesture) => "");
  render(<TableSurface model={model} definition={given} onGesture={onGesture} />);
  return { onGesture };
}

/** Lays the three headers out side by side, a hundred pixels each, since the test renderer lays nothing out. */
function layOutHeaders() {
  screen.getAllByRole("columnheader").forEach((header, index) => {
    header.getBoundingClientRect = () => ({ left: index * 100, right: index * 100 + 100, top: 0, bottom: 33, width: 100, height: 33, x: index * 100, y: 0, toJSON: () => ({}) });
  });
}

const header = (name: string) => screen.getAllByRole("columnheader").find((candidate) => candidate.textContent === name)!;
const headerButton = (name: string) => within(header(name)).getByRole("button");
const openMenu = (name: string) => {
  fireEvent(headerButton(name), pointer("pointerdown", { pointerId: 1, button: 0, clientX: 10, clientY: 10 }));
  fireEvent(headerButton(name), pointer("pointerup", { pointerId: 1, button: 0, clientX: 10, clientY: 10 }));
};
const choose = async (label: string) => {
  await act(async () => {
    fireEvent.click(screen.getByRole("menuitem", { name: label }));
  });
};
const drag = (element: Element, from: number, to: number, steps = 1) => {
  fireEvent(element, pointer("pointerdown", { pointerId: 1, button: 0, clientX: from, clientY: 10 }));
  for (let step = 1; step <= steps; step++) {
    fireEvent(element, pointer("pointermove", { pointerId: 1, clientX: from + ((to - from) * step) / steps, clientY: 10 }));
  }
  fireEvent(element, pointer("pointerup", { pointerId: 1, button: 0, clientX: to, clientY: 10 }));
};

afterEach(cleanup);

describe("a column's menu", () => {
  it.each([
    ["Filter", { kind: "addFilter", columnId: "p2" }],
    ["Sort ascending", { kind: "addSort", columnId: "p2", settings: { direction: "ascending" } }],
    ["Sort descending", { kind: "addSort", columnId: "p2", settings: { direction: "descending" } }],
    ["Group", { kind: "groupBy", columnId: "p2" }],
    ["Hide in view", { kind: "hideColumn", columnId: "p2" }],
    ["Wrap text", { kind: "setColumnWrap", columnId: "p2", settings: { wrap: "true" } }],
    ["Insert left", { kind: "addColumn", targetId: "p2", settings: { side: "left", type: "text" } }],
    ["Insert right", { kind: "addColumn", targetId: "p2", settings: { side: "right", type: "text" } }],
    ["Duplicate", { kind: "duplicateColumn", columnId: "p2" }],
    ["Delete", { kind: "deleteColumn", columnId: "p2" }],
  ] as const)("raises its gesture for %s", async (label, gesture) => {
    // Arrange.
    const { onGesture } = renderTable();

    // Act.
    openMenu("Population");
    await choose(label);

    // Assert: one gesture, naming the column.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith(gesture);
  });

  it("renames in the header itself, and raises the new name once", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    openMenu("Population");
    await choose("Rename");
    const field = screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement;
    expect(field.value).toBe("Population");

    // Act: Enter keeps the name, and the blur that follows must not raise it again.
    fireEvent.change(field, { target: { value: "Inhabitants" } });
    await act(async () => {
      fireEvent.keyDown(field, { key: "Enter" });
    });

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "renameColumn", columnId: "p2", values: ["Inhabitants"] });
    expect(screen.queryByRole("textbox", { name: "Name" })).toBeNull();
  });

  it("keeps the name when renaming is cancelled, left unchanged or emptied", async () => {
    // Arrange.
    const { onGesture } = renderTable();

    for (const finish of [
      (field: HTMLElement) => fireEvent.keyDown(field, { key: "Escape" }),
      (field: HTMLElement) => fireEvent.keyDown(field, { key: "Enter" }),
      (field: HTMLElement) => {
        fireEvent.change(field, { target: { value: "   " } });
        fireEvent.keyDown(field, { key: "Enter" });
      },
    ]) {
      // Act.
      openMenu("Population");
      await choose("Rename");
      await act(async () => finish(screen.getByRole("textbox", { name: "Name" })));
    }

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(header("Population")).toBeTruthy();
  });

  it("offers only the entries the definition lists", () => {
    // Arrange.
    renderTable(table(), { ...definition, columnActions: ["rename", "delete"] });

    // Act.
    openMenu("Population");

    // Assert.
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Rename", "Delete"]);
  });

  it("has no menu when the definition lists no entry, and none on a table that is read-only", () => {
    // Act and assert.
    renderTable(table(), { ...definition, columnActions: undefined });
    expect(within(header("Population")).queryByRole("button")).toBeNull();
    cleanup();

    renderTable(table("This file is of a newer version."));
    expect(within(header("Population")).queryByRole("button")).toBeNull();
    expect(screen.queryByRole("button", { name: "Add a property" })).toBeNull();
    expect(within(header("Population")).queryByRole("separator")).toBeNull();
  });
});

describe("columnMenuGroups", () => {
  const raise = vi.fn();
  const startRename = vi.fn();
  const flat = (given: TableColumn) => columnMenuGroups(given, definition, { raise, startRename }).flat();

  it("never hides or deletes the column that names a row, and says why", () => {
    // Act.
    const items = flat(column("p1", "Name", "text", { isTitle: true }));

    // Assert.
    for (const id of ["hide", "delete"]) {
      const item = items.find((candidate) => candidate.id === id)!;
      expect(item.disabled).toBe(true);
      expect(item.disabledReason).toBe("The column that names a row is always there.");
    }
    expect(items.find((candidate) => candidate.id === "rename")!.disabled).toBeUndefined();
  });

  it("offers the kinds that can be added as types, with the column's own greyed", () => {
    // Act.
    const change = flat(column("p2", "Population", "number")).find((candidate) => candidate.id === "changeType")!;
    const kinds = change.items![0]!;

    // Assert: the kind that cannot be added by hand is not offered at all.
    expect(kinds.map((kind) => kind.label)).toEqual(["Text", "Number"]);
    expect(kinds.map((kind) => kind.disabled)).toEqual([false, true]);

    kinds[0]!.onSelect!();
    expect(raise).toHaveBeenLastCalledWith({ kind: "setColumnType", columnId: "p2", settings: { type: "text" } });
  });

  it("offers to stop wrapping a column that wraps", () => {
    // Act.
    const wrap = flat(column("p2", "Notes", "text", { wraps: true })).find((candidate) => candidate.id === "wrap")!;

    // Assert.
    expect(wrap.label).toBe("Do not wrap");
    wrap.onSelect!();
    expect(raise).toHaveBeenLastCalledWith({ kind: "setColumnWrap", columnId: "p2", settings: { wrap: "false" } });
  });

  it("separates its entries into the four groups, leaving out a group with nothing offered", () => {
    expect(columnMenuGroups(column("p2", "Population", "number"), definition, { raise, startRename }).map((group) => group.length)).toEqual([2, 4, 2, 4]);
    expect(columnMenuGroups(column("p2", "Population", "number"), { ...definition, columnActions: ["filter", "delete"] }, { raise, startRename }).map((group) => group.map((item) => item.id))).toEqual([["filter"], ["delete"]]);
  });
});

describe("adding a column", () => {
  it("asks which kind, offers those that can be added, and raises one gesture", async () => {
    // Arrange.
    const { onGesture } = renderTable();

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Add a property" }));
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Text", "Number"]);
    await choose("Number");

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "addColumn", settings: { type: "number" } });
  });

  it("is not a column of the grid", () => {
    // Act.
    renderTable();

    // Assert.
    expect(screen.getAllByRole("columnheader")).toHaveLength(3);
    expect(screen.getByRole("grid").getAttribute("aria-colcount")).toBe("3");
  });
});

describe("dragging a header", () => {
  it("raises one reorder with the place the column lands in", () => {
    // Arrange.
    const { onGesture } = renderTable();
    layOutHeaders();

    // Act: the second column is dragged past the middle of the third, in several moves.
    drag(headerButton("Population"), 150, 270, 6);

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "moveColumn", columnId: "p2", index: 2 });
    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("raises nothing when the column is let go where it was", () => {
    // Arrange.
    const { onGesture } = renderTable();
    layOutHeaders();

    // Act: far enough to be a drag, not far enough to pass a neighbour's middle.
    drag(headerButton("Population"), 150, 180, 3);

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("opens the menu on a press that does not move", () => {
    // Arrange.
    const { onGesture } = renderTable();

    // Act.
    openMenu("Population");

    // Assert.
    expect(screen.getByRole("menu")).toBeTruthy();
    expect(onGesture).not.toHaveBeenCalled();
  });
});

describe("dragging a header's border", () => {
  const border = (name: string) => within(header(name)).getByRole("separator");
  const widthsDrawn = () => (screen.getAllByRole("row")[0] as HTMLElement).style.gridTemplateColumns;

  it("raises one width on release, however many moves the drag took", () => {
    // Arrange.
    const { onGesture } = renderTable();

    // Act: forty pixels wider, in twenty moves.
    drag(border("Population"), 200, 240, 20);

    // Assert: not one per pixel.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "resizeColumn", columnId: "p2", settings: { width: "140" } });
  });

  it("shows the width while it is dragged, without raising anything", () => {
    // Arrange.
    const { onGesture } = renderTable();
    const handle = border("Population");

    // Act: pressed and moved, not yet let go.
    fireEvent(handle, pointer("pointerdown", { pointerId: 1, button: 0, clientX: 200, clientY: 10 }));
    fireEvent(handle, pointer("pointermove", { pointerId: 1, clientX: 230, clientY: 10 }));

    // Assert.
    expect(widthsDrawn()).toBe("100px 130px 100px 40px");
    expect(onGesture).not.toHaveBeenCalled();

    // The preview goes when the drag ends; the model gives the width from then on.
    fireEvent(handle, pointer("pointerup", { pointerId: 1, button: 0, clientX: 230, clientY: 10 }));
    expect(widthsDrawn()).toBe("100px 100px 100px 40px");
  });

  it("stops at the narrowest a column may be", () => {
    // Arrange.
    const { onGesture } = renderTable();

    // Act.
    drag(border("Population"), 200, 20, 4);

    // Assert.
    expect(onGesture).toHaveBeenCalledWith({ kind: "resizeColumn", columnId: "p2", settings: { width: String(MINIMUM_COLUMN_WIDTH) } });
  });
});

describe("the first column", () => {
  it("is marked to stay in sight, in the header and in every row", () => {
    // Arrange.
    const model = applyTableEvent(table(), {
      kind: "rows",
      first: 0,
      rowCount: 1,
      rows: [{ id: "r1", depth: 0, cells: [], isGroup: false, label: "", count: 0, collapsed: false, hasChildren: false }],
    });

    // Act.
    renderTable(model);

    // Assert: a class the stylesheet makes sticky. That it stays put is a browser's to show, not this renderer's.
    expect(screen.getAllByRole("columnheader").map((candidate) => candidate.classList.contains("table-cell-first"))).toEqual([true, false, false]);
    expect(screen.getAllByRole("gridcell").map((candidate) => candidate.classList.contains("table-cell-first"))).toEqual([true, false, false]);
  });
});

describe("the arithmetic of a header drag", () => {
  const columns = [
    { left: 0, right: 100 },
    { left: 100, right: 200 },
    { left: 200, right: 300 },
  ];

  it("lands a column after every other whose middle the pointer has passed", () => {
    expect(dropIndex(0, 10, columns)).toBe(0);
    expect(dropIndex(0, 160, columns)).toBe(1);
    expect(dropIndex(0, 290, columns)).toBe(2);
    expect(dropIndex(2, 40, columns)).toBe(0);
    expect(dropIndex(2, 120, columns)).toBe(1);
    expect(dropIndex(1, 150, columns)).toBe(1);
  });

  it("gives whole pixels, never under the minimum", () => {
    expect(resizedWidth(100, 12.6)).toBe(113);
    expect(resizedWidth(100, -500)).toBe(MINIMUM_COLUMN_WIDTH);
  });

  it("knows which kinds can be added and which a new column starts as", () => {
    expect(addableKinds(definition).map((kind) => kind.kind)).toEqual(["text", "number"]);
    expect(defaultKind(definition)).toBe("text");
    expect(defaultKind({ ...definition, defaultKind: "number" })).toBe("number");
  });
});
