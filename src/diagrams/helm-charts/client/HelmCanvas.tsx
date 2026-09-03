import { useCallback, useMemo, useRef, useState } from "react";
import { forwardBezierPath, straightPath } from "@client/canvas/connectors";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { HelmEdgeKind, HelmElementKind } from "@client/generated/helm-charts_pb";
import { anchorsOf, edgesOf, nodesOf, type HelmElement, type HelmModel } from "./helmModel";
import { useHelmStream } from "./useHelmStream";

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
const PADDING = 60;
const DRAG_THRESHOLD_PX = 3;

export interface HelmCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * Renders a helm chart's anatomy and reports what the user selects.
 *
 * It holds no document state and offers one edit exactly: dragging a box stores an authored
 * position in the registration's `layout:` block - a view arrangement, never a chart edit,
 * undoable like everything else (Requirement 6.2). Everything else is navigation: activating
 * a file-backed node reveals it, which is most of the value (Requirement 8).
 */
export function HelmCanvas({ projectId, entryId, path }: HelmCanvasProps) {
  const { model, loading, failed, moveElementTo } = useHelmStream(projectId, path);

  // This type's palette is empty by design - the module registers no toolbox provider,
  // because chart content is created by helm tooling, not by dropping shapes. Registering
  // the backend's empty answer makes the panel say exactly that.
  useRegisterDiagramToolbox(useToolboxItems(projectId, path));
  const { select, revealPath } = useContextConnection();
  const { selection } = useContextSelection();

  const [view, setView] = useState<ViewBox | null>(null);
  const [rejection, setRejection] = useState<string | null>(null);
  const [drag, setDrag] = useState<{ id: string; x: number; y: number } | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const panRef = useRef<{ x: number; y: number; view: ViewBox } | null>(null);
  const dragRef = useRef<{ id: string; x: number; y: number; clientX: number; clientY: number; moved: boolean } | null>(null);

  const nodes = useMemo(() => nodesOf(model), [model]);
  const edges = useMemo(() => edgesOf(model), [model]);
  const bounds = useMemo(() => boundsOf(nodes), [nodes]);
  const effective = view ?? bounds;

  const selectedId = selectedElementIdOf(selection);

  const onSelect = useCallback(
    (element: HelmElement, gesture?: ContextSelectionAction) => {
      select(elementSelectionOf(entryId, path, element.id, gesture));
    },
    [entryId, path, select],
  );

  /**
   * Activating a file-backed node reveals its artifact in the explorer; a subchart reveals
   * its folder - beside the subchart's own `.adp` where one exists, so its diagram is one
   * double-click away (Requirement 8.2). A node with no backing artifact (a dependency, a
   * sealed archive's interior) has nothing to open, and the grid says why (Requirement 8.3).
   */
  const onActivate = useCallback(
    (element: HelmElement) => {
      const segments = element.payload.chartRelativePath;
      if (segments.length > 0) {
        revealPath([...path.slice(0, -1), ...segments]);
      }
    },
    [path, revealPath],
  );

  const unitsPerPixel = () => {
    const rect = svgRef.current?.getBoundingClientRect();
    return effective.w / Math.max(rect?.width ?? 1, 1);
  };

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

  const onSurfaceMouseDown = (event: React.MouseEvent) => {
    if (event.button !== 0 || event.target !== svgRef.current) {
      return;
    }
    // A drag on empty canvas pans; a drag on a node repositions it (below).
    panRef.current = { x: event.clientX, y: event.clientY, view: effective };
  };

  const onNodeMouseDown = (node: HelmElement, event: React.MouseEvent) => {
    if (event.button !== 0) {
      return;
    }
    event.stopPropagation();
    dragRef.current = { id: node.id, x: node.x, y: node.y, clientX: event.clientX, clientY: event.clientY, moved: false };
  };

  const onMouseMove = (event: React.MouseEvent) => {
    const dragging = dragRef.current;
    if (dragging) {
      dragging.moved ||=
        Math.abs(event.clientX - dragging.clientX) + Math.abs(event.clientY - dragging.clientY) > DRAG_THRESHOLD_PX;
      if (dragging.moved) {
        const scale = unitsPerPixel();
        setDrag({
          id: dragging.id,
          x: dragging.x + (event.clientX - dragging.clientX) * scale,
          y: dragging.y + (event.clientY - dragging.clientY) * scale,
        });
      }
      return;
    }

    const pan = panRef.current;
    if (pan && svgRef.current) {
      const scale = pan.view.w / Math.max(svgRef.current.getBoundingClientRect().width, 1);
      setView({
        x: pan.view.x - (event.clientX - pan.x) * scale,
        y: pan.view.y - (event.clientY - pan.y) * scale,
        w: pan.view.w,
        h: pan.view.h,
      });
    }
  };

  const onMouseUp = () => {
    panRef.current = null;

    const dragging = dragRef.current;
    const landed = drag;
    dragRef.current = null;
    setDrag(null);
    if (dragging && landed && dragging.moved) {
      // The authored position, raw: the layout block stores what the author placed, and
      // rounding it here would quietly turn the canvas into a grid.
      void (async () => {
        const error = await moveElementTo(landed.id, landed.x, landed.y);
        if (error) {
          setRejection(error);
        }
      })();
    }
  };

  if (failed) {
    return <div className="helm-canvas-message">This helm chart diagram could not be opened.</div>;
  }

  if (loading) {
    return <div className="helm-canvas-message">Reading the chart…</div>;
  }

  if (nodes.length === 0) {
    return (
      <div className="helm-canvas-message">
        This folder is not a Helm chart: it has no <code>Chart.yaml</code>, so there is nothing to draw.
      </div>
    );
  }

  return (
    <div className="helm-canvas-frame">
      {rejection ? (
        <div className="helm-canvas-rejection" role="status" onClick={() => setRejection(null)}>
          {rejection}
        </div>
      ) : null}
      <svg
        ref={svgRef}
        className="helm-canvas"
        viewBox={`${effective.x} ${effective.y} ${effective.w} ${effective.h}`}
        role="application"
        aria-label="Helm chart anatomy"
        tabIndex={0}
        onWheel={onWheel}
        onMouseDown={onSurfaceMouseDown}
        onMouseMove={onMouseMove}
        onMouseUp={onMouseUp}
        onMouseLeave={onMouseUp}
      >
        <defs>
          {/* One arrowhead, reused: every helm edge points from the user of a thing to it. */}
          <marker id="helm-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" className="helm-arrowhead" />
          </marker>
        </defs>
        <g className="helm-edges">
          {edges.map((edge) => (
            <Edge key={edge.id} edge={edge} model={model} />
          ))}
        </g>
        <g className="helm-nodes">
          {nodes.map((node) => {
            const dragged = drag && drag.id === node.id ? drag : null;
            return (
              <Node
                key={node.id}
                node={dragged ? { ...node, x: dragged.x, y: dragged.y } : node}
                selected={node.id === selectedId}
                onSelect={onSelect}
                onActivate={onActivate}
                onMouseDown={onNodeMouseDown}
              />
            );
          })}
        </g>
      </svg>
    </div>
  );
}

