import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { straightPath, type ConnectorBox } from "@client/canvas/connectors";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import { StraightConnection } from "@client/canvas/connections/straight/StraightConnection";
import { SymbolElement } from "@client/canvas/elements/symbol/SymbolElement";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import {
  WardleyAttitudeKind,
  WardleyDecorator,
  WardleyElementKind,
} from "@client/generated/wardley-map_pb";
import type { WardleyAxis, WardleyElement, WardleyModel } from "./wardleyModel";
import { useWardleyStream } from "./useWardleyStream";
import { useViewReport } from "@client/diagrams/useViewReport";
import { shownRectOf, type Viewport } from "@client/diagrams/viewReport";

/**
 * The map's own space is 0..1 on both axes. It is drawn into a fixed box of canvas units so
 * labels and stroke widths have a sensible scale to be expressed in; the viewBox then pans and
 * zooms over that.
 */
const SPACE = 1000;

/** Room outside the space for the axis labels, which sit beyond the plotted area. */
const MARGIN = 90;

const ZOOM_STEP = 1.25;
const MIN_VIEW_WIDTH = 120;
const MAX_VIEW_WIDTH = 12000;

/** The visible rectangle, in canvas units - the svg viewBox as data. */
interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

const fullView: ViewBox = { x: -MARGIN, y: -MARGIN, w: SPACE + MARGIN * 2, h: SPACE + MARGIN * 2 };

export interface WardleyCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * A 0..1 map coordinate as canvas units.
 *
 * Rounded to two decimals, which at a 1000-unit space is far finer than a pixel. Without it,
 * ordinary arithmetic on the author's numbers puts values like `350.00000000000006` into the
 * DOM - noise in every attribute, and a diff nobody can read when a snapshot is compared.
 */
function scale(value: number): number {
  return Math.round(value * SPACE * 100) / 100;
}

/**
 * Renders one Wardley map.
 *
 * **The axes are drawn by this module, and core was asked for nothing to allow it.** A module
 * claims its MIME type through `DiagramCanvasRegistration` and supplies a component that owns
 * its whole `<svg>` - its viewBox, its pan and zoom, its layer order - so the axis chrome is
 * simply what this draws first, beneath its elements (Requirement 8.4).
 *
 * Every position it draws came from the document. There is no layout here and no layout
 * anywhere in this module: a component sits where its author put it, and that is the whole
 * claim a Wardley map makes (Requirement 7.1).
 */
