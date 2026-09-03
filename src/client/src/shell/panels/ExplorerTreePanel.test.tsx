import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { base64Encode } from "@bufbuild/protobuf/wire";
import {
  EntryDiagramState,
  EntryKind,
  EntrySchema,
  HierarchyChangeSchema,
  type Entry,
} from "../../generated/hierarchy_pb";
import {
  ContextActionGroupSchema,
  ContextSelectionAction,
  ContextSelectionSource,
  type ContextActionGroup,
  type ContextSelection,
} from "../../generated/context_pb";
import { NONE_DETAIL, selectionFor } from "../context/ContextConnectionProvider";
import {
  EMPTY_TREE_STATE,
  ExplorerTreePanel,
  applyEntries,
  applyHierarchyChange,
  entryFocusKey,
  matchShortcut,
  neighbourKey,
  pathOf,
  iconClassFor,
  resolveRevealPath,
  visibleKeys,
  type ShortcutEventLike,
  type TreeState,
} from "./ExplorerTreePanel";

const listEntries = vi.fn();
const watchHierarchy = vi.fn();
const select = vi.fn<(selection: ContextSelection | null) => void>();
const executeAction = vi.fn();
const executeShortcut = vi.fn();
const clearReveal = vi.fn();

/** What the (mocked) context connection currently holds; tests set it to simulate a push. */
const contextState: { selection: ContextSelection | null; actions: ContextActionGroup[]; pendingReveal: string[] | null } = {
  selection: null,
  actions: [],
  pendingReveal: null,
};

function resetContext() {
  contextState.selection = null;
  contextState.actions = [];
  contextState.pendingReveal = null;
}

vi.mock("../../auth/AuthContext", () => ({
  useAuth: () => ({ transport: {} }),
}));

vi.mock("@connectrpc/connect", () => ({
  createClient: () => ({ listEntries, watchHierarchy }),
}));

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, executeAction, executeShortcut, clearReveal }),
    useContextSelection: () => ({ ...contextState, levels: [], preview: null, connected: true }),
  };
});

function id(byte: number): Uint8Array {
  return new Uint8Array(16).fill(byte);
}

function key(byte: number): string {
  return base64Encode(id(byte));
}

function makeEntry(idByte: number, name: string, kind: EntryKind, parentByte?: number, hasChildren = false, diagramState = EntryDiagramState.ENTRY_DIAGRAM_STATE_UNSPECIFIED): Entry {
  return create(EntrySchema, {
    id: { value: id(idByte) },
    parentId: parentByte === undefined ? undefined : { value: id(parentByte) },
    name,
    kind,
    available: true,
    hasChildren,
    diagramState,
  });
}

describe("applyEntries", () => {
  it("populates root state from a ListEntries response for the root folder", () => {
    // Arrange.
    const entries = [makeEntry(1, "a.txt", EntryKind.FILE), makeEntry(2, "sub", EntryKind.FOLDER)];

    // Act.
    const state = applyEntries(EMPTY_TREE_STATE, undefined, entries);

    // Assert.
    expect(state.rootFetched).toBe(true);
    expect(state.rootKeys).toEqual([key(1), key(2)]);
    expect(state.nodesByKey[key(1)]?.name).toBe("a.txt");
    expect(state.nodesByKey[key(2)]?.name).toBe("sub");
  });

  it("attaches a folder's children under that folder's key, not the root", () => {
    // Arrange.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);

    // Act.
    state = applyEntries(state, key(2), [makeEntry(3, "inside.txt", EntryKind.FILE, 2)]);

    // Assert.
    expect(state.rootKeys).toEqual([key(2)]);
    expect(state.nodesByKey[key(2)]?.childKeys).toEqual([key(3)]);
    expect(state.nodesByKey[key(3)]?.name).toBe("inside.txt");
  });

  it("preserves a folder's expanded state when it is re-listed", () => {
    // Arrange.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);
    state = {
      ...state,
      nodesByKey: { ...state.nodesByKey, [key(2)]: { ...state.nodesByKey[key(2)]!, expanded: true } },
    };

    // Act.
    state = applyEntries(state, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);

    // Assert.
    expect(state.nodesByKey[key(2)]?.expanded).toBe(true);
  });
});

