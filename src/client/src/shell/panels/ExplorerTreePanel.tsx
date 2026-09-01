import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
} from "react";
import { createClient } from "@connectrpc/connect";
import { base64Encode } from "@bufbuild/protobuf/wire";
import { useAuth } from "../../auth/AuthContext";
import { EntryKind, HierarchyService } from "../../generated/hierarchy_pb";
import type { Entry, HierarchyChange } from "../../generated/hierarchy_pb";
import { ContextSelectionAction, ContextSelectionSource } from "../../generated/context_pb";
import type { ContextAction, ContextActionGroup, ContextSelection } from "../../generated/context_pb";
import { ContextMenu } from "../context/ContextMenu";
import { toMenuGroups } from "../context/toMenuGroups";
import {
  NONE_DETAIL,
  innermostAction,
  innermostKey,
  selectionFor,
  useContextConnection,
  useContextSelection,
} from "../context/ContextConnectionProvider";

export interface TreeNode {
  id: Uint8Array;
  parentKey: string | undefined;
  name: string;
  kind: EntryKind;
  available: boolean;
  /** For a folder: whether it currently has any entry inside it; always false for a file. */
  hasChildren: boolean;
  /** undefined = never fetched (Requirement 2.2's expand-on-demand). */
  childKeys?: string[];
  expanded: boolean;
  loading: boolean;
}

export interface TreeState {
  rootKeys: string[];
  rootFetched: boolean;
  nodesByKey: Record<string, TreeNode>;
}

export const EMPTY_TREE_STATE: TreeState = { rootKeys: [], rootFetched: false, nodesByKey: {} };

function keyOf(id: Uint8Array | undefined): string | undefined {
  return id ? base64Encode(id) : undefined;
}

/** Whether a registration file name carries a qualifier (`subject.qualifier.adp`). */
function isQualifiedRegistrationName(name: string): boolean {
  if (!name.toLowerCase().endsWith(".adp")) {
    return false;
  }
  const base = name.slice(0, -".adp".length);
  return base.lastIndexOf(".") > 0;
}

/** Folders before files, then alphabetical (Requirement 3.5) - applied client-side too, so a
 *  live-pushed create (Requirement 4.5) lands in the same position a fresh listing would give it.
 *  Under a FILE parent the children are diagram registrations, whose stated order is the
 *  unqualified form first and then the qualifiers (adp-file-nesting Requirement 3.4) - plain
 *  alphabetical would put `subject.aa.adp` before `subject.adp`, which is why the parent's kind
 *  matters here (Requirement 9.5's review, with the intended result stated). */
function sortKeys(nodesByKey: Record<string, TreeNode>, keys: string[], parentIsFile = false): string[] {
  return [...keys].sort((a, b) => {
    const nodeA = nodesByKey[a];
    const nodeB = nodesByKey[b];
    if (!nodeA || !nodeB) {
      return 0;
    }
    if (parentIsFile) {
      const aQualified = isQualifiedRegistrationName(nodeA.name);
      const bQualified = isQualifiedRegistrationName(nodeB.name);
      if (aQualified !== bQualified) {
        return aQualified ? 1 : -1;
      }
      return nodeA.name.localeCompare(nodeB.name);
    }
    const aIsFolder = nodeA.kind === EntryKind.FOLDER;
    const bIsFolder = nodeB.kind === EntryKind.FOLDER;
    if (aIsFolder !== bIsFolder) {
      return aIsFolder ? -1 : 1;
    }
    return nodeA.name.localeCompare(nodeB.name);
  });
}

/** A node the tree can expand: a folder with entries, or a subject file with registrations
 *  nested under it (adp-file-nesting Requirement 3.1). */
function isExpandableNode(node: TreeNode): boolean {
  return node.hasChildren && (node.kind === EntryKind.FOLDER || node.kind === EntryKind.FILE);
}

function entryToNode(entry: Entry, previous?: TreeNode): TreeNode {
  return {
    id: entry.id?.value ?? new Uint8Array(0),
    parentKey: keyOf(entry.parentId?.value),
    name: entry.name,
    kind: entry.kind,
    available: entry.available,
    hasChildren: entry.hasChildren,
    childKeys: previous?.childKeys,
    expanded: previous?.expanded ?? false,
    loading: previous?.loading ?? false,
  };
}

