import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, fireEvent } from "@testing-library/react";
import { markTabDirty } from "./dirtyTabs";
import { requestTextTab } from "./textTabRequests";
import { create } from "@bufbuild/protobuf";
import { ContextLevelDetailSchema, ContextSelectionSource } from "../../generated/context-contract_pb";
import { type ContextLevelDetail } from "../../generated/context-contract_pb";
import { ContextSelectionAction, type ContextSelection } from "../../generated/context_pb";
import { EntryKind } from "../../generated/shared_pb";
import { NONE_DETAIL, selectionFor } from "../context/ContextConnectionProvider";
import { ToolTabsPanel } from "./ToolTabsPanel";

// Streams the canvases have opened and not yet closed. `vi.hoisted` because `vi.mock` is
// hoisted above ordinary module scope, so a plain `const` here would not exist yet when the
// factory runs.
//
// This counts STREAMS, not mounted canvases and not hook calls: `live` moves on a canvas's
// stream lifecycle, `opened` only ever rises. Reading one population for another is how the
// two-tab wedge stayed hidden - "three streams per diagram" says how many times the hook runs,
// not how many streams exist at once.
const streams = vi.hoisted(() => ({ live: 0, opened: 0, peakLive: 0 }));

// The canvas would open a stream; the tab model is what is under test. Mocked at the registry
// rather than at any one canvas, because this panel routes through the registry and knows no
// diagram type by name - the path shown is what proves the right diagram reached the panel.
vi.mock("./toolPanels", async () => {
  const { useEffect, useState } = await import("react");
  // ONE stable component, exactly like the real registry's registered canvases: a fresh
  // function per call would be a new component type to React and force a remount on every
  // render, hiding the very instance-reuse defect the remount guard exists to pin. The
  // canvas records which path it MOUNTED with; a stateful canvas reused across a tab switch
  // keeps its old state - for a real editor, the text being edited.
  const Canvas = ({ path }: { path: string[] }) => {
    const [mountedAt] = useState(() => path.join("/"));
    // The one thing a real canvas does that this guard is about: it opens a server stream
    // while it is mounted and closes it when it goes away.
    useEffect(() => {
      streams.opened += 1;
      streams.live += 1;
      streams.peakLive = Math.max(streams.peakLive, streams.live);
      return () => {
        streams.live -= 1;
      };
    }, []);
    return (
      <div data-testid="mindmap-canvas" data-mounted-at={mountedAt}>
        {path.join("/")}
      </div>
    );
  };
  const registration = { matches: () => true, Panel: Canvas };
  return {
    // One claimed family and everything else unclaimed, so both halves of the panel stay
    // exercised: the tab that renders, and the tab that says what it cannot render.
    panelFor: (mimeType: string) =>
      mimeType === "freeplane/mindmap" || mimeType.startsWith("editor/") ? registration : undefined,
  };
});