describe("applyHierarchyChange", () => {
  function stateWithFolderAndListedChild(): TreeState {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);
    state = applyEntries(state, key(2), []);
    return state;
  }

  it("created: inserts a new node under its parent when the parent's children are already listed", () => {
    // Arrange.
    const state = stateWithFolderAndListedChild();
    const change = create(HierarchyChangeSchema, {
      change: { case: "created", value: { entry: makeEntry(3, "new.txt", EntryKind.FILE, 2) } },
    });

    // Act.
    const next = applyHierarchyChange(state, change);

    // Assert.
    expect(next.nodesByKey[key(2)]?.childKeys).toEqual([key(3)]);
    expect(next.nodesByKey[key(3)]?.name).toBe("new.txt");
  });

  it("created: does nothing when the parent's children were never listed on this connection", () => {
    // Arrange.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);
    // Note: unlike stateWithFolderAndListedChild, "sub"'s own children are never listed here.
    const change = create(HierarchyChangeSchema, {
      change: { case: "created", value: { entry: makeEntry(3, "new.txt", EntryKind.FILE, 2) } },
    });

    // Act.
    const next = applyHierarchyChange(state, change);

    // Assert.
    expect(next.nodesByKey[key(2)]?.childKeys).toBeUndefined();
    expect(next).toEqual(state);
  });

  it("created: appends a new root-level entry", () => {
    // Arrange.
    const state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(1, "a.txt", EntryKind.FILE)]);
    const change = create(HierarchyChangeSchema, {
      change: { case: "created", value: { entry: makeEntry(4, "b.txt", EntryKind.FILE) } },
    });

    // Act.
    const next = applyHierarchyChange(state, change);

    // Assert.
    expect(next.rootKeys).toEqual([key(1), key(4)]);
  });

  it("removed: removes the node and drops it from its parent's childKeys", () => {
    // Arrange.
    const state = stateWithFolderAndListedChild();
    const withChild = applyEntries(state, key(2), [makeEntry(3, "child.txt", EntryKind.FILE, 2)]);
    const change = create(HierarchyChangeSchema, { change: { case: "removed", value: { entryId: { value: id(3) } } } });

    // Act.
    const next = applyHierarchyChange(withChild, change);

    // Assert.
    expect(next.nodesByKey[key(3)]).toBeUndefined();
    expect(next.nodesByKey[key(2)]?.childKeys).toEqual([]);
  });

  it("removed: removes an entire subtree, not just the direct node", () => {
    // Arrange.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);
    state = applyEntries(state, key(2), [makeEntry(3, "nested", EntryKind.FOLDER, 2)]);
    state = applyEntries(state, key(3), [makeEntry(4, "leaf.txt", EntryKind.FILE, 3)]);
    const change = create(HierarchyChangeSchema, { change: { case: "removed", value: { entryId: { value: id(2) } } } });

    // Act.
    const next = applyHierarchyChange(state, change);

    // Assert.
    expect(next.nodesByKey[key(2)]).toBeUndefined();
    expect(next.nodesByKey[key(3)]).toBeUndefined();
    expect(next.nodesByKey[key(4)]).toBeUndefined();
    expect(next.rootKeys).toEqual([]);
  });

  it("renamed: updates the node's displayed name, preserving its id and children", () => {
    // Arrange.
    const state = stateWithFolderAndListedChild();
    const change = create(HierarchyChangeSchema, {
      change: { case: "renamed", value: { entryId: { value: id(2) }, newName: "renamed-sub" } },
    });

    // Act.
    const next = applyHierarchyChange(state, change);

    // Assert.
    expect(next.nodesByKey[key(2)]?.name).toBe("renamed-sub");
    expect(next.nodesByKey[key(2)]?.childKeys).toEqual([]);
  });

  it("carries each entry's diagram state into its node, and iconClassFor names the class", () => {
    // Arrange and act: one node per state the backend can push (small-refinements Req 3.1-3.3).
    const state = applyEntries(EMPTY_TREE_STATE, undefined, [
      makeEntry(1, "ideas.mm", EntryKind.FILE, undefined, false, EntryDiagramState.REGISTERED),
      makeEntry(2, "build.yml", EntryKind.FILE, undefined, false, EntryDiagramState.POTENTIAL),
      makeEntry(3, "notes.txt", EntryKind.FILE),
    ]);

    // Assert: registered and potential get their classes; neutral gets none at all.
    expect(iconClassFor(state.nodesByKey[key(1)]!)).toBe("explorer-tree-icon-registered");
    expect(iconClassFor(state.nodesByKey[key(2)]!)).toBe("explorer-tree-icon-potential");
    expect(iconClassFor(state.nodesByKey[key(3)]!)).toBe("");
  });

  it("updated: moves an entry's diagram state - a registration appearing upgrades its subject", () => {
    // Arrange.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [
      makeEntry(1, "bare.mm", EntryKind.FILE, undefined, false, EntryDiagramState.POTENTIAL),
    ]);
    const change = create(HierarchyChangeSchema, {
      change: { case: "updated", value: { entryId: { value: id(1) }, hasChildren: false, diagramState: EntryDiagramState.REGISTERED } },
    });

    // Act.
    state = applyHierarchyChange(state, change);

    // Assert.
    expect(iconClassFor(state.nodesByKey[key(1)]!)).toBe("explorer-tree-icon-registered");
  });

  it("updated: refreshes a folder's hasChildren flag", () => {
    // Arrange.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER, undefined, false)]);
    const change = create(HierarchyChangeSchema, {
      change: { case: "updated", value: { entryId: { value: id(2) }, hasChildren: true } },
    });

    // Act.
    state = applyHierarchyChange(state, change);

    // Assert.
    expect(state.nodesByKey[key(2)]?.hasChildren).toBe(true);
  });

  it("an id it doesn't know about is a no-op", () => {
    // Arrange.
    const state = stateWithFolderAndListedChild();
    const change = create(HierarchyChangeSchema, { change: { case: "removed", value: { entryId: { value: id(99) } } } });

    // Act.
    const next = applyHierarchyChange(state, change);

    // Assert.
    expect(next).toEqual(state);
  });
});