/** Merges a ListEntries response into state. Exported for unit testing (Requirement 3.10-adjacent). */
export function applyEntries(state: TreeState, parentKey: string | undefined, entries: Entry[]): TreeState {
  const nodesByKey = { ...state.nodesByKey };
  const keys: string[] = [];
  for (const entry of entries) {
    const key = keyOf(entry.id?.value);
    if (!key) {
      continue;
    }
    keys.push(key);
    nodesByKey[key] = entryToNode(entry, nodesByKey[key]);
  }

  if (parentKey === undefined) {
    return { rootKeys: keys, rootFetched: true, nodesByKey };
  }

  const parent = nodesByKey[parentKey];
  if (!parent) {
    return { ...state, nodesByKey };
  }

  nodesByKey[parentKey] = { ...parent, childKeys: keys };
  return { ...state, nodesByKey };
}

/** Applies one pushed HierarchyChange by id (Requirement 3.10). Exported for unit testing, no gRPC involved. */
export function applyHierarchyChange(state: TreeState, change: HierarchyChange): TreeState {
  switch (change.change.case) {
    case "created": {
      const entry = change.change.value.entry;
      const key = keyOf(entry?.id?.value);
      if (!entry || !key) {
        return state;
      }

      const parentKey = keyOf(entry.parentId?.value);

      if (parentKey === undefined) {
        const nodesByKey = { ...state.nodesByKey, [key]: entryToNode(entry) };
        if (state.rootKeys.includes(key)) {
          return { ...state, nodesByKey };
        }
        return { ...state, rootKeys: sortKeys(nodesByKey, [...state.rootKeys, key]), nodesByKey };
      }

      const parent = state.nodesByKey[parentKey];
      if (!parent || parent.childKeys === undefined) {
        // This connection has never fetched this folder's children - nothing
        // rendered there yet to insert into (client-side mirror of Requirement 4.7).
        // Discarded entirely, same as the server does - not even tracked as an
        // orphan node, so a later expand of this folder starts from a clean slate.
        return state;
      }

      if (parent.childKeys.includes(key)) {
        return state;
      }

      const nodesByKey = { ...state.nodesByKey, [key]: entryToNode(entry) };
      nodesByKey[parentKey] = { ...parent, childKeys: sortKeys(nodesByKey, [...parent.childKeys, key], parent.kind === EntryKind.FILE) };
      return { ...state, nodesByKey };
    }

    case "removed": {
      const key = keyOf(change.change.value.entryId?.value);
      const target = key ? state.nodesByKey[key] : undefined;
      if (!key || !target) {
        return state;
      }

      const nodesByKey = { ...state.nodesByKey };
      const removeSubtree = (nodeKey: string) => {
        const node = nodesByKey[nodeKey];
        if (!node) {
          return;
        }
        (node.childKeys ?? []).forEach(removeSubtree);
        delete nodesByKey[nodeKey];
      };
      removeSubtree(key);

      if (target.parentKey === undefined) {
        return { ...state, rootKeys: state.rootKeys.filter((k) => k !== key), nodesByKey };
      }

      const parent = nodesByKey[target.parentKey];
      if (parent?.childKeys) {
        nodesByKey[target.parentKey] = { ...parent, childKeys: parent.childKeys.filter((k) => k !== key) };
      }

      return { ...state, nodesByKey };
    }

    case "renamed": {
      const key = keyOf(change.change.value.entryId?.value);
      const node = key ? state.nodesByKey[key] : undefined;
      if (!key || !node) {
        return state;
      }

      return { ...state, nodesByKey: { ...state.nodesByKey, [key]: { ...node, name: change.change.value.newName } } };
    }

    case "updated": {
      const key = keyOf(change.change.value.entryId?.value);
      const node = key ? state.nodesByKey[key] : undefined;
      if (!key || !node) {
        return state;
      }

      // A present parent_id is a re-parent - an orphan whose subject appeared, or a subject
      // vanishing under its registrations. Present-but-empty means the root (the proto's
      // convention, since unset means "unchanged"). The entry keeps its id, so selection and
      // focus survive the move (adp-file-nesting Requirement 10.3).
      const parentId = change.change.value.parentId;
      if (parentId === undefined) {
        return { ...state, nodesByKey: { ...state.nodesByKey, [key]: { ...node, hasChildren: change.change.value.hasChildren } } };
      }

      const newParentKey = keyOf(parentId.value) || undefined;
      const nodesByKey = { ...state.nodesByKey, [key]: { ...node, hasChildren: change.change.value.hasChildren, parentKey: newParentKey } };
      let rootKeys = state.rootKeys;

      if (node.parentKey === undefined) {
        rootKeys = rootKeys.filter((k) => k !== key);
      } else {
        const oldParent = nodesByKey[node.parentKey];
        if (oldParent?.childKeys) {
          nodesByKey[node.parentKey] = { ...oldParent, childKeys: oldParent.childKeys.filter((k) => k !== key) };
        }
      }

      if (newParentKey === undefined) {
        rootKeys = sortKeys(nodesByKey, [...rootKeys, key]);
      } else {
        const newParent = nodesByKey[newParentKey];
        if (newParent && newParent.childKeys !== undefined) {
          nodesByKey[newParentKey] = { ...newParent, childKeys: sortKeys(nodesByKey, [...newParent.childKeys, key], newParent.kind === EntryKind.FILE) };
        }
      }

      return { ...state, rootKeys, nodesByKey };
    }

    default:
      return state;
  }
}