// `entryId` is part of every canvas's props and is not destructured yet: it names the `.adp`
// entry a selection reports as its outer level, which task 19's context resolver needs and
// nothing here does.
export function WardleyCanvas({ projectId, path }: WardleyCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useWardleyStream(projectId, path);

  // The palette the Toolbox panel shows while this map is open - described by the backend
  // (Requirement 13), registered here and withdrawn on unmount.
  useRegisterDiagramToolbox(useToolboxItems(projectId, path));
  const [view, setView] = useState<ViewBox>(fullView);
  // Read by the debounced report when it fires rather than when it was scheduled, so the
  // rectangle sent is where the reader's view came to rest.
  const viewRef = useRef(view);
  viewRef.current = view;
  const surfaceRef = useRef<SVGSVGElement | null>(null);
  const panRef = useRef<{ clientX: number; clientY: number; view: ViewBox } | null>(null);

  // A drag in flight: which element, where the pointer started, where the element started, and
  // whether it has moved far enough to be a drag rather than a wobbly click. A ref for what
  // nothing renders from; the live offset is state, because the shape follows the pointer.
  const dragRef = useRef<{ id: string; clientX: number; clientY: number; x: number; y: number } | null>(null);
  const [drag, setDrag] = useState<{ id: string; x: number; y: number } | null>(null);
  const [rejection, setRejection] = useState("");

  const zoomBy = useCallback((factor: number) => {
    setView((current) => {
      const w = Math.min(MAX_VIEW_WIDTH, Math.max(MIN_VIEW_WIDTH, current.w * factor));
      const h = (current.h / current.w) * w;
      // Zoom about the centre, so the thing being looked at stays where it is.
      return { x: current.x + (current.w - w) / 2, y: current.y + (current.h - h) / 2, w, h };
    });
  }, []);

  const fitToView = useCallback(() => setView(fullView), []);

  useRegisterDiagramView(
    useMemo(
      () => ({
        zoomIn: () => zoomBy(1 / ZOOM_STEP),
        zoomOut: () => zoomBy(ZOOM_STEP),
        fitToView,
      }),
      [zoomBy, fitToView],
    ),
  );

  // Attached by hand as non-passive: React's synthetic wheel listener cannot preventDefault,
  // and without that every zoom also scrolls the page.
  useEffect(() => {
    const surface = surfaceRef.current;
    if (!surface) {
      return;
    }

    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      zoomBy(event.deltaY > 0 ? ZOOM_STEP : 1 / ZOOM_STEP);
    };

    surface.addEventListener("wheel", onWheel, { passive: false });
    return () => surface.removeEventListener("wheel", onWheel);
  }, [zoomBy]);

  /** Canvas units per screen pixel, for turning a pointer delta into a map delta. */
  const unitsPerPixel = useCallback(() => {
    const surface = surfaceRef.current;
    return surface ? view.w / surface.getBoundingClientRect().width : 1;
  }, [view.w]);

  const onPointerDown = (event: React.MouseEvent) => {
    panRef.current = { clientX: event.clientX, clientY: event.clientY, view };
  };

  const onElementPointerDown = (event: React.MouseEvent, element: WardleyElement) => {
    // The element takes the gesture; the surface must not also pan under it.
    event.stopPropagation();
    setRejection("");
    dragRef.current = {
      id: element.id,
      clientX: event.clientX,
      clientY: event.clientY,
      x: element.x,
      y: element.y,
    };
  };

  const onPointerMove = (event: React.MouseEvent) => {
    const dragging = dragRef.current;
    if (dragging) {
      const perPixel = unitsPerPixel() / SPACE;
      setDrag({
        id: dragging.id,
        // Clamped here as well as in the backend: the shape must not be draggable outside the
        // map while the pointer is still down, or the user is shown a position that cannot
        // exist (Requirement 7.3).
        x: clamp01(dragging.x + (event.clientX - dragging.clientX) * perPixel),
        y: clamp01(dragging.y + (event.clientY - dragging.clientY) * perPixel),
      });
      return;
    }

    const pan = panRef.current;
    const surface = surfaceRef.current;
    if (!pan || !surface) {
      return;
    }

    const rect = surface.getBoundingClientRect();
    const perPixel = pan.view.w / rect.width;
    setView({
      ...pan.view,
      x: pan.view.x - (event.clientX - pan.clientX) * perPixel,
      y: pan.view.y - (event.clientY - pan.clientY) * perPixel,
    });
  };

  const onPointerUp = () => {
    const dragging = dragRef.current;
    const landed = drag;
    dragRef.current = null;
    panRef.current = null;

    if (!dragging || !landed) {
      // A press with no movement is a click, not a drag, and must not write to the document.
      setDrag(null);
      return;
    }

    setDrag(null);
    void (async () => {
      // A drag is a DOCUMENT EDIT here, not a view change: moving a component right asserts
      // that it is more evolved. The backend converts the point back into the document's axes,
      // dispatches it as a command, and the new position returns as an ordinary delta
      // (Requirement 7.2).
      const error = await moveElementTo(landed.id, landed.x, landed.y);
      if (error) {
        // A refusal is shown rather than swallowed - read-only, or an element that has since
        // gone. The shape snaps back because the model never changed.
        setRejection(error);
      }
    })();
  };

  // What the reader can see, reported once the view settles, so the session delivers what falls
  // inside it rather than the whole map. Converted here and only here: the canvas draws the 0..1
  // map into a SPACE-unit box, the session compares against 0..1, and the shared library is not
  // told about either (view-delta-adoption Requirement 3.4).
  useViewReport({
    view,
    report: reportView,
    convert: () => inMapSpace(shownRectOf(viewRef.current, surfaceRef.current)),
    ready: !loading && !failed,
  });

  if (failed) {
    return (
      <div className="wardley-canvas wardley-canvas-message">
        <p>This map could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="wardley-canvas">
      <svg
        ref={surfaceRef}
        className="wardley-surface"
        viewBox={`${view.x} ${view.y} ${view.w} ${view.h}`}
        onMouseDown={onPointerDown}
        onMouseMove={onPointerMove}
        onMouseUp={onPointerUp}
        onMouseLeave={onPointerUp}
        role="img"
        aria-label={model.axis?.title ? `Wardley map: ${model.axis.title}` : "Wardley map"}
      >
        {/* Drawn first, so everything else sits on top of it (Requirement 8.4). */}
        <WardleyChrome axis={model.axis} scaleFactor={view.w / (SPACE + MARGIN * 2)} />
        {loading ? null : (
          <WardleyContents model={model} drag={drag} onElementPointerDown={onElementPointerDown} />
        )}
      </svg>
      <CanvasScrollbars
        {...scrollAxesOf(view)}
        className="wardley-scrollbars"
        onPan={(x, y) => setView({ ...view, x, y })}
      />
      {rejection ? <p className="wardley-rejection">{rejection}</p> : null}
    </div>
  );
}

