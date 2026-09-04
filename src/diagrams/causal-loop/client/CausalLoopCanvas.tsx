import { useCallback, useMemo, useRef, useState } from "react";
import { facingAnchorsBetween, forwardBezierPath, midpointOf } from "@client/canvas/connectors";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { useViewReport } from "@client/diagrams/useViewReport";
import { shownRectOf, type ViewBox } from "@client/diagrams/viewReport";
import { useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { ContextSelectionAction } from "@client/generated/context_pb";
import {
  LoopPolarityProto,
  anchorsOf,
  loopCaption,
  polarityMark,
  weightStep,
  type CausalLoopLink,
  type CausalLoopVariable,
} from "./causalLoopModel";
import { useCausalLoopStream } from "./useCausalLoopStream";

const ZOOM_STEP = 1.25;
const MIN_VIEW_WIDTH = 40;
const MAX_VIEW_WIDTH = 100000;
const PADDING = 60;
const DRAG_THRESHOLD_PX = 3;

export interface CausalLoopCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * Draws a causal loop diagram in the notation the world uses: variables joined by polarised
 * arrows, delays marked with strokes across the link, and each loop carrying its R or B
 * identifier among the variables it runs through.
 *
 * Everything visual here composes the shared canvas: `canvas-*` classes, the shared connectors,
 * the shared scrollbars. What this module's own stylesheet adds is the handful of things the
 * notation needs and the shared appearance has no opinion about - the polarity mark, the delay
 * strokes, and the loop badge.
 */
export function CausalLoopCanvas({ projectId, entryId, path }: CausalLoopCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useCausalLoopStream(projectId, path);

  // The palette is empty until group 3 registers a toolbox provider; registering the backend's
  // answer makes the panel say exactly that rather than showing nothing without explanation.
  useRegisterDiagramToolbox(useToolboxItems(projectId, path));
  const { select } = useContextConnection();
  const { selection } = useContextSelection();

  const [view, setView] = useState<ViewBox | null>(null);
  const [rejection, setRejection] = useState<string | null>(null);
  const [drag, setDrag] = useState<{ id: string; x: number; y: number } | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const panRef = useRef<{ x: number; y: number; view: ViewBox } | null>(null);
  const dragRef = useRef<{ id: string; x: number; y: number; clientX: number; clientY: number; moved: boolean } | null>(null);

  const variables = useMemo(() => [...model.variables.values()], [model.variables]);
  const links = useMemo(() => [...model.links.values()], [model.links]);
  const loops = useMemo(() => [...model.loops.values()], [model.loops]);
  const bounds = useMemo(() => boundsOf(variables), [variables]);
  const effective = view ?? bounds;

  // The report fires on a timer, so it reads the view from a ref rather than closing over the
  // value one render happened to see.
  const effectiveRef = useRef(effective);
  effectiveRef.current = effective;

  useViewReport({
    view: effective,
    report: reportView,
    convert: () => shownRectOf(effectiveRef.current, svgRef.current),
    ready: !loading && !failed,
  });

  const selectedId = selectedElementIdOf(selection);

  const onSelect = useCallback(
    (id: string, gesture?: ContextSelectionAction) => select(elementSelectionOf(entryId, path, id, gesture)),
    [entryId, path, select],
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
    panRef.current = { x: event.clientX, y: event.clientY, view: effective };
  };

  const onVariableMouseDown = (variable: CausalLoopVariable, event: React.MouseEvent) => {
    if (event.button !== 0) {
      return;
    }
    event.stopPropagation();
    dragRef.current = {
      id: variable.id,
      x: variable.x,
      y: variable.y,
      clientX: event.clientX,
      clientY: event.clientY,
      moved: false,
    };
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
      void (async () => {
        const error = await moveElementTo(landed.id, landed.x, landed.y);
        if (error) {
          setRejection(error);
        }
      })();
    }
  };

  if (failed) {
    return <div className="causal-loop-message">This causal loop diagram could not be opened.</div>;
  }

  if (loading) {
    return <div className="causal-loop-message">Reading the diagram…</div>;
  }

  if (variables.length === 0) {
    return <div className="causal-loop-message">This causal loop diagram states no variables yet.</div>;
  }

  /** Where a variable is drawn now - its dragged position while a drag is in flight. */
  const placed = (variable: CausalLoopVariable) =>
    drag?.id === variable.id ? { ...variable, x: drag.x, y: drag.y } : variable;

  const boxOf = (variable: CausalLoopVariable) => {
    const at = placed(variable);
    return {
      x: at.x - variable.payload.width / 2,
      y: at.y - variable.payload.height / 2,
      width: variable.payload.width,
      height: variable.payload.height,
    };
  };

  return (
    <div className="causal-loop-frame">
      {rejection !== null && (
        <div className="canvas-rejection" role="status" onClick={() => setRejection(null)}>
          {rejection}
        </div>
      )}
      <svg
        ref={svgRef}
        className="canvas-host causal-loop-canvas"
        viewBox={`${effective.x} ${effective.y} ${effective.w} ${effective.h}`}
        onWheel={onWheel}
        onMouseDown={onSurfaceMouseDown}
        onMouseMove={onMouseMove}
        onMouseUp={onMouseUp}
        onMouseLeave={onMouseUp}
      >
        <defs>
          <marker
            id="causal-loop-arrow"
            viewBox="0 0 10 10"
            refX="9"
            refY="5"
            markerWidth="6"
            markerHeight="6"
            orient="auto-start-reverse"
          >
            <path className="canvas-arrowhead" d="M 0 0 L 10 5 L 0 10 z" />
          </marker>
        </defs>

        <g className="canvas-drawing">
          {links.map((link) => {
            const ends = anchorsOf(model, link);
            if (ends === null) {
              // The far end is not held - off screen, or never declared. Drawing a line to
              // nothing would be worse than drawing no line.
              return null;
            }

            const [start, finish] = facingAnchorsBetween(boxOf(ends.from), boxOf(ends.to));
            const mid = midpointOf(start, finish);
            const mark = polarityMark(link.payload.polarity);

            return (
              <g
                key={link.id}
                className={`canvas-connection causal-loop-link causal-loop-weight-${weightStep(link)}${
                  selectedId === link.id ? " canvas-selected" : ""
                }`}
                data-element-id={link.id}
                onClick={() => onSelect(link.id)}
              >
                <path
                  className="canvas-connection-line"
                  d={forwardBezierPath(start, finish)}
                  markerEnd="url(#causal-loop-arrow)"
                />
                <path className="canvas-connection-hit" d={forwardBezierPath(start, finish)} />
                {link.payload.delayed && (
                  // The conventional delay mark: two short strokes across the link.
                  <g className="causal-loop-delay">
                    <line x1={mid.x - 5} y1={mid.y - 8} x2={mid.x + 1} y2={mid.y + 8} />
                    <line x1={mid.x + 3} y1={mid.y - 8} x2={mid.x + 9} y2={mid.y + 8} />
                  </g>
                )}
                {mark !== "" && (
                  <text className="causal-loop-polarity" x={finish.x} y={finish.y - 8}>
                    {mark}
                  </text>
                )}
              </g>
            );
          })}

          {loops.map((loop) => (
            <g
              key={loop.id}
              className={`causal-loop-loop${loop.payload.disagrees ? " causal-loop-disagrees" : ""}${
                selectedId === loop.id ? " canvas-selected" : ""
              }`}
              data-element-id={loop.id}
              onClick={() => onSelect(loop.id)}
            >
              <text
                className={`causal-loop-badge causal-loop-${polarityWord(loop.payload.computed)}`}
                x={loop.x}
                y={loop.y}
                textAnchor="middle"
              >
                {loopCaption(loop)}
              </text>
            </g>
          ))}

          {variables.map((variable) => {
            const box = boxOf(variable);
            return (
              <BoxElement
                key={variable.id}
                className={`canvas-element causal-loop-variable${
                  selectedId === variable.id ? " canvas-selected" : ""
                }`}
                data-element-id={variable.id}
                x={box.x}
                y={box.y}
                width={box.width}
                height={box.height}
                rx={box.height / 2}
                label={variable.payload.display}
                boxClassName="canvas-node"
                labelClassName="canvas-node-label"
                labelX={box.width / 2}
                onMouseDown={(event: React.MouseEvent) => onVariableMouseDown(variable, event)}
                onClick={() => onSelect(variable.id)}
                onDoubleClick={() => onSelect(variable.id, ContextSelectionAction.ACTIVATE)}
              />
            );
          })}
        </g>
      </svg>

      <CanvasScrollbars
        {...scrollAxesOf(effective, variables)}
        onPan={(x, y) => setView({ ...effective, x, y })}
      />
    </div>
  );
}

