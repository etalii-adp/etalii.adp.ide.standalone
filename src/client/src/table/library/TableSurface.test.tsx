import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableModel, type TableRow } from "./api/tableModel";
import type { TableDefinition } from "./definition/tableDefinition";
import { DEFAULT_MARGIN, DEFAULT_ROW_HEIGHT } from "./rows/windowing";
import { pointer } from "../../canvas/library/testing/canvasHarness";
import type { TableGesture } from "./api/tableEvents";
import { TableSurface } from "./TableSurface";

const definition: TableDefinition = {
  kinds: {
    text: { icon: "mdi-format-text", label: "Text" },
    number: { icon: "mdi-pound", label: "Number" },
  },
};

function column(id: string, name: string, kind: string, extra: Partial<TableColumn> = {}): TableColumn {
  return { id, name, kind, options: [], width: 0, visible: true, isTitle: false, wraps: false, settings: {}, ...extra };
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
    hasChildren: false, isNewRow: false,
    ...extra,
  };
}

/** A table of `rowCount` lines of which the client holds the given window, as a stream would leave it. */
function tableOf(rowCount: number, first: number, rows: readonly TableRow[]): TableModel {
  const structure = {
    title: "Cities",
    columns: [column("p1", "Name", "text", { isTitle: true }), column("p2", "Population", "number", { width: 120 }), column("p3", "Hidden", "text", { visible: false })],
    views: [{ id: "v1", name: "All" }],
    settings: EMPTY_TABLE.settings,
    rowCount,
    readOnlyReason: "",
  };
  const baseline = applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] });
  return applyTableEvent(baseline, { kind: "rows", first, rows, rowCount });
}

/** The rows of the body: every row of the grid but the header. */
const bodyRows = () => screen.getAllByRole("row").slice(1);

afterEach(cleanup);

