import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { ContextSelectionSchema } from "@client/generated/context_pb";
import type { ContextSelection } from "@client/generated/context_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramView, type DiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { PipelineElementKindProto } from "@client/generated/azure-pipeline_pb";
import {
  boxesOf,
  endpointsOf,
  jobCountLabel,
  JOB_TYPE,
  STAGE_TYPE,
  TEMPLATE_TYPE,
  type PipelineEdgeLine,
  type PipelineModel,
  type PipelineNode,
} from "./pipelineModel";
import { usePipelineStream } from "./usePipelineStream";

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
const FIT_MARGIN = 40;

export interface PipelineCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * Renders one Azure pipeline: its stages left to right, the jobs inside whichever stages are
 * open, and the arrows saying what waits for what.
 *
 * Everything drawn here was decided by the backend - where each box goes, how big it is, whether
 * an edge is implicit or broken. The canvas is a renderer rather than a second opinion about what
 * a pipeline is, which is the same division the C4 and mindmap canvases keep.
 *
 * What this deliberately does not draw is any particular *run*: which stage passed, which failed,
 * how long it took. That needs a live Azure DevOps connection and credentials ADP does not have,
 * and Requirement 8.8 excludes it rather than half-building it.
 */
export function PipelineCanvas({ projectId, entryId, path }: PipelineCanvasProps) {
  const { model, loading, failed, reportView } = usePipelineStream(projectId, path);
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

  // Attached by hand as non-passive: React's synthetic wheel listener cannot preventDefault, and
  // without that every zoom also scrolls the page.
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
      () => reportViewRef.current(shownRectOf(viewRef.current)),
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

  const onNodeClick = (node: PipelineNode) => {
    setFocusedId(node.id);
    surfaceRef.current?.focus();
    select(nodeSelection(entryId, path, node.id));
  };

  if (failed) {
    return (
      <div className="pipeline-canvas" data-testid="pipeline-canvas">
        <div className="pipeline-canvas-unavailable" role="alert">
          This pipeline is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  const nodes = [...model.nodes.values()];
  const edges = [...model.edges.values()];
  // Stages first, so the jobs inside them draw on top rather than behind.
  const stages = nodes.filter((node) => node.type === STAGE_TYPE);
  const others = nodes.filter((node) => node.type !== STAGE_TYPE);

  return (
    <div className="pipeline-canvas" data-testid="pipeline-canvas">
      {loading ? (
        <div className="pipeline-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <svg
          ref={surfaceRef}
          className="pipeline-canvas-surface"
          viewBox={`${effectiveView.x} ${effectiveView.y} ${effectiveView.w} ${effectiveView.h}`}
          tabIndex={0}
          role="img"
          aria-label={`Pipeline ${path.join("/")}`}
          onClick={onBackgroundClick}
          onMouseDown={onSurfacePointerDown}
          onMouseMove={onSurfacePointerMove}
          onMouseUp={onSurfacePointerUp}
          onMouseLeave={onSurfacePointerUp}
        >
          <defs>
            <marker
              id="pipeline-arrow"
              viewBox="0 0 10 10"
              refX="9"
              refY="5"
              markerWidth="6"
              markerHeight="6"
              orient="auto-start-reverse"
            >
              <path d="M 0 0 L 10 5 L 0 10 z" className="pipeline-arrowhead" />
            </marker>
          </defs>

          {stages.map((stage) => (
            <PipelineStageShape
              key={stage.id}
              stage={stage}
              expanded={!model.collapsed.has(stage.id)}
              focused={stage.id === focusedId}
              onSelect={() => onNodeClick(stage)}
            />
          ))}
          {edges.map((edge) => (
            <PipelineEdgeShape key={edge.id} edge={edge} model={model} />
          ))}
          {others.map((node) => (
            <PipelineBoxShape
              key={node.id}
              node={node}
              focused={node.id === focusedId}
              onSelect={() => onNodeClick(node)}
            />
          ))}
        </svg>
      )}
    </div>
  );
}

/**
 * A stage: a container holding its jobs when open, a single box with its job count when closed
 * (Requirement 8.2). A manual-trigger stage is marked, because "this one waits for a person" is
 * not something a reader should have to open the file to discover (Requirement 8.3).
 */
function PipelineStageShape({
  stage,
  expanded,
  focused,
  onSelect,
}: {
  stage: PipelineNode;
  expanded: boolean;
  focused: boolean;
  onSelect: () => void;
}) {
  const { displayName, width, height, jobCount, indeterminate, fromTemplate } = stage.payload;
  const classes = [
    "pipeline-stage",
    expanded ? "pipeline-stage-expanded" : "pipeline-stage-collapsed",
    focused ? "pipeline-focused" : "",
    indeterminate ? "pipeline-indeterminate" : "",
    fromTemplate ? "pipeline-from-template" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <g
      className={classes}
      transform={`translate(${stage.x} ${stage.y})`}
      onClick={onSelect}
      role="button"
      aria-label={displayName}
      data-testid={`stage-${stage.id}`}
      data-expanded={expanded}
    >
      <rect className="pipeline-stage-box" x={0} y={0} width={width} height={height} rx={6} />
      <text className="pipeline-stage-name" x={12} y={24}>
        {displayName}
      </text>
      {!expanded && (
        <text className="pipeline-stage-count" x={12} y={44}>
          {jobCountLabel(jobCount)}
        </text>
      )}
    </g>
  );
}

/** A job, a step or an unfollowed template: a plain box, drawn by what kind it says it is. */
function PipelineBoxShape({
  node,
  focused,
  onSelect,
}: {
  node: PipelineNode;
  focused: boolean;
  onSelect: () => void;
}) {
  const { displayName, width, height, kind, indeterminate, fromTemplate, unresolvedReason } = node.payload;
  const deployment = kind === PipelineElementKindProto.PIPELINE_ELEMENT_KIND_DEPLOYMENT_JOB;
  const classes = [
    node.type === JOB_TYPE ? "pipeline-job" : node.type === TEMPLATE_TYPE ? "pipeline-template" : "pipeline-step",
    deployment ? "pipeline-deployment" : "",
    focused ? "pipeline-focused" : "",
    indeterminate ? "pipeline-indeterminate" : "",
    fromTemplate ? "pipeline-from-template" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <g
      className={classes}
      transform={`translate(${node.x} ${node.y})`}
      onClick={onSelect}
      role="button"
      aria-label={displayName}
      data-testid={`node-${node.id}`}
    >
      <rect className="pipeline-box" x={0} y={0} width={width} height={height} rx={4} />
      <text className="pipeline-box-name" x={10} y={height / 2 + 4}>
        {displayName}
      </text>
      {unresolvedReason.length > 0 && <title>{unresolvedReason}</title>}
    </g>
  );
}

/**
 * One "waits for" arrow, drawn between the edges of the two boxes it joins.
 *
 * An edge whose ends are not both on the canvas is not drawn: a job's dependency inside a
 * collapsed stage has nowhere to start, and a line into empty space says less than no line.
 */
function PipelineEdgeShape({ edge, model }: { edge: PipelineEdgeLine; model: PipelineModel }) {
  const ends = endpointsOf(model, edge);
  if (ends === null) {
    return null;
  }

  const from = {
    x: ends.from.x + ends.from.payload.width,
    y: ends.from.y + ends.from.payload.height / 2,
  };
  const to = { x: ends.to.x, y: ends.to.y + ends.to.payload.height / 2 };
  const classes = [
    "pipeline-edge",
    edge.payload.implicitDependency ? "pipeline-edge-implicit" : "",
    edge.payload.broken ? "pipeline-edge-broken" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <path
      className={classes}
      data-testid={`edge-${edge.id}`}
      d={`M ${from.x} ${from.y} C ${from.x + 30} ${from.y}, ${to.x - 30} ${to.y}, ${to.x} ${to.y}`}
      markerEnd="url(#pipeline-arrow)"
      fill="none"
    />
  );
}

/** The whole pipeline with a margin, which is what fit-to-view starts from. */
function fitBoxOf(model: PipelineModel): ViewBox {
  const boxes = boxesOf(model);
  if (boxes.length === 0) {
    return { x: 0, y: 0, w: 800, h: 600 };
  }

  const minX = Math.min(...boxes.map((box) => box.x));
  const minY = Math.min(...boxes.map((box) => box.y));
  const maxX = Math.max(...boxes.map((box) => box.x + box.width));
  const maxY = Math.max(...boxes.map((box) => box.y + box.height));

  return {
    x: minX - FIT_MARGIN,
    y: minY - FIT_MARGIN,
    w: Math.max(maxX - minX + 2 * FIT_MARGIN, MIN_VIEW_WIDTH),
    h: Math.max(maxY - minY + 2 * FIT_MARGIN, MIN_VIEW_WIDTH),
  };
}

/** The viewBox as the rectangle the backend understands. */
function shownRectOf(box: ViewBox) {
  return { minX: box.x, minY: box.y, maxX: box.x + box.w, maxY: box.y + box.h };
}

/**
 * The selection a click on an element publishes, so the rest of the shell follows along.
 *
 * Nested: the outer level is the diagram entry, the inner one the element inside it. That is the
 * shape the context mechanism expects, and it is what lets the property grid ask about the stage
 * while the explorer still knows which file is open.
 */
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
