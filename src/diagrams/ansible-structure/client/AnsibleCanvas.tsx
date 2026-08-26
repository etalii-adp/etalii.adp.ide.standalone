import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { anchorsBetween, midpointOf, straightPath, type ConnectorBox } from "@client/canvas/connectors";
import { useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction, ContextSelectionSchema } from "@client/generated/context_pb";
import type { ContextSelection } from "@client/generated/context_pb";
import { AnsibleEdgeKind, AnsibleElementKind } from "@client/generated/ansible-structure_pb";
import { anchorsOf, edgesOf, nodesOf, paletteSlotOf, type AnsibleElement, type AnsibleModel } from "./ansibleModel";
import { useAnsibleStream } from "./useAnsibleStream";

/** The visible rectangle, in canvas units - the svg viewBox as data. */
interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

const ZOOM_STEP = 1.25;
const MIN_VIEW_WIDTH = 40;
const MAX_VIEW_WIDTH = 100000;
const VIEW_REPORT_DEBOUNCE_MS = 200;
const PADDING = 60;

/** How many play colours the stylesheet defines. The palette itself is CSS's; this is its size. */
const PALETTE_SLOTS = 6;

export interface AnsibleCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * Renders an Ansible project's structure and reports what the user selects.
 *
 * It holds no document state and offers no edit: there is no drag, no toolbox drop, no context
 * menu of commands and no keyboard mutation, because this diagram type has none of those to
 * offer. What it does have is the jump from a node to its file, which is most of the value
 * (Requirement 8.1).
 */
