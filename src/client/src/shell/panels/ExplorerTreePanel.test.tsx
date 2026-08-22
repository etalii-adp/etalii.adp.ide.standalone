import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { base64Encode } from "@bufbuild/protobuf/wire";
import {
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
  visibleKeys,
  type ShortcutEventLike,
  type TreeState,
} from "./ExplorerTreePanel";

const listEntries = vi.fn();
const watchHierarchy = vi.fn();
const select = vi.fn<(selection: ContextSelection | null) => void>();
const executeAction = vi.fn();
const executeShortcut = vi.fn();

/** What the (mocked) context connection currently holds; tests set it to simulate a push. */
const contextState: { selection: ContextSelection | null; actions: ContextActionGroup[] } = { selection: null, actions: [] };

function resetContext() {
  contextState.selection = null;
  contextState.actions = [];
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
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, executeAction, executeShortcut }),
    useContextSelection: () => ({ ...contextState, levels: [], preview: null, connected: true }),
  };
});

function id(byte: number): Uint8Array {
  return new Uint8Array(16).fill(byte);
}

function key(byte: number): string {
  return base64Encode(id(byte));
}

function makeEntry(idByte: number, name: string, kind: EntryKind, parentByte?: number, hasChildren = false): Entry {
  return create(EntrySchema, {
    id: { value: id(idByte) },
    parentId: parentByte === undefined ? undefined : { value: id(parentByte) },
    name,
    kind,
    available: true,
    hasChildren,
  });
}

