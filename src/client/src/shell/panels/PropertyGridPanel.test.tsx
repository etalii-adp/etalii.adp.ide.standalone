import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  ContextLevelDetailSchema,
  ContextSelectionAction,
  ContextSelectionSource,
  type ContextActionGroup,
  type ContextLevelDetail,
  type ContextSelection,
} from "../../generated/context_pb";
import { EntryKind } from "../../generated/hierarchy_pb";
import { NONE_DETAIL, selectionFor } from "../context/ContextConnectionProvider";
import { PropertyGridPanel, levelsOf } from "./PropertyGridPanel";

const contextState: { selection: ContextSelection | null; levels: ContextLevelDetail[]; actions: ContextActionGroup[] } = {
  selection: null,
  levels: [],
  actions: [],
};

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextSelection: () => ({ ...contextState, preview: null, pendingReveal: null, connected: true }),
  };
});

function entryDetail(kind: EntryKind, available = true): ContextLevelDetail {
  return create(ContextLevelDetailSchema, { detail: { case: "entry", value: { kind, available } } });
}

describe("PropertyGridPanel", () => {
  beforeEach(() => {
    contextState.selection = null;
    contextState.levels = [];
  });

  it("shows an intentional empty state when nothing is selected", () => {
    render(<PropertyGridPanel />);

    expect(screen.getByText("Nothing selected")).toBeTruthy();
  });

  it("shows a selected file's name, path, kind, availability and source", () => {
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16), ["docs", "design.mm"], NONE_DETAIL);
    contextState.levels = [entryDetail(EntryKind.FILE)];

    render(<PropertyGridPanel />);

    expect(screen.getByRole("heading", { name: "design.mm" })).toBeTruthy();
    expect(screen.getByText("docs/design.mm")).toBeTruthy();
    expect(screen.getByText("File")).toBeTruthy();
    expect(screen.getByText("Yes")).toBeTruthy();
    expect(screen.getByText("Explorer")).toBeTruthy();
  });

  it("shows every level of a chain, outermost first", () => {
    const inner = selectionFor(ContextSelectionSource.DIAGRAM_CANVAS, new Uint8Array(16).fill(2), ["root", "node"], {
      case: "action",
      value: ContextSelectionAction.ACTIVATE,
    });
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16).fill(1), ["docs"], { case: "child", value: inner });
    contextState.levels = [entryDetail(EntryKind.FOLDER, false)];

    render(<PropertyGridPanel />);

    const headings = screen.getAllByRole("heading").map((h) => h.textContent);
    expect(headings).toEqual(["docs", "node"]);
    expect(screen.getByText("Folder")).toBeTruthy();
    expect(screen.getByText("No")).toBeTruthy();
    expect(screen.getByText("Diagram")).toBeTruthy();
    expect(levelsOf(contextState.selection, contextState.levels)).toHaveLength(2);
  });
});
