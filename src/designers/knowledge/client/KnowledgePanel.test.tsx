import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { elementSelectionOf } from "@client/canvas/selection";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableModel } from "@client/table/library/api/tableModel";
import type { TableGesture } from "@client/table/library/api/tableEvents";
import { requestReveal, takeElementReveal } from "@client/shell/panels/revealRequests";
import { KnowledgePanel } from "./KnowledgePanel";

/**
 * The panel by what it hands on: the table the stream holds reaches the library, with the count
 * of edits not written yet, and an edit leaves naming the view it was made in. What the frame
 * says of the stream is everyCanvasHasOneRefusalSurface's to assert, over every registered tool.
 */

const stream = vi.hoisted(() => ({
  current: undefined as unknown,
  paths: [] as string[][],
}));

vi.mock("@client/designers/useTableStream", () => ({
  useTableStream: (path: string[]) => {
    stream.paths.push(path);
    return stream.current;
  },
}));

const surface = vi.hoisted(() => ({
  onGesture: undefined as ((gesture: TableGesture) => Promise<string>) | undefined,
  onSelectRows: undefined as ((rowIds: readonly string[]) => void) | undefined,
  onOpenValue: undefined as ((column: TableColumn, value: string) => void) | undefined,
}));

const connection = vi.hoisted(() => ({ select: vi.fn(), revealPath: vi.fn(), executeAction: vi.fn(() => Promise.resolve({})) }));
const pushed = vi.hoisted(() => ({ selection: null as unknown, actions: [] as unknown[] }));

vi.mock("@client/shell/context/ContextConnectionProvider", async (original) => ({
  ...(await original<typeof import("@client/shell/context/ContextConnectionProvider")>()),
  useContextConnection: () => connection,
  useContextSelection: () => pushed,
}));

vi.mock("@client/table/library/TableSurface", async (original) => {
  const actual = await original<typeof import("@client/table/library/TableSurface")>();
  return {
    ...actual,
    TableSurface: (props: Parameters<typeof actual.TableSurface>[0]) => {
      surface.onGesture = props.onGesture;
      surface.onSelectRows = props.onSelectRows;
      surface.onOpenValue = props.onOpenValue;
      return actual.TableSurface(props);
    },
  };
});

const ID = new Uint8Array([1]);

function cities(viewId: string): TableModel {
  const structure = {
    title: "Cities",
    columns: [{ id: "p1", name: "Name", kind: "text", options: [], width: 0, visible: true, isTitle: true, wraps: false, settings: {} }],
    views: [
      { id: "v1", name: "All" },
      { id: "v2", name: "Large" },
    ],
    settings: { ...EMPTY_TABLE.settings, viewId },
    rowCount: 0,
    readOnlyReason: "",
  };
  return applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] });
}

/** A table with rows of the given ids, as its stream holds them once they have arrived. */
function rowsOf(model: TableModel, ids: readonly string[]): TableModel {
  const rows = ids.map((id) => ({ id, depth: 0, cells: [{ columnId: "p1", values: [id], labels: [], pending: false }], isGroup: false, label: "", count: 0, collapsed: false, hasChildren: false, isNewRow: false }));
  return applyTableEvent(model, { kind: "rows", first: 0, rows, rowCount: ids.length });
}

function streamOf(model: TableModel, extra: Record<string, unknown> = {}) {
  return { model, loading: false, failure: "", pendingEdits: 0, refusal: "", setWindow: vi.fn(), setView: vi.fn(), edit: vi.fn(() => Promise.resolve("")), ...extra };
}

afterEach(() => {
  cleanup();
  stream.paths.length = 0;
  pushed.selection = null;
  pushed.actions = [];
});