describe("applyEntries", () => {
  it("populates root state from a ListEntries response for the root folder", () => {
    const entries = [makeEntry(1, "a.txt", EntryKind.FILE), makeEntry(2, "sub", EntryKind.FOLDER)];

    const state = applyEntries(EMPTY_TREE_STATE, undefined, entries);

    expect(state.rootFetched).toBe(true);
    expect(state.rootKeys).toEqual([key(1), key(2)]);
    expect(state.nodesByKey[key(1)]?.name).toBe("a.txt");
    expect(state.nodesByKey[key(2)]?.name).toBe("sub");
  });

  it("attaches a folder's children under that folder's key, not the root", () => {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);

    state = applyEntries(state, key(2), [makeEntry(3, "inside.txt", EntryKind.FILE, 2)]);

    expect(state.rootKeys).toEqual([key(2)]);
    expect(state.nodesByKey[key(2)]?.childKeys).toEqual([key(3)]);
    expect(state.nodesByKey[key(3)]?.name).toBe("inside.txt");
  });

  it("preserves a folder's expanded state when it is re-listed", () => {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);
    state = {
      ...state,
      nodesByKey: { ...state.nodesByKey, [key(2)]: { ...state.nodesByKey[key(2)]!, expanded: true } },
    };

    state = applyEntries(state, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);

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
    const state = stateWithFolderAndListedChild();
    const change = create(HierarchyChangeSchema, {
      change: { case: "created", value: { entry: makeEntry(3, "new.txt", EntryKind.FILE, 2) } },
    });

    const next = applyHierarchyChange(state, change);

    expect(next.nodesByKey[key(2)]?.childKeys).toEqual([key(3)]);
    expect(next.nodesByKey[key(3)]?.name).toBe("new.txt");
  });

  it("created: does nothing when the parent's children were never listed on this connection", () => {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);
    // Note: unlike stateWithFolderAndListedChild, "sub"'s own children are never listed here.
    const change = create(HierarchyChangeSchema, {
      change: { case: "created", value: { entry: makeEntry(3, "new.txt", EntryKind.FILE, 2) } },
    });

    const next = applyHierarchyChange(state, change);

    expect(next.nodesByKey[key(2)]?.childKeys).toBeUndefined();
    expect(next).toEqual(state);
  });

  it("created: appends a new root-level entry", () => {
    const state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(1, "a.txt", EntryKind.FILE)]);
    const change = create(HierarchyChangeSchema, {
      change: { case: "created", value: { entry: makeEntry(4, "b.txt", EntryKind.FILE) } },
    });

    const next = applyHierarchyChange(state, change);

    expect(next.rootKeys).toEqual([key(1), key(4)]);
  });

  it("removed: removes the node and drops it from its parent's childKeys", () => {
    const state = stateWithFolderAndListedChild();
    const withChild = applyEntries(state, key(2), [makeEntry(3, "child.txt", EntryKind.FILE, 2)]);
    const change = create(HierarchyChangeSchema, { change: { case: "removed", value: { entryId: { value: id(3) } } } });

    const next = applyHierarchyChange(withChild, change);

    expect(next.nodesByKey[key(3)]).toBeUndefined();
    expect(next.nodesByKey[key(2)]?.childKeys).toEqual([]);
  });

  it("removed: removes an entire subtree, not just the direct node", () => {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER)]);
    state = applyEntries(state, key(2), [makeEntry(3, "nested", EntryKind.FOLDER, 2)]);
    state = applyEntries(state, key(3), [makeEntry(4, "leaf.txt", EntryKind.FILE, 3)]);
    const change = create(HierarchyChangeSchema, { change: { case: "removed", value: { entryId: { value: id(2) } } } });

    const next = applyHierarchyChange(state, change);

    expect(next.nodesByKey[key(2)]).toBeUndefined();
    expect(next.nodesByKey[key(3)]).toBeUndefined();
    expect(next.nodesByKey[key(4)]).toBeUndefined();
    expect(next.rootKeys).toEqual([]);
  });

  it("renamed: updates the node's displayed name, preserving its id and children", () => {
    const state = stateWithFolderAndListedChild();
    const change = create(HierarchyChangeSchema, {
      change: { case: "renamed", value: { entryId: { value: id(2) }, newName: "renamed-sub" } },
    });

    const next = applyHierarchyChange(state, change);

    expect(next.nodesByKey[key(2)]?.name).toBe("renamed-sub");
    expect(next.nodesByKey[key(2)]?.childKeys).toEqual([]);
  });

  it("updated: refreshes a folder's hasChildren flag", () => {
    let state = applyEntries(EMPTY_TREE_STATE, undefined, [makeEntry(2, "sub", EntryKind.FOLDER, undefined, false)]);
    const change = create(HierarchyChangeSchema, {
      change: { case: "updated", value: { entryId: { value: id(2) }, hasChildren: true } },
    });

    state = applyHierarchyChange(state, change);

    expect(state.nodesByKey[key(2)]?.hasChildren).toBe(true);
  });

  it("an id it doesn't know about is a no-op", () => {
    const state = stateWithFolderAndListedChild();
    const change = create(HierarchyChangeSchema, { change: { case: "removed", value: { entryId: { value: id(99) } } } });

    const next = applyHierarchyChange(state, change);

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
    expect(visibleKeys(nestedState())).toEqual([key(2), key(4), key(3), key(1)]);
  });

  it("includes a newly-expanded folder's children from then on", () => {
    expect(visibleKeys(expand(nestedState(), key(4)))).toEqual([key(2), key(4), key(5), key(3), key(1)]);
  });

  describe("neighbourKey", () => {
    it("moves down and up through the rendered order", () => {
      const state = nestedState();

      expect(neighbourKey(state, key(2), 1)).toBe(key(4));
      expect(neighbourKey(state, key(3), -1)).toBe(key(4));
    });

    it("stops rather than wrapping at either end", () => {
      const state = nestedState();

      expect(neighbourKey(state, key(1), 1)).toBeUndefined();
      expect(neighbourKey(state, key(2), -1)).toBeUndefined();
    });

    it("starts at the first entry when nothing is focused yet", () => {
      expect(neighbourKey(nestedState(), undefined, 1)).toBe(key(2));
    });
  });

  describe("entryFocusKey", () => {
    it("returns to the previously-focused entry when it is still rendered", () => {
      expect(entryFocusKey(nestedState(), key(3))).toBe(key(3));
    });

    it("falls back to the first entry when the previous one is gone", () => {
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
    expect(matchShortcut(groups(), press("F2"))?.id).toBe("hierarchy.rename");
  });

  it("ignores a key nothing is bound to", () => {
    expect(matchShortcut(groups(), press("F4"))).toBeUndefined();
  });

  it("requires the modifiers to match exactly", () => {
    expect(matchShortcut(groups(), press("F2", { shiftKey: true }))).toBeUndefined();
    expect(matchShortcut(groups({ shift: true }), press("F2", { shiftKey: true }))?.id).toBe("hierarchy.rename");
  });

  it("ignores an action the backend reported unavailable, so its shortcut is inert too", () => {
    expect(matchShortcut(groups({ available: false }), press("F2"))).toBeUndefined();
  });

  it("finds nothing at all when no actions were reported for the entry", () => {
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
    await renderPanel();

    const tabbable = screen.getAllByRole("button").filter((button) => button.tabIndex === 0);

    expect(tabbable).toHaveLength(1);
  });

  it("marks the focused row with a class distinct from hover", async () => {
    await renderPanel();

    focusRow("a.txt");

    expect(row("a.txt").className).toContain("explorer-tree-node-focused");
    expect(row("sub").className).not.toContain("explorer-tree-node-focused");
  });

  it("reports a focused entry as a plain selection carrying its id and project-relative path", async () => {
    await renderPanel();

    focusRow("a.txt");

    await waitFor(() => expect(select).toHaveBeenCalled());
    const sent = select.mock.calls.at(-1)?.[0];
    expect(sent?.source).toBe(ContextSelectionSource.EXPLORER);
    expect(sent?.path?.segments).toEqual(["a.txt"]);
    expect(sent?.id?.source).toMatchObject({ case: "entryId", value: { value: id(1) } });
    expect(sent?.detail.case).toBe("none");
  });

  it("reports a nested entry with the path built from its parents", async () => {
    await renderPanel();

    focusRow("sub");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    await screen.findByText("inside.txt");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });

    await waitFor(() => expect(select.mock.calls.at(-1)?.[0]?.path?.segments).toEqual(["sub", "inside.txt"]));
  });

  it("moves focus down and up through the rendered rows with the arrow keys", async () => {
    await renderPanel();
    focusRow("sub");

    fireEvent.keyDown(tree(), { key: "ArrowDown" });
    expect(document.activeElement).toBe(row("a.txt"));

    fireEvent.keyDown(tree(), { key: "ArrowUp" });
    expect(document.activeElement).toBe(row("sub"));
  });

  it("expands a collapsed folder with ArrowRight, then steps into its first child", async () => {
    await renderPanel();
    focusRow("sub");

    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    await screen.findByText("inside.txt");
    expect(document.activeElement).toBe(row("sub"));

    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    expect(document.activeElement).toBe(row("inside.txt"));
  });

  it("collapses an expanded folder with ArrowLeft, and steps out to the parent from a child", async () => {
    await renderPanel();
    focusRow("sub");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    await screen.findByText("inside.txt");
    fireEvent.keyDown(tree(), { key: "ArrowRight" });

    fireEvent.keyDown(tree(), { key: "ArrowLeft" });
    expect(document.activeElement).toBe(row("sub"));

    fireEvent.keyDown(tree(), { key: "ArrowLeft" });
    await waitFor(() => expect(screen.queryByText("inside.txt")).toBeNull());
  });

  it("activates a file with Enter, and toggles a folder as well as activating it", async () => {
    await renderPanel();

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
    await renderPanel();
    focusRow("a.txt");

    fireEvent.keyDown(tree(), { key: "Escape" });

    expect(select).toHaveBeenLastCalledWith(null);
    expect(row("a.txt").className).not.toContain("explorer-tree-node-focused");
  });

  it("triggers the backend action a pressed key is bound to for the focused entry", async () => {
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

    focusRow("a.txt");
    fireEvent.keyDown(tree(), { key: "F2" });

    await waitFor(() => expect(executeAction).toHaveBeenCalledWith("hierarchy.rename"));
  });

  it("ignores a shortcut while the pushed actions belong to a different entry than the focused one", async () => {
    pushed(2, ["sub"], renameGroups());
    await renderPanel();

    focusRow("a.txt");
    fireEvent.keyDown(tree(), { key: "F2" });

    expect(executeAction).not.toHaveBeenCalled();
  });

  it("does nothing for a key bound to an action the backend reported unavailable", async () => {
    pushed(1, ["a.txt"], [
      create(ContextActionGroupSchema, {
        actions: [{ id: "hierarchy.rename", label: "Rename…", icon: "", available: false, unavailableReason: "Locked.", shortcut: { key: "F2" } }],
      }),
    ]);
    await renderPanel();

    focusRow("a.txt");
    fireEvent.keyDown(tree(), { key: "F2" });

    expect(executeAction).not.toHaveBeenCalled();
  });

  it("does not react to a shortcut pressed outside the tree", async () => {
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

    focusRow("a.txt");
    fireEvent.keyDown(document.body, { key: "F2" });

    expect(executeAction).not.toHaveBeenCalled();
  });

  it("opens the menu at once from actions already held for the entry, on right-click and on Shift+F10", async () => {
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

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
    const { rerender } = await renderPanel();

    fireEvent.contextMenu(row("a.txt"));
    expect(screen.queryByRole("menu")).toBeNull();
    expect(select.mock.calls.at(-1)?.[0]?.detail).toMatchObject({ case: "action", value: ContextSelectionAction.CONTEXT_MENU });

    pushed(1, ["a.txt"], renameGroups(), { case: "action", value: ContextSelectionAction.CONTEXT_MENU });
    rerender(<ExplorerTreePanel projectId={new Uint8Array(16)} />);

    await screen.findByRole("menu");
    expect(screen.getByRole("menuitem", { name: "Rename…" })).toBeTruthy();
  });

  it("runs the same action from the menu that its shortcut runs", async () => {
    pushed(1, ["a.txt"], renameGroups());
    await renderPanel();

    fireEvent.contextMenu(row("a.txt"));
    fireEvent.click(await screen.findByRole("menuitem", { name: "Rename…" }));

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
    await renderTree();

    fireEvent.click(folderLabel());

    await waitFor(() => expect(screen.getByText("sub")).toBeTruthy());
    expect(isExpanded()).toBe(false);
    expect(screen.queryByText("inside.txt")).toBeNull();
  });

  it("expands on a single click on the chevron widget", async () => {
    await renderTree();

    fireEvent.click(folderChevron(), { detail: 1 });

    expect(await screen.findByText("inside.txt")).toBeTruthy();
    expect(isExpanded()).toBe(true);
  });

  it("collapses again on a second click on the chevron", async () => {
    await renderTree();
    fireEvent.click(folderChevron(), { detail: 1 });
    await screen.findByText("inside.txt");

    fireEvent.click(folderChevron(), { detail: 1 });

    await waitFor(() => expect(isExpanded()).toBe(false));
  });

  it("expands on a double click on the label", async () => {
    await renderTree();

    fireEvent.doubleClick(folderLabel());

    expect(await screen.findByText("inside.txt")).toBeTruthy();
    expect(isExpanded()).toBe(true);
  });

  it("toggles once for a real double click on the chevron, not twice", async () => {
    // A real double click is click(detail 1), click(detail 2), dblclick - fireEvent.doubleClick
    // alone sends only the last. Handled naively the two clicks would toggle twice and land
    // back collapsed.
    await renderTree();

    fireEvent.click(folderChevron(), { detail: 1 });
    fireEvent.click(folderChevron(), { detail: 2 });
    fireEvent.doubleClick(folderChevron(), { detail: 2 });

    expect(await screen.findByText("inside.txt")).toBeTruthy();
    expect(isExpanded()).toBe(true);
  });

  it("keeps the chevron out of the tab order so the tree stays one Tab stop", async () => {
    // Arrow keys are the keyboard route to expand/collapse, so the chevron must not add a
    // second stop per row.
    await renderTree();

    expect(folderChevron().tabIndex).toBe(-1);
    expect(screen.getAllByRole("button").filter((button) => button.tabIndex === 0)).toHaveLength(1);
  });

  it("renders no chevron for a folder without children", async () => {
    await renderTree();

    const fileRow = screen.getByText("a.txt").closest(".explorer-tree-row") as HTMLElement;
    expect(fileRow.querySelector(".explorer-tree-chevron-button")).toBeNull();
    // The placeholder keeps its label aligned with the folder's.
    expect(fileRow.querySelector(".explorer-tree-chevron")).not.toBeNull();
  });
});