describe("TableSurface", () => {
  it("draws only the window's rows for a model of ten thousand", () => {
    // Arrange: ten thousand lines, of which the client holds the first sixty.
    const held = Array.from({ length: 60 }, (_unused, index) => row(`r${index}`, { p1: `City ${index}` }));
    const viewport = 10 * DEFAULT_ROW_HEIGHT;

    // Act.
    render(<TableSurface model={tableOf(10_000, 0, held)} definition={definition} fallbackViewportHeight={viewport} />);

    // Assert: the ten rows in sight and the margin below them - not ten thousand, and not the sixty held.
    expect(bodyRows()).toHaveLength(10 + DEFAULT_MARGIN);
    expect(screen.getByRole("grid").getAttribute("aria-rowcount")).toBe("10001");
    expect(within(bodyRows()[0]!).getAllByRole("gridcell")[0]!.textContent).toBe("City 0");
  });

  it("says which rows it wants, once per window", () => {
    // Arrange.
    const onWindow = vi.fn();
    const model = tableOf(10_000, 0, []);
    const viewport = 10 * DEFAULT_ROW_HEIGHT;

    // Act: a second render of the same window must not ask again.
    const { rerender } = render(<TableSurface model={model} definition={definition} fallbackViewportHeight={viewport} onWindow={onWindow} />);
    rerender(<TableSurface model={{ ...model }} definition={definition} fallbackViewportHeight={viewport} onWindow={onWindow} />);

    // Assert.
    expect(onWindow).toHaveBeenCalledTimes(1);
    expect(onWindow).toHaveBeenCalledWith({ first: 0, count: 10 + DEFAULT_MARGIN });
  });

  it("asks for other rows when it is scrolled to them", () => {
    // Arrange.
    const onWindow = vi.fn();
    render(<TableSurface model={tableOf(10_000, 0, [])} definition={definition} fallbackViewportHeight={10 * DEFAULT_ROW_HEIGHT} onWindow={onWindow} />);
    const grid = screen.getByRole("grid");

    // Act: scrolled to line 500.
    Object.defineProperty(grid, "scrollTop", { configurable: true, value: 500 * DEFAULT_ROW_HEIGHT });
    fireEvent.scroll(grid);

    // Assert: the rows there, with the margin either side, and each drawn row says where it is.
    expect(onWindow).toHaveBeenLastCalledWith({ first: 500 - DEFAULT_MARGIN, count: 10 + 2 * DEFAULT_MARGIN });
    expect(bodyRows()).toHaveLength(10 + 2 * DEFAULT_MARGIN);
    expect(bodyRows()[0]!.getAttribute("aria-rowindex")).toBe(String(500 - DEFAULT_MARGIN + 2));
  });

  it("draws a line it does not hold yet as an empty row, so nothing jumps when it arrives", () => {
    // Arrange: the window is asked for, and only its first two lines have arrived.
    const model = tableOf(100, 0, [row("r0", { p1: "Amsterdam" }), row("r1", { p1: "Antwerp" })]);

    // Act.
    render(<TableSurface model={model} definition={definition} fallbackViewportHeight={4 * DEFAULT_ROW_HEIGHT} />);

    // Assert.
    const rows = bodyRows();
    expect(rows).toHaveLength(4 + DEFAULT_MARGIN);
    expect(rows[1]!.getAttribute("aria-busy")).toBeNull();
    expect(rows[2]!.getAttribute("aria-busy")).toBe("true");
    expect(rows.every((drawn) => drawn.style.height === `${DEFAULT_ROW_HEIGHT}px`)).toBe(true);
  });

  it("heads each visible column with its kind's icon and its name", () => {
    // Act.
    render(<TableSurface model={tableOf(1, 0, [row("r0", { p1: "Amsterdam", p2: "931298", p3: "secret" })])} definition={definition} />);

    // Assert: the hidden column has neither a header nor a cell.
    const headers = screen.getAllByRole("columnheader");
    expect(headers.map((header) => header.textContent)).toEqual(["Name", "Population"]);
    expect(within(headers[0]!).getByRole("img", { name: "Text" })).toBeTruthy();
    expect(within(headers[1]!).getByRole("img", { name: "Number" })).toBeTruthy();
    expect(screen.getByRole("grid").getAttribute("aria-colcount")).toBe("2");
    expect(within(bodyRows()[0]!).getAllByRole("gridcell").map((cell) => cell.textContent)).toEqual(["Amsterdam", "931298"]);
  });

  it("shows a kind its definition does not know, rather than hiding the column", () => {
    // Arrange.
    const model = { ...tableOf(0, 0, []), columns: [column("p9", "Mystery", "hologram")] };

    // Act.
    render(<TableSurface model={model} definition={definition} />);

    // Assert.
    expect(within(screen.getByRole("columnheader")).getByRole("img", { name: "Unknown type" })).toBeTruthy();
  });

  it("gives each column the width the model says, and the default otherwise", () => {
    // Act.
    render(<TableSurface model={tableOf(1, 0, [row("r0", { p1: "Amsterdam" })])} definition={{ ...definition, columnWidth: 200 }} />);

    // Assert.
    expect(bodyRows()[0]!.style.gridTemplateColumns).toBe("200px 120px");
  });

  it("draws a group's heading with its label and its count", () => {
    // Arrange.
    const heading = row("capital", {}, { isGroup: true, label: "Capital", count: 12 });

    // Act.
    render(<TableSurface model={tableOf(2, 0, [heading, row("r0", { p1: "Amsterdam" })])} definition={definition} />);

    // Assert.
    const cell = within(bodyRows()[0]!).getByRole("gridcell");
    expect(cell.textContent).toBe("Capital12");
    expect(cell.getAttribute("aria-colspan")).toBe("2");
  });

  it("marks a value that is not written yet", () => {
    // Arrange.
    const pending = { ...row("r0", { p1: "Amsterdam" }), cells: [{ columnId: "p1", values: ["Amsterdam"], labels: [], pending: true }] };

    // Act.
    render(<TableSurface model={tableOf(1, 0, [pending])} definition={definition} />);

    // Assert.
    expect(within(bodyRows()[0]!).getAllByRole("gridcell")[0]!.className).toContain("table-cell-pending");
  });

  it("shows a value's label where it has one", () => {
    // Arrange: a related row is stored by id and shown by title.
    const related = { ...row("r0", {}), cells: [{ columnId: "p1", values: ["a1", "b2"], labels: ["Netherlands", ""], pending: false }] };

    // Act.
    render(<TableSurface model={tableOf(1, 0, [related])} definition={definition} />);

    // Assert.
    expect(within(bodyRows()[0]!).getAllByRole("gridcell")[0]!.textContent).toBe("Netherlands, b2");
  });

  it("deletes a row from the row's menu, and puts a question it is answered with before deleting", async () => {
    // Arrange: a table whose rows have a menu; the first delete is answered with a question, the second is made.
    const answers = ["confirm:2 rows in other tables relate to this row.", ""];
    const onGesture = vi.fn(async () => answers.shift() ?? "");
    const rows = [row("r1", { p1: "Amsterdam" }), row("r2", { p1: "Antwerp" })];
    render(<TableSurface model={tableOf(2, 0, rows)} definition={{ ...definition, rowActions: ["delete"] }} onGesture={onGesture} />);

    // Act: the menu of the first row, and its one entry.
    fireEvent.contextMenu(bodyRows()[0]!, { clientX: 20, clientY: 40 });
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete row" }));

    // Assert: asked for, and the question put with nothing confirmed yet.
    expect(onGesture).toHaveBeenLastCalledWith({ kind: "deleteRows", rowId: "r1" });
    const ask = await screen.findByRole("alertdialog");
    expect(ask.textContent).toContain("2 rows in other tables relate to this row.");
    expect(onGesture).toHaveBeenCalledTimes(1);

    // Act.
    fireEvent.click(within(ask).getByRole("button", { name: "Delete" }));

    // Assert: the same gesture, confirmed, and the question gone.
    expect(onGesture).toHaveBeenLastCalledWith({ kind: "deleteRows", rowId: "r1", settings: { confirmed: "true" } });
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("keeps the row when the question is answered with Keep, and has no row menu unless its definition offers one", async () => {
    // Arrange.
    const onGesture = vi.fn(async () => "confirm:1 row in another table relates to this row.");
    const rows = [row("r1", { p1: "Amsterdam" })];
    const { rerender } = render(<TableSurface model={tableOf(1, 0, rows)} definition={{ ...definition, rowActions: ["delete"] }} onGesture={onGesture} />);
    fireEvent.contextMenu(bodyRows()[0]!, { clientX: 20, clientY: 40 });
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete row" }));

    // Act.
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Keep" }));

    // Assert: nothing more was raised.
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(onGesture).toHaveBeenCalledTimes(1);

    // A definition that offers no row action has no menu on its rows.
    rerender(<TableSurface model={tableOf(1, 0, rows)} definition={definition} onGesture={onGesture} />);
    fireEvent.contextMenu(bodyRows()[0]!, { clientX: 20, clientY: 40 });
    expect(screen.queryByRole("menuitem", { name: "Delete row" })).toBeNull();
  });

  it("draws what it is told about a cell on that cell, as the most serious of it, and on no other", () => {
    // Arrange: two findings about one cell, one about the table as a whole.
    const model = applyTableEvent(tableOf(2, 0, [row("r1", { p1: "Amsterdam", p2: "many" }), row("r2", { p1: "Antwerp" })]), {
      kind: "findings",
      findings: [
        { code: "a", severity: "info", message: "Kept as it was.", rowId: "r1", columnId: "p2" },
        { code: "b", severity: "warning", message: "Not a number.", rowId: "r1", columnId: "p2" },
        { code: "c", severity: "warning", message: "No title.", rowId: "", columnId: "" },
      ],
    });

    // Act.
    render(<TableSurface model={model} definition={definition} />);

    // Assert.
    const cells = within(bodyRows()[0]!).getAllByRole("gridcell");
    expect(cells[1]!.className).toContain("table-cell-finding table-cell-finding-warning");
    expect(cells[1]!.getAttribute("title")).toBe("Kept as it was.\nNot a number.");
    expect(cells[1]!.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getAllByRole("gridcell").filter((cell) => cell.className.includes("table-cell-finding"))).toHaveLength(1);
  });

  it("selects rows by their checkboxes, says which, and deletes those selected together", async () => {
    // Arrange.
    const onGesture = vi.fn(async () => "");
    const onSelectRows = vi.fn();
    const rows = [row("r1", { p1: "Amsterdam" }), row("r2", { p1: "Antwerp" }), row("r3", { p1: "Utrecht" })];
    render(<TableSurface model={tableOf(3, 0, rows)} definition={{ ...definition, rowActions: ["delete", "move"] }} onGesture={onGesture} onSelectRows={onSelectRows} />);
    const press = (name: string) => fireEvent.click(screen.getByRole("checkbox", { name }));

    // Act: two rows selected, one after the other.
    press("Select row 1");
    press("Select row 3");

    // Assert.
    expect(onSelectRows.mock.calls.map((call) => call[0])).toEqual([["r1"], ["r1", "r3"]]);
    expect(bodyRows().map((line) => line.getAttribute("aria-selected"))).toEqual(["true", "false", "true"]);

    // Act: the menu of one of them.
    fireEvent.contextMenu(bodyRows()[2]!, { clientX: 20, clientY: 40 });
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete 2 rows" }));

    // Assert: one gesture for both, and nothing selected any more.
    expect(onGesture).toHaveBeenLastCalledWith({ kind: "deleteRows", values: ["r1", "r3"] });
    expect(onSelectRows).toHaveBeenLastCalledWith([]);

    // A row that is not among the selected ones is deleted alone.
    press("Select row 1");
    press("Select row 3");
    fireEvent.contextMenu(bodyRows()[1]!, { clientX: 20, clientY: 40 });
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete row" }));
    expect(onGesture).toHaveBeenLastCalledWith({ kind: "deleteRows", rowId: "r2" });
  });

  it("moves a row by dragging its handle to another row, and has nothing to drag by in a sorted view", () => {
    // Arrange.
    const onGesture = vi.fn(async (_gesture: TableGesture) => "");
    const rows = [row("r1", { p1: "Amsterdam" }), row("r2", { p1: "Antwerp" }), row("r3", { p1: "Utrecht" })];
    const moving = { ...definition, rowActions: ["move"] as const };
    const { rerender } = render(<TableSurface model={tableOf(3, 0, rows)} definition={moving} onGesture={onGesture} />);
    const dragBy = (name: string, dy: number) => {
      const handle = screen.getByRole("img", { name });
      fireEvent(handle, pointer("pointerdown", { pointerId: 1, button: 0, clientX: 5, clientY: 5 }));
      fireEvent(handle, pointer("pointermove", { pointerId: 1, clientX: 5, clientY: 5 + dy }));
      fireEvent(handle, pointer("pointerup", { pointerId: 1, button: 0, clientX: 5, clientY: 5 + dy }));
    };

    // Act: the first row is let go two lines further down, and the last one line up.
    dragBy("Move row 1", 2 * DEFAULT_ROW_HEIGHT);
    dragBy("Move row 3", -DEFAULT_ROW_HEIGHT);

    // Assert: one gesture each, naming the row it lands beside and on which side.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "moveRows", rowId: "r1", targetId: "r3", settings: { place: "after" } },
      { kind: "moveRows", rowId: "r3", targetId: "r2", settings: { place: "before" } },
    ]);

    // A sorted view has its own order: a drag there moves nothing.
    const sorted = tableOf(3, 0, rows);
    rerender(<TableSurface model={{ ...sorted, settings: { ...sorted.settings, sorts: [{ columnId: "p1", descending: false }] } }} definition={{ ...moving, rowActions: ["move", "delete"] }} onGesture={onGesture} />);
    expect(screen.queryByRole("img", { name: "Move row 1" })).toBeNull();
    expect(onGesture).toHaveBeenCalledTimes(2);
  });

  it("draws each row a cell names as a link that opens it, where the table is given a way to open one", () => {
    // Arrange: a kind whose values name rows, shown by their titles.
    const onOpenValue = vi.fn();
    const relating: TableDefinition = { kinds: { ...definition.kinds, number: { icon: "mdi-arrow-top-right", label: "Relation", editor: "rows" } } };
    const cell = { columnId: "p2", values: ["nh", "zh"], labels: ["Noord-Holland", ""], pending: false };
    const model = tableOf(1, 0, [{ ...row("r1", { p1: "Amsterdam" }), cells: [{ columnId: "p1", values: ["Amsterdam"], labels: [], pending: false }, cell] }]);
    const { rerender } = render(<TableSurface model={model} definition={relating} onOpenValue={onOpenValue} />);

    // Act.
    const links = within(bodyRows()[0]!).getAllByRole("button");
    fireEvent.click(links[1]!);

    // Assert: by its title, or by its id when it has none; and opened by its id.
    expect(links.map((link) => link.textContent)).toEqual(["Noord-Holland", "zh"]);
    expect(onOpenValue).toHaveBeenCalledTimes(1);
    expect(onOpenValue.mock.calls[0]![1]).toBe("zh");

    // Without a way to open one, the values are text.
    rerender(<TableSurface model={model} definition={relating} />);
    expect(within(bodyRows()[0]!).queryAllByRole("button")).toHaveLength(0);
    expect(within(bodyRows()[0]!).getAllByRole("gridcell")[1]!.textContent).toBe("Noord-Holland, zh");
  });

  it("selects every row by the checkbox in the header, and sets one property for all selected rows at once", async () => {
    // Arrange: a table with a text and a number column, both of which can be edited.
    const onGesture = vi.fn(async (_gesture: TableGesture) => "");
    const editing: TableDefinition = { kinds: { text: { icon: "mdi-format-text", label: "Text", editor: "text" }, number: { icon: "mdi-pound", label: "Number", editor: "number" } }, rowActions: ["delete"] };
    const rows = [row("r1", { p1: "Amsterdam" }), row("r2", { p1: "Antwerp" }), row("r3", { p1: "Utrecht" })];
    render(<TableSurface model={tableOf(3, 0, rows)} definition={editing} onGesture={onGesture} />);
    expect(screen.queryByRole("toolbar", { name: "Selected rows" })).toBeNull();

    // Act: all of them, then one of them no longer.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select row 2" }));

    // Assert: the bar says how many, and offers the properties that can be edited.
    const bar = screen.getByRole("toolbar", { name: "Selected rows" });
    expect(bar.textContent).toContain("2 rows selected");
    const choose = within(bar).getByRole("combobox", { name: "Edit a property of the selected rows" });
    expect(within(choose).getAllByRole("option").map((option) => option.textContent)).toEqual(["Edit a property…", "Name", "Population"]);

    // Act: the number is set for both.
    fireEvent.change(choose, { target: { value: "p2" } });
    const field = within(screen.getByRole("group", { name: "Population of the selected rows" })).getByRole("textbox");
    fireEvent.change(field, { target: { value: "12" } });
    await act(async () => {
      fireEvent.keyDown(field, { key: "Enter" });
    });

    // Assert: one gesture, naming the property, the value and the rows.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "setCells", columnId: "p2", values: ["12"], settings: { rows: "r1,r3" } });

    // Act: and deleted together from the bar.
    fireEvent.click(within(bar).getByRole("button", { name: "Delete 2 rows" }));

    // Assert.
    expect(onGesture).toHaveBeenLastCalledWith({ kind: "deleteRows", values: ["r1", "r3"] });
    expect(screen.queryByRole("toolbar", { name: "Selected rows" })).toBeNull();
  });

  it("shows under each column what it calculates, and gives a column a calculation from the list its type has", () => {
    // Arrange: three columns that can calculate, the second of which does.
    const columns = [
      column("p1", "Name", "text", { isTitle: true, width: 200, settings: { calculations: "countAll,countValues" } }),
      column("p2", "Population", "number", { width: 120, settings: { calculations: "countAll,sum,average", calculate: "sum", calculation: "1276643" } }),
      column("p3", "Area", "number", { width: 90, settings: { calculations: "countAll,sum" } }),
    ];
    const structure = { title: "Cities", columns, views: [{ id: "v1", name: "All" }], settings: { ...EMPTY_TABLE.settings, viewId: "v1", settings: { freeze: "p2" } }, rowCount: 1, readOnlyReason: "" };
    const model = applyTableEvent(applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] }), { kind: "rows", first: 0, rows: [row("r1", { p1: "Amsterdam", p2: "931298" })], rowCount: 1 });
    const onGesture = vi.fn(async (_gesture: TableGesture) => "");
    render(<TableSurface model={model} definition={definition} onGesture={onGesture} />);

    // Assert: the line is the grid's last row, and says the sum under the population and nothing under the others.
    const line = screen.getByRole("row", { name: "Calculations" });
    expect(line.getAttribute("aria-rowindex")).toBe("3");
    expect(screen.getByRole("grid").getAttribute("aria-rowcount")).toBe("3");
    expect(within(line).getByRole("button", { name: "Calculate Population" }).textContent).toBe("Sum1276643");
    expect(within(line).getByRole("button", { name: "Calculate Name" }).textContent).toBe("Calculate");

    // The columns up to the one the view freezes stay where they stand: the second after the first's width, the third not at all.
    const cells = within(screen.getAllByRole("row")[1]!).getAllByRole("gridcell");
    expect(cells.map((cell) => cell.classList.contains("table-cell-frozen"))).toEqual([false, true, false]);
    expect(cells[1]!.style.left).toBe("200px");
    expect(cells[1]!.classList.contains("table-cell-frozen-edge")).toBe(true);
    expect(screen.getByRole("columnheader", { name: /Population/ }).style.left).toBe("200px");

    // Act: the area's list is opened, and its sum chosen; then the population's calculation is taken away.
    fireEvent.click(within(line).getByRole("button", { name: "Calculate Area" }));
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["None", "Count all", "Sum"]);
    fireEvent.click(screen.getByRole("menuitem", { name: "Sum" }));
    fireEvent.click(within(line).getByRole("button", { name: "Calculate Population" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "None" }));

    // Assert.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "setColumnCalculation", columnId: "p3", settings: { calculate: "sum" } },
      { kind: "setColumnCalculation", columnId: "p2", settings: { calculate: "" } },
    ]);
  });

  it("has no line of calculations where no column can calculate, and shows one that cannot be changed in a table that cannot be edited", () => {
    // Arrange and act: the plain table, then one of a file that cannot be edited whose column calculates.
    const { unmount } = render(<TableSurface model={tableOf(1, 0, [row("r1", { p1: "Amsterdam" })])} definition={definition} onGesture={vi.fn(async () => "")} />);
    expect(screen.queryByRole("row", { name: "Calculations" })).toBeNull();
    unmount();
    const columns = [column("p1", "Name", "text", { isTitle: true, settings: { calculations: "countAll", calculate: "countAll", calculation: "9" } })];
    const structure = { title: "Cities", columns, views: [], settings: EMPTY_TABLE.settings, rowCount: 0, readOnlyReason: "The file is of a newer version." };
    render(<TableSurface model={applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] })} definition={definition} onGesture={vi.fn(async () => "")} />);

    // Assert: said, with nothing to press.
    const line = screen.getByRole("row", { name: "Calculations" });
    expect(line.textContent).toBe("Count all9");
    expect(within(line).queryByRole("button")).toBeNull();
  });

  it("says nothing about itself while all is well", () => {
    // Act.
    render(<TableSurface model={tableOf(0, 0, [])} definition={definition} pendingEdits={0} />);

    // Assert.
    expect(screen.queryByRole("note")).toBeNull();
  });

  it("says why a table cannot be changed, before anything else", () => {
    // Act.
    render(<TableSurface model={{ ...tableOf(0, 0, []), readOnlyReason: "This table was written by a newer version." }} definition={definition} pendingEdits={2} />);

    // Assert.
    expect(screen.getByRole("note").textContent).toBe("This table was written by a newer version.");
  });

  it("says how many edits are not written yet", () => {
    // Act.
    const { rerender } = render(<TableSurface model={tableOf(0, 0, [])} definition={definition} pendingEdits={1} />);

    // Assert.
    expect(screen.getByRole("note").textContent).toBe("1 change is being saved…");

    // Act.
    rerender(<TableSurface model={tableOf(0, 0, [])} definition={definition} pendingEdits={3} />);

    // Assert.
    expect(screen.getByRole("note").textContent).toBe("3 changes are being saved…");
  });
});