describe("KnowledgePanel", () => {
  it("opens the table of the file it is given and shows it", () => {
    // Arrange.
    stream.current = streamOf(cities("v1"));

    // Act.
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["cities.adp"]} />);

    // Assert.
    expect(stream.paths.every((path) => path.join("/") === "cities.adp")).toBe(true);
    expect(stream.paths.length).toBeGreaterThan(0);
    expect(screen.getByRole("columnheader", { name: /Name/ })).toBeDefined();
    expect(screen.getByRole("tab", { name: "Large" })).toBeDefined();
  });

  it("hands on how many edits are not written yet", () => {
    // Arrange.
    stream.current = streamOf(cities("v1"), { pendingEdits: 2 });

    // Act.
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["cities.adp"]} />);

    // Assert.
    expect(screen.getByRole("status").textContent).toBe("2 changes are being saved…");
  });

  it("sends an edit with the view it was made in", async () => {
    // Arrange.
    const current = streamOf(cities("v2"));
    stream.current = current;
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["cities.adp"]} />);

    // Act.
    await surface.onGesture!({ kind: "addRow" });

    // Assert.
    expect(current.edit).toHaveBeenCalledWith({ kind: "addRow", viewId: "v2" });
  });

  it("makes one selected row the application's selection, as an element of its file, and several or none no selection", () => {
    // Arrange.
    stream.current = streamOf(cities("v1"));
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["tables", "cities.adp"]} />);

    // Act.
    surface.onSelectRows!(["r1"]);
    surface.onSelectRows!(["r1", "r2"]);
    surface.onSelectRows!([]);

    // Assert: the row under its file, by its id; then nothing, twice.
    const selections = connection.select.mock.calls.map((call) => call[0]);
    expect(selections[0].path.segments).toEqual(["tables", "cities.adp"]);
    expect(selections[0].detail.value.id.source.value.value).toBe("r1");
    expect(selections.slice(1)).toEqual([null, null]);
  });

  it("offers in a row's right-click menu what the application has for that row once it is the selection, and runs it", () => {
    // Arrange: the row r1 is the application's selection, and "Linked from" is its action.
    stream.current = streamOf(rowsOf(cities("v1"), ["r1", "r2"]));
    pushed.selection = elementSelectionOf(ID, ["tables", "cities.adp"], "r1");
    pushed.actions = [{ actions: [{ id: "knowledge.linked-from", label: "Linked from (1)…", icon: "", available: true, unavailableReason: "", items: [] }] }];
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["tables", "cities.adp"]} />);
    const [first, second] = screen.getAllByRole("row").slice(1);

    // Act.
    fireEvent.contextMenu(second!, { clientX: 20, clientY: 40 });
    const onAnotherRow = screen.queryByRole("menuitem", { name: "Linked from (1)…" });
    fireEvent.contextMenu(first!, { clientX: 20, clientY: 40 });
    fireEvent.click(screen.getByRole("menuitem", { name: "Linked from (1)…" }));

    // Assert: another row's menu has not the selection's actions; the selected row's runs its action.
    expect(onAnotherRow).toBeNull();
    expect(connection.executeAction).toHaveBeenCalledWith("knowledge.linked-from");
  });

  it("opens a related row where it lives: the other table's file, or this table's own row", () => {
    // Arrange: the table is in a folder, and relates to a table beside it and to itself.
    stream.current = streamOf(cities("v1"));
    connection.select.mockClear();
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["tables", "cities.adp"]} />);
    const relation = (targetFile: string, targetPath?: string): TableColumn => ({ id: "p5", name: "Province", kind: "relation", options: [], width: 0, visible: true, isTitle: false, wraps: false, settings: targetPath === undefined ? { targetFile } : { targetFile, targetPath } });

    // Act.
    surface.onOpenValue!(relation("provinces.yaml"), "nh");
    surface.onOpenValue!(relation("../shared/regions.yaml"), "x");
    surface.onOpenValue!(relation("$p7v3k9qd@provinces.yaml", "../reference/provinces.yaml"), "ut");
    surface.onOpenValue!(relation("."), "r2");

    // Assert: the files, from the table's own folder, a linked one where the designer found it; and the row of this table as the selection.
    expect(connection.revealPath.mock.calls.map((call) => call[0])).toEqual([
      ["tables", "provinces.yaml"],
      ["shared", "regions.yaml"],
      ["reference", "provinces.yaml"],
    ]);
    expect(connection.select).toHaveBeenCalledTimes(1);
    expect(connection.select.mock.calls[0]![0].detail.value.id.source.value.value).toBe("r2");
  });

  it("lands on the row that was opened from another table, in the table of that row", () => {
    // Arrange: the cities' table and the provinces' table, each in its panel.
    stream.current = streamOf(cities("v1"));
    connection.select.mockClear();
    const relation: TableColumn = { id: "p5", name: "Province", kind: "relation", options: [], width: 0, visible: true, isTitle: false, wraps: false, settings: { targetFile: "provinces.yaml" } };
    const { unmount } = render(<KnowledgePanel projectId={ID} entryId={ID} path={["tables", "cities.adp"]} />);
    const open = surface.onOpenValue!;
    render(<KnowledgePanel projectId={ID} entryId={new Uint8Array([2])} path={["tables", "provinces.adp"]} />);

    // Act: a province is opened from the cities' table.
    act(() => open(relation, "nh"));

    // Assert: the provinces' panel made that row the selection, under its own file; the cities' panel did not.
    expect(connection.select).toHaveBeenCalledTimes(1);
    const selection = connection.select.mock.calls[0]![0];
    expect(selection.path.segments).toEqual(["tables", "provinces.adp"]);
    expect(selection.detail.value.id.source.value.value).toBe("nh");
    unmount();
  });

  it("leaves an element of a diagram a cell links to for the diagram's canvas to select (spec 092, FR-025)", () => {
    // Arrange: a relation to a timeline, which the designer marks by the kinds of element Create adds there.
    stream.current = streamOf(cities("v1"));
    connection.revealPath.mockClear();
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["tables", "people.adp"]} />);
    const relation: TableColumn = { id: "p5", name: "Milestone", kind: "relation", options: [], width: 0, visible: true, isTitle: false, wraps: false, settings: { targetFile: "$lmy6158h@roadmap.tml", targetPath: "roadmap.tml", createKinds: "[]" } };

    // Act.
    act(() => surface.onOpenValue!(relation, "launch"));

    // Assert: the timeline is opened, and the moment waits there for its canvas, which takes it once.
    expect(connection.revealPath.mock.calls.map((call) => call[0])).toEqual([["tables", "roadmap.tml"]]);
    expect(takeElementReveal(["tables", "roadmap.tml"], () => true)).toBe("launch");
    expect(takeElementReveal(["tables", "roadmap.tml"], () => true)).toBeNull();
  });

  it("selects the row a link opened, in the table of its file, whichever of its two files the panel shows", () => {
    // Arrange: the provinces' table, open at its registration; a link names the row in its data file.
    stream.current = streamOf(cities("v1"));
    connection.select.mockClear();
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["tables", "provinces.adp"]} />);

    // Act.
    act(() => requestReveal({ entryId: ID, path: ["tables", "provinces.yaml"], mimeType: "", elementId: "nh" }));

    // Assert: the row is the selection, under the panel's own file, and it is taken once.
    expect(connection.select).toHaveBeenCalledTimes(1);
    const selection = connection.select.mock.calls[0]![0];
    expect(selection.path.segments).toEqual(["tables", "provinces.adp"]);
    expect(selection.detail.value.id.source.value.value).toBe("nh");
  });

  it("leaves a view an edit names itself alone", async () => {
    // Arrange.
    const current = streamOf(cities("v2"));
    stream.current = current;
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["cities.adp"]} />);

    // Act: renaming another view than the one in sight.
    await surface.onGesture!({ kind: "renameView", viewId: "v1", values: ["Everything"] });

    // Assert.
    expect(current.edit).toHaveBeenCalledWith({ kind: "renameView", viewId: "v1", values: ["Everything"] });
  });
});
