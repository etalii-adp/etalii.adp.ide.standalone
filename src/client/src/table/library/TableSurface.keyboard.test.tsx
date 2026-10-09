import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import type { TableGesture } from "./api/tableEvents";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableModel, type TableRow } from "./api/tableModel";
import type { TableDefinition } from "./definition/tableDefinition";
import { TableSurface } from "./TableSurface";

/**
 * The keyboard and the editors on the surface itself: which cell the keys are on, what each key
 * raises, and that a gesture the backend refuses changes nothing but says why.
 */

const definition: TableDefinition = {
  kinds: {
    text: { icon: "mdi-format-text", label: "Text", editor: "text" },
    number: { icon: "mdi-pound", label: "Number", editor: "number" },
    checkbox: { icon: "mdi-checkbox-marked-outline", label: "Checkbox", editor: "checkbox" },
    formula: { icon: "mdi-function", label: "Formula" },
  },
};

function column(id: string, name: string, kind: string): TableColumn {
  return { id, name, kind, options: [], width: 0, visible: true, isTitle: id === "p1", wraps: false, settings: {} };
}

function row(id: string, cells: Record<string, string>, extra: Partial<TableRow> = {}): TableRow {
  return {
    id,
    depth: 0,
    cells: Object.entries(cells).map(([columnId, value]) => ({ columnId, values: [value], labels: [], pending: false })),
    isGroup: false,
    label: "",
    count: 0,
    collapsed: false,
    hasChildren: false,
    ...extra,
  };
}

function table(readOnlyReason = ""): TableModel {
  const structure = {
    title: "Cities",
    columns: [column("p1", "Name", "text"), column("p2", "Population", "number"), column("p3", "Capital", "checkbox"), column("p4", "Density", "formula")],
    views: [],
    settings: EMPTY_TABLE.settings,
    rowCount: 3,
    readOnlyReason,
  };
  const rows = [row("r1", { p1: "Amsterdam", p2: "931298", p3: "true", p4: "4.2" }), row("r2", { p1: "Antwerp" }), row("r3", { p1: "Ghent" })];
  return applyTableEvent(applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] }), { kind: "rows", first: 0, rows, rowCount: 3 });
}

function renderTable(model = table(), refusal = "") {
  const onGesture = vi.fn(async (_gesture: TableGesture) => refusal);
  render(<TableSurface model={model} definition={definition} onGesture={onGesture} />);
  return { onGesture };
}

const grid = () => screen.getByRole("grid");
const cellAt = (rowIndex: number, columnIndex: number) => within(screen.getAllByRole("row")[rowIndex + 1]!).getAllByRole("gridcell")[columnIndex]!;
const activeCell = () => screen.getAllByRole("gridcell").find((candidate) => candidate.getAttribute("aria-selected") === "true");
const press = async (key: string, init: Partial<KeyboardEventInit> = {}) => {
  await act(async () => {
    fireEvent.keyDown(document.activeElement ?? grid(), { key, ...init });
  });
};
const focus = (rowIndex: number, columnIndex: number) => act(() => cellAt(rowIndex, columnIndex).focus());

afterEach(cleanup);

