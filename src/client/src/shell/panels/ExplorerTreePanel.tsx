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
import { ContextScope } from "../../generated/context_pb";
import type { ContextAction, ContextActionGroup, ContextPrompt } from "../../generated/context_pb";
import { ContextMenu, type ContextMenuGroup, type ContextMenuItem } from "../ContextMenu";
import { ContextPromptHost } from "../ContextPromptHost";

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

/** Folders before files, then alphabetical (Requirement 3.5) - applied client-side too, so a
 *  live-pushed create (Requirement 4.5) lands in the same position a fresh listing would give it. */
function sortKeys(nodesByKey: Record<string, TreeNode>, keys: string[]): string[] {
  return [...keys].sort((a, b) => {
    const nodeA = nodesByKey[a];
    const nodeB = nodesByKey[b];
    if (!nodeA || !nodeB) {
      return 0;
    }
    const aIsFolder = nodeA.kind === EntryKind.FOLDER;
    const bIsFolder = nodeB.kind === EntryKind.FOLDER;
    if (aIsFolder !== bIsFolder) {
      return aIsFolder ? -1 : 1;
    }
    return nodeA.name.localeCompare(nodeB.name);
  });
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
      nodesByKey[parentKey] = { ...parent, childKeys: sortKeys(nodesByKey, [...parent.childKeys, key]) };
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

      return { ...state, nodesByKey: { ...state.nodesByKey, [key]: { ...node, hasChildren: change.change.value.hasChildren } } };
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
    if (node.kind === EntryKind.FOLDER && node.expanded) {
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

/** Maps backend-reported actions onto the menu's own vocabulary; the menu learns nothing about files. */
export function toMenuGroups(groups: ContextActionGroup[], onSelect: (action: ContextAction) => void): ContextMenuGroup[] {
  return groups.map((group) =>
    group.actions.map((action): ContextMenuItem => {
      const base = {
        id: action.id,
        label: action.label,
        icon: action.icon,
        disabled: !action.available,
        disabledReason: action.unavailableReason,
      };
      return action.items.length > 0
        ? { ...base, items: toMenuGroups(action.items, onSelect) }
        : { ...base, onSelect: () => onSelect(action) };
    }),
  );
}

const FILE_ICONS_BY_EXTENSION: Record<string, string> = {
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
  return node.kind === EntryKind.FOLDER ? folderIcon(node) : fileIcon(node.name);
}

export interface ExplorerTreePanelProps {
  projectId: Uint8Array;
}

export function ExplorerTreePanel({ projectId }: ExplorerTreePanelProps) {
  const { transport } = useAuth();
  const hierarchyClient = useMemo(() => createClient(HierarchyService, transport), [transport]);
  const watchIdRef = useRef<Uint8Array>(crypto.getRandomValues(new Uint8Array(16)));

  const [state, setState] = useState<TreeState>(EMPTY_TREE_STATE);
  const [error, setError] = useState<string | null>(null);
  const [focusedKey, setFocusedKey] = useState<string | undefined>();
  const [actionsByKey, setActionsByKey] = useState<Record<string, ContextActionGroup[]>>({});
  const [menu, setMenu] = useState<{ groups: ContextActionGroup[]; position: { x: number; y: number } } | null>(null);
  const [prompt, setPrompt] = useState<ContextPrompt | null>(null);
  const nodeRefs = useRef(new Map<string, HTMLButtonElement>());

  const fetchChildren = useCallback(
    async (parentKey: string | undefined, folderId: Uint8Array | undefined) => {
      const response = await hierarchyClient.listEntries({
        projectId: { value: projectId },
        folderId: folderId ? { value: folderId } : undefined,
        watchId: { value: watchIdRef.current },
      });

      const result = response.result;
      if (result.case === "error") {
        setError(result.value.message);
        return;
      }

      const entries = result.value?.entries ?? [];
      setState((previous) => applyEntries(previous, parentKey, entries));
    },
    [hierarchyClient, projectId],
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
          { projectId: { value: projectId }, watchId: { value: watchIdRef.current } },
          { signal: abortController.signal },
        );
        // The stream now carries two kinds of message: hierarchy deltas, and prompts the
        // backend raises for an action running on this very connection.
        for await (const message of stream) {
          if (message.message.case === "prompt") {
            setPrompt(message.message.value);
            continue;
          }
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

  const focusNode = useCallback((key: string | undefined) => {
    if (key === undefined) {
      return;
    }
    setFocusedKey(key);
    nodeRefs.current.get(key)?.focus();
  }, []);

  /**
   * The focused entry's actions, fetched once and kept on hand. The same cache answers the
   * context menu and every keypress, so an unrelated key never costs a round trip and the
   * client never needs to know which key means what.
   */
  const actionsFor = useCallback(
    async (key: string, node: TreeNode): Promise<ContextActionGroup[]> => {
      const cached = actionsByKey[key];
      if (cached) {
        return cached;
      }

      const response = await hierarchyClient.discoverActions({
        projectId: { value: projectId },
        watchId: { value: watchIdRef.current },
        scope: ContextScope.HIERARCHY,
        source: { source: { case: "entryId", value: { value: node.id } } },
      });

      setActionsByKey((previous) => ({ ...previous, [key]: response.groups }));
      return response.groups;
    },
    [actionsByKey, hierarchyClient, projectId],
  );

  const runAction = useCallback(
    async (node: TreeNode, trigger: { case: "actionId"; value: string } | { case: "shortcut"; value: ShortcutEventLike }) => {
      const response = await hierarchyClient.executeAction({
        projectId: { value: projectId },
        watchId: { value: watchIdRef.current },
        scope: ContextScope.HIERARCHY,
        source: { source: { case: "entryId", value: { value: node.id } } },
        interactionId: { value: crypto.getRandomValues(new Uint8Array(16)) },
        trigger:
          trigger.case === "actionId"
            ? { case: "actionId", value: trigger.value }
            : {
                case: "shortcut",
                value: {
                  key: trigger.value.key,
                  ctrl: trigger.value.ctrlKey,
                  shift: trigger.value.shiftKey,
                  alt: trigger.value.altKey,
                  meta: trigger.value.metaKey,
                },
              },
      });

      if (!response.accepted && response.error) {
        setError(response.error);
      }
    },
    [hierarchyClient, projectId],
  );

  const openMenuFor = useCallback(
    async (key: string, node: TreeNode, position: { x: number; y: number }) => {
      const groups = await actionsFor(key, node);
      // An entry that has gone away reports no actions at all; ContextMenu renders nothing
      // for empty groups, so no hollow menu appears.
      setMenu({ groups, position });
    },
    [actionsFor],
  );

  /** The keyboard equivalent of a right-click: anchored to the focused row, not a pointer. */
  const openMenuAtRow = useCallback(
    (key: string, node: TreeNode) => {
      const rect = nodeRefs.current.get(key)?.getBoundingClientRect();
      void openMenuFor(key, node, { x: rect?.left ?? 0, y: rect?.bottom ?? 0 }).catch(() => {
        // A failed discovery just means no menu opens; the next attempt tries again.
      });
    },
    [openMenuFor],
  );

  const handleContextMenu = useCallback(
    (event: ReactMouseEvent, key: string, node: TreeNode) => {
      event.preventDefault();
      focusNode(key);
      void openMenuFor(key, node, { x: event.clientX, y: event.clientY }).catch(() => {
        // Discovery failing is not worth replacing the tree with an error; the menu simply
        // does not open, and the next attempt tries again.
      });
    },
    [focusNode, openMenuFor],
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
          if (!node || node.kind !== EntryKind.FOLDER) {
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
          if (node.kind === EntryKind.FOLDER && node.expanded) {
            toggleExpand(key!, node);
            return;
          }
          focusNode(node.parentKey);
          return;
        }

        case "ContextMenu": {
          if (!node || !key) {
            return;
          }
          event.preventDefault();
          openMenuAtRow(key, node);
          return;
        }

        case "F10": {
          if (!event.shiftKey || !node || !key) {
            return;
          }
          event.preventDefault();
          openMenuAtRow(key, node);
          return;
        }

        default:
          break;
      }

      if (!node || !key) {
        return;
      }

      // Anything else is only a shortcut if the backend said so for this very entry - the
      // client holds no key-to-action mapping of its own, here or anywhere.
      const match = matchShortcut(actionsByKey[key] ?? [], event);
      if (!match) {
        return;
      }

      event.preventDefault();
      void runAction(node, { case: "actionId", value: match.id });
    },
    [actionsByKey, focusedKey, focusNode, openMenuAtRow, runAction, state, toggleExpand],
  );

  // Discovering on focus is what lets a keypress be answered from the cache; it also means
  // the menu usually has its items ready by the time it is asked for.
  useEffect(() => {
    const node = focusedKey ? state.nodesByKey[focusedKey] : undefined;
    if (!focusedKey || !node || actionsByKey[focusedKey]) {
      return;
    }
    void actionsFor(focusedKey, node).catch(() => {
      // Leaving the entry without cached actions is the safe outcome: its shortcuts simply
      // do not fire, rather than firing against a guess.
    });
  }, [actionsByKey, actionsFor, focusedKey, state]);

  const promptInteractionId = prompt?.interactionId?.value;

  const handlePropose = useCallback(
    async (revision: number, value: string) => {
      const response = await hierarchyClient.proposeInput({
        interactionId: promptInteractionId ? { value: promptInteractionId } : undefined,
        revision,
        value,
      });
      return { revision: response.revision, valid: response.valid, reason: response.reason };
    },
    [hierarchyClient, promptInteractionId],
  );

  const handleSubmitPrompt = useCallback(
    async (value: string) => {
      const response = await hierarchyClient.submitInteraction({
        interactionId: promptInteractionId ? { value: promptInteractionId } : undefined,
        value,
      });
      if (response.completed) {
        setPrompt(null);
      }
      return { completed: response.completed, error: response.error };
    },
    [hierarchyClient, promptInteractionId],
  );

  const handleCancelPrompt = useCallback(() => {
    setPrompt(null);
    void hierarchyClient
      .cancelInteraction({ interactionId: promptInteractionId ? { value: promptInteractionId } : undefined })
      .catch(() => {
        // The dialog is already gone client-side; the interaction also dies with the
        // connection, so a failed cancel leaves nothing stranded that matters.
      });
  }, [hierarchyClient, promptInteractionId]);

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
      <ul className="explorer-tree" role="tree" onKeyDown={handleKeyDown}>
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
            onFocusNode={setFocusedKey}
            onContextMenu={handleContextMenu}
          />
        ))}
      </ul>
      <ContextMenu
        open={menu !== null}
        groups={menu ? toMenuGroups(menu.groups, (action) => {
          const node = focusedKey ? state.nodesByKey[focusedKey] : undefined;
          if (node) {
            void runAction(node, { case: "actionId", value: action.id });
          }
        }) : []}
        position={menu?.position ?? { x: 0, y: 0 }}
        onClose={() => setMenu(null)}
      />
      <ContextPromptHost
        prompt={prompt}
        onPropose={handlePropose}
        onSubmit={handleSubmitPrompt}
        onCancel={handleCancelPrompt}
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
  onFocusNode: (key: string) => void;
  onContextMenu: (event: ReactMouseEvent, key: string, node: TreeNode) => void;
}

