import { useEffect, useRef, useState } from "react";
import { isTabDirty } from "./dirtyTabs";
import { onTextTabRequested } from "./textTabRequests";
import { base64Encode } from "@bufbuild/protobuf/wire";
import { ContextSelectionAction } from "../../generated/context_pb";
import type { ContextLevelDetail } from "../../generated/context-contract_pb";
import type { ContextSelection } from "../../generated/context_pb";
import { innermostAction, useContextSelection } from "../context/ContextConnectionProvider";
import { TabbedPane, type TabDef } from "../panes/TabbedPane";
import { DiagramPanel, type OpenDiagram } from "./DiagramPanel";
import { PanelEmptyState } from "./PanelEmptyState";

interface DiagramTab {
  /** Entry AND location: re-activating a renamed file opens a fresh tab while the stale one stays closable. */
  key: string;
  diagram: OpenDiagram;
}

/** The innermost level of a selection chain - where the activated thing itself is named. */
function innermostLevel(selection: ContextSelection): ContextSelection {
  let cursor = selection;
  while (cursor.detail.case === "child") {
    cursor = cursor.detail.value;
  }
  return cursor;
}

/** The tab label: the registration's base name without `.adp`; a bare body keeps its own extension. */
function labelFor(path: readonly string[]): string {
  const name = path[path.length - 1] ?? "";
  return name.toLowerCase().endsWith(".adp") ? name.slice(0, -".adp".length) : name;
}

export interface DiagramTabsPanelProps {
  projectId: Uint8Array;
}

/**
 * The centre pane: which diagrams are open, and which has focus. A pure subscriber of the
 * pushed selection - a new non-transient push whose innermost level is a diagram entry
 * carrying the ACTIVATE gesture opens a tab or focuses the one it already has; a plain
 * selection or a preview never does (diagram-workspace-tabs Requirement 2). Nothing here
 * names a diagram type: which canvas renders a tab is {@link DiagramPanel}'s decision, from
 * the MIME type the backend resolved.
 *
 * Open tabs are per-connection, in-memory state only - they die with the page, like every
 * other per-connection state in the system (Requirement 4.5).
 */
export function DiagramTabsPanel({ projectId }: DiagramTabsPanelProps) {
  const { selection, levels } = useContextSelection();
  const [tabs, setTabs] = useState<DiagramTab[]>([]);
  const [activeKey, setActiveKey] = useState<string | undefined>(undefined);
  // The push, not the render, is the trigger: transient previews and unrelated re-renders
  // re-observe the same selection object, and must not re-run the rule (Requirement 2.3).
  const lastHandledRef = useRef<ContextSelection | null>(null);

  useEffect(() => {
    if (selection === null || selection === lastHandledRef.current) {
      return;
    }
    lastHandledRef.current = selection;

    if (innermostAction(selection) !== ContextSelectionAction.ACTIVATE) {
      return;
    }

    const innermost = innermostLevel(selection);
    const id = innermost.id?.source;
    if (id?.case !== "entryId") {
      return; // a diagram element or a future source: not an entry, not this panel's business
    }

    const detail: ContextLevelDetail | undefined = levels[levels.length - 1];
    const entry = detail?.detail.case === "entry" ? detail.detail.value : undefined;
    if (entry === undefined || entry.diagramMimeType === "") {
      return; // not a diagram: activation simply selected it (Requirement 2.3)
    }

    const path = innermost.path?.segments ?? [];
    const key = `${base64Encode(id.value.value)}|${path.join("/")}`;
    setTabs((current) =>
      current.some((tab) => tab.key === key)
        ? current
        : [...current, { key, diagram: { projectId, entryId: id.value.value, path: [...path], mimeType: entry.diagramMimeType } }],
    );
    setActiveKey(key);
  }, [selection, levels, projectId]);

  // A text tab asked for from elsewhere - "Open as text", "Open with…", or a problem's
  // go-to-line. Requested rather than pushed, because tabs are client state and the context
  // channel carries no gesture data. Re-requesting an open tab focuses it and updates its
  // line, so a second problem in the same file still navigates (R8.1).
  useEffect(
    () =>
      onTextTabRequested((request) => {
        const key = `text|${request.path.join("/")}`;
        const mimeType = request.editorId === "*" ? "editor/*" : `editor/${request.editorId}`;
        setTabs((current) =>
          current.some((tab) => tab.key === key)
            ? current.map((tab) =>
                tab.key === key ? { key, diagram: { ...tab.diagram, initialLine: request.line } } : tab,
              )
            : [
                ...current,
                {
                  key,
                  diagram: {
                    projectId,
                    // No entry id: the request names the file by path, and text canvases
                    // never read the id. An empty id keeps the tab model honest about that.
                    entryId: new Uint8Array(0),
                    path: [...request.path],
                    mimeType,
                    editorId: request.editorId,
                    initialLine: request.line,
                  },
                },
              ],
        );
        setActiveKey(key);
      }),
    [projectId],
  );

  const close = (key: string) => {
    // An editor tab with unsaved edits asks first (modular-text-editors R6.5); diagram tabs
    // never register as dirty, so nothing changes for them. The dirty registry is keyed by
    // the file's path - the one identity the editor panel and this strip share.
    const closing = tabs.find((tab) => tab.key === key);
    const dirtyKey = closing?.diagram.path.join("/") ?? key;
    if (isTabDirty(dirtyKey) && !window.confirm("This tab has unsaved changes. Close it anyway?")) {
      return;
    }

    const index = tabs.findIndex((tab) => tab.key === key);
    const remaining = tabs.filter((tab) => tab.key !== key);
    setTabs(remaining);
    if (key === activeKey) {
      // The nearest surviving neighbour takes focus - the one that slid into the closed
      // tab's place, or the new last when the last was closed.
      setActiveKey(remaining[Math.min(index, remaining.length - 1)]?.key);
    }
  };

  const tabDefs: TabDef[] = tabs.map((tab) => ({
    id: tab.key,
    label: labelFor(tab.diagram.path),
    icon: tab.diagram.mimeType.startsWith("editor/") ? "mdi-file-document-outline" : "mdi-graph-outline",
    tooltip: tab.diagram.path.join("/"),
    // Keyed per tab, and it is not optional: the pane renders one tab's content at a time,
    // so two files of the same type land the same component at the same position, and
    // without the key React reuses the instance across a tab switch - which is how opening
    // a second markdown file kept showing the first one's editor, state and all.
    content: <DiagramPanel key={tab.key} diagram={tab.diagram} />,
  }));

  return (
    <TabbedPane
      tabs={tabDefs}
      activeTabId={activeKey}
      onSelectTab={setActiveKey}
      onCloseTab={close}
      emptyState={
        <PanelEmptyState description="Double-click a diagram in the explorer to open it here." />
      }
    />
  );
}