describe("visibleKeys", () => {
  /** root: [folder 2 (expanded, children 3 & 4), file 1]; folder 4 collapsed with a child 5. */
  function nestedState(): TreeState {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [
      makeEntry(2, "sub", EntryKind.FOLDER, undefined, true),
      makeEntry(1, "a.txt", EntryKind.FILE),
    ]);
    state = applyEntries(state, key(2), [
      makeEntry(4, "deeper", EntryKind.FOLDER, 2, true),
      makeEntry(3, "inside.txt", EntryKind.FILE, 2),
    ]);
    state = applyEntries(state, key(4), [makeEntry(5, "leaf.txt", EntryKind.FILE, 4)]);
    return expand(state, key(2));
  }

  function expand(state: TreeState, nodeKey: string): TreeState {
    return { ...state, nodesByKey: { ...state.nodesByKey, [nodeKey]: { ...state.nodesByKey[nodeKey]!, expanded: true } } };
  }

  it("walks an expanded folder's children but skips a collapsed one's", () => {
    // Arrange, act and assert.
    expect(visibleKeys(nestedState())).toEqual([key(2), key(4), key(3), key(1)]);
  });

  it("includes a newly-expanded folder's children from then on", () => {
    // Arrange, act and assert.
    expect(visibleKeys(expand(nestedState(), key(4)))).toEqual([key(2), key(4), key(5), key(3), key(1)]);
  });

  describe("neighbourKey", () => {
    it("moves down and up through the rendered order", () => {
      // Act.
      const state = nestedState();

      // Assert.
      expect(neighbourKey(state, key(2), 1)).toBe(key(4));
      expect(neighbourKey(state, key(3), -1)).toBe(key(4));
    });

    it("stops rather than wrapping at either end", () => {
      // Act.
      const state = nestedState();

      // Assert.
      expect(neighbourKey(state, key(1), 1)).toBeUndefined();
      expect(neighbourKey(state, key(2), -1)).toBeUndefined();
    });

    it("starts at the first entry when nothing is focused yet", () => {
      // Arrange, act and assert.
      expect(neighbourKey(nestedState(), undefined, 1)).toBe(key(2));
    });
  });

  describe("entryFocusKey", () => {
    it("returns to the previously-focused entry when it is still rendered", () => {
      // Arrange, act and assert.
      expect(entryFocusKey(nestedState(), key(3))).toBe(key(3));
    });

    it("falls back to the first entry when the previous one is gone", () => {
      // Arrange, act and assert.
      expect(entryFocusKey(nestedState(), key(99))).toBe(key(2));
    });
  });
});

describe("matchShortcut", () => {
  function groups(overrides?: { available?: boolean; shift?: boolean }): ContextActionGroup[] {
    return [
      create(ContextActionGroupSchema, {
        actions: [
          {
            id: "hierarchy.rename",
            label: "Rename…",
            icon: "mdi-pencil-outline",
            available: overrides?.available ?? true,
            shortcut: { key: "F2", shift: overrides?.shift ?? false },
          },
        ],
      }),
    ];
  }

  function press(key: string, modifiers?: Partial<ShortcutEventLike>): ShortcutEventLike {
    return { key, ctrlKey: false, shiftKey: false, altKey: false, metaKey: false, ...modifiers };
  }

  it("finds the action bound to the pressed key", () => {
    // Arrange, act and assert.
    expect(matchShortcut(groups(), press("F2"))?.id).toBe("hierarchy.rename");
  });

  it("ignores a key nothing is bound to", () => {
    // Arrange, act and assert.
    expect(matchShortcut(groups(), press("F4"))).toBeUndefined();
  });

  it("requires the modifiers to match exactly", () => {
    // Arrange, act and assert.
    expect(matchShortcut(groups(), press("F2", { shiftKey: true }))).toBeUndefined();
    expect(matchShortcut(groups({ shift: true }), press("F2", { shiftKey: true }))?.id).toBe("hierarchy.rename");
  });

  it("ignores an action the backend reported unavailable, so its shortcut is inert too", () => {
    // Arrange, act and assert.
    expect(matchShortcut(groups({ available: false }), press("F2"))).toBeUndefined();
  });

  it("finds nothing at all when no actions were reported for the entry", () => {
    // Arrange, act and assert.
    expect(matchShortcut([], press("F2"))).toBeUndefined();
  });
});

