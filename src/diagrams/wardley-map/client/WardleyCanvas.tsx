import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { anchorsBetween, straightPath, type ConnectorBox } from "@client/canvas/connectors";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import {
  WardleyAttitudeKind,
  WardleyDecorator,
  WardleyElementKind,
} from "@client/generated/wardley-map_pb";
import type { WardleyAxis, WardleyElement, WardleyModel } from "./wardleyModel";
import { useWardleyStream } from "./useWardleyStream";

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
  const { model, loading, failed } = useWardleyStream(projectId, path);
  const [view, setView] = useState<ViewBox>(fullView);
  const surfaceRef = useRef<SVGSVGElement | null>(null);
  const panRef = useRef<{ clientX: number; clientY: number; view: ViewBox } | null>(null);

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

  const onPointerDown = (event: React.MouseEvent) => {
    panRef.current = { clientX: event.clientX, clientY: event.clientY, view };
  };

  const onPointerMove = (event: React.MouseEvent) => {
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
    panRef.current = null;
  };

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
        {loading ? null : <WardleyContents model={model} />}
      </svg>
    </div>
  );
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
function WardleyContents({ model }: { model: WardleyModel }) {
  const elements = [...model.elements.values()];
  const byId = model.elements;

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

        // The shared connector geometry: a Wardley link joins two boxes, and that the centres
        // came from the document rather than from a layout changes nothing about the maths.
        const [from, to] = anchorsBetween(boxOf(source), boxOf(target));
        return (
          <path
            key={link.id}
            className={`wardley-link${link.isFlow ? " wardley-link-flow" : ""}`}
            d={straightPath(from, to)}
          >
            {link.context ? <title>{link.context}</title> : null}
          </path>
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
          const [from, to] = anchorsBetween(boxOf(element), boxOf(target));
          return (
            <g key={`${element.id}-evolve`}>
              <path className="wardley-evolve" d={straightPath(from, to)} />
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
        <WardleyElementShape key={element.id} element={element} />
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
function WardleyElementShape({ element }: { element: WardleyElement }) {
  const x = scale(element.x);
  const y = scale(element.y);

  // The label offset is in PIXELS rather than map coordinates - a property of the format, which
  // ADP reproduces rather than corrects (Requirement 5.5).
  const labelX = x + (element.labelOffset?.x ?? DOT + 6);
  const labelY = y + (element.labelOffset?.y ?? 4);

  const decorations = element.decorators.map(decoratorName).filter((name) => name.length > 0);
  const badges = [...decorations, ...(element.inertia ? ["inertia"] : [])];

  return (
    <g className={`wardley-element-group wardley-kind-${kindName(element.kind)}`}>
      {element.kind === WardleyElementKind.ANCHOR ? (
        // An anchor is the user need the chain hangs from, so it is drawn as a distinct mark
        // rather than as one more component.
        <rect className="wardley-element" x={x - DOT} y={y - DOT} width={DOT * 2} height={DOT * 2} />
      ) : element.kind === WardleyElementKind.SUBMAP ? (
        // A submap is a door to another map; the double ring says there is something behind it.
        <g>
          <circle className="wardley-element" cx={x} cy={y} r={DOT} />
          <circle className="wardley-element-outer" cx={x} cy={y} r={DOT + 4} />
        </g>
      ) : (
        <circle className="wardley-element" cx={x} cy={y} r={DOT} />
      )}

      {element.inertia ? (
        // The wall a component is pushed against: drawn where movement would be resisted.
        <line className="wardley-inertia" x1={x + DOT + 4} y1={y - DOT - 2} x2={x + DOT + 4} y2={y + DOT + 2} />
      ) : null}

      <text className="wardley-element-label" x={labelX} y={labelY}>
        {element.name}
      </text>

      {badges.length > 0 ? (
        <text className="wardley-element-badges" x={labelX} y={labelY + 16}>
          {badges.join(" · ")}
        </text>
      ) : null}
    </g>
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
