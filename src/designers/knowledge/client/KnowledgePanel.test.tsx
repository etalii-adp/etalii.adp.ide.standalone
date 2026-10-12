import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render, screen } from "@testing-library/react";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableModel } from "@client/table/library/api/tableModel";
import type { TableGesture } from "@client/table/library/api/tableEvents";
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

const connection = vi.hoisted(() => ({ select: vi.fn(), revealPath: vi.fn() }));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({ useContextConnection: () => connection }));

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

function streamOf(model: TableModel, extra: Record<string, unknown> = {}) {
  return { model, loading: false, failure: "", pendingEdits: 0, refusal: "", setWindow: vi.fn(), setView: vi.fn(), edit: vi.fn(() => Promise.resolve("")), ...extra };
}

afterEach(() => {
  cleanup();
  stream.paths.length = 0;
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

  it("opens a related row where it lives: the other table's file, or this table's own row", () => {
    // Arrange: the table is in a folder, and relates to a table beside it and to itself.
    stream.current = streamOf(cities("v1"));
    connection.select.mockClear();
    render(<KnowledgePanel projectId={ID} entryId={ID} path={["tables", "cities.adp"]} />);
    const relation = (targetFile: string): TableColumn => ({ id: "p5", name: "Province", kind: "relation", options: [], width: 0, visible: true, isTitle: false, wraps: false, settings: { targetFile } });

    // Act.
    surface.onOpenValue!(relation("provinces.yaml"), "nh");
    surface.onOpenValue!(relation("../shared/regions.yaml"), "x");
    surface.onOpenValue!(relation("."), "r2");

    // Assert: the files, from the table's own folder; and the row of this table as the selection.
    expect(connection.revealPath.mock.calls.map((call) => call[0])).toEqual([
      ["tables", "provinces.yaml"],
      ["shared", "regions.yaml"],
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