describe("ExplorerTreePanel keyboard navigation and triggers", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetContext();
    // A stream that never yields: these tests drive the tree, not the change feed.
    watchHierarchy.mockReturnValue({
      // eslint-disable-next-line @typescript-eslint/no-empty-function
      async *[Symbol.asyncIterator]() {},
    });
    executeAction.mockResolvedValue({ accepted: true, error: "" });
  });

  /** Root holds folder "sub" (with one child) and file "a.txt"; the folder is not expanded yet. */
  function mockRootAndChildren() {
    listEntries.mockImplementation(({ folderId }: { folderId?: { value: Uint8Array } }) =>
      Promise.resolve(
        folderId
          ? { result: { case: "entries", value: { entries: [makeEntry(3, "inside.txt", EntryKind.FILE, 2)] } } }
          : {
              result: {
                case: "entries",
                value: { entries: [makeEntry(2, "sub", EntryKind.FOLDER, undefined, true), makeEntry(1, "a.txt", EntryKind.FILE)] },
              },
            },
      ),
    );
  }

  async function renderPanel() {
    mockRootAndChildren();
    const rendered = render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("sub");
    return rendered;
  }

  function row(name: string): HTMLButtonElement {
    return screen.getByText(name).closest("button") as HTMLButtonElement;
  }

  /** Focus for real, inside act, so the row's onFocus state update is flushed before asserting. */
  function focusRow(name: string) {
    act(() => row(name).focus());
  }

  function tree(): HTMLElement {
    return screen.getByRole("tree");
  }

  const renameGroups = () => [
    create(ContextActionGroupSchema, {
      actions: [{ id: "hierarchy.rename", label: "Rename…", icon: "mdi-pencil-outline", available: true, shortcut: { key: "F2" } }],
    }),
  ];

  /** What the backend would push after selecting entry `byte`: the chain and its actions. */
  function pushed(byte: number, path: string[], groups: ContextActionGroup[], detail = NONE_DETAIL) {
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, id(byte), path, detail);
    contextState.actions = groups;
  }

  it("is a single Tab stop: exactly one row is tabbable at a time", async () => {
    // Arrange.
    await renderPanel();

    // Act.
    const tabbable = screen.getAllByRole("button").filter((button) => button.tabIndex === 0);

    // Assert.
    expect(tabbable).toHaveLength(1);
  });

  it("marks the focused row with a class distinct from hover", async () => {
    // Arrange.
    await renderPanel();

    // Act.
    focusRow("a.txt");

    // Assert.
    expect(row("a.txt").className).toContain("explorer-tree-node-focused");
    expect(row("sub").className).not.toContain("explorer-tree-node-focused");
  });

  it("reports a focused entry as a plain selection carrying its id and project-relative path", async () => {
    // Arrange.
    await renderPanel();

    // Act.
    focusRow("a.txt");

    // Assert.
    await waitFor(() => expect(select).toHaveBeenCalled());
    const sent = select.mock.calls.at(-1)?.[0];
    expect(sent?.source).toBe(ContextSelectionSource.EXPLORER);
    expect(sent?.path?.segments).toEqual(["a.txt"]);
    expect(sent?.id?.source).toMatchObject({ case: "entryId", value: { value: id(1) } });
    expect(sent?.detail.case).toBe("none");
  });

  it("reports a nested entry with the path built from its parents", async () => {
    // Arrange.
    await renderPanel();

    // Act.
    focusRow("sub");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    await screen.findByText("inside.txt");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });

    // Assert.
    await waitFor(() => expect(select.mock.calls.at(-1)?.[0]?.path?.segments).toEqual(["sub", "inside.txt"]));
  });

  it("moves focus down and up through the rendered rows with the arrow keys", async () => {
    // Arrange.
    await renderPanel();
    focusRow("sub");

    // Act and assert, step by step.
    fireEvent.keyDown(tree(), { key: "ArrowDown" });
    expect(document.activeElement).toBe(row("a.txt"));

    fireEvent.keyDown(tree(), { key: "ArrowUp" });
    expect(document.activeElement).toBe(row("sub"));
  });

  it("expands a collapsed folder with ArrowRight, then steps into its first child", async () => {
    // Arrange.
    await renderPanel();
    focusRow("sub");

    // Act and assert, step by step.
    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    await screen.findByText("inside.txt");
    expect(document.activeElement).toBe(row("sub"));

    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    expect(document.activeElement).toBe(row("inside.txt"));
  });

  it("collapses an expanded folder with ArrowLeft, and steps out to the parent from a child", async () => {
    // Arrange.
    await renderPanel();
    focusRow("sub");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    await screen.findByText("inside.txt");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });

    // Act and assert, step by step.
    fireEvent.keyDown(tree(), { key: "ArrowLeft" });
    expect(document.activeElement).toBe(row("sub"));

    fireEvent.keyDown(tree(), { key: "ArrowLeft" });
    await waitFor(() => expect(screen.queryByText("inside.txt")).toBeNull());
  });

  it("activates a file with Enter, and toggles a folder as well as activating it", async () => {
    // Arrange.
    await renderPanel();

    // Act and assert, step by step.
    focusRow("a.txt");
    fireEvent.keyDown(tree(), { key: "Enter" });
    expect(select.mock.calls.at(-1)?.[0]?.detail).toMatchObject({ case: "action", value: ContextSelectionAction.ACTIVATE });

    focusRow("sub");
    fireEvent.keyDown(tree(), { key: "Enter" });
    await screen.findByText("inside.txt");
    expect(select.mock.calls.at(-1)?.[0]?.detail).toMatchObject({ case: "action", value: ContextSelectionAction.ACTIVATE });
    expect(select.mock.calls.at(-1)?.[0]?.path?.segments).toEqual(["sub"]);
  });

  it("clears the selection with Escape", async () => {
    // Arrange.
    await renderPanel();
    focusRow("a.txt");

    // Act.
    fireEvent.keyDown(tree(), { key: "Escape" });

    // Assert.
    expect(select).toHaveBeenLastCalledWith(null);
    expect(row("a.txt").className).not.toContain("explorer-tree-node-focused");
  });

  it("triggers the backend action a pressed key is bound to for the focused entry", async () => {
    // Arrange.
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

    // Act.
    focusRow("a.txt");
    fireEvent.keyDown(tree(), { key: "F2" });

    // Assert.
    await waitFor(() => expect(executeAction).toHaveBeenCalledWith("hierarchy.rename"));
  });

  it("ignores a shortcut while the pushed actions belong to a different entry than the focused one", async () => {
    // Arrange.
    pushed(2, ["sub"], renameGroups());
    await renderPanel();

    // Act.
    focusRow("a.txt");
    fireEvent.keyDown(tree(), { key: "F2" });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("does nothing for a key bound to an action the backend reported unavailable", async () => {
    // Arrange.
    pushed(1, ["a.txt"], [
      create(ContextActionGroupSchema, {
        actions: [{ id: "hierarchy.rename", label: "Rename…", icon: "", available: false, unavailableReason: "Locked.", shortcut: { key: "F2" } }],
      }),
    ]);
    await renderPanel();

    // Act.
    focusRow("a.txt");
    fireEvent.keyDown(tree(), { key: "F2" });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("does not react to a shortcut pressed outside the tree", async () => {
    // Arrange.
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

    // Act.
    focusRow("a.txt");
    fireEvent.keyDown(document.body, { key: "F2" });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("opens the menu at once from actions already held for the entry, on right-click and on Shift+F10", async () => {
    // Arrange.
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

    // Act and assert, step by step.
    fireEvent.contextMenu(row("a.txt"));
    await screen.findByRole("menu");
    expect(screen.getByRole("menuitem", { name: "Rename…" })).toBeTruthy();
    expect(row("a.txt").className).toContain("explorer-tree-node-focused");
    expect(select.mock.calls.at(-1)?.[0]?.detail).toMatchObject({ case: "action", value: ContextSelectionAction.CONTEXT_MENU });

    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    fireEvent.keyDown(tree(), { key: "F10", shiftKey: true });

    await screen.findByRole("menu");
    expect(screen.getByRole("menuitem", { name: "Rename…" })).toBeTruthy();
  });

  it("opens the menu when the CONTEXT_MENU push for the entry arrives, if its actions were not held yet", async () => {
    // Arrange.
    const { rerender } = await renderPanel();

    // Act and assert, step by step.
    fireEvent.contextMenu(row("a.txt"));
    expect(screen.queryByRole("menu")).toBeNull();
    expect(select.mock.calls.at(-1)?.[0]?.detail).toMatchObject({ case: "action", value: ContextSelectionAction.CONTEXT_MENU });

    pushed(1, ["a.txt"], renameGroups(), { case: "action", value: ContextSelectionAction.CONTEXT_MENU });
    rerender(<ExplorerTreePanel projectId={new Uint8Array(16)} />);

    await screen.findByRole("menu");
    expect(screen.getByRole("menuitem", { name: "Rename…" })).toBeTruthy();
  });

  it("runs the same action from the menu that its shortcut runs", async () => {
    // Arrange.
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

    // Act.
    fireEvent.contextMenu(row("a.txt"));
    fireEvent.click(await screen.findByRole("menuitem", { name: "Rename…" }));

    // Assert.
    await waitFor(() => expect(executeAction).toHaveBeenCalledWith("hierarchy.rename"));
  });
});

describe("ExplorerTreePanel collapse and expand triggers", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    watchHierarchy.mockReturnValue({ [Symbol.asyncIterator]: () => ({ next: () => new Promise(() => {}) }) });
    resetContext();
    listEntries.mockImplementation(({ folderId }: { folderId?: { value: Uint8Array } }) =>
      Promise.resolve(
        folderId
          ? { result: { case: "entries", value: { entries: [makeEntry(3, "inside.txt", EntryKind.FILE, 2)] } } }
          : {
              result: {
                case: "entries",
                value: { entries: [makeEntry(2, "sub", EntryKind.FOLDER, undefined, true), makeEntry(1, "a.txt", EntryKind.FILE)] },
              },
            },
      ),
    );
  });

  async function renderTree() {
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("sub");
  }

  const folderRow = () => screen.getByText("sub").closest(".explorer-tree-row") as HTMLElement;
  const folderLabel = () => screen.getByText("sub").closest("button") as HTMLElement;
  const folderChevron = () => folderRow().querySelector(".explorer-tree-chevron-button") as HTMLElement;
  const isExpanded = () => screen.getByText("sub").closest("li")?.getAttribute("aria-expanded") === "true";

  it("does not expand on a single click on the label", async () => {
    // Arrange.
    await renderTree();

    // Act.
    fireEvent.click(folderLabel());

    // Assert.
    await waitFor(() => expect(screen.getByText("sub")).toBeTruthy());
    expect(isExpanded()).toBe(false);
    expect(screen.queryByText("inside.txt")).toBeNull();
  });

  it("expands on a single click on the chevron widget", async () => {
    // Arrange.
    await renderTree();

    // Act.
    fireEvent.click(folderChevron(), { detail: 1 });

    // Assert.
    expect(await screen.findByText("inside.txt")).toBeTruthy();
    expect(isExpanded()).toBe(true);
  });

  it("collapses again on a second click on the chevron", async () => {
    // Arrange.
    await renderTree();
    fireEvent.click(folderChevron(), { detail: 1 });
    await screen.findByText("inside.txt");

    // Act.
    fireEvent.click(folderChevron(), { detail: 1 });

    // Assert.
    await waitFor(() => expect(isExpanded()).toBe(false));
  });

  it("expands on a double click on the label", async () => {
    // Arrange.
    await renderTree();

    // Act.
    fireEvent.doubleClick(folderLabel());

    // Assert.
    expect(await screen.findByText("inside.txt")).toBeTruthy();
    expect(isExpanded()).toBe(true);
  });

  it("toggles once for a real double click on the chevron, not twice", async () => {
    // Arrange.
    // A real double click is click(detail 1), click(detail 2), dblclick - fireEvent.doubleClick
    // alone sends only the last. Handled naively the two clicks would toggle twice and land
    // back collapsed.
    await renderTree();

    // Act.
    fireEvent.click(folderChevron(), { detail: 1 });
    fireEvent.click(folderChevron(), { detail: 2 });
    fireEvent.doubleClick(folderChevron(), { detail: 2 });

    // Assert.
    expect(await screen.findByText("inside.txt")).toBeTruthy();
    expect(isExpanded()).toBe(true);
  });

  it("keeps the chevron out of the tab order so the tree stays one Tab stop", async () => {
    // Act.
    // Arrow keys are the keyboard route to expand/collapse, so the chevron must not add a
    // second stop per row.
    await renderTree();

    // Assert.
    expect(folderChevron().tabIndex).toBe(-1);
    expect(screen.getAllByRole("button").filter((button) => button.tabIndex === 0)).toHaveLength(1);
  });

  it("renders no chevron for a folder without children", async () => {
    // Arrange.
    await renderTree();

    // Act and assert, step by step.
    const fileRow = screen.getByText("a.txt").closest(".explorer-tree-row") as HTMLElement;
    expect(fileRow.querySelector(".explorer-tree-chevron-button")).toBeNull();
    // The placeholder keeps its label aligned with the folder's.
    expect(fileRow.querySelector(".explorer-tree-chevron")).not.toBeNull();
  });
});

describe("ExplorerTreePanel empty space: the project root", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetContext();
    watchHierarchy.mockReturnValue({
      // eslint-disable-next-line @typescript-eslint/no-empty-function
      async *[Symbol.asyncIterator]() {},
    });
    listEntries.mockResolvedValue({
      result: {
        case: "entries",
        value: { entries: [makeEntry(2, "sub", EntryKind.FOLDER, undefined, true), makeEntry(1, "a.txt", EntryKind.FILE)] },
      },
    });
  });

  /** What the backend pushes while nothing is selected: the root's actions, no selection. */
  const rootGroups = () => [
    create(ContextActionGroupSchema, {
      actions: [
        { id: "hierarchy.add", label: "Add…", icon: "mdi-plus", available: true, shortcut: { key: "Insert" } },
        { id: "hierarchy.rename", label: "Rename…", icon: "mdi-pencil-outline", available: false, unavailableReason: "The project folder itself cannot be renamed or deleted here." },
      ],
    }),
  ];

  function pushedRoot() {
    contextState.selection = null;
    contextState.actions = rootGroups();
  }

  async function renderPanel() {
    const rendered = render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("sub");
    return rendered;
  }

  const tree = () => screen.getByRole("tree");
  const row = (name: string) => screen.getByText(name).closest("button") as HTMLButtonElement;

  it("right-clicking the empty space clears the selection and opens the menu from the root's actions", async () => {
    // Arrange.
    pushedRoot();
    await renderPanel();

    // Act.
    fireEvent.contextMenu(tree());

    // Assert.
    expect(select).toHaveBeenLastCalledWith(null);
    await screen.findByRole("menu");
    expect(screen.getByRole("menuitem", { name: "Add…" })).toBeTruthy();
    // Rendered straight from what was pushed: greyed, with the backend's reason, not omitted.
    const rename = screen.getByRole("menuitem", { name: "Rename…" });
    expect(rename.getAttribute("aria-disabled")).toBe("true");
  });

  it("right-clicking a row is unchanged: it still selects that entry, not the root", async () => {
    // Arrange.
    pushedRoot();
    await renderPanel();

    fireEvent.contextMenu(row("a.txt"));

    // Act and assert, step by step.
    const last = select.mock.calls.at(-1)?.[0];
    expect(last).not.toBeNull();
    expect(last?.detail).toMatchObject({ case: "action", value: ContextSelectionAction.CONTEXT_MENU });
  });

  it("opens the root menu once the cleared baseline with the root's actions arrives, if they were not held yet", async () => {
    // Arrange.
    // Something is selected when the user right-clicks the empty space: the root's actions
    // are not here yet, so the menu waits for the push that clearing causes.
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, id(1), ["a.txt"], NONE_DETAIL);
    contextState.actions = [];
    const { rerender } = await renderPanel();

    // Act and assert, step by step.
    fireEvent.contextMenu(tree());
    expect(select).toHaveBeenLastCalledWith(null);
    expect(screen.queryByRole("menu")).toBeNull();

    pushedRoot();
    rerender(<ExplorerTreePanel projectId={new Uint8Array(16)} />);

    await screen.findByRole("menu");
    expect(screen.getByRole("menuitem", { name: "Add…" })).toBeTruthy();
  });

  it("Insert with no row focused runs the root's Add, straight from the pushed shortcut", async () => {
    // Arrange.
    pushedRoot();
    await renderPanel();
    executeAction.mockResolvedValue({ accepted: true, error: "" });

    // Act.
    // Focus the tree itself, as a right-click on the empty space would have.
    act(() => tree().focus());
    fireEvent.keyDown(tree(), { key: "Insert" });

    // Assert.
    expect(executeAction).toHaveBeenCalledWith("hierarchy.add");
  });

  it("Insert with a row focused does not fall back to the root's actions", async () => {
    // Arrange.
    // The row's own actions are not held (nothing pushed for it), so nothing must run - the
    // root's Insert must not leak onto a focused row.
    pushedRoot();
    await renderPanel();

    // Act.
    act(() => row("a.txt").focus());
    fireEvent.keyDown(tree(), { key: "Insert" });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("Shift+F10 with no row focused opens the root menu", async () => {
    // Arrange.
    pushedRoot();
    await renderPanel();

    act(() => tree().focus());
    fireEvent.keyDown(tree(), { key: "F10", shiftKey: true });

    // Act and assert, step by step.
    await screen.findByRole("menu");
    expect(screen.getByRole("menuitem", { name: "Add…" })).toBeTruthy();
  });

  it("right-clicking the empty space while a row is focused moves focus off the row before opening the root menu", async () => {
    // Arrange.
    pushedRoot();
    await renderPanel();

    // Act and assert, step by step.
    act(() => row("a.txt").focus());
    expect(row("a.txt").className).toContain("explorer-tree-node-focused");

    fireEvent.contextMenu(tree());

    // The row is no longer the focused entry, so the held root actions apply again and
    // the menu opens from them (the menu then takes focus itself, as it always does).
    await screen.findByRole("menu");
    expect(row("a.txt").className).not.toContain("explorer-tree-node-focused");
    expect(screen.getByRole("menuitem", { name: "Add…" })).toBeTruthy();
  });
});

