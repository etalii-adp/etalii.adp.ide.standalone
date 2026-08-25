import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  ContextLevelDetailSchema,
  ContextSelectionAction,
  ContextSelectionSource,
  type ContextLevelDetail,
  type ContextSelection,
} from "../../generated/context_pb";
import { EntryKind } from "../../generated/hierarchy_pb";
import { NONE_DETAIL, selectionFor } from "../context/ContextConnectionProvider";
import { DiagramTabsPanel } from "./DiagramTabsPanel";

// The canvas would open a stream; the tab model is what is under test. Mocked at the registry
// rather than at any one canvas, because this panel routes through the registry and knows no
// diagram type by name - the path shown is what proves the right diagram reached the panel.
vi.mock("./diagramCanvases", () => ({
  // One claimed type and everything else unclaimed, so both halves of the panel stay
  // exercised: the tab that renders, and the tab that says what it cannot render.
  canvasFor: (mimeType: string) =>
    mimeType === "freeplane/mindmap"
      ? {
          matches: () => true,
          Canvas: ({ path }: { path: string[] }) => <div data-testid="mindmap-canvas">{path.join("/")}</div>,
        }
      : undefined,
}));

const contextState: { selection: ContextSelection | null; levels: ContextLevelDetail[] } = { selection: null, levels: [] };

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextSelection: () => ({ ...contextState, actions: [], preview: null, pendingReveal: null, connected: true }),
  };
});

const projectId = new Uint8Array(16).fill(1);
const entryA = new Uint8Array(16).fill(7);
const entryB = new Uint8Array(16).fill(9);

function entryDetail(diagramMimeType: string): ContextLevelDetail {
  return create(ContextLevelDetailSchema, {
    detail: { case: "entry", value: { kind: EntryKind.FILE, available: true, diagramMimeType } },
  });
}

/** A fresh pushed selection: a new object every time, the way each push arrives. */
function push(entryId: Uint8Array, path: string[], mimeType: string, options: { plain?: boolean } = {}) {
  contextState.selection = selectionFor(
    ContextSelectionSource.EXPLORER,
    entryId,
    path,
    options.plain === true ? NONE_DETAIL : { case: "action", value: ContextSelectionAction.ACTIVATE },
  );
  contextState.levels = [entryDetail(mimeType)];
}

function renderPanel() {
  return render(<DiagramTabsPanel projectId={projectId} />);
}

describe("DiagramTabsPanel", () => {
  beforeEach(() => {
    contextState.selection = null;
    contextState.levels = [];
  });

  it("shows the empty state until something opens, with no placeholder tabs", () => {
    // Act.
    renderPanel();

    // Assert.
    expect(screen.getByText("Double-click a diagram in the explorer to open it here.")).toBeTruthy();
    expect(screen.queryByRole("tab")).toBeNull();
  });

  it("opens a focused tab on an ACTIVATE push of a diagram entry", () => {
    // Arrange.
    const { rerender } = renderPanel();
    push(entryA, ["docs", "architecture.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Act and assert, step by step.
    const tab = screen.getByRole("tab", { name: /architecture/ });
    expect(tab.getAttribute("aria-selected")).toBe("true");
    expect(tab.title).toBe("docs/architecture.adp");
    expect(screen.getByTestId("mindmap-canvas").textContent).toBe("docs/architecture.adp");
  });

  it("opens nothing for a plain selection of a diagram entry", () => {
    // Arrange and act.
    const { rerender } = renderPanel();
    push(entryA, ["docs", "architecture.adp"], "freeplane/mindmap", { plain: true });
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.queryByRole("tab")).toBeNull();
  });

  it("opens nothing for an activation of a non-diagram entry", () => {
    // Arrange and act.
    const { rerender } = renderPanel();
    push(entryA, ["readme.txt"], "");
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.queryByRole("tab")).toBeNull();
  });

  it("focuses the existing tab on re-activation instead of opening a duplicate", () => {
    // Arrange.
    const { rerender } = renderPanel();
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);
    push(entryB, ["b.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);
    expect(screen.getByRole("tab", { name: /^b$/ }).getAttribute("aria-selected")).toBe("true");

    // Act.
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.getAllByRole("tab")).toHaveLength(2);
    expect(screen.getByRole("tab", { name: /^a$/ }).getAttribute("aria-selected")).toBe("true");
  });

  it("opens a second tab when the same entry arrives under a changed path", () => {
    // Arrange.
    // A renamed file re-activated under its new name: the stale tab stays, independently
    // closable, and the fresh one opens (Requirement 5.2).
    const { rerender } = renderPanel();
    push(entryA, ["old.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Act.
    push(entryA, ["new.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.getAllByRole("tab")).toHaveLength(2);
    expect(screen.getByRole("tab", { name: /^new$/ }).getAttribute("aria-selected")).toBe("true");
  });

  it("does not re-run the rule for the same observed selection object", () => {
    // Arrange.
    const { rerender } = renderPanel();
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);
    fireEvent.click(screen.getByRole("button", { name: /Close a/ }));
    expect(screen.queryByRole("tab")).toBeNull();

    // Act.
    // The same selection object re-observed (an unrelated re-render): the closed tab must
    // not spring back - only a new push may open one.
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.queryByRole("tab")).toBeNull();
  });

  it("closing the active tab focuses the nearest neighbour, and closing the last shows the empty state", () => {
    // Arrange.
    const { rerender } = renderPanel();
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);
    push(entryB, ["b.adp"], "freeplane/mindmap");
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Act and assert, step by step.
    fireEvent.click(screen.getByRole("button", { name: /Close b/ }));
    expect(screen.getByRole("tab", { name: /^a$/ }).getAttribute("aria-selected")).toBe("true");

    fireEvent.click(screen.getByRole("button", { name: /Close a/ }));
    expect(screen.queryByRole("tab")).toBeNull();
    expect(screen.getByText("Double-click a diagram in the explorer to open it here.")).toBeTruthy();
  });

  it("names the type it cannot render inside the tab it still opens", async () => {
    // Arrange and act.
    const { rerender } = renderPanel();
    push(entryA, ["future.adp"], "vendor/unheard-of");
    rerender(<DiagramTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.getByRole("tab", { name: /future/ })).toBeTruthy();
    expect(await screen.findByText("No canvas can render vendor/unheard-of diagrams yet.")).toBeTruthy();
  });
});