describe("the table's keyboard", () => {
  it("lets one cell take the Tab key, so the table is entered and left in one press", () => {
    // Act.
    renderTable();

    // Assert: the first cell until another is active.
    const reachable = screen.getAllByRole("gridcell").filter((candidate) => candidate.tabIndex === 0);
    expect(reachable).toEqual([cellAt(0, 0)]);
  });

  it("moves between cells with the arrow keys, and the focus goes with it", async () => {
    // Arrange.
    renderTable();
    focus(0, 0);

    // Act.
    await press("ArrowDown");
    await press("ArrowRight");

    // Assert.
    expect(activeCell()).toBe(cellAt(1, 1));
    expect(document.activeElement).toBe(cellAt(1, 1));
    expect(cellAt(1, 1).tabIndex).toBe(0);
    expect(cellAt(0, 0).tabIndex).toBe(-1);
  });

  it("opens the editor with Enter, sends the value, and gives the cell the focus back", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    focus(1, 0);

    // Act.
    await press("Enter");
    const editor = within(cellAt(1, 0)).getByLabelText("Name") as HTMLInputElement;
    expect(editor.value).toBe("Antwerp");
    fireEvent.change(editor, { target: { value: "Antwerpen" } });
    await act(async () => {
      fireEvent.keyDown(editor, { key: "Enter" });
    });

    // Assert: one gesture naming the row and the column, and the editor is gone.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "setCell", rowId: "r2", columnId: "p1", values: ["Antwerpen"], settings: undefined });
    expect(within(cellAt(1, 0)).queryByLabelText("Name")).toBeNull();
    expect(document.activeElement).toBe(cellAt(1, 0));
  });

  it("starts an edit by typing, replacing what the cell held", async () => {
    // Arrange.
    renderTable();
    focus(0, 0);

    // Act.
    await press("R");

    // Assert.
    expect((within(cellAt(0, 0)).getByLabelText("Name") as HTMLInputElement).value).toBe("R");
  });

  it("moves to the next cell when an edit ends with Tab", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    focus(1, 0);
    await press("Enter");
    const editor = within(cellAt(1, 0)).getByLabelText("Name");

    // Act.
    fireEvent.change(editor, { target: { value: "Antwerpen" } });
    await act(async () => {
      fireEvent.keyDown(editor, { key: "Tab" });
    });

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(activeCell()).toBe(cellAt(1, 1));
  });

  it("leaves the cell as it was when the edit is cancelled", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    focus(1, 0);
    await press("Enter");
    const editor = within(cellAt(1, 0)).getByLabelText("Name");

    // Act.
    fireEvent.change(editor, { target: { value: "Something else" } });
    await act(async () => {
      fireEvent.keyDown(editor, { key: "Escape" });
    });

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(cellAt(1, 0).textContent).toBe("Antwerp");
    expect(document.activeElement).toBe(cellAt(1, 0));
  });

  it("keeps the editor open with the reason when the backend refuses the value", async () => {
    // Arrange.
    renderTable(table(), "A number is expected.");
    focus(0, 1);
    await press("Enter");
    const editor = within(cellAt(0, 1)).getByLabelText("Population");

    // Act.
    fireEvent.change(editor, { target: { value: "many" } });
    await act(async () => {
      fireEvent.keyDown(editor, { key: "Enter" });
    });

    // Assert: the editor, with its reason, over a cell that is still what the model says.
    expect(within(cellAt(0, 1)).getByRole("alert").textContent).toBe("A number is expected.");
    expect((within(cellAt(0, 1)).getByLabelText("Population") as HTMLInputElement).value).toBe("many");
  });

  it("ticks and unticks a checkbox with Enter, without opening an editor", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    focus(0, 2);
    expect(within(cellAt(0, 2)).getByRole("checkbox").getAttribute("aria-checked")).toBe("true");

    // Act.
    await press("Enter");
    focus(1, 2);
    await press("Enter");

    // Assert: the opposite of what each cell holds, and an empty cell counts as unticked.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "setCell", rowId: "r1", columnId: "p3", values: ["false"], settings: undefined },
      { kind: "setCell", rowId: "r2", columnId: "p3", values: ["true"], settings: undefined },
    ]);
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("asks for a new row after the one the keys are on", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    focus(1, 0);

    // Act.
    await press("Enter", { shiftKey: true });

    // Assert.
    expect(onGesture).toHaveBeenCalledWith({ kind: "addRow", targetId: "r2" });
  });

  it("clears a cell with Delete, and leaves an empty one alone", async () => {
    // Arrange.
    const { onGesture } = renderTable();

    // Act.
    focus(0, 0);
    await press("Delete");
    focus(1, 1);
    await press("Delete");

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "setCell", rowId: "r1", columnId: "p1", values: [], settings: undefined });
  });

  it("shows why a gesture made outside an editor was refused", async () => {
    // Arrange.
    renderTable(table(), "This row is locked.");
    focus(0, 0);

    // Act.
    await press("Delete");

    // Assert: on the table's own line, and the cell is unchanged.
    expect(screen.getByRole("alert").textContent).toBe("This row is locked.");
    expect(cellAt(0, 0).textContent).toBe("Amsterdam");
  });

  it("does not edit a kind that names no editor", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    focus(0, 3);

    // Act.
    await press("Enter");
    await press("x");
    await press("Delete");

    // Assert.
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(onGesture).not.toHaveBeenCalled();
    expect(cellAt(0, 3).getAttribute("aria-readonly")).toBe("true");
  });

  it("does not edit a table that is read-only, and still moves through it", async () => {
    // Arrange.
    const { onGesture } = renderTable(table("This file is of a newer version."));
    focus(0, 0);

    // Act.
    await press("Enter");
    await press("Delete");
    await press("ArrowDown");

    // Assert.
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(onGesture).not.toHaveBeenCalled();
    expect(activeCell()).toBe(cellAt(1, 0));
    expect(grid().getAttribute("aria-readonly")).toBe("true");
  });

  it("opens the editor on a double click", async () => {
    // Arrange.
    renderTable();

    // Act.
    await act(async () => {
      fireEvent.doubleClick(cellAt(2, 0));
    });

    // Assert.
    expect((within(cellAt(2, 0)).getByLabelText("Name") as HTMLInputElement).value).toBe("Ghent");
  });
});