/**
 * One box. Its kind is a CSS class rather than an inline style, so every colour and shape
 * stays in the stylesheet - tech.md's centralised-styling rule.
 */
function Node({
  node,
  selected,
  onSelect,
  onActivate,
  onMouseDown,
}: {
  node: HelmElement;
  selected: boolean;
  onSelect: (element: HelmElement, gesture?: ContextSelectionAction) => void;
  onActivate: (element: HelmElement) => void;
  onMouseDown: (element: HelmElement, event: React.MouseEvent) => void;
}) {
  const classes = [
    "helm-node",
    `helm-node-${kindClass(node.payload.kind)}`,
    selected ? "helm-node-selected" : "",
    node.payload.unreadable ? "helm-node-unreadable" : "",
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
      boxClassName="helm-node-box"
      labelClassName="helm-node-label"
      role="button"
      tabIndex={0}
      aria-label={`${kindLabel(node.payload.kind)} ${node.payload.name}`}
      onClick={() => onSelect(node)}
      onDoubleClick={() => onActivate(node)}
      onMouseDown={(event: React.MouseEvent) => onMouseDown(node, event)}
      onContextMenu={(event) => {
        event.preventDefault();
        onSelect(node, ContextSelectionAction.CONTEXT_MENU);
      }}
    >
      <title>{`${kindLabel(node.payload.kind)} ${node.payload.name}`}</title>
    </BoxElement>
  );
}