describe("resolveRevealPath", () => {
  function tree(): TreeState {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [
      makeEntry(2, "sub", EntryKind.FOLDER, undefined, true),
      makeEntry(1, "a.txt", EntryKind.FILE),
    ]);
    state = applyEntries(state, key(2), [makeEntry(3, "inside.adp", EntryKind.FILE, 2)]);
    return state;
  }

  it("finds an entry that is already listed", () => {
    // Arrange, act and assert.
    expect(resolveRevealPath(tree(), ["a.txt"])).toEqual({ leafKey: key(1) });
  });

  it("finds a nested entry whose folder has been listed", () => {
    // Arrange, act and assert.
    expect(resolveRevealPath(tree(), ["sub", "inside.adp"])).toEqual({ leafKey: key(3) });
  });

  it("names the folder to expand when its children were never listed", () => {
    // Act.
    const unlisted = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER, undefined, true)]);

    // Assert.
    expect(resolveRevealPath(unlisted, ["sub", "later.adp"])).toEqual({ expandKey: key(2) });
  });

  it("asks for nothing when a root-level entry has simply not arrived yet", () => {
    // Arrange, act and assert.
    expect(resolveRevealPath(tree(), ["not-there-yet.adp"])).toEqual({ expandKey: undefined });
  });
});

