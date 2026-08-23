import { useEffect, useMemo, useRef, useState } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { useContextConnection, useContextSelection } from "../../context/ContextConnectionProvider";
import { ContextSelectionSchema, ContextSourceSchema } from "../../../generated/context_pb";
import type { ContextSelection, ContextShortcut } from "../../../generated/context_pb";
import { isFolded, type MindmapElement, type MindmapModel } from "./mindmapModel";
import { useMindmapStream } from "./useMindmapStream";

/** The diagram file this canvas shows: its project id, and the project-relative path of its `.adp`. */
export interface MindmapCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

const NODE_HALF_WIDTH = 60;
const NODE_HALF_HEIGHT = 16;

/**
 * Renders a mindmap and reports what the user does to the context service. It holds no
 * document state of its own beyond the stream's model and which node has focus; every edit is
 * an `ExecuteAction` (mindmap-diagram Requirements 6, 8, 10). Selection follows what the
 * backend pushes, so a selection made anywhere else moves focus here too.
 */
export function MindmapCanvas({ projectId, entryId, path }: MindmapCanvasProps) {
  const { model, loading, failed } = useMindmapStream(projectId, path);
  const { select, executeShortcut } = useContextConnection();
  const { selection } = useContextSelection();

  const [focusedId, setFocusedId] = useState<string | undefined>(undefined);

  // The node the backend says is selected inside this diagram, so focus follows a selection
  // from anywhere - the canvas reacts to the push, not to its own click (Requirement 10.5).
  const selectedNodeId = useMemo(() => nodeOf(selection, path), [selection, path]);
  useEffect(() => {
    if (selectedNodeId && model.elements.has(selectedNodeId)) {
      setFocusedId(selectedNodeId);
    }
  }, [selectedNodeId, model]);

  const surfaceRef = useRef<SVGSVGElement>(null);

  const reportSelection = (element: MindmapElement) => {
    setFocusedId(element.id);
    // Clicking a node must also give the surface the keyboard: browsers do not reliably move
    // DOM focus into an SVG when a child shape is clicked, and without it every shortcut
    // lands wherever focus last was - the explorer, typically (Requirement 8.4).
    surfaceRef.current?.focus();
    // A nested selection: the .adp file, then the node as a DIAGRAM_CANVAS child.
    select(nodeSelection(entryId, path, element));
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    // A key that means something in a text field is left to it - the rename prompt is a real
    // input the shell mounts, so text editing wins there (Requirement 8.2).
    if (!focusedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = shortcutFor(event);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    // The backend holds the key->action table; the canvas only forwards the keystroke as data
    // against the focused node's selection (Requirement 8.4).
    void executeShortcut(shortcut, create(ContextSourceSchema, { source: { case: "elementId", value: { value: focusedId } } }));
  };

  const elements = [...model.elements.values()];

  if (failed) {
    // The backend gave a permanent answer - the file is gone, moved, or unroutable. The tab
    // stays, closable as ever; re-activating the diagram under its new location opens a fresh
    // one (diagram-workspace-tabs Requirement 5).
    return (
      <div className="mindmap-canvas" data-testid="mindmap-canvas">
        <div className="mindmap-canvas-unavailable" role="alert">
          This diagram is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  return (
    <div className="mindmap-canvas" data-testid="mindmap-canvas">
      {loading ? (
        <div className="mindmap-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <svg
          ref={surfaceRef}
          className="mindmap-canvas-surface"
          viewBox={viewBoxOf(elements)}
          tabIndex={0}
          role="tree"
          aria-label="Mind map"
          onKeyDown={onKeyDown}
        >
          {elements.map((element) => (
            <MindmapNode
              key={element.id}
              element={element}
              focused={element.id === focusedId}
              folded={isFolded(model, element.id)}
              onSelect={() => reportSelection(element)}
            />
          ))}
        </svg>
      )}
    </div>
  );
}

interface MindmapNodeProps {
  element: MindmapElement;
  focused: boolean;
  folded: boolean;
  onSelect: () => void;
}

function MindmapNode({ element, focused, folded, onSelect }: MindmapNodeProps) {
  const { text, notes, hasChildren, link } = element.payload;
  const indicators = [notes ? "•" : "", link ? "↗" : "", folded && hasChildren ? "⊕" : ""].join(" ").trim();

  return (
    <g
      className={`mindmap-node${focused ? " mindmap-node-focused" : ""}`}
      transform={`translate(${element.x} ${element.y})`}
      onClick={onSelect}
      role="treeitem"
      aria-selected={focused}
    >
      <rect x={-NODE_HALF_WIDTH} y={-NODE_HALF_HEIGHT} width={NODE_HALF_WIDTH * 2} height={NODE_HALF_HEIGHT * 2} rx={6} />
      <text textAnchor="middle" dominantBaseline="central">
        {text || " "}
      </text>
      {indicators && (
        <text className="mindmap-node-indicators" x={NODE_HALF_WIDTH - 4} y={-NODE_HALF_HEIGHT + 4} textAnchor="end">
          {indicators}
        </text>
      )}
    </g>
  );
}

/** The node id the pushed selection names inside this diagram, or undefined. */
function nodeOf(selection: ContextSelection | null, path: readonly string[]): string | undefined {
  let cursor: ContextSelection | undefined = selection ?? undefined;
  // Walk the chain; the element id, wherever it sits, is the selected node.
  while (cursor) {
    if (cursor.id?.source.case === "elementId") {
      return cursor.id.source.value.value;
    }
    cursor = cursor.detail.case === "child" ? cursor.detail.value : undefined;
  }
  void path;
  return undefined;
}

/** The nested `file -> node` selection a canvas click reports (Requirement 10.1). */
function nodeSelection(entryId: Uint8Array, path: readonly string[], element: MindmapElement): ContextSelection {
  const child = create(ContextSelectionSchema, {
    source: 2, // DIAGRAM_CANVAS
    id: { source: { case: "elementId", value: { value: element.id } } },
    path: { segments: [element.payload.text] },
    detail: { case: "none", value: create(EmptySchema) },
  });

  return create(ContextSelectionSchema, {
    source: 1, // EXPLORER-origin file, selected on the canvas's behalf
    id: { source: { case: "entryId", value: { value: entryId } } },
    path: { segments: [...path] },
    detail: { case: "child", value: child },
  });
}

/** A keyboard event as a backend shortcut, or null for a key that carries no structural meaning. */
function shortcutFor(event: React.KeyboardEvent): ContextShortcut | null {
  const structural = ["Insert", "Enter", "F2", "Delete", " ", "Tab"];
  if (!structural.includes(event.key)) {
    return null;
  }

  // Tab is the XMind convention for "add child"; the backend's child action carries Insert, so
  // the alias is resolved to it here - a key-to-key mapping, never a key-to-action one.
  const key = event.key === "Tab" ? "Insert" : event.key;
  return { key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey, meta: event.metaKey } as ContextShortcut;
}

function viewBoxOf(elements: readonly MindmapElement[]): string {
  if (elements.length === 0) {
    return "-200 -150 400 300";
  }

  const xs = elements.map((element) => element.x);
  const ys = elements.map((element) => element.y);
  const minX = Math.min(...xs) - NODE_HALF_WIDTH - 20;
  const minY = Math.min(...ys) - NODE_HALF_HEIGHT - 20;
  const maxX = Math.max(...xs) + NODE_HALF_WIDTH + 20;
  const maxY = Math.max(...ys) + NODE_HALF_HEIGHT + 20;
  return `${minX} ${minY} ${maxX - minX} ${maxY - minY}`;
}

// Kept exported for the panel and tests; the model type is re-exported so consumers need one import.
export type { MindmapModel };

/** Whether an event's target is a text input the browser should handle instead of the canvas. */
function isTextTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  const tag = target.tagName.toLowerCase();
  return tag === "input" || tag === "textarea" || target.isContentEditable;
}