function ExplorerTreeNodeView({
  nodeKey,
  state,
  depth,
  focusedKey,
  tabbableKey,
  nodeRefs,
  onToggle,
  onFocusNode,
  onContextMenu,
}: ExplorerTreeNodeViewProps) {
  const node = state.nodesByKey[nodeKey];
  if (!node) {
    return null;
  }

  const isFolder = node.kind === EntryKind.FOLDER;
  const isExpandable = isFolder && node.hasChildren;
  const isFocused = nodeKey === focusedKey;

  const childProps = { state, depth: depth + 1, focusedKey, tabbableKey, nodeRefs, onToggle, onFocusNode, onContextMenu };

  return (
    <li role="treeitem" aria-expanded={isFolder ? node.expanded : undefined} aria-selected={isFocused || undefined}>
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
        style={{ paddingLeft: `${depth * 16 + 8}px` }}
        tabIndex={nodeKey === tabbableKey ? 0 : -1}
        onFocus={() => onFocusNode(nodeKey)}
        onClick={() => isExpandable && onToggle(nodeKey, node)}
        onContextMenu={(event) => onContextMenu(event, nodeKey, node)}
      >
        <span
          className={`mdi explorer-tree-chevron ${isExpandable ? (node.expanded ? "mdi-chevron-down" : "mdi-chevron-right") : ""}`}
          aria-hidden="true"
        />
        <span className={`mdi ${iconFor(node)}`} aria-hidden="true" />
        <span className="explorer-tree-node-name">{node.name}</span>
      </button>
      {isFolder && node.expanded && (
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
