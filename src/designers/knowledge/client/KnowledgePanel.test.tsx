import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { applyTableEvent, EMPTY_TABLE, type TableModel } from "@client/table/library/api/tableModel";
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

const surface = vi.hoisted(() => ({ onGesture: undefined as ((gesture: TableGesture) => Promise<string>) | undefined }));

vi.mock("@client/table/library/TableSurface", async (original) => {
  const actual = await original<typeof import("@client/table/library/TableSurface")>();
  return {
    ...actual,
    TableSurface: (props: Parameters<typeof actual.TableSurface>[0]) => {
      surface.onGesture = props.onGesture;
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
    expect(screen.getByRole("note").textContent).toBe("2 changes are being saved…");
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
