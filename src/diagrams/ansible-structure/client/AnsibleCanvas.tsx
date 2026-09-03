import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { forwardBezierPath, straightPath } from "@client/canvas/connectors";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { ContextSelectionAction } from "@client/generated/context_pb";
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

  // This type's palette is empty by design - the module registers no toolbox provider,
  // because it edits nothing. Registering the backend's empty answer makes the panel say
  // exactly that, instead of claiming no diagram is open.
  useRegisterDiagramToolbox(useToolboxItems(projectId, path));
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

  const selectedId = selectedElementIdOf(selection);

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
      select(elementSelectionOf(entryId, path, element.id, gesture));
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
      <defs>
        {/* One arrowhead, reused: every Ansible edge points from the user of a thing to it. */}
        <marker id="ansible-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" className="ansible-arrowhead" />
        </marker>
      </defs>
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
    <BoxElement
      className={classes}
      data-element-id={node.id}
      data-kind={kindClass(node.payload.kind)}
      x={node.x}
      y={node.y}
      width={node.payload.width}
      height={node.payload.height}
      label={node.payload.name}
      boxClassName="ansible-node-box"
      labelClassName="ansible-node-label"
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
      {node.payload.hosts ? (
        <title>{`${kindLabel(node.payload.kind)} ${node.payload.name} — hosts: ${node.payload.hosts}`}</title>
      ) : (
        <title>{`${kindLabel(node.payload.kind)} ${node.payload.name}`}</title>
      )}
    </BoxElement>
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

  const classes = [
    "ansible-edge",
    `ansible-edge-${edgeClass(wire.kind)}`,
    wire.dynamic ? "ansible-edge-dynamic" : "ansible-edge-static",
  ].join(" ");

  // The layout is columnar, left to right, so an edge reads the same way: out of the source's
  // right side, into the target's left side, curving horizontally through the corridor
  // between the columns. The forward bezier loops around for the rare backward edge. The
  // centre-line anchoring this replaces treated each box's top-left corner as its centre,
  // which is why every line started half a box off and cut diagonally across the layout.
  const from = { x: anchors.from.x + anchors.from.payload.width, y: anchors.from.y + anchors.from.payload.height / 2 };
  const to = { x: anchors.to.x, y: anchors.to.y + anchors.to.payload.height / 2 };

  return (
    <g className={classes} data-edge-id={edge.id}>
      <path className="ansible-edge-line" d={forwardBezierPath(from, to)} markerEnd="url(#ansible-arrow)" />
      <text className="ansible-edge-label" x={(from.x + to.x) / 2} y={(from.y + to.y) / 2 - 6} textAnchor="middle">
        {wire.condition ? `${wire.directive} when ${wire.condition}` : wire.directive}
      </text>
    </g>
  );
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