function polarityWord(computed: LoopPolarityProto): string {
  switch (computed) {
    case LoopPolarityProto.REINFORCING:
      return "reinforcing";
    case LoopPolarityProto.BALANCING:
      return "balancing";
    default:
      return "undecidable";
  }
}

function clamp(value: number, low: number, high: number): number {
  return Math.min(Math.max(value, low), high);
}

/**
 * A ViewBox canvas needs no DOM measurement: the view's span in content units IS `w` and `h`.
 */
function scrollAxesOf(view: ViewBox, variables: readonly CausalLoopVariable[]) {
  const half = (variable: CausalLoopVariable) => ({
    left: variable.x - variable.payload.width / 2,
    right: variable.x + variable.payload.width / 2,
    top: variable.y - variable.payload.height / 2,
    bottom: variable.y + variable.payload.height / 2,
  });

  const spans = variables.map(half);
  const widest = spans.length > 0 ? Math.max(...variables.map((v) => v.payload.width)) : view.w;
  const tallest = spans.length > 0 ? Math.max(...variables.map((v) => v.payload.height)) : view.h;
  const minX = spans.length > 0 ? Math.min(...spans.map((s) => s.left)) : view.x;
  const maxX = spans.length > 0 ? Math.max(...spans.map((s) => s.right)) : view.x + view.w;
  const minY = spans.length > 0 ? Math.min(...spans.map((s) => s.top)) : view.y;
  const maxY = spans.length > 0 ? Math.max(...spans.map((s) => s.bottom)) : view.y + view.h;

  return {
    horizontal: {
      viewStart: view.x,
      viewSpan: view.w,
      ...scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: Math.max(widest, PADDING) }),
    },
    vertical: {
      viewStart: view.y,
      viewSpan: view.h,
      ...scrollExtentOf(minY, maxY, { factor: 0.5, minimumSpan: Math.max(tallest, PADDING) }),
    },
  };
}

function boundsOf(variables: readonly CausalLoopVariable[]): ViewBox {
  if (variables.length === 0) {
    return { x: 0, y: 0, w: 400, h: 300 };
  }

  const minX = Math.min(...variables.map((v) => v.x - v.payload.width / 2)) - PADDING;
  const minY = Math.min(...variables.map((v) => v.y - v.payload.height / 2)) - PADDING;
  const maxX = Math.max(...variables.map((v) => v.x + v.payload.width / 2)) + PADDING;
  const maxY = Math.max(...variables.map((v) => v.y + v.payload.height / 2)) + PADDING;

  return { x: minX, y: minY, w: Math.max(maxX - minX, 1), h: Math.max(maxY - minY, 1) };
}

/** Unused in this file, but the link type is part of the drawn vocabulary. */
export type { CausalLoopLink };
