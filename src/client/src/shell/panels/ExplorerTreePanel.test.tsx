import { describe, expect, it } from "vitest";
import { create } from "@bufbuild/protobuf";
import { base64Encode } from "@bufbuild/protobuf/wire";
import {
  EntryKind,
  EntrySchema,
  HierarchyChangeSchema,
  type Entry,
} from "../../generated/hierarchy_pb";
import { EMPTY_TREE_STATE, applyEntries, applyHierarchyChange, type TreeState } from "./ExplorerTreePanel";

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