export function AnsibleCanvas({ projectId, entryId, path }: AnsibleCanvasProps) {
  const { model, loading, failed, reportView } = useAnsibleStream(projectId, path);
  const { select, revealPath } = useContextConnection();
  const { selection } = useContextSelection();

  const [view, setView] = useState<ViewBox | null>(null);
  const [focusedId, setFocusedId] = useState<string | undefined>(undefined);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const panRef = useRef<{ x: number; y: number; view: ViewBox } | null>(null);

  const nodes = useMemo(() => nodesOf(model), [model]);
  const edges = useMemo(() => edgesOf(model), [model]);
  const bounds = useMemo(() => boundsOf(nodes), [nodes]);
  const effective = view ?? bounds;

  const selectedId = nodeIdOf(selection);

  // The backend culls to what a connection can see, so it has to be told - debounced, because
  // a pan produces a report per frame otherwise.
  useEffect(() => {
    const handle = setTimeout(
      () => reportView({ minX: effective.x, minY: effective.y, maxX: effective.x + effective.w, maxY: effective.y + effective.h }),
      VIEW_REPORT_DEBOUNCE_MS,
    );
    return () => clearTimeout(handle);
    // reportView is recreated per render; the view is what actually changed.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effective.x, effective.y, effective.w, effective.h]);

  const onSelect = useCallback(
    (element: AnsibleElement, gesture?: ContextSelectionAction) => {
      setFocusedId(element.id);
      select(elementSelection(entryId, path, element, gesture));
    },
    [entryId, path, select],
  );

  /**
   * Activating a node reveals its file in the explorer. The jump from the picture to the file
   * is most of what this diagram type is for, so it is bound to both the double-click and the
   * keyboard - a reader who navigates by keyboard should not have to reach for the mouse to use
   * the one thing the diagram offers.
   */
  const onActivate = useCallback(
    (element: AnsibleElement) => {
      const segments = element.payload.projectRelativePath;
      if (segments.length > 0) {
        revealPath([...path.slice(0, -1), ...segments]);
      }
    },
    [path, revealPath],
  );

  const onWheel = (event: React.WheelEvent) => {
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1 / ZOOM_STEP : ZOOM_STEP;
    const width = clamp(effective.w * factor, MIN_VIEW_WIDTH, MAX_VIEW_WIDTH);
    const scale = width / effective.w;
    setView({
      x: effective.x + (effective.w - width) / 2,
      y: effective.y + (effective.h - effective.h * scale) / 2,
      w: width,
      h: effective.h * scale,
    });
  };

  const onPointerDown = (event: React.PointerEvent) => {
    if (event.button !== 0 || event.target !== svgRef.current) {
      return;
    }
    // A drag on empty canvas pans. There is nothing else a drag could mean here: no element
    // can be moved, so the gesture is free for navigation.
    panRef.current = { x: event.clientX, y: event.clientY, view: effective };
    svgRef.current?.setPointerCapture(event.pointerId);
  };

  const onPointerMove = (event: React.PointerEvent) => {
    const pan = panRef.current;
    if (!pan || !svgRef.current) {
      return;
    }
    const rect = svgRef.current.getBoundingClientRect();
    const unitsPerPixel = pan.view.w / Math.max(rect.width, 1);
    setView({
      x: pan.view.x - (event.clientX - pan.x) * unitsPerPixel,
      y: pan.view.y - (event.clientY - pan.y) * unitsPerPixel,
      w: pan.view.w,
      h: pan.view.h,
    });
  };

  const onPointerUp = (event: React.PointerEvent) => {
    panRef.current = null;
    svgRef.current?.releasePointerCapture(event.pointerId);
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key !== "Enter" && event.key !== " ") {
      return;
    }
    const element = focusedId ? model.elements.get(focusedId) : undefined;
    if (element) {
      event.preventDefault();
      onActivate(element);
    }
  };

  if (failed) {
    return <div className="ansible-canvas-message">This Ansible project structure diagram could not be opened.</div>;
  }

  if (loading) {
    return <div className="ansible-canvas-message">Reading the folder…</div>;
  }

  if (nodes.length === 0) {
    return (
      <div className="ansible-canvas-message">
        Nothing here is laid out the way Ansible expects, so there is nothing to draw. Playbooks at the folder root or
        under <code>playbooks/</code>, roles under <code>roles/</code>, inventories under <code>inventories/</code>.
      </div>
    );
  }

  return (
    <svg
      ref={svgRef}
      className="ansible-canvas"
      viewBox={`${effective.x} ${effective.y} ${effective.w} ${effective.h}`}
      role="application"
      aria-label="Ansible project structure"
      tabIndex={0}
      onWheel={onWheel}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onKeyDown={onKeyDown}
    >
      <g className="ansible-edges">
        {edges.map((edge) => (
          <Edge key={edge.id} edge={edge} model={model} />
        ))}
      </g>
      <g className="ansible-nodes">
        {nodes.map((node) => (
          <Node
            key={node.id}
            node={node}
            selected={node.id === selectedId}
            onSelect={onSelect}
            onActivate={onActivate}
          />
        ))}
      </g>
    </svg>
  );
}

/**
 * One box. Its kind and its play slot are CSS classes rather than inline styles, so every colour
 * and every shape stays in the stylesheet - tech.md's centralised-styling rule, and what keeps a
 * theme change out of this file.
 */
function Node({
  node,
  selected,
  onSelect,
  onActivate,
}: {
  node: AnsibleElement;
  selected: boolean;
  onSelect: (element: AnsibleElement, gesture?: ContextSelectionAction) => void;
  onActivate: (element: AnsibleElement) => void;
}) {
  const slot = paletteSlotOf(node, PALETTE_SLOTS);
  const classes = [
    "ansible-node",
    `ansible-node-${kindClass(node.payload.kind)}`,
    slot >= 0 ? `ansible-play-${slot}` : "ansible-play-none",
    selected ? "ansible-node-selected" : "",
    node.payload.hollow ? "ansible-node-hollow" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <g
      className={classes}
      data-element-id={node.id}
      data-kind={kindClass(node.payload.kind)}
      transform={`translate(${node.x} ${node.y})`}
      role="button"
      tabIndex={0}
      aria-label={`${kindLabel(node.payload.kind)} ${node.payload.name}`}
      onClick={() => onSelect(node)}
      onDoubleClick={() => onActivate(node)}
      onContextMenu={(event) => {
        event.preventDefault();
        onSelect(node, ContextSelectionAction.CONTEXT_MENU);
      }}
    >
      <rect className="ansible-node-box" width={node.payload.width} height={node.payload.height} rx={4} />
      <text className="ansible-node-label" x={8} y={node.payload.height / 2 + 4}>
        {node.payload.name}
      </text>
      {node.payload.hosts ? (
        <title>{`${kindLabel(node.payload.kind)} ${node.payload.name} — hosts: ${node.payload.hosts}`}</title>
      ) : (
        <title>{`${kindLabel(node.payload.kind)} ${node.payload.name}`}</title>
      )}
    </g>
  );
}

/**
 * One relationship. Static mechanisms draw solid and dynamic ones dashed - the convention
 * ansible-playbook-grapher's users already read (Requirement 5.4) - with `dependsOn` in a style
 * of its own, because being depended upon is not the same as being listed by a playbook.
 */
function Edge({ edge, model }: { edge: AnsibleElement; model: AnsibleModel }) {
  const wire = edge.payload.edge;
  const anchors = anchorsOf(model, edge);
  if (!wire) {
    return null;
  }

  // An edge whose target is missing or unknowable has no second box to reach. It is drawn as a
  // stub from its source rather than not drawn at all: a reader has to be able to see that a
  // playbook names something that is not there.
  if (!anchors) {
    const source = model.elements.get(wire.sourceId);
    if (!source) {
      return null;
    }
    return (
      <g className={`ansible-edge ansible-edge-${edgeClass(wire.kind)} ansible-edge-unresolved`} data-edge-id={edge.id}>
        <path
          className="ansible-edge-line"
          d={straightPath(
            { x: source.x + source.payload.width, y: source.y + source.payload.height / 2 },
            { x: source.x + source.payload.width + 48, y: source.y + source.payload.height / 2 },
          )}
        />
        <text className="ansible-edge-label" x={source.x + source.payload.width + 8} y={source.y + source.payload.height / 2 - 6}>
          {wire.targetAsWritten}
          {edge.payload.unresolvable ? " (expression)" : " (missing)"}
        </text>
      </g>
    );
  }

  const [from, to] = anchorsBetween(boxOf(anchors.from), boxOf(anchors.to));
  const label = midpointOf(from, to);
  const classes = [
    "ansible-edge",
    `ansible-edge-${edgeClass(wire.kind)}`,
    wire.dynamic ? "ansible-edge-dynamic" : "ansible-edge-static",
  ].join(" ");

  return (
    <g className={classes} data-edge-id={edge.id}>
      <path className="ansible-edge-line" d={straightPath(from, to)} />
      <text className="ansible-edge-label" x={label.x} y={label.y - 4}>
        {wire.condition ? `${wire.directive} when ${wire.condition}` : wire.directive}
      </text>
    </g>
  );
}

function boxOf(element: AnsibleElement): ConnectorBox {
  return { x: element.x, y: element.y, width: element.payload.width, height: element.payload.height };
}

function boundsOf(nodes: readonly AnsibleElement[]): ViewBox {
  if (nodes.length === 0) {
    return { x: 0, y: 0, w: 800, h: 600 };
  }

  const minX = Math.min(...nodes.map((node) => node.x));
  const minY = Math.min(...nodes.map((node) => node.y));
  const maxX = Math.max(...nodes.map((node) => node.x + node.payload.width));
  const maxY = Math.max(...nodes.map((node) => node.y + node.payload.height));

  return {
    x: minX - PADDING,
    y: minY - PADDING,
    w: Math.max(maxX - minX + 2 * PADDING, MIN_VIEW_WIDTH),
    h: Math.max(maxY - minY + 2 * PADDING, MIN_VIEW_WIDTH),
  };
}

function clamp(value: number, low: number, high: number): number {
  return Math.min(Math.max(value, low), high);
}

function kindClass(kind: AnsibleElementKind): string {
  switch (kind) {
    case AnsibleElementKind.PLAYBOOK:
      return "playbook";
    case AnsibleElementKind.PLAY:
      return "play";
    case AnsibleElementKind.ROLE:
      return "role";
    case AnsibleElementKind.TASK_FILE:
      return "taskfile";
    case AnsibleElementKind.INVENTORY:
      return "inventory";
    case AnsibleElementKind.VARIABLE_FOLDER:
      return "vars";
    default:
      return "unknown";
  }
}

function kindLabel(kind: AnsibleElementKind): string {
  switch (kind) {
    case AnsibleElementKind.PLAYBOOK:
      return "Playbook";
    case AnsibleElementKind.PLAY:
      return "Play";
    case AnsibleElementKind.ROLE:
      return "Role";
    case AnsibleElementKind.TASK_FILE:
      return "Task file";
    case AnsibleElementKind.INVENTORY:
      return "Inventory";
    case AnsibleElementKind.VARIABLE_FOLDER:
      return "Variables";
    default:
      return "Element";
  }
}

function edgeClass(kind: AnsibleEdgeKind): string {
  switch (kind) {
    case AnsibleEdgeKind.USES_ROLE:
      return "uses-role";
    case AnsibleEdgeKind.IMPORTS_PLAYBOOK:
      return "imports-playbook";
    case AnsibleEdgeKind.INCLUDES_TASKS:
      return "includes-tasks";
    case AnsibleEdgeKind.DEPENDS_ON:
      return "depends-on";
    case AnsibleEdgeKind.TARGETS:
      return "targets";
    default:
      return "unknown";
  }
}

/** The element id in the pushed selection chain, wherever it sits. */
function nodeIdOf(selection: ContextSelection | null): string | undefined {
  let cursor: ContextSelection | undefined = selection ?? undefined;
  while (cursor) {
    if (cursor.id?.source.case === "elementId") {
      return cursor.id.source.value.value;
    }
    cursor = cursor.detail.case === "child" ? cursor.detail.value : undefined;
  }
  return undefined;
}

/** The nested `file -> element` selection a canvas click reports. */
function elementSelection(
  entryId: Uint8Array,
  path: readonly string[],
  element: AnsibleElement,
  gesture?: ContextSelectionAction,
): ContextSelection {
  const child = create(ContextSelectionSchema, {
    source: 2, // DIAGRAM_CANVAS
    id: { source: { case: "elementId", value: { value: element.id } } },
    // Empty asks the backend to fill it in: the resolver derives the element's path and echoes
    // it back, and a canvas that guessed at it would be rejected for disagreeing.
    path: { segments: [] },
    detail: gesture === undefined ? { case: "none", value: create(EmptySchema) } : { case: "action", value: gesture },
  });

  return create(ContextSelectionSchema, {
    source: 1, // EXPLORER-origin file, selected on the canvas's behalf
    id: { source: { case: "entryId", value: { value: entryId } } },
    path: { segments: [...path] },
    detail: { case: "child", value: child },
  });
}