/**
 * Every node currently rendered, top to bottom — a collapsed folder's children are not in
 * it. This is the order arrow-up/down move through, so what the keyboard walks is exactly
 * what the eye sees. Exported for unit testing.
 */
export function visibleKeys(state: TreeState): string[] {
  const keys: string[] = [];

  const walk = (nodeKey: string) => {
    const node = state.nodesByKey[nodeKey];
    if (!node) {
      return;
    }
    keys.push(nodeKey);
    if (node.expanded) {
      (node.childKeys ?? []).forEach(walk);
    }
  };

  state.rootKeys.forEach(walk);
  return keys;
}

/** The node arrow-down (`1`) or arrow-up (`-1`) moves to, or undefined at either end. */
export function neighbourKey(state: TreeState, currentKey: string | undefined, direction: 1 | -1): string | undefined {
  const keys = visibleKeys(state);
  if (keys.length === 0) {
    return undefined;
  }
  if (currentKey === undefined) {
    return direction === 1 ? keys[0] : keys[keys.length - 1];
  }

  const index = keys.indexOf(currentKey);
  if (index < 0) {
    return keys[0];
  }

  return keys[index + direction];
}

/** The entry the tree focuses when focus arrives from outside: where it was, else the top. */
export function entryFocusKey(state: TreeState, previousKey: string | undefined): string | undefined {
  const keys = visibleKeys(state);
  return previousKey !== undefined && keys.includes(previousKey) ? previousKey : keys[0];
}

export interface ShortcutEventLike {
  key: string;
  ctrlKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
  metaKey: boolean;
}

function flattenActions(groups: ContextActionGroup[]): ContextAction[] {
  return groups.flatMap((group) => group.actions.flatMap((action) => [action, ...flattenActions(action.items)]));
}

/**
 * The action bound to this keypress among the ones the backend reported for the focused
 * entry — nothing else. There is deliberately no client-side table of which key means what,
 * so this spec's own bindings and any a later one adds are backend data all the way down.
 * An action reported unavailable is skipped, which makes its shortcut inert too.
 */
export function matchShortcut(groups: ContextActionGroup[], event: ShortcutEventLike): ContextAction | undefined {
  return flattenActions(groups).find((action) => {
    const shortcut = action.shortcut;
    if (!action.available || !shortcut) {
      return false;
    }
    return (
      shortcut.key.toLowerCase() === event.key.toLowerCase() &&
      shortcut.ctrl === event.ctrlKey &&
      shortcut.shift === event.shiftKey &&
      shortcut.alt === event.altKey &&
      shortcut.meta === event.metaKey
    );
  });
}

const FILE_ICONS_BY_EXTENSION: Record<string, string> = {
  adp: "mdi-graph-outline", // ADP's own diagram files
  ts: "mdi-language-typescript",
  tsx: "mdi-language-typescript",
  js: "mdi-language-javascript",
  jsx: "mdi-language-javascript",
  json: "mdi-code-json",
  cs: "mdi-language-csharp",
  csproj: "mdi-xml",
  sln: "mdi-microsoft-visual-studio",
  slnx: "mdi-microsoft-visual-studio",
  proto: "mdi-file-cog-outline",
  md: "mdi-language-markdown-outline",
  css: "mdi-language-css3",
  html: "mdi-language-html5",
  png: "mdi-file-image-outline",
  jpg: "mdi-file-image-outline",
  jpeg: "mdi-file-image-outline",
  gif: "mdi-file-image-outline",
  svg: "mdi-file-image-outline",
  yml: "mdi-file-cog-outline",
  yaml: "mdi-file-cog-outline",
  gitignore: "mdi-git",
};

function fileIcon(name: string): string {
  const dotIndex = name.lastIndexOf(".");
  const extension = dotIndex >= 0 ? name.slice(dotIndex + 1).toLowerCase() : "";
  return FILE_ICONS_BY_EXTENSION[extension] ?? "mdi-file-outline";
}

function folderIcon(node: TreeNode): string {
  if (!node.available) {
    return "mdi-folder-alert-outline";
  }
  return node.expanded ? "mdi-folder-open-outline" : "mdi-folder-outline";
}