/**
 * A rectangle of canvas units as the map's own 0..1 space - the units its elements are in, and so
 * the units a viewport report has to be in.
 *
 * The map's space is drawn at {@link SPACE} units to the unit interval, so this is a division and
 * nothing else. It deliberately does **not** clamp to 0..1: the view can and does extend into the
 * margin where the axis labels sit, and a report clamped to the plotted area would cull the
 * elements sitting closest to the edge the reader has just panned to.
 */
function inMapSpace(rect: Viewport): Viewport {
  return {
    minX: rect.minX / SPACE,
    minY: rect.minY / SPACE,
    maxX: rect.maxX / SPACE,
    maxY: rect.maxY / SPACE,
  };
}

/**
 * Where the view sits inside the map, as the two axes the shared scrollbars take.
 *
 * The one canvas here whose extent is NOT its content: a Wardley map's plane is the 0..1 space
 * itself, so a map carrying two components still has the whole space to show, and deriving the
 * extent from content would let a reader pan into emptiness while half the map went missing.
 * `fullView` already expresses that space, so the extent is simply it - passed through the
 * shared helper with no margin, because the map's own MARGIN is already part of it.
 *
 * This is a call-site decision and stays one: the component takes four plain numbers and does
 * not care where they came from, so nothing here justifies widening it or teaching the shared
 * geometry about map space.
 */
function scrollAxesOf(view: ViewBox) {
  return {
    horizontal: { viewStart: view.x, viewSpan: view.w, ...scrollExtentOf(fullView.x, fullView.x + fullView.w) },
    vertical: { viewStart: view.y, viewSpan: view.h, ...scrollExtentOf(fullView.y, fullView.y + fullView.h) },
  };
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value));
}

/**
 * The bands, the two axes and their labels.
 *
 * The stage boundaries come from `axis`, never from a constant here. They are not published in
 * the DSL - they are derived from the reference renderer's own offsets - so the backend holds
 * the one copy and this draws what it is told (Requirement 8.2).
 */
function WardleyChrome({ axis, scaleFactor }: { axis?: WardleyAxis; scaleFactor: number }) {
  // Stage labels hold a readable size as the map is zoomed; the boundaries they name do not,
  // because a component's position is only meaningful against its own axes (Requirement 8.5).
  const labelSize = 20 * Math.max(0.35, Math.min(2.5, scaleFactor));

  return (
    <g className="wardley-chrome" aria-hidden="true">
      {(axis?.stages ?? []).map((stage, index) => (
        <g key={stage.label}>
          <rect
            className={`wardley-band wardley-band-${index}`}
            x={scale(stage.start)}
            y={0}
            width={scale(stage.end - stage.start)}
            height={SPACE}
          />
          {index > 0 ? (
            <line
              className="wardley-band-edge"
              x1={scale(stage.start)}
              y1={0}
              x2={scale(stage.start)}
              y2={SPACE}
            />
          ) : null}
          <text
            className="wardley-band-label"
            x={scale((stage.start + stage.end) / 2)}
            y={SPACE + 34}
            fontSize={labelSize}
            textAnchor="middle"
          >
            {stage.label}
          </text>
        </g>
      ))}

      {/* The value chain: the user need at the top, invisible at the bottom. */}
      <line className="wardley-axis" x1={0} y1={0} x2={0} y2={SPACE} />
      {/* Evolution: genesis at the left, commodity at the right. */}
      <line className="wardley-axis" x1={0} y1={SPACE} x2={SPACE} y2={SPACE} />

      <text
        className="wardley-axis-label"
        transform={`translate(${-34} ${SPACE / 2}) rotate(-90)`}
        fontSize={labelSize}
        textAnchor="middle"
      >
        Value chain
      </text>
      <text
        className="wardley-axis-end"
        x={-14}
        y={12}
        fontSize={labelSize * 0.8}
        textAnchor="end"
      >
        Visible
      </text>
      <text
        className="wardley-axis-end"
        x={-14}
        y={SPACE}
        fontSize={labelSize * 0.8}
        textAnchor="end"
      >
        Invisible
      </text>
      <text
        className="wardley-axis-label"
        x={SPACE / 2}
        y={SPACE + 66}
        fontSize={labelSize}
        textAnchor="middle"
      >
        Evolution
      </text>
    </g>
  );
}

