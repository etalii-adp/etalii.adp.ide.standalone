import { useCallback, useMemo, useRef, useState } from "react";
import { arcBetween, normalAlong, pointAlong } from "./causalLoopArc";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { usePointerGesture } from "@client/canvas/gesture/usePointerGesture";
import { useViewReport } from "@client/diagrams/useViewReport";
import { shownRectOf, type ViewBox } from "@client/diagrams/viewReport";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
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

/**
 * What a press lands on. Threaded through the shared arbiter untouched: the arbiter decides
 * click-or-drag at the gesture's end, and this type is how its verdict comes back knowing
 * what the gesture was about.
 */
type CausalLoopPressTarget =
  | { kind: "variable"; variable: CausalLoopVariable }
  | { kind: "link"; id: string }
  | { kind: "loop"; id: string };

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
  const { select, executeAction } = useContextConnection();
  const { selection, actions } = useContextSelection();

  const [view, setView] = useState<ViewBox | null>(null);
  const [rejection, setRejection] = useState<string | null>(null);
  const [drag, setDrag] = useState<{ id: string; x: number; y: number } | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const panRef = useRef<{ x: number; y: number; view: ViewBox } | null>(null);

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

  // A press selects - variable, link and loop badge alike - decided by the shared arbiter at
  // the gesture's end, never by the trailing click: the click after a drop lands on whatever
  // the geometry left under the pointer (the dragged variable itself, or the loop badge at
  // the loop's centre), and a raw handler there changed a selection nobody asked to change.
  const gesture = usePointerGesture<CausalLoopPressTarget>({
    onPress: (target) => onSelect(target.kind === "variable" ? target.variable.id : target.id),
    onDragMove: (target, dx, dy) => {
      if (target.kind !== "variable") {
        return; // a link or a loop badge has no position of its own; dragging one moves nothing
      }

      const scale = unitsPerPixel();
      setDrag({ id: target.variable.id, x: target.variable.x + dx * scale, y: target.variable.y + dy * scale });
    },
    onDragEnd: (target, dx, dy) => {
      if (target.kind !== "variable") {
        return;
      }

      setDrag(null);
      const scale = unitsPerPixel();
      void (async () => {
        const error = await moveElementTo(target.variable.id, target.variable.x + dx * scale, target.variable.y + dy * scale);
        if (error) {
          setRejection(error);
        }
      })();
    },
    onDragAbandon: (target) => {
      if (target.kind === "variable") {
        setDrag(null);
      }
    },
  });

  /**
   * A right-click's menu, opened once the pushed selection for that element arrives with its
   * actions - the same discipline every other canvas follows, so the menu shows the backend's
   * answer rather than a guess.
   *
   * This canvas shipped without it, which is worth a sentence because the failure was silent:
   * the backend offered eleven actions, the provider had sixteen tests proving it offered them,
   * and not one of them was reachable, because nothing here ever asked. A provider test cannot
   * see that; only opening the diagram can.
   */
  const selectionKey = innermostKey(selection);
  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  /**
   * The menu for the diagram itself, opened on empty canvas.
   *
   * With nothing selected the diagram is the subject, which is where the diagram-wide actions
   * live - Arrange diagram among them. The point is carried as a placement id so the backend can
   * put a new variable where the user actually right-clicked.
   */
  const onSurfaceContextMenu = useCallback(
    (event: React.MouseEvent) => {
      if (event.target !== svgRef.current && (event.target as Element).closest("[data-element-id]") !== null) {
        return;
      }

      const rect = svgRef.current?.getBoundingClientRect();
      const scale = effectiveRef.current.w / Math.max(rect?.width ?? 1, 1);
      const x = effectiveRef.current.x + (event.clientX - (rect?.left ?? 0)) * scale;
      const y = effectiveRef.current.y + (event.clientY - (rect?.top ?? 0)) * scale;

      openMenuAt(event, placementIdOf(x, y));
    },
    [openMenuAt],
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

  const onMouseMove = (event: React.MouseEvent) => {
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
        onContextMenu={onSurfaceContextMenu}
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

            // One control point, bowed perpendicular to the chord and always to the same side
            // of travel - which is what makes A -> B and B -> A draw an ellipse between them
            // rather than two lines on top of each other. See causalLoopArc.ts.
            const arc = arcBetween(boxOf(ends.from), boxOf(ends.to));
            const mark = polarityMark(link.payload.polarity);

            // The polarity sits just short of the arrowhead and off to one side, so it is beside
            // the line it describes rather than under the arrow or on top of the target.
            const markAt = pointAlong(arc, 0.86);
            const markNormal = normalAlong(arc, 0.86);

            return (
              <g
                key={link.id}
                className={`canvas-connection causal-loop-link causal-loop-weight-${weightStep(link)}${
                  selectedId === link.id ? " canvas-selected" : ""
                }`}
                data-element-id={link.id}
                {...gesture.press({ kind: "link", id: link.id })}
                onContextMenu={(event) => openMenuAt(event, link.id)}
              >
                <path
                  className="canvas-connection-line"
                  d={arc.path}
                  markerEnd="url(#causal-loop-arrow)"
                />
                <path className="canvas-connection-hit" d={arc.path} />
                {link.payload.delayed && (
                  // The conventional delay mark: two short strokes ACROSS the link. Across means
                  // along the curve's own normal - drawn vertically they would lie along a
                  // near-vertical arc rather than crossing it, and read as nothing at all.
                  <g className="causal-loop-delay">
                    <line
                      x1={pointAlong(arc, 0.44).x - arc.apexNormal.x * 8}
                      y1={pointAlong(arc, 0.44).y - arc.apexNormal.y * 8}
                      x2={pointAlong(arc, 0.44).x + arc.apexNormal.x * 8}
                      y2={pointAlong(arc, 0.44).y + arc.apexNormal.y * 8}
                    />
                    <line
                      x1={pointAlong(arc, 0.56).x - arc.apexNormal.x * 8}
                      y1={pointAlong(arc, 0.56).y - arc.apexNormal.y * 8}
                      x2={pointAlong(arc, 0.56).x + arc.apexNormal.x * 8}
                      y2={pointAlong(arc, 0.56).y + arc.apexNormal.y * 8}
                    />
                  </g>
                )}
                {mark !== "" && (
                  <text
                    className="causal-loop-polarity"
                    x={markAt.x + markNormal.x * 11}
                    y={markAt.y + markNormal.y * 11}
                    textAnchor="middle"
                  >
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
              {...gesture.press({ kind: "loop", id: loop.id })}
              onContextMenu={(event) => openMenuAt(event, loop.id)}
            >
              {/*
                The conventional loop marker: a curved arrow encircling the identifier, drawn at
                the centre of the variables the loop runs through. The notation draws this because
                the identifier alone says a loop exists while the marker shows one - which is the
                whole complaint the arcs above also answer.

                The sweep follows the polarity: a reinforcing loop is drawn clockwise and a
                balancing one anticlockwise, which is how the reference tools distinguish them at
                a glance before anyone reads the letter.
              */}
              <path
                className="causal-loop-marker"
                d={loopMarkerPath(loop.x, loop.y - 15, 13, loop.payload.computed === LoopPolarityProto.REINFORCING)}
                markerEnd="url(#causal-loop-arrow)"
              />
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
                {...gesture.press({ kind: "variable", variable })}
                onDoubleClick={() => onSelect(variable.id, ContextSelectionAction.ACTIVATE)}
                onContextMenu={(event: React.MouseEvent) => openMenuAt(event, variable.id)}
              />
            );
          })}
        </g>
      </svg>

      <CanvasScrollbars
        {...scrollAxesOf(effective, variables)}
        onPan={(x, y) => setView({ ...effective, x, y })}
      />

      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          closeMenu();
          void executeAction(action.id);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={closeMenu}
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

/**
 * A near-complete circle with a gap for its arrowhead - the loop marker the notation draws at the
 * centre of a feedback loop.
 *
 * Two arcs rather than one, because a single SVG elliptical arc cannot exceed a half turn without
 * the large-arc flag, and using it would leave the arrowhead pointing the wrong way at the seam.
 */
function loopMarkerPath(centreX: number, centreY: number, radius: number, clockwise: boolean): string {
  const sweep = clockwise ? 1 : 0;
  const start = -Math.PI / 2;
  const end = start + (clockwise ? 1 : -1) * Math.PI * 1.7;
  const middle = (start + end) / 2;

  const at = (angle: number) => `${(centreX + radius * Math.cos(angle)).toFixed(2)} ${(centreY + radius * Math.sin(angle)).toFixed(2)}`;

  return `M ${at(start)} A ${radius} ${radius} 0 0 ${sweep} ${at(middle)} A ${radius} ${radius} 0 0 ${sweep} ${at(end)}`;
}

/**
 * The element id the backend reads as "the user asked for something here rather than on
 * something" - CausalLoopSelection.PlacementFor's counterpart on this side of the wire.
 */
function placementIdOf(x: number, y: number): string {
  return `new:${x},${y}`;
}
