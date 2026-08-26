import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import type { WardleyAxis, WardleyModel } from "./wardleyModel";
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

/** A 0..1 map coordinate as canvas units. */
function scale(value: number): number {
  return value * SPACE;
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

/**
 * Placeholder for the elements themselves, which task 15 draws. The chrome is deliberately a
 * separate component: it is the half that must be correct before anything is plotted against
 * it, and it is what an empty map shows on its own (Requirement 1.4).
 */
function WardleyContents({ model }: { model: WardleyModel }) {
  return (
    <g className="wardley-contents">
      {[...model.elements.values()].map((element) => (
        <circle
          key={element.id}
          className="wardley-element"
          cx={scale(element.x)}
          cy={scale(element.y)}
          r={8}
        />
      ))}
    </g>
  );
}