/** The radius a component is drawn at, and the box a connector anchors on. */
const DOT = 9;

/** A component as a box for the shared connector geometry, which works in centres and sizes. */
function boxOf(element: { x: number; y: number }): ConnectorBox {
  return { x: scale(element.x), y: scale(element.y), width: DOT * 2, height: DOT * 2 };
}

/**
 * Everything the author put on the map: the attitude regions behind, then the links, then the
 * elements and their annotations on top.
 *
 * The chrome is a separate component because it is the half that must be correct before
 * anything is plotted against it, and it is what an empty map shows on its own
 * (Requirement 1.4).
 */
function WardleyContents({
  model,
  drag,
  onElementPointerDown,
}: {
  model: WardleyModel;
  drag: { id: string; x: number; y: number } | null;
  onElementPointerDown: (event: React.MouseEvent, element: WardleyElement) => void;
}) {
  // The element being dragged is shown where the pointer is, so the gesture is visible before
  // the round trip answers. Everything else - the links that reach it, its evolve indicator -
  // follows from the same substituted position rather than lagging behind it.
  const at = (element: WardleyElement): WardleyElement =>
    drag && drag.id === element.id ? { ...element, x: drag.x, y: drag.y } : element;

  const elements = [...model.elements.values()].map(at);
  const byId = new Map(elements.map((element) => [element.id, element]));

  return (
    <g className="wardley-contents">
      {/* Behind the elements they cover (Requirement 6.4). */}
      {[...model.attitudes.values()].map((attitude) => (
        <g key={attitude.id}>
          <rect
            className={`wardley-attitude wardley-attitude-${attitudeName(attitude.kind)}`}
            x={scale(Math.min(attitude.x, attitude.opposite.x))}
            y={scale(Math.min(attitude.y, attitude.opposite.y))}
            width={scale(Math.abs(attitude.opposite.x - attitude.x))}
            height={scale(Math.abs(attitude.opposite.y - attitude.y))}
          />
          <text
            className="wardley-attitude-label"
            x={scale(Math.min(attitude.x, attitude.opposite.x)) + 8}
            y={scale(Math.min(attitude.y, attitude.opposite.y)) + 22}
          >
            {attitudeName(attitude.kind)}
          </text>
        </g>
      ))}

      {[...model.links.values()].map((link) => {
        const source = byId.get(link.sourceId);
        const target = byId.get(link.targetId);
        if (!source || !target) {
          // An endpoint that does not resolve is a dangling link. It is not drawn - there is
          // nowhere to draw it to - and the elements involved are marked instead
          // (Requirements 3.5, 14.2).
          return null;
        }

        // The shared connection: a Wardley link joins two boxes, and that the centres came
        // from the document rather than from a layout changes nothing about the maths.
        return (
          <StraightConnection
            key={link.id}
            from={boxOf(source)}
            to={boxOf(target)}
            pathClassName={`wardley-link${link.isFlow ? " wardley-link-flow" : ""}`}
            title={link.context || undefined}
          />
        );
      })}

      {/*
        An evolving component is shown at BOTH positions joined by a movement indicator, because
        the pair is the point of the statement (Requirement 6.1). The same connector call as a
        link, styled dashed - the target sits at the same visibility, so this is a horizontal
        move along the evolution axis.
      */}
      {elements
        .filter((element) => element.evolve)
        .map((element) => {
          const target = { x: element.evolve!.maturity, y: element.y };
          return (
            <g key={`${element.id}-evolve`}>
              <StraightConnection from={boxOf(element)} to={boxOf(target)} pathClassName="wardley-evolve" />
              <circle
                className="wardley-evolve-target"
                cx={scale(target.x)}
                cy={scale(target.y)}
                r={DOT}
              />
              {element.evolve!.overrideName ? (
                <text
                  className="wardley-element-label"
                  x={scale(target.x) + DOT + 6}
                  y={scale(target.y) + 4}
                >
                  {element.evolve!.overrideName}
                </text>
              ) : null}
            </g>
          );
        })}

      {elements.map((element) => (
        <WardleyElementShape
          key={element.id}
          element={element}
          dragging={drag?.id === element.id}
          onPointerDown={onElementPointerDown}
        />
      ))}

      {[...model.accelerators.values()].map((accelerator) => (
        <g key={accelerator.id} className="wardley-accelerator">
          <path
            className={accelerator.isDeaccelerator ? "wardley-accelerator-back" : "wardley-accelerator-forward"}
            d={straightPath(
              { x: scale(accelerator.x) - 16, y: scale(accelerator.y) },
              { x: scale(accelerator.x) + 16, y: scale(accelerator.y) },
            )}
          />
          <text className="wardley-element-label" x={scale(accelerator.x) + 22} y={scale(accelerator.y) + 4}>
            {accelerator.name}
          </text>
        </g>
      ))}

      {[...model.notes.values()].map((note) => (
        <text key={note.id} className="wardley-note" x={scale(note.x)} y={scale(note.y)}>
          {note.text}
        </text>
      ))}

      {/*
        Every occurrence, not just the first. The DSL permits one numbered annotation pinned in
        several places, and none of them may be lost (Requirement 6.8).
      */}
      {[...model.annotations.values()].flatMap((annotation) =>
        annotation.occurrences.map((occurrence, index) => (
          <g key={`${annotation.id}-${index}`} className="wardley-annotation">
            <circle cx={scale(occurrence.x)} cy={scale(occurrence.y)} r={11} />
            <text x={scale(occurrence.x)} y={scale(occurrence.y) + 4} textAnchor="middle">
              {annotation.number}
            </text>
            <title>{annotation.text}</title>
          </g>
        )),
      )}
    </g>
  );
}

