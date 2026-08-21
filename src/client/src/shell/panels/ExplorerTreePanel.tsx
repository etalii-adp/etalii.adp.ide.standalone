import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createClient } from "@connectrpc/connect";
import { base64Encode } from "@bufbuild/protobuf/wire";
import { useAuth } from "../../auth/AuthContext";
import { EntryKind, HierarchyService } from "../../generated/hierarchy_pb";
import type { Entry, HierarchyChange } from "../../generated/hierarchy_pb";

export interface TreeNode {
  id: Uint8Array;
  parentKey: string | undefined;
  name: string;
  kind: EntryKind;
  available: boolean;
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

    default:
      return state;
  }
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
        for await (const change of stream) {
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

  return (
    <ul className="explorer-tree" role="tree">
      {state.rootKeys.map((key) => (
        <ExplorerTreeNodeView key={key} nodeKey={key} state={state} depth={0} onToggle={toggleExpand} />
      ))}
    </ul>
  );
}

interface ExplorerTreeNodeViewProps {
  nodeKey: string;
  state: TreeState;
  depth: number;
  onToggle: (key: string, node: TreeNode) => void;
}

function ExplorerTreeNodeView({ nodeKey, state, depth, onToggle }: ExplorerTreeNodeViewProps) {
  const node = state.nodesByKey[nodeKey];
  if (!node) {
    return null;
  }

  const isFolder = node.kind === EntryKind.FOLDER;

  return (
    <li role="treeitem" aria-expanded={isFolder ? node.expanded : undefined}>
      <button
        type="button"
        className={`explorer-tree-node${node.available ? "" : " explorer-tree-node-unavailable"}`}
        style={{ paddingLeft: `${depth * 16 + 8}px` }}
        onClick={() => isFolder && onToggle(nodeKey, node)}
      >
        {isFolder && (
          <span
            className={`mdi explorer-tree-chevron ${node.expanded ? "mdi-chevron-down" : "mdi-chevron-right"}`}
            aria-hidden="true"
          />
        )}
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
              <ExplorerTreeNodeView key={childKey} nodeKey={childKey} state={state} depth={depth + 1} onToggle={onToggle} />
            ))}
          </ul>
        )
      )}
    </li>
  );
}