function iconFor(node: TreeNode): string {
  // Reviewed for adp-file-nesting Requirement 9.4: a nested registration reads as a diagram
  // (`mdi-graph-outline`, by extension) beside its subject's own file icon - the two are
  // already told apart, so kind-plus-extension stays the whole rule.
  return node.kind === EntryKind.FOLDER ? folderIcon(node) : fileIcon(node.name);
}

export interface ExplorerTreePanelProps {
  projectId: Uint8Array;
}

/** How long to keep trying to reveal something before giving up on it. */
const REVEAL_TIMEOUT_MS = 2000;

/**
 * Walks a project-relative path through the tree by name, as far as the tree currently
 * reaches: either the entry itself, or the deepest folder along the way whose children this
 * connection has not listed yet - which is the one to expand to get any further.
 */
export function resolveRevealPath(
  state: TreeState,
  segments: string[],
): { leafKey?: string; expandKey?: string } {
  let keys = state.rootKeys;
  let parentKey: string | undefined;

  for (let index = 0; index < segments.length; index++) {
    const key = keys.find((candidate) => state.nodesByKey[candidate]?.name === segments[index]);
    if (key === undefined) {
      // Not there yet: either its create is still on its way, or this folder was never listed.
      return { expandKey: parentKey };
    }
    if (index === segments.length - 1) {
      return { leafKey: key };
    }

    const node = state.nodesByKey[key];
    if (node?.childKeys === undefined) {
      return { expandKey: key };
    }

    keys = node.childKeys;
    parentKey = key;
  }

  return {};
}

/** The entry's project-relative path, read off the tree the ids came from. */
export function pathOf(state: TreeState, key: string): string[] {
  // FILESYSTEM segments, not tree parentage. Since adp-file-nesting the two differ: a nested
  // registration's tree parent is its subject FILE, but on disk both live in the same folder -
  // so a FILE ancestor contributes nothing to the path. The backend validates these segments
  // against the entry's real path and rejects a mismatch ("The path does not match the entry"),
  // which is exactly what silently stopped every nested diagram from opening.
  const segments: string[] = [];
  let cursor: string | undefined = key;
  let isLeaf = true;
  while (cursor !== undefined) {
    const node: TreeNode | undefined = state.nodesByKey[cursor];
    if (!node) {
      break;
    }
    if (isLeaf || node.kind === EntryKind.FOLDER) {
      segments.unshift(node.name);
    }
    isLeaf = false;
    cursor = node.parentKey;
  }
  return segments;
}