/**
 * One component, anchor or submap: its shape says which kind it is, and its decorations say
 * what the author claimed about it - both without entering an edit mode (Requirement 6.9).
 */
function WardleyElementShape({
  element,
  dragging,
  onPointerDown,
}: {
  element: WardleyElement;
  dragging: boolean;
  onPointerDown: (event: React.MouseEvent, element: WardleyElement) => void;
}) {
  const x = scale(element.x);
  const y = scale(element.y);

  // The label offset is in PIXELS rather than map coordinates - a property of the format, which
  // ADP reproduces rather than corrects (Requirement 5.5).
  const labelX = x + (element.labelOffset?.x ?? DOT + 6);
  const labelY = y + (element.labelOffset?.y ?? 4);

  const decorations = element.decorators.map(decoratorName).filter((name) => name.length > 0);
  const badges = [...decorations, ...(element.inertia ? ["inertia"] : [])];

  // An anchor is the user need the chain hangs from, drawn as a distinct mark rather than one
  // more component; a submap's double ring says there is something behind it.
  const variant =
    element.kind === WardleyElementKind.ANCHOR
      ? ("square" as const)
      : element.kind === WardleyElementKind.SUBMAP
        ? ("double-circle" as const)
        : ("circle" as const);

  return (
    <SymbolElement
      className={`wardley-element-group wardley-kind-${kindName(element.kind)}${dragging ? " wardley-dragging" : ""}`}
      x={x}
      y={y}
      radius={DOT}
      variant={variant}
      label={element.name}
      labelX={labelX}
      labelY={labelY}
      badges={badges}
      inertia={element.inertia}
      markClassName="wardley-element"
      outerClassName="wardley-element-outer"
      labelClassName="wardley-element-label"
      badgesClassName="wardley-element-badges"
      inertiaClassName="wardley-inertia"
      onMouseDown={(event) => onPointerDown(event, element)}
      data-element-id={element.id}
    />
  );
}

function kindName(kind: WardleyElementKind): string {
  switch (kind) {
    case WardleyElementKind.ANCHOR:
      return "anchor";
    case WardleyElementKind.SUBMAP:
      return "submap";
    default:
      return "component";
  }
}

function decoratorName(decorator: WardleyDecorator): string {
  switch (decorator) {
    case WardleyDecorator.MARKET:
      return "market";
    case WardleyDecorator.ECOSYSTEM:
      return "ecosystem";
    case WardleyDecorator.BUILD:
      return "build";
    case WardleyDecorator.BUY:
      return "buy";
    case WardleyDecorator.OUTSOURCE:
      return "outsource";
    default:
      return "";
  }
}

function attitudeName(kind: WardleyAttitudeKind): string {
  switch (kind) {
    case WardleyAttitudeKind.SETTLERS:
      return "settlers";
    case WardleyAttitudeKind.TOWN_PLANNERS:
      return "townplanners";
    default:
      return "pioneers";
  }
}