/**
 * One relationship, out of the source's right side and into the target's left - the bands run
 * left to right, so the edges read the same way. An open end draws as a stub from its source:
 * a reader has to be able to see that something names what is not there (R5.2, R5.6).
 */
function Edge({ edge, model }: { edge: HelmElement; model: HelmModel }) {
  const wire = edge.payload.edge;
  const anchors = anchorsOf(model, edge);
  if (!wire) {
    return null;
  }

  if (!anchors) {
    const source = model.elements.get(wire.sourceId);
    if (!source) {
      return null;
    }
    return (
      <g className={`helm-edge helm-edge-${edgeClass(wire.kind)} helm-edge-open`} data-edge-id={edge.id}>
        <path
          className="helm-edge-line"
          d={straightPath(
            { x: source.x + source.payload.width, y: source.y + source.payload.height / 2 },
            { x: source.x + source.payload.width + 48, y: source.y + source.payload.height / 2 },
          )}
        />
        <text className="helm-edge-label" x={source.x + source.payload.width + 8} y={source.y + source.payload.height / 2 - 6}>
          {wire.label}
          {wire.kind === HelmEdgeKind.RESOLVES ? " (unvendored)" : " (not defined here)"}
        </text>
      </g>
    );
  }

  const from = { x: anchors.from.x + anchors.from.payload.width, y: anchors.from.y + anchors.from.payload.height / 2 };
  const to = { x: anchors.to.x, y: anchors.to.y + anchors.to.payload.height / 2 };

  return (
    <g className={`helm-edge helm-edge-${edgeClass(wire.kind)}`} data-edge-id={edge.id}>
      <path className="helm-edge-line" d={forwardBezierPath(from, to)} markerEnd="url(#helm-arrow)" />
      {wire.label ? (
        <text className="helm-edge-label" x={(from.x + to.x) / 2} y={(from.y + to.y) / 2 - 6} textAnchor="middle">
          {wire.label}
        </text>
      ) : null}
    </g>
  );
}

function boundsOf(nodes: readonly HelmElement[]): ViewBox {
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

function kindClass(kind: HelmElementKind): string {
  switch (kind) {
    case HelmElementKind.CHART:
      return "chart";
    case HelmElementKind.VALUES:
      return "values";
    case HelmElementKind.SCHEMA:
      return "schema";
    case HelmElementKind.TEMPLATE:
      return "template";
    case HelmElementKind.CRDS:
      return "crds";
    case HelmElementKind.DEPENDENCY:
      return "dependency";
    case HelmElementKind.SUBCHART:
      return "subchart";
    case HelmElementKind.ARCHIVE:
      return "archive";
    case HelmElementKind.LOCK:
      return "lock";
    default:
      return "unknown";
  }
}

function kindLabel(kind: HelmElementKind): string {
  switch (kind) {
    case HelmElementKind.CHART:
      return "Chart";
    case HelmElementKind.VALUES:
      return "Values";
    case HelmElementKind.SCHEMA:
      return "Values schema";
    case HelmElementKind.TEMPLATE:
      return "Template";
    case HelmElementKind.CRDS:
      return "CRDs";
    case HelmElementKind.DEPENDENCY:
      return "Dependency";
    case HelmElementKind.SUBCHART:
      return "Subchart";
    case HelmElementKind.ARCHIVE:
      return "Archive";
    case HelmElementKind.LOCK:
      return "Lock";
    default:
      return "Element";
  }
}

function edgeClass(kind: HelmEdgeKind): string {
  switch (kind) {
    case HelmEdgeKind.DECLARES:
      return "declares";
    case HelmEdgeKind.RESOLVES:
      return "resolves";
    case HelmEdgeKind.OVERRIDES:
      return "overrides";
    case HelmEdgeKind.CONFIGURES:
      return "configures";
    case HelmEdgeKind.INCLUDES:
      return "includes";
    default:
      return "unknown";
  }
}