// An "Open as text" tab's canvas opens a stream of its own; the tab model is what is under
// test here, so the resolved-editor panel becomes a marker div showing what it was handed.
vi.mock("@client/editors/ResolvedTextEditorPanel", () => ({
  ResolvedTextEditorPanel: ({ path, editorId, initialLine }: { path: string[]; editorId?: string; initialLine?: number }) => (
    <div data-testid="text-canvas">
      {path.join("/")}|{editorId ?? ""}|{initialLine ?? ""}
    </div>
  ),
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
  return render(<ToolTabsPanel projectId={projectId} />);
}

describe("ToolTabsPanel", () => {
  beforeEach(() => {
    contextState.selection = null;
    contextState.levels = [];
    streams.live = 0;
    streams.opened = 0;
    streams.peakLive = 0;
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
    rerender(<ToolTabsPanel projectId={projectId} />);

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
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.queryByRole("tab")).toBeNull();
  });

  it("opens nothing for an activation of a non-diagram entry", () => {
    // Arrange and act.
    const { rerender } = renderPanel();
    push(entryA, ["readme.txt"], "");
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.queryByRole("tab")).toBeNull();
  });

  it("focuses the existing tab on re-activation instead of opening a duplicate", () => {
    // Arrange.
    const { rerender } = renderPanel();
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);
    push(entryB, ["b.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);
    expect(screen.getByRole("tab", { name: /^b$/ }).getAttribute("aria-selected")).toBe("true");

    // Act.
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.getAllByRole("tab")).toHaveLength(2);
    expect(screen.getByRole("tab", { name: /^a$/ }).getAttribute("aria-selected")).toBe("true");
  });

  it("remounts the content when activating a second file of the same editor", () => {
    // Arrange: two markdown files, one after the other - the double-click flow.
    const { rerender } = renderPanel();
    push(entryA, ["notes.md"], "editor/markdown");
    rerender(<ToolTabsPanel projectId={projectId} />);
    push(entryB, ["todo.md"], "editor/markdown");
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Assert.
    // Both tabs render the same component type at the same position; without a per-tab key
    // React reuses the instance, and the editor's own state - the text being edited - stays
    // the FIRST file's. The mount marker is what catches that.
    expect(screen.getByRole("tab", { name: /todo/ }).getAttribute("aria-selected")).toBe("true");
    expect(screen.getByTestId("mindmap-canvas").getAttribute("data-mounted-at")).toBe("todo.md");
  });

  it("opens a second tab when the same entry arrives under a changed path", () => {
    // Arrange.
    // A renamed file re-activated under its new name: the stale tab stays, independently
    // closable, and the fresh one opens (Requirement 5.2).
    const { rerender } = renderPanel();
    push(entryA, ["old.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Act.
    push(entryA, ["new.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.getAllByRole("tab")).toHaveLength(2);
    expect(screen.getByRole("tab", { name: /^new$/ }).getAttribute("aria-selected")).toBe("true");
  });

  it("does not re-run the rule for the same observed selection object", () => {
    // Arrange.
    const { rerender } = renderPanel();
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);
    fireEvent.click(screen.getByRole("button", { name: /Close a/ }));
    expect(screen.queryByRole("tab")).toBeNull();

    // Act.
    // The same selection object re-observed (an unrelated re-render): the closed tab must
    // not spring back - only a new push may open one.
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.queryByRole("tab")).toBeNull();
  });

  it("closing the active tab focuses the nearest neighbour, and closing the last shows the empty state", () => {
    // Arrange.
    const { rerender } = renderPanel();
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);
    push(entryB, ["b.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);

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
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Assert.
    expect(screen.getByRole("tab", { name: /future/ })).toBeTruthy();
    expect(await screen.findByText("No module can show vendor/unheard-of yet.")).toBeTruthy();
  });

  it("opens a focused text tab on a request - the Open as text gesture (R5.2)", async () => {
    // Arrange.
    renderPanel();

    // Act.
    act(() => requestTextTab({ path: ["docs", "flow.dsl"], editorId: "*" }));

    // Assert: the tab is open, focused, and its canvas was told to force the resolver.
    expect(screen.getByRole("tab", { name: /flow\.dsl/ }).getAttribute("aria-selected")).toBe("true");
    expect((await screen.findByTestId("text-canvas")).textContent).toBe("docs/flow.dsl|*|");
  });

  it("focuses the open text tab on a re-request and carries the new line (R8.1)", async () => {
    // Arrange: the same file asked for twice - an Open as text, then, after a diagram tab
    // took the focus away, a problem's line in it.
    const { rerender } = renderPanel();
    act(() => requestTextTab({ path: ["notes.md"], editorId: "*" }));
    push(entryA, ["a.adp"], "freeplane/mindmap");
    rerender(<ToolTabsPanel projectId={projectId} />);

    // Act.
    act(() => requestTextTab({ path: ["notes.md"], editorId: "*", line: 12 }));

    // Assert: still one text tab (beside the diagram's), focused again, now navigating.
    expect(screen.getAllByRole("tab", { name: /notes\.md/ }).length).toBe(1);
    expect(screen.getByRole("tab", { name: /notes\.md/ }).getAttribute("aria-selected")).toBe("true");
    expect((await screen.findByTestId("text-canvas")).textContent).toBe("notes.md|*|12");
  });

  it("asks before closing a text tab whose file has unsaved edits (R6.5)", () => {
    // Arrange: the dirty registry is keyed by the file's path - the identity the editor
    // panel registers under. This pins the key the two sides share; a mismatch here is the
    // silent-discard bug this test exists to keep out.
    renderPanel();
    act(() => requestTextTab({ path: ["notes.txt"], editorId: "plain" }));
    markTabDirty("notes.txt", true);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);

    try {
      // Act: decline the confirmation.
      fireEvent.click(screen.getByRole("button", { name: /Close notes/ }));

      // Assert: asked, and the tab survived the refusal.
      expect(confirm).toHaveBeenCalled();
      expect(screen.getByRole("tab", { name: /notes\.txt/ })).toBeTruthy();
    } finally {
      confirm.mockRestore();
      markTabDirty("notes.txt", false);
    }
  });
  it("holds one live Open stream however many documents are open (two-tab-connection-wedge task 3)", () => {
    // Arrange. Four documents opened one after another, the double-click flow four times.
    const { rerender } = renderPanel();
    const files: [Uint8Array, string][] = [
      [entryA, "one.adp"],
      [entryB, "two.adp"],
      [entryA, "three.adp"],
      [entryB, "four.adp"],
    ];

    // Act, asserting as we go: the count must not grow at any N, not merely at the end.
    for (const [id, name] of files) {
      push(id, [name], "freeplane/mindmap");
      rerender(<ToolTabsPanel projectId={projectId} />);
      expect(streams.live).toBe(1);
    }

    // Assert.
    // The invariant: one live stream for every N, because `TabbedPane` renders one tab's
    // content at a time and the inactive tabs' canvases are unmounted. Three server streams
    // per open document against a browser's ~6 connections per origin is what made the
    // second tab wedge, so this is a correctness property and not tidiness.
    expect(streams.peakLive).toBe(1);
    expect(screen.getAllByRole("tab")).toHaveLength(4);

    // And the floor that stops this passing while measuring nothing: streams really were
    // opened and closed four times. Without it a mock whose effect never ran would report
    // `live: 0`, `peakLive: 0` and a confident green - a zero from a dead instrument looks
    // exactly like a zero from a healthy one.
    expect(streams.opened).toBe(4);
  });

});
