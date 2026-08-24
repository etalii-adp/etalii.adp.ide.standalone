import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { useContextConnection } from "../../context/ContextConnectionProvider";
import { ContextSelectionSchema } from "../../../generated/context_pb";
import type { ContextSelection } from "../../../generated/context_pb";
import { useRegisterDiagramView, type DiagramViewControls } from "../DiagramViewContext";
import { boxesOf, type C4BoundaryBox, type C4Model, type C4Node, type C4Relationship } from "./c4Model";
import { useC4Stream } from "./useC4Stream";

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

export interface C4CanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * Renders one C4 view. Everything it draws was decided by the backend - the boxes at the sizes
 * it measured, the palette it resolved, the title and the key it composed - so the canvas is a
 * renderer rather than a second opinion about what a C4 diagram is
 * (c4-diagrams Requirement 4).
 */
export function C4Canvas({ projectId, entryId, path }: C4CanvasProps) {
  const { model, loading, failed, reportView } = useC4Stream(projectId, path);
  const { select } = useContextConnection();

  const [focusedId, setFocusedId] = useState<string | undefined>(undefined);
  const [view, setView] = useState<ViewBox | null>(null);
  const panRef = useRef<{ clientX: number; clientY: number; view: ViewBox; moved: boolean } | null>(null);
  const panJustEndedRef = useRef(false);
  const surfaceRef = useRef<SVGSVGElement>(null);

  const fitBox = useMemo(() => fitBoxOf(model), [model]);
  const effectiveView = view ?? fitBox;
  const viewRef = useRef(effectiveView);
  viewRef.current = effectiveView;

  const zoomBy = useCallback((factor: number, aboutX?: number, aboutY?: number) => {
    setView(() => {
      const current = viewRef.current;
      const w = Math.min(MAX_VIEW_WIDTH, Math.max(MIN_VIEW_WIDTH, current.w / factor));
      const applied = current.w / w;
      const h = current.h / applied;
      const px = aboutX ?? current.x + current.w / 2;
      const py = aboutY ?? current.y + current.h / 2;
      return { x: px - (px - current.x) / applied, y: py - (py - current.y) / applied, w, h };
    });
  }, []);

  // Attached by hand as non-passive: React's synthetic wheel listener cannot preventDefault,
  // and without that every zoom also scrolls the page.
  useEffect(() => {
    const surface = surfaceRef.current;
    if (surface === null) {
      return;
    }

    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const rect = surface.getBoundingClientRect();
      const current = viewRef.current;
      const aboutX = rect.width > 0 ? current.x + ((event.clientX - rect.left) / rect.width) * current.w : undefined;
      const aboutY = rect.height > 0 ? current.y + ((event.clientY - rect.top) / rect.height) * current.h : undefined;
      zoomBy(event.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP, aboutX, aboutY);
    };

    surface.addEventListener("wheel", onWheel, { passive: false });
    return () => surface.removeEventListener("wheel", onWheel);
  }, [zoomBy, loading, failed]);

  const viewControls = useMemo<DiagramViewControls>(
    () => ({
      zoomIn: () => zoomBy(ZOOM_STEP),
      zoomOut: () => zoomBy(1 / ZOOM_STEP),
      fitToView: () => setView(null),
    }),
    [zoomBy],
  );
  useRegisterDiagramView(viewControls);

  const reportViewRef = useRef(reportView);
  reportViewRef.current = reportView;
  const viewKey = `${effectiveView.x},${effectiveView.y},${effectiveView.w},${effectiveView.h}`;
  useEffect(() => {
    if (loading || failed) {
      return;
    }

    const timer = setTimeout(
      () => reportViewRef.current(shownRectOf(viewRef.current, surfaceRef.current)),
      VIEW_REPORT_DEBOUNCE_MS,
    );
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewKey, loading, failed]);

  const unitsPerPixel = (box: ViewBox): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return rect !== undefined && rect.width > 0 ? box.w / rect.width : 1;
  };

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    if (event.target !== event.currentTarget) {
      return;
    }
    panRef.current = { clientX: event.clientX, clientY: event.clientY, view: viewRef.current, moved: false };
  };

  const onSurfacePointerMove = (event: React.MouseEvent) => {
    const pan = panRef.current;
    if (!pan) {
      return;
    }
    if (!pan.moved && Math.hypot(event.clientX - pan.clientX, event.clientY - pan.clientY) <= 4) {
      return;
    }
    pan.moved = true;
    const scale = unitsPerPixel(pan.view);
    setView({
      ...pan.view,
      x: pan.view.x - (event.clientX - pan.clientX) * scale,
      y: pan.view.y - (event.clientY - pan.clientY) * scale,
    });
  };

  const onSurfacePointerUp = () => {
    const pan = panRef.current;
    panRef.current = null;
    if (pan?.moved) {
      panJustEndedRef.current = true;
    }
  };

  const onBackgroundClick = (event: React.MouseEvent) => {
    if (event.target !== event.currentTarget) {
      return;
    }
    if (panJustEndedRef.current) {
      // The click that trails a pan is the same gesture: letting go must not also deselect.
      panJustEndedRef.current = false;
      return;
    }
    setFocusedId(undefined);
    select(null);
  };

  const onNodeClick = (node: C4Node) => {
    setFocusedId(node.id);
    surfaceRef.current?.focus();
    select(nodeSelection(entryId, path, node.id));
  };

  if (failed) {
    return (
      <div className="c4-canvas" data-testid="c4-canvas">
        <div className="c4-canvas-unavailable" role="alert">
          This diagram is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  const nodes = [...model.nodes.values()];
  const relationships = [...model.relationships.values()];
  const boundaries = [...model.boundaries.values()];

  return (
    <div className="c4-canvas" data-testid="c4-canvas">
      {loading ? (
        <div className="c4-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <>
          {/* C4 requires every diagram to carry a title describing its type and scope. */}
          {model.view && (
            <div className="c4-canvas-title" data-testid="c4-title">
              {model.view.title}
            </div>
          )}
          <svg
            ref={surfaceRef}
            className="c4-canvas-surface"
            viewBox={`${effectiveView.x} ${effectiveView.y} ${effectiveView.w} ${effectiveView.h}`}
            tabIndex={0}
            role="img"
            aria-label={model.view?.title ?? "C4 diagram"}
            onClick={onBackgroundClick}
            onMouseDown={onSurfacePointerDown}
            onMouseMove={onSurfacePointerMove}
            onMouseUp={onSurfacePointerUp}
            onMouseLeave={onSurfacePointerUp}
          >
            <defs>
              {/* One arrowhead, reused: C4 relationships are unidirectional. */}
              <marker id="c4-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                <path d="M 0 0 L 10 5 L 0 10 z" className="c4-arrowhead" />
              </marker>
            </defs>

            {/* Boundaries first, so everything they enclose draws on top of them. */}
            {boundaries.map((boundary) => (
              <C4BoundaryShape key={boundary.id} boundary={boundary} />
            ))}
            {relationships.map((relationship) => (
              <C4RelationshipShape key={relationship.id} relationship={relationship} />
            ))}
            {nodes.map((node) => (
              <C4NodeShape
                key={node.id}
                node={node}
                focused={node.id === focusedId}
                onSelect={() => onNodeClick(node)}
              />
            ))}
          </svg>

          {/* C4 requires a key explaining every shape and colour the diagram uses, so it can be
              read without accompanying narrative. Built from what the backend actually drew. */}
          {model.view && model.view.legend.length > 0 && (
            <div className="c4-canvas-legend" data-testid="c4-legend">
              <span className="c4-legend-title">Key</span>
              {model.view.legend.map((entry) => (
                <span className="c4-legend-entry" key={entry.label}>
                  <span
                    className="c4-legend-swatch"
                    style={{ background: entry.style?.background, borderColor: entry.style?.background }}
                  />
                  {entry.label}
                </span>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

/** One element box: name, the bracketed type-and-technology line, and the description. */
function C4NodeShape({ node, focused, onSelect }: { node: C4Node; focused: boolean; onSelect: () => void }) {
  const { name, typeLine, description, width, height, style } = node.payload;
  const halfWidth = width / 2;
  const halfHeight = height / 2;
  const background = style?.background ?? "#1168bd";
  const color = style?.color ?? "#ffffff";
  const shape = style?.shape ?? "RoundedBox";

  return (
    <g
      className={`c4-node${focused ? " c4-node-focused" : ""}`}
      transform={`translate(${node.x} ${node.y})`}
      onClick={onSelect}
      role="button"
      aria-label={name}
    >
      {shape === "Person" ? (
        <>
          {/* The person shape: a head above the box, as the reference diagrams draw it. */}
          <circle cx={0} cy={-halfHeight - 8} r={10} fill={background} />
          <rect x={-halfWidth} y={-halfHeight} width={width} height={height} rx={8} fill={background} />
        </>
      ) : shape === "Cylinder" ? (
        <>
          {/* A data store, drawn as the cylinder the notation uses for one. */}
          <rect x={-halfWidth} y={-halfHeight + 6} width={width} height={height - 12} fill={background} />
          <ellipse cx={0} cy={-halfHeight + 6} rx={halfWidth} ry={6} fill={background} />
          <ellipse cx={0} cy={halfHeight - 6} rx={halfWidth} ry={6} fill={background} />
        </>
      ) : (
        <rect
          x={-halfWidth}
          y={-halfHeight}
          width={width}
          height={height}
          rx={shape === "Box" ? 0 : 8}
          fill={background}
        />
      )}

      <text className="c4-node-name" y={-halfHeight + 22} textAnchor="middle" fill={color}>
        {name}
      </text>
      <text className="c4-node-type" y={-halfHeight + 38} textAnchor="middle" fill={color}>
        {typeLine}
      </text>
      {description && (
        <text className="c4-node-description" y={-halfHeight + 58} textAnchor="middle" fill={color}>
          {description}
        </text>
      )}
    </g>
  );
}

/**
 * One relationship: a dashed, unidirectional arrow labelled with what it is for and, where the
 * model says so, the technology it uses - which is what a Container diagram exists to show.
 */
function C4RelationshipShape({ relationship }: { relationship: C4Relationship }) {
  const p = relationship.payload;
  const [x1, y1, x2, y2] = anchorsBetween(p);
  const label = p.technology ? `${p.description} [${p.technology}]` : p.description;
  const order = p.interactionOrder;

  return (
    <g className="c4-relationship">
      <line x1={x1} y1={y1} x2={x2} y2={y2} markerEnd="url(#c4-arrow)" />
      {label && (
        <text className="c4-relationship-label" x={(x1 + x2) / 2} y={(y1 + y2) / 2 - 6} textAnchor="middle">
          {order ? `${order}. ${label}` : label}
        </text>
      )}
    </g>
  );
}

/** The dashed rectangle around a system's containers or a container's components. */
function C4BoundaryShape({ boundary }: { boundary: C4BoundaryBox }) {
  const { name, kind, width, height } = boundary.payload;
  return (
    <g className="c4-boundary" transform={`translate(${boundary.x} ${boundary.y})`}>
      <rect x={-width / 2} y={-height / 2} width={width} height={height} rx={6} />
      <text className="c4-boundary-label" x={-width / 2 + 12} y={height / 2 - 12}>
        {name} [{kind}]
      </text>
    </g>
  );
}

/**
 * Where a relationship's line starts and ends: on the two boxes' edges, on the straight line
 * between their centres, so an arrow touches the box rather than disappearing under it.
 */
export function anchorsBetween(p: {
  sourceX: number;
  sourceY: number;
  sourceWidth: number;
  sourceHeight: number;
  destinationX: number;
  destinationY: number;
  destinationWidth: number;
  destinationHeight: number;
}): [number, number, number, number] {
  const dx = p.destinationX - p.sourceX;
  const dy = p.destinationY - p.sourceY;
  const from = edgePoint(p.sourceX, p.sourceY, p.sourceWidth, p.sourceHeight, dx, dy);
  const to = edgePoint(p.destinationX, p.destinationY, p.destinationWidth, p.destinationHeight, -dx, -dy);
  return [from[0], from[1], to[0], to[1]];
}

/** The point on a box's edge in the direction (dx, dy) from its centre. */
function edgePoint(cx: number, cy: number, width: number, height: number, dx: number, dy: number): [number, number] {
  if (dx === 0 && dy === 0) {
    return [cx, cy];
  }

  const halfWidth = width / 2;
  const halfHeight = height / 2;
  // Scale the direction until it touches whichever edge it reaches first.
  const scale = Math.min(
    dx === 0 ? Number.POSITIVE_INFINITY : halfWidth / Math.abs(dx),
    dy === 0 ? Number.POSITIVE_INFINITY : halfHeight / Math.abs(dy),
  );
  return [cx + dx * scale, cy + dy * scale];
}

/**
 * What the svg actually puts on screen, in canvas units - which is not the viewBox. With the
 * default `preserveAspectRatio` the browser fits the box inside the element and centres it, so
 * the axis with room to spare shows more of the model than the box asked for. Reporting the
 * bare viewBox would have the backend cull elements the user is looking straight at.
 */
export function shownRectOf(box: ViewBox, surface: SVGSVGElement | null) {
  const rect = surface?.getBoundingClientRect();
  if (rect === undefined || rect.width <= 0 || rect.height <= 0 || box.w <= 0 || box.h <= 0) {
    return { minX: box.x, minY: box.y, maxX: box.x + box.w, maxY: box.y + box.h };
  }

  const scale = Math.min(rect.width / box.w, rect.height / box.h);
  const shownWidth = rect.width / scale;
  const shownHeight = rect.height / scale;
  const centerX = box.x + box.w / 2;
  const centerY = box.y + box.h / 2;
  return {
    minX: centerX - shownWidth / 2,
    minY: centerY - shownHeight / 2,
    maxX: centerX + shownWidth / 2,
    maxY: centerY + shownHeight / 2,
  };
}

/** The box that fits everything with a margin - what the canvas opens with and Fit to View returns to. */
function fitBoxOf(model: C4Model): ViewBox {
  const boxes = boxesOf(model);
  if (boxes.length === 0) {
    return { x: -200, y: -150, w: 400, h: 300 };
  }

  const margin = 40;
  const minX = Math.min(...boxes.map((box) => box.x)) - margin;
  const minY = Math.min(...boxes.map((box) => box.y)) - margin;
  const maxX = Math.max(...boxes.map((box) => box.x + box.width)) + margin;
  const maxY = Math.max(...boxes.map((box) => box.y + box.height)) + margin;
  return { x: minX, y: minY, w: maxX - minX, h: maxY - minY };
}

/** The nested `file -> element` selection a canvas click reports. */
function nodeSelection(entryId: Uint8Array, path: readonly string[], elementId: string): ContextSelection {
  const child = create(ContextSelectionSchema, {
    source: 2, // DIAGRAM_CANVAS
    id: { source: { case: "elementId", value: { value: elementId } } },
    // Empty asks the backend to fill in the full path; sending a partial one is refused.
    path: { segments: [] },
    detail: { case: "none", value: create(EmptySchema) },
  });

  return create(ContextSelectionSchema, {
    source: 1,
    id: { source: { case: "entryId", value: { value: entryId } } },
    path: { segments: [...path] },
    detail: { case: "child", value: child },
  });
}

export type { C4Model };