describe("ExplorerTreePanel revealing what was just created", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetContext();
    watchHierarchy.mockReturnValue({
      // eslint-disable-next-line @typescript-eslint/no-empty-function
      async *[Symbol.asyncIterator]() {},
    });
    listEntries.mockImplementation(({ folderId }: { folderId?: { value: Uint8Array } }) =>
      Promise.resolve(
        folderId
          ? { result: { case: "entries", value: { entries: [makeEntry(3, "inside.adp", EntryKind.FILE, 2)] } } }
          : {
              result: {
                case: "entries",
                value: { entries: [makeEntry(2, "sub", EntryKind.FOLDER, undefined, true), makeEntry(1, "a.txt", EntryKind.FILE)] },
              },
            },
      ),
    );
  });

  it("focuses a created entry that is already listed, and forgets the reveal", async () => {
    // Arrange and act.
    contextState.pendingReveal = ["a.txt"];
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("a.txt");

    // Assert.
    await waitFor(() => expect(document.activeElement).toBe(screen.getByText("a.txt").closest("button")));
    expect(clearReveal).toHaveBeenCalled();
  });

  it("activates the revealed entry - exactly once, with no duplicate plain select", async () => {
    // Arrange and act.
    // The activation is what opens a created diagram's tab through the same rule a
    // double-click uses (diagram-workspace-tabs Requirement 3); the focus that precedes it
    // must not also push a plain select for the same entry. The clearReveal mock mirrors
    // production here - it really forgets the reveal - or the effect would re-run it.
    contextState.pendingReveal = ["a.txt"];
    clearReveal.mockImplementation(() => {
      contextState.pendingReveal = null;
    });
    try {
      render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
      await screen.findByText("a.txt");

    // Assert.
      await waitFor(() => expect(select).toHaveBeenCalled());
      const forRevealedEntry = select.mock.calls.map(([selection]) => selection as ContextSelection);
      expect(forRevealedEntry).toHaveLength(1);
      expect(forRevealedEntry[0]!.detail).toEqual({ case: "action", value: ContextSelectionAction.ACTIVATE });
    } finally {
      clearReveal.mockReset();
    }
  });

  it("expands the folder it was created in, then focuses it there", async () => {
    // Arrange.
    contextState.pendingReveal = ["sub", "inside.adp"];
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("sub");

    // Act and assert, step by step.
    // The folder's children were never listed, so revealing has to expand it first.
    await screen.findByText("inside.adp");
    await waitFor(() => expect(document.activeElement).toBe(screen.getByText("inside.adp").closest("button")));
    expect(clearReveal).toHaveBeenCalled();
  });

  it("gives up on a reveal that never resolves, rather than holding on to it", async () => {
    // Arrange and act.
    vi.useFakeTimers();
    try {
      contextState.pendingReveal = ["never-arrives.adp"];
      render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(2500);
      });

    // Assert.
      expect(clearReveal).toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe("ExplorerTreePanel nested registrations (adp-file-nesting)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetContext();
    watchHierarchy.mockReturnValue({
      // eslint-disable-next-line @typescript-eslint/no-empty-function
      async *[Symbol.asyncIterator]() {},
    });
  });

  /** Root holds subject file "test.mm" (id 2, expandable); its children are two registrations. */
  function mockSubjectAndRegistrations() {
    listEntries.mockImplementation(({ folderId }: { folderId?: { value: Uint8Array } }) =>
      Promise.resolve(
        folderId
          ? {
              result: {
                case: "entries",
                value: {
                  entries: [
                    makeEntry(3, "test.adp", EntryKind.FILE, 2),
                    makeEntry(4, "test.first.adp", EntryKind.FILE, 2),
                  ],
                },
              },
            }
          : { result: { case: "entries", value: { entries: [makeEntry(2, "test.mm", EntryKind.FILE, undefined, true)] } } },
      ),
    );
  }

  it("walks visibleKeys through an expanded subject file's registrations", () => {
    // Arrange.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "test.mm", EntryKind.FILE, undefined, true)]);
    state = applyEntries(state, key(2), [makeEntry(3, "test.adp", EntryKind.FILE, 2)]);
    state = { ...state, nodesByKey: { ...state.nodesByKey, [key(2)]: { ...state.nodesByKey[key(2)]!, expanded: true } } };

    // Act and assert: the keyboard sees exactly what the eye sees, files included.
    expect(visibleKeys(state)).toEqual([key(2), key(3)]);
  });

  it("re-parents an entry on updated.parentId, keeping its key", () => {
    // Arrange: an orphan at the root, whose subject then appears.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [
      makeEntry(2, "test.mm", EntryKind.FILE, undefined, true),
      makeEntry(3, "test.adp", EntryKind.FILE),
    ]);
    state = applyEntries(state, key(2), []);

    // Act: the push Phase D sends - an update carrying the new parent, not a remove/create pair.
    state = applyHierarchyChange(state, create(HierarchyChangeSchema, {
      change: { case: "updated", value: { entryId: { value: id(3) }, hasChildren: false, parentId: { value: id(2) } } },
    }));

    // Assert: same key, new place.
    expect(state.rootKeys).toEqual([key(2)]);
    expect(state.nodesByKey[key(2)]?.childKeys).toEqual([key(3)]);
    expect(state.nodesByKey[key(3)]?.parentKey).toBe(key(2));
  });

  it("re-parents to the root when the pushed parent id is empty", () => {
    // Arrange: a nested registration whose subject vanishes.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "test.mm", EntryKind.FILE, undefined, true)]);
    state = applyEntries(state, key(2), [makeEntry(3, "test.adp", EntryKind.FILE, 2)]);

    // Act: present-but-empty means the root - unset would mean "unchanged".
    state = applyHierarchyChange(state, create(HierarchyChangeSchema, {
      change: { case: "updated", value: { entryId: { value: id(3) }, hasChildren: false, parentId: { value: new Uint8Array(0) } } },
    }));

    // Assert.
    expect(state.rootKeys).toContain(key(3));
    expect(state.nodesByKey[key(2)]?.childKeys).toEqual([]);
    expect(state.nodesByKey[key(3)]?.parentKey).toBeUndefined();
  });

  it("sorts a pushed registration under its subject with the unqualified form first", () => {
    // Arrange: the counter-example that breaks plain alphabetical - subject.aa.adp would sort
    // before subject.adp, and the unqualified default must stay first (Requirement 3.4).
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "subject.mm", EntryKind.FILE, undefined, true)]);
    state = applyEntries(state, key(2), [makeEntry(3, "subject.aa.adp", EntryKind.FILE, 2)]);

    // Act.
    state = applyHierarchyChange(state, create(HierarchyChangeSchema, {
      change: { case: "created", value: { entry: makeEntry(4, "subject.adp", EntryKind.FILE, 2) } },
    }));

    // Assert.
    expect(state.nodesByKey[key(2)]?.childKeys).toEqual([key(4), key(3)]);
  });

  it("renders an expandable file with aria-expanded, and loads its registrations on expand", async () => {
    // Arrange.
    mockSubjectAndRegistrations();
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("test.mm");
    const item = screen.getByText("test.mm").closest("li") as HTMLElement;
    expect(item.getAttribute("aria-expanded")).toBe("false");

    // Act: the chevron a folder would have, on a file.
    fireEvent.click(screen.getByLabelText("Expand test.mm"));

    // Assert: children load through the file's own id, exactly as a folder's would.
    await screen.findByText("test.adp");
    await screen.findByText("test.first.adp");
    expect(item.getAttribute("aria-expanded")).toBe("true");
    expect(listEntries).toHaveBeenCalledWith(expect.objectContaining({ folderId: { value: id(2) } }));
  });

  it("expands and collapses an expandable file with the keyboard", async () => {
    // Arrange.
    mockSubjectAndRegistrations();
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("test.mm");
    act(() => (screen.getByText("test.mm").closest("button") as HTMLButtonElement).focus());
    const tree = screen.getByRole("tree");

    // Act and assert: ArrowRight expands and loads...
    fireEvent.keyDown(tree, { key: "ArrowRight" });
    await screen.findByText("test.adp");

    // ...ArrowLeft collapses again.
    fireEvent.keyDown(tree, { key: "ArrowLeft" });
    expect(screen.queryByText("test.adp")).toBeNull();
  });

  it("builds a nested registration's path from the FILESYSTEM, not the tree", () => {
    // Arrange: root > folder > subject > registration in the tree; on disk the registration
    // sits in the FOLDER, beside its subject. The backend rejects a selection whose path
    // disagrees with the entry ("The path does not match the entry"), which is how every
    // nested diagram silently failed to open.
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(1, "architecture", EntryKind.FOLDER, undefined, true)]);
    state = applyEntries(state, key(1), [makeEntry(2, "test.mm", EntryKind.FILE, 1, true)]);
    state = applyEntries(state, key(2), [makeEntry(3, "test.adp", EntryKind.FILE, 2)]);

    // Act and assert.
    expect(pathOf(state, key(3))).toEqual(["architecture", "test.adp"]);
    expect(pathOf(state, key(2))).toEqual(["architecture", "test.mm"]);
  });

  it("selects a nested registration with its disk path when clicked directly", async () => {
    // Arrange.
    mockSubjectAndRegistrations();
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("test.mm");
    fireEvent.click(screen.getByLabelText("Expand test.mm"));
    await screen.findByText("test.adp");

    // Act.
    fireEvent.doubleClick(screen.getByText("test.adp"));

    // Assert: the subject's name is nowhere in the selection's path.
    await waitFor(() => {
      const sent = select.mock.calls.at(-1)?.[0];
      expect(sent?.path?.segments).toEqual(["test.adp"]);
    });
  });

  it("activates the default registration with its disk path, not the tree path", async () => {
    // Arrange.
    mockSubjectAndRegistrations();
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("test.mm");

    // Act.
    fireEvent.doubleClick(screen.getByText("test.mm"));

    // Assert.
    await waitFor(() => {
      const sent = select.mock.calls.at(-1)?.[0];
      expect(sent?.path?.segments).toEqual(["test.adp"]);
    });
  });

  it("activates the default registration when the subject is double-clicked, without expanding", async () => {
    // Arrange.
    mockSubjectAndRegistrations();
    render(<ExplorerTreePanel projectId={new Uint8Array(16)} />);
    await screen.findByText("test.mm");

    // Act: the activation gesture on the subject row (Requirements 7.1, 7.2).
    fireEvent.doubleClick(screen.getByText("test.mm"));

    // Assert: the selection goes to the FIRST registration in the stable order - the
    // unqualified default - fetched on demand rather than requiring an expand.
    await waitFor(() => {
      const source = select.mock.calls.at(-1)?.[0]?.id?.source;
      expect(source?.case).toBe("entryId");
      expect((source?.value as { value: Uint8Array }).value).toEqual(id(3));
    });
  });
});