export function ExplorerTreePanel({ projectId }: ExplorerTreePanelProps) {
  const { transport } = useAuth();
  const hierarchyClient = useMemo(() => createClient(HierarchyService, transport), [transport]);
  const { watchId, select, executeAction, clearReveal } = useContextConnection();
  const { selection, actions, pendingReveal } = useContextSelection();

  const [state, setState] = useState<TreeState>(EMPTY_TREE_STATE);
  const [error, setError] = useState<string | null>(null);
  const [focusedKey, setFocusedKey] = useState<string | undefined>();
  const [menuPosition, setMenuPosition] = useState<{ x: number; y: number } | null>(null);
  const pendingMenuRef = useRef<{ key: string; position: { x: number; y: number } } | null>(null);
  // A gesture selects its entry itself; the focus it also causes must not follow up with a plain one.
  const gestureKeyRef = useRef<string | undefined>(undefined);
  const nodeRefs = useRef(new Map<string, HTMLButtonElement>());

  // The pushed actions belong to whatever the backend currently has selected. They only
  // answer for the focused row once the two agree, so a stale list never fires.
  const selectionKey = innermostKey(selection);
  const focusedActions = focusedKey !== undefined && selectionKey === focusedKey ? actions : [];
  // With nothing selected the backend pushes what applies to the project root - the
  // explorer's empty space. Those answer only while no row is focused, for the same reason.
  const rootActions = focusedKey === undefined && selection === null ? actions : [];
  const menuActions = focusedKey === undefined ? rootActions : focusedActions;
  const treeRef = useRef<HTMLUListElement>(null);
  const pendingRootMenuRef = useRef<{ x: number; y: number } | null>(null);

  const fetchChildren = useCallback(
    async (parentKey: string | undefined, folderId: Uint8Array | undefined) => {
      const response = await hierarchyClient.listEntries({
        projectId: { value: projectId },
        folderId: folderId ? { value: folderId } : undefined,
        watchId: { value: watchId },
      });

      const result = response.result;
      if (result.case === "error") {
        setError(result.value.message);
        return;
      }

      const entries = result.value?.entries ?? [];
      setState((previous) => applyEntries(previous, parentKey, entries));
      return entries;
    },
    [hierarchyClient, projectId, watchId],
  );

  useEffect(() => {
    fetchChildren(undefined, undefined).catch((err: unknown) => {
      setError(err instanceof Error ? err.message : "Failed to load the project's file hierarchy.");
    });
    // Intentionally runs once per mounted panel (one watch_id per project session, Requirement 3.11).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const abortController = new AbortController();

    void (async () => {
      try {
        const stream = hierarchyClient.watchHierarchy(
          { projectId: { value: projectId }, watchId: { value: watchId } },
          { signal: abortController.signal },
        );
        for await (const message of stream) {
          if (message.message.case !== "change") {
            continue;
          }

          const change = message.message.value;
          if (change.change.case === "rootUnavailable") {
            setError(change.change.value.message);
            continue;
          }
          setState((previous) => applyHierarchyChange(previous, change));
        }
      } catch (err) {
        if (!abortController.signal.aborted) {
          setError(err instanceof Error ? err.message : "Lost connection to the file hierarchy.");
        }
      }
    })();

    return () => abortController.abort();
    // Intentionally runs once per mounted panel (one change-feed stream per project session, Requirement 3.11).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const toggleExpand = useCallback(
    (key: string, node: TreeNode) => {
      if (!node.expanded && node.childKeys === undefined) {
        setState((previous) => {
          const current = previous.nodesByKey[key];
          return current
            ? { ...previous, nodesByKey: { ...previous.nodesByKey, [key]: { ...current, expanded: true, loading: true } } }
            : previous;
        });
        fetchChildren(key, node.id)
          .catch((err: unknown) => {
            setError(err instanceof Error ? err.message : "Failed to load this folder's contents.");
          })
          .finally(() => {
            setState((previous) => {
              const current = previous.nodesByKey[key];
              return current ? { ...previous, nodesByKey: { ...previous.nodesByKey, [key]: { ...current, loading: false } } } : previous;
            });
          });
        return;
      }

      // Read the toggled-from state off `previous`, not the closed-over `node`, so
      // two rapid clicks (before React re-renders between them) don't both compute
      // their new `expanded` value from the same stale snapshot.
      setState((previous) => {
        const current = previous.nodesByKey[key];
        return current
          ? { ...previous, nodesByKey: { ...previous.nodesByKey, [key]: { ...current, expanded: !current.expanded } } }
          : previous;
      });
    },
    [fetchChildren],
  );

  /** Reports an entry to the context service, with the gesture that selected it. */
  const selectNode = useCallback(
    (key: string, detail: ContextSelection["detail"]) => {
      const node = state.nodesByKey[key];
      if (!node) {
        return;
      }
      if (detail.case === "action") {
        gestureKeyRef.current = key;
      }
      select(selectionFor(ContextSelectionSource.EXPLORER, node.id, pathOf(state, key), detail));
    },
    [select, state],
  );

  const focusNode = useCallback((key: string | undefined) => {
    if (key === undefined) {
      return;
    }
    setFocusedKey(key);
    nodeRefs.current.get(key)?.focus();
  }, []);

  // Focus is the explorer's; selection is the context service's. Every focus change is
  // reported as a plain selection, which the connection coalesces for a held arrow key.
  useEffect(() => {
    if (gestureKeyRef.current === focusedKey) {
      gestureKeyRef.current = undefined;
      return;
    }
    if (focusedKey !== undefined && state.nodesByKey[focusedKey]) {
      selectNode(focusedKey, NONE_DETAIL);
    }
    // The path is read at the moment of focusing; a later rename reaches the backend on its own.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusedKey]);

  // A focused entry the tree no longer has (removed by a pushed change) is no selection either.
  useEffect(() => {
    if (focusedKey !== undefined && state.rootFetched && !state.nodesByKey[focusedKey]) {
      setFocusedKey(undefined);
      select(null);
    }
  }, [focusedKey, select, state]);

  // Something was just created on this connection. It arrives like any other entry, through
  // the watcher's own change - so this waits for it to appear rather than inserting it, then
  // focuses it, which is what reports it as the selection. A folder that was never listed is
  // expanded on the way, since nothing would ever arrive for it otherwise.
  useEffect(() => {
    if (!pendingReveal) {
      return;
    }

    const { leafKey, expandKey } = resolveRevealPath(state, pendingReveal);
    if (leafKey !== undefined) {
      focusNode(leafKey);
      // Activated, not merely selected: a created diagram opens its view through exactly the
      // rule a double-click uses, and the gestureKeyRef suppresses the duplicate plain select
      // the focus above would otherwise push (diagram-workspace-tabs Requirement 3). For an
      // entry that is no diagram, an activation is just a selection.
      selectNode(leafKey, { case: "action", value: ContextSelectionAction.ACTIVATE });
      clearReveal();
      return;
    }

    if (expandKey !== undefined) {
      const node = state.nodesByKey[expandKey];
      if (node && !node.expanded) {
        toggleExpand(expandKey, node);
      }
    }
  }, [clearReveal, focusNode, pendingReveal, selectNode, state, toggleExpand]);

  // Nothing arrived in time - the entry may be somewhere this connection cannot see. Giving
  // up keeps a stale reveal from sitting there and grabbing focus much later.
  useEffect(() => {
    if (!pendingReveal) {
      return;
    }

    const timer = setTimeout(clearReveal, REVEAL_TIMEOUT_MS);
    return () => clearTimeout(timer);
  }, [clearReveal, pendingReveal]);

  const activate = useCallback(
    (key: string, node: TreeNode) => {
      if (node.kind === EntryKind.FOLDER && node.hasChildren) {
        toggleExpand(key, node);
      }

      // A subject file's activation opens its DEFAULT registration - the first in the stable
      // order, which the unqualified form leads - without the user expanding anything
      // (adp-file-nesting Requirements 7.1 and 7.2). The children may not be loaded yet, so
      // they are fetched first when needed; a file with no registrations behaves exactly as
      // it always has (Requirement 7.4).
      if (node.kind === EntryKind.FILE && node.hasChildren) {
        if (node.childKeys !== undefined) {
          const first = node.childKeys[0];
          if (first) {
            selectNode(first, { case: "action", value: ContextSelectionAction.ACTIVATE });
          }
          return;
        }

        // Children not loaded yet: fetch, then select from the returned entry itself rather
        // than from React state, which has not re-rendered by the time this continuation runs.
        const parentPath = pathOf(state, key);
        fetchChildren(key, node.id)
          .then((entries) => {
            const first = entries?.[0];
            const firstId = first?.id?.value;
            if (!first || !firstId) {
              return;
            }
            const childKey = keyOf(firstId);
            if (childKey) {
              gestureKeyRef.current = childKey;
            }
            select(selectionFor(
              ContextSelectionSource.EXPLORER,
              firstId,
              [...parentPath.slice(0, -1), first.name],
              { case: "action", value: ContextSelectionAction.ACTIVATE },
            ));
          })
          .catch((err: unknown) => {
            setError(err instanceof Error ? err.message : "Failed to open this file's diagram.");
          });
        return;
      }

      selectNode(key, { case: "action", value: ContextSelectionAction.ACTIVATE });
    },
    [fetchChildren, select, selectNode, state, toggleExpand],
  );

  const runAction = useCallback(
    async (actionId: string) => {
      const outcome = await executeAction(actionId);
      if (!outcome.accepted && outcome.error) {
        setError(outcome.error);
      }
    },
    [executeAction],
  );

  /**
   * Right-click selects with CONTEXT_MENU, which makes the backend push this entry's
   * actions. If they are already here (the row was focused first, as a click does), the
   * menu opens at once; otherwise it opens when that push arrives.
   */
  const openMenuFor = useCallback(
    (key: string, position: { x: number; y: number }) => {
      focusNode(key);
      selectNode(key, { case: "action", value: ContextSelectionAction.CONTEXT_MENU });
      if (selectionKey === key && actions.length > 0) {
        pendingMenuRef.current = null;
        setMenuPosition(position);
      } else {
        pendingMenuRef.current = { key, position };
      }
    },
    [actions.length, focusNode, selectNode, selectionKey],
  );

  useEffect(() => {
    const pending = pendingMenuRef.current;
    if (pending && selectionKey === pending.key && innermostAction(selection) === ContextSelectionAction.CONTEXT_MENU) {
      pendingMenuRef.current = null;
      setMenuPosition(pending.position);
    }
  }, [selection, selectionKey]);

  /** The keyboard equivalent of a right-click: anchored to the focused row, not a pointer. */
  const openMenuAtRow = useCallback(
    (key: string) => {
      const rect = nodeRefs.current.get(key)?.getBoundingClientRect();
      openMenuFor(key, { x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
    },
    [openMenuFor],
  );

  /**
   * A right-click on the tree's empty space means the project root. Clearing the selection
   * is what makes the backend push the root's actions; as with a row, the menu opens at once
   * if they are already here and otherwise when that push arrives. Focus moves to the tree
   * itself so a shortcut pressed next - Insert - still reaches the handler.
   */
  const openRootMenu = useCallback(
    (position: { x: number; y: number }) => {
      setFocusedKey(undefined);
      treeRef.current?.focus();
      select(null);
      if (selection === null && actions.length > 0) {
        pendingRootMenuRef.current = null;
        setMenuPosition(position);
      } else {
        pendingRootMenuRef.current = position;
      }
    },
    [actions.length, select, selection],
  );

  useEffect(() => {
    const pending = pendingRootMenuRef.current;
    if (pending && selection === null && focusedKey === undefined && actions.length > 0) {
      pendingRootMenuRef.current = null;
      setMenuPosition(pending);
    }
  }, [actions.length, focusedKey, selection]);

  /** Where a keyboard-opened root menu goes: the tree's top-left, there being no row to anchor to. */
  const rootMenuPosition = useCallback((): { x: number; y: number } => {
    const rect = treeRef.current?.getBoundingClientRect();
    return { x: rect?.left ?? 0, y: rect?.top ?? 0 };
  }, []);

  const handleTreeContextMenu = useCallback(
    (event: ReactMouseEvent<HTMLUListElement>) => {
      // A row's own right-click bubbles up here too; only the tree's bare surface is the root.
      if (event.target !== event.currentTarget) {
        return;
      }
      event.preventDefault();
      openRootMenu({ x: event.clientX, y: event.clientY });
    },
    [openRootMenu],
  );

  const handleContextMenu = useCallback(
    (event: ReactMouseEvent, key: string) => {
      event.preventDefault();
      openMenuFor(key, { x: event.clientX, y: event.clientY });
    },
    [openMenuFor],
  );

  const handleKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLUListElement>) => {
      const key = focusedKey;
      const node = key ? state.nodesByKey[key] : undefined;

      switch (event.key) {
        case "ArrowDown":
        case "ArrowUp": {
          event.preventDefault();
          focusNode(neighbourKey(state, key, event.key === "ArrowDown" ? 1 : -1));
          return;
        }

        case "ArrowRight": {
          if (!node || !isExpandableNode(node)) {
            return;
          }
          event.preventDefault();
          if (!node.expanded) {
            if (node.hasChildren) {
              toggleExpand(key!, node);
            }
            return;
          }
          focusNode((node.childKeys ?? [])[0]);
          return;
        }

        case "ArrowLeft": {
          if (!node) {
            return;
          }
          event.preventDefault();
          if (node.expanded) {
            toggleExpand(key!, node);
            return;
          }
          focusNode(node.parentKey);
          return;
        }

        case "Enter": {
          if (!node || !key) {
            return;
          }
          event.preventDefault();
          activate(key, node);
          return;
        }

        case "Escape": {
          if (menuPosition !== null) {
            return; // the menu's own Escape handling closes it
          }
          event.preventDefault();
          setFocusedKey(undefined);
          select(null);
          return;
        }

        case "ContextMenu": {
          event.preventDefault();
          if (node && key) {
            openMenuAtRow(key);
          } else {
            openRootMenu(rootMenuPosition());
          }
          return;
        }

        case "F10": {
          if (!event.shiftKey) {
            return;
          }
          event.preventDefault();
          if (node && key) {
            openMenuAtRow(key);
          } else {
            openRootMenu(rootMenuPosition());
          }
          return;
        }

        default:
          break;
      }

      // Anything else is only a shortcut if the backend said so for this very entry - or,
      // with no row focused, for the project root. The client holds no key-to-action
      // mapping of its own, here or anywhere.
      const match = matchShortcut(menuActions, event);
      if (!match) {
        return;
      }

      event.preventDefault();
      void runAction(match.id);
    },
    [activate, focusedKey, focusNode, menuActions, menuPosition, openMenuAtRow, openRootMenu, rootMenuPosition, runAction, select, state, toggleExpand],
  );

  if (error) {
    return (
      <div className="explorer-tree-error" role="alert">
        {error}
      </div>
    );
  }

  if (!state.rootFetched) {
    return <p className="explorer-tree-loading">Loading…</p>;
  }

  // Roving tabindex: exactly one row is tabbable, so Tab enters the tree once and the
  // arrows take over from there rather than the user tabbing through every node.
  const tabbableKey = entryFocusKey(state, focusedKey);

  return (
    <>
      <ul
        ref={treeRef}
        className="explorer-tree"
        role="tree"
        // Focusable only by script: after a right-click on the empty space, so Insert reaches us.
        tabIndex={-1}
        onKeyDown={handleKeyDown}
        onContextMenu={handleTreeContextMenu}
      >
        {state.rootKeys.map((key) => (
          <ExplorerTreeNodeView
            key={key}
            nodeKey={key}
            state={state}
            depth={0}
            focusedKey={focusedKey}
            tabbableKey={tabbableKey}
            nodeRefs={nodeRefs.current}
            onToggle={toggleExpand}
            onActivate={activate}
            onFocusNode={setFocusedKey}
            onContextMenu={handleContextMenu}
          />
        ))}
      </ul>
      <ContextMenu
        open={menuPosition !== null}
        groups={menuPosition ? toMenuGroups(menuActions, (action) => void runAction(action.id)) : []}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={() => setMenuPosition(null)}
      />
    </>
  );
}

interface ExplorerTreeNodeViewProps {
  nodeKey: string;
  state: TreeState;
  depth: number;
  focusedKey: string | undefined;
  tabbableKey: string | undefined;
  nodeRefs: Map<string, HTMLButtonElement>;
  onToggle: (key: string, node: TreeNode) => void;
  onActivate: (key: string, node: TreeNode) => void;
  onFocusNode: (key: string) => void;
  onContextMenu: (event: ReactMouseEvent, key: string) => void;
}

function ExplorerTreeNodeView({
  nodeKey,
  state,
  depth,
  focusedKey,
  tabbableKey,
  nodeRefs,
  onToggle,
  onActivate,
  onFocusNode,
  onContextMenu,
}: ExplorerTreeNodeViewProps) {
  const node = state.nodesByKey[nodeKey];
  if (!node) {
    return null;
  }

  const isFolder = node.kind === EntryKind.FOLDER;
  const isExpandable = isExpandableNode(node);
  const isFocused = nodeKey === focusedKey;

  const childProps = { state, depth: depth + 1, focusedKey, tabbableKey, nodeRefs, onToggle, onActivate, onFocusNode, onContextMenu };

  return (
    <li role="treeitem" aria-expanded={isFolder || isExpandable ? node.expanded : undefined} aria-selected={isFocused || undefined}>
      {/* The chevron is its own control, so it sits beside the row button rather than inside
          it - a button cannot legally nest in another. The double click that activates is
          bound here, on their shared parent, so it covers the chevron and the label alike. */}
      <div
        className="explorer-tree-row"
        style={{ paddingLeft: `${depth * 16 + 8}px` }}
        onDoubleClick={() => onActivate(nodeKey, node)}
      >
        {isExpandable ? (
          <button
            type="button"
            className="explorer-tree-chevron-button"
            aria-label={`${node.expanded ? "Collapse" : "Expand"} ${node.name}`}
            // Out of the tab order on purpose: the tree is a single Tab stop and the arrow
            // keys are the keyboard way to expand and collapse.
            tabIndex={-1}
            onClick={(event) => {
              // Without this the click reaches the row's double-click handler as well.
              event.stopPropagation();

              // detail counts the clicks in this burst, so this skips the second click of a
              // double click: otherwise double-clicking the chevron would toggle twice and
              // land back where it started, while the label toggles once.
              if (event.detail > 1) {
                return;
              }

              onToggle(nodeKey, node);
            }}
            onDoubleClick={(event) => event.stopPropagation()}
          >
            <span
              className={`mdi explorer-tree-chevron ${node.expanded ? "mdi-chevron-down" : "mdi-chevron-right"}`}
              aria-hidden="true"
            />
          </button>
        ) : (
          // Holds the chevron's width so labels stay aligned whether or not a row has one.
          <span className="mdi explorer-tree-chevron" aria-hidden="true" />
        )}
        <button
          type="button"
          ref={(element) => {
            if (element) {
              nodeRefs.set(nodeKey, element);
            } else {
              nodeRefs.delete(nodeKey);
            }
          }}
          // A class rather than :focus-visible alone: a right-click focuses the row too, and
          // the user needs to see which entry the menu they just opened belongs to.
          className={`explorer-tree-node${node.available ? "" : " explorer-tree-node-unavailable"}${isFocused ? " explorer-tree-node-focused" : ""}`}
          tabIndex={nodeKey === tabbableKey ? 0 : -1}
          onFocus={() => onFocusNode(nodeKey)}
          onContextMenu={(event) => onContextMenu(event, nodeKey)}
        >
          <span className={`mdi ${iconFor(node)}`} aria-hidden="true" />
          <span className="explorer-tree-node-name">{node.name}</span>
        </button>
      </div>
      {(isFolder || isExpandable) && node.expanded && (
        node.loading ? (
          <p className="explorer-tree-loading-children" style={{ paddingLeft: `${(depth + 1) * 16 + 8}px` }}>
            Loading…
          </p>
        ) : (
          <ul role="group">
            {(node.childKeys ?? []).map((childKey) => (
              <ExplorerTreeNodeView key={childKey} nodeKey={childKey} {...childProps} />
            ))}
          </ul>
        )
      )}
    </li>
  );
}
