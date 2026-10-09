import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableModel, type TableRow } from "./api/tableModel";
import type { TableDefinition } from "./definition/tableDefinition";
import { DEFAULT_MARGIN, DEFAULT_ROW_HEIGHT } from "./rows/windowing";
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
});
