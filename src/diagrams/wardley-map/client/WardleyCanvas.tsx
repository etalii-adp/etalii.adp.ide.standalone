import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { straightPath, type ConnectorBox } from "@client/canvas/connectors";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import { StraightConnection } from "@client/canvas/connections/straight/StraightConnection";
import { SymbolElement } from "@client/canvas/elements/symbol/SymbolElement";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { InlineLabelEditor } from "@client/canvas/label/InlineLabelEditor";
import { asideLabelPlacement } from "@client/canvas/label/labelPlacement";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { useRegisterInlineLabelPlacement, type LabelPlacement } from "@client/shell/panels/InlineLabelPlacementContext";
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
 * The map's own space is 0..1 on both axes. It is drawn into a box of canvas units so labels
 * and stroke widths have a sensible scale to be expressed in; the viewBox then pans and zooms
 * over that.
 *
 * The box is only square when the pane is. A Wardley map's two axes measure unrelated things -
 * evolution across, value chain up - so the space is stretched to the shape of the pane it is
 * drawn in rather than letterboxed inside it. {@link SPACE} is the height; the width comes from
 * {@link spaceWidthFor}. Because both are canvas units in one uniform coordinate system, a
 * component's dot stays round and its label stays unstretched: what varies is where the space's
 * right-hand edge falls, not how anything drawn in it is shaped.
 */
const SPACE = 1000;

/** Room outside the space for the axis labels, which sit beyond the plotted area. */
const MARGIN = 90;

/** A floor for the plotted width, so a very tall narrow pane still leaves a map to read. */
const MIN_SPACE_WIDTH = 300;

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

/**
 * The plotted width that makes the whole view - space plus both margins - sit at the pane's own
 * proportions, so the map fills the canvas instead of being letterboxed inside it with dead
 * space either side, and the axes land on the canvas's left and bottom.
 *
 * Before this, the view was square whatever the pane was, and an svg centres the slack: on a
 * 469x285 pane the map was drawn 285 wide with 92px of nothing on each side, and the value-chain
 * axis floated 114px in from the left edge. It also made every pointer conversion wrong, because
 * `unitsPerPixel` divides the view's width by the surface's width and that is only the true
 * scale when the two shapes agree - a drag moved a component at 1.6x the pointer's rate.
 */
function spaceWidthFor(paneAspect: number | null): number {
  if (paneAspect === null || !Number.isFinite(paneAspect) || paneAspect <= 0) {
    return SPACE;
  }

  return Math.max(MIN_SPACE_WIDTH, paneAspect * (SPACE + MARGIN * 2) - MARGIN * 2);
}

/**
 * The map's 0..1 space as canvas units, one function per axis.
 *
 * Rounded to two decimals, which at a 1000-unit space is far finer than a pixel. Without it,
 * ordinary arithmetic on the author's numbers puts values like `350.00000000000006` into the
 * DOM - noise in every attribute, and a diff nobody can read when a snapshot is compared.
 */
export interface MapScale {
  /** A 0..1 evolution coordinate as canvas units. */
  x: (value: number) => number;
  /** A 0..1 value-chain coordinate as canvas units. */
  y: (value: number) => number;
  /** The plotted space's own size in canvas units. */
  width: number;
  height: number;
}

function mapScaleOf(spaceWidth: number): MapScale {
  return {
    x: (value) => Math.round(value * spaceWidth * 100) / 100,
    y: (value) => Math.round(value * SPACE * 100) / 100,
    width: spaceWidth,
    height: SPACE,
  };
}

function fullViewOf(scale: MapScale): ViewBox {
  return { x: -MARGIN, y: -MARGIN, w: scale.width + MARGIN * 2, h: scale.height + MARGIN * 2 };
}

/** The surface's size in real pixels, or null while nothing has been measured. */
interface PaneSize {
  width: number;
  height: number;
}

/**
 * The surface's own size, measured. jsdom computes no layout and has no `ResizeObserver`, so
 * this stays null there, {@link spaceWidthFor} hands back the square space, and every existing
 * test in this module keeps saying exactly what it said.
 */
function usePaneSize(ref: React.RefObject<SVGSVGElement | null>): PaneSize | null {
  const [size, setSize] = useState<PaneSize | null>(null);

  useLayoutEffect(() => {
    const surface = ref.current;
    if (surface === null) {
      return;
    }

    const measure = () => {
      const rect = surface.getBoundingClientRect();
      if (rect.width <= 0 || rect.height <= 0) {
        return;
      }
      setSize((current) =>
        current !== null && current.width === rect.width && current.height === rect.height
          ? current
          : { width: rect.width, height: rect.height },
      );
    };

    measure();
    if (typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(measure);
    observer.observe(surface);
    return () => observer.disconnect();
  }, [ref]);

  return size;
}

export interface WardleyCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
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
export function WardleyCanvas({ projectId, entryId, path }: WardleyCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useWardleyStream(projectId, path);

  // Measured first, because everything positional depends on it: the space's width follows the
  // pane's shape, and the label-placement resolver below is called during this very render.
  const surfaceRef = useRef<SVGSVGElement | null>(null);
  const paneSize = usePaneSize(surfaceRef);
  const scale = useMemo(
    () => mapScaleOf(spaceWidthFor(paneSize === null ? null : paneSize.width / paneSize.height)),
    [paneSize],
  );

  // The context channel this canvas never had: every wardley action the backend has offered
  // all along becomes reachable the moment an element can be selected. Renaming is one of
  // them, but the seam is general on purpose.
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey);
  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  // Where an element's label is drawn - for the shell's inline editor. The map's own 0..1
  // space is not a special case: the map scale puts elements into canvas units independent of the
  // view, so the resolver keys on the model alone, like every viewBox canvas.
  //
  // A wardley label is a bare start-anchored text at a document-carried pixel offset from the
  // mark - to its right by default, and wherever the author put it otherwise, left included.
  // That is asideLabelPlacement's shape exactly, with the offset as the (possibly negative)
  // gap, and the vertical anchored to the label's own baseline rather than the mark's centre.
  const placementOfLabel = useCallback(
    (elementId: string): LabelPlacement | null => {
      const element = model.elements.get(elementId);
      if (element === undefined) {
        return null;
      }

      const offsetX = element.labelOffset?.x ?? DOT + 6;
      const offsetY = element.labelOffset?.y ?? 4;
      return asideLabelPlacement(
        { x: scale.x(element.x), y: scale.y(element.y) + offsetY - 4 },
        offsetX,
        element.name,
      );
    },
    [model, scale],
  );
  useRegisterInlineLabelPlacement(placementOfLabel);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);
  const editingPlacement = editingId === null ? null : placementOfLabel(editingId);
  const returnFocusToSurface = useCallback(() => surfaceRef.current?.focus(), []);

  /** Ends an open inline edit before a gesture begins; the editor commits on blur. */
  const endInlineEditBeforeGesture = () => {
    if (editingPlacement !== null) {
      surfaceRef.current?.focus();
    }
  };

  // The palette the Toolbox panel shows while this map is open - described by the backend
  // (Requirement 13), registered here and withdrawn on unmount.
  useRegisterDiagramToolbox(useToolboxItems(projectId, path));
  const fullView = useMemo(() => fullViewOf(scale), [scale]);
  const [view, setView] = useState<ViewBox>(fullView);

  // The pane changed shape, so the space did: carry the view across proportionally rather than
  // throwing the reader's pan and zoom away. At the fitted view this maps one full view onto the
  // next exactly; zoomed in, the same part of the map stays in front of the reader, and the
  // view comes out at the new pane's proportions - which is what keeps the mapping uniform.
  const spaceWidthRef = useRef(scale.width);
  useLayoutEffect(() => {
    const previous = spaceWidthRef.current;
    if (previous === scale.width) {
      return;
    }

    spaceWidthRef.current = scale.width;
    const ratio = (scale.width + MARGIN * 2) / (previous + MARGIN * 2);
    setView((current) => ({ ...current, x: (current.x + MARGIN) * ratio - MARGIN, w: current.w * ratio }));
  }, [scale.width]);
  // Read by the debounced report when it fires rather than when it was scheduled, so the
  // rectangle sent is where the reader's view came to rest.
  const viewRef = useRef(view);
  viewRef.current = view;
  const panRef = useRef<{ clientX: number; clientY: number; view: ViewBox } | null>(null);
  const panMovedRef = useRef(false);

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

  const fitToView = useCallback(() => setView(fullView), [fullView]);

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

  /** Right-click on an element: select it with the menu gesture and open the menu on the push. */
  const onElementContextMenu = (event: React.MouseEvent, element: WardleyElement) => {
    surfaceRef.current?.focus();
    openMenuAt(event, element.id);
  };

  /**
   * F2 on the selected element, forwarded as data: the backend owns the key-to-action map,
   * so no table of keys lives on this canvas.
   */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    void executeShortcut(shortcut, elementSourceOf(selectedId));
  };

  /** Canvas units per screen pixel, for turning a pointer delta into a map delta. */
  const unitsPerPixel = useCallback(() => {
    const surface = surfaceRef.current;
    return surface ? view.w / surface.getBoundingClientRect().width : 1;
  }, [view.w]);

  const onPointerDown = (event: React.MouseEvent) => {
    endInlineEditBeforeGesture();
    panRef.current = { clientX: event.clientX, clientY: event.clientY, view };
    panMovedRef.current = false;
  };

  const onElementPointerDown = (event: React.MouseEvent, element: WardleyElement) => {
    // The element takes the gesture; the surface must not also pan under it.
    event.stopPropagation();
    endInlineEditBeforeGesture();
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
      // One conversion per axis: the space is only square when the pane is, so a pixel is
      // worth a different fraction of the evolution axis than of the value chain.
      const perPixel = unitsPerPixel();
      setDrag({
        id: dragging.id,
        // Clamped here as well as in the backend: the shape must not be draggable outside the
        // map while the pointer is still down, or the user is shown a position that cannot
        // exist (Requirement 7.3).
        x: clamp01(dragging.x + ((event.clientX - dragging.clientX) * perPixel) / scale.width),
        y: clamp01(dragging.y + ((event.clientY - dragging.clientY) * perPixel) / scale.height),
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
    if (event.clientX !== pan.clientX || event.clientY !== pan.clientY) {
      panMovedRef.current = true;
    }
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
      // On an element it is the selection gesture - a nested selection, the .adp then the
      // element - and the surface takes the keyboard so F2 lands here rather than wherever
      // focus last was. On the background it clears, unless the press was really a pan.
      setDrag(null);
      if (dragging) {
        surfaceRef.current?.focus();
        select(elementSelectionOf(entryId, path, dragging.id));
      } else if (!panMovedRef.current) {
        select(null);
      }
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
    convert: () => inMapSpace(shownRectOf(viewRef.current, surfaceRef.current), scale),
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
        tabIndex={0}
        onKeyDown={onKeyDown}
        onMouseDown={onPointerDown}
        onMouseMove={onPointerMove}
        onMouseUp={onPointerUp}
        onMouseLeave={onPointerUp}
        role="img"
        aria-label={model.axis?.title ? `Wardley map: ${model.axis.title}` : "Wardley map"}
      >
        {/* Drawn first, so everything else sits on top of it (Requirement 8.4). */}
        <WardleyChrome axis={model.axis} scale={scale} scaleFactor={view.w / fullView.w} />
        {loading ? null : (
          <WardleyContents model={model} scale={scale} drag={drag} selectedId={selectedId} onElementPointerDown={onElementPointerDown} onElementContextMenu={onElementContextMenu} />
        )}

        {/* Last of all, so the editor is above every mark and link it overlaps. Placed in
            canvas units; the viewBox carries it through pans and zooms like everything else. */}
        {editingPlacement !== null && (
          <InlineLabelEditor
            placement={editingPlacement}
            onPropose={onProposeLabel}
            onSubmit={onSubmitLabel}
            onCancel={onCancelLabel}
            onReturnFocus={returnFocusToSurface}
          />
        )}
      </svg>
      <CanvasScrollbars
        {...scrollAxesOf(view, fullView)}
        className="wardley-scrollbars"
        onPan={(x, y) => setView({ ...view, x, y })}
      />
      {rejection ? <p className="wardley-rejection">{rejection}</p> : null}
      {/* The element's right-click menu: the shared menu, filled with the actions the backend
          pushed for this very selection - never a client-side guess. */}
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

/**
 * A rectangle of canvas units as the map's own 0..1 space - the units its elements are in, and so
 * the units a viewport report has to be in.
 *
 * The map's space is drawn at {@link SPACE} units to the unit interval, so this is a division and
 * nothing else. It deliberately does **not** clamp to 0..1: the view can and does extend into the
 * margin where the axis labels sit, and a report clamped to the plotted area would cull the
 * elements sitting closest to the edge the reader has just panned to.
 */
function inMapSpace(rect: Viewport, scale: MapScale): Viewport {
  return {
    minX: rect.minX / scale.width,
    minY: rect.minY / scale.height,
    maxX: rect.maxX / scale.width,
    maxY: rect.maxY / scale.height,
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
function scrollAxesOf(view: ViewBox, fullView: ViewBox) {
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
function WardleyChrome({ axis, scale, scaleFactor }: { axis?: WardleyAxis; scale: MapScale; scaleFactor: number }) {
  // Stage labels hold a readable size as the map is zoomed; the boundaries they name do not,
  // because a component's position is only meaningful against its own axes (Requirement 8.5).
  const labelSize = 20 * Math.max(0.35, Math.min(2.5, scaleFactor));

  return (
    <g className="wardley-chrome" aria-hidden="true">
      {(axis?.stages ?? []).map((stage, index) => (
        <g key={stage.label}>
          <rect
            className={`wardley-band wardley-band-${index}`}
            x={scale.x(stage.start)}
            y={0}
            width={scale.x(stage.end - stage.start)}
            height={scale.height}
          />
          {index > 0 ? (
            <line
              className="wardley-band-edge"
              x1={scale.x(stage.start)}
              y1={0}
              x2={scale.x(stage.start)}
              y2={scale.height}
            />
          ) : null}
          <text
            className="wardley-band-label"
            x={scale.x((stage.start + stage.end) / 2)}
            y={scale.height + 34}
            fontSize={labelSize}
            textAnchor="middle"
          >
            {stage.label}
          </text>
        </g>
      ))}

      {/* The value chain: the user need at the top, invisible at the bottom. */}
      <line className="wardley-axis" x1={0} y1={0} x2={0} y2={scale.height} />
      {/* Evolution: genesis at the left, commodity at the right. */}
      <line className="wardley-axis" x1={0} y1={scale.height} x2={scale.width} y2={scale.height} />

      <text
        className="wardley-axis-label"
        transform={`translate(${-34} ${scale.height / 2}) rotate(-90)`}
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
        y={scale.height}
        fontSize={labelSize * 0.8}
        textAnchor="end"
      >
        Invisible
      </text>
      <text
        className="wardley-axis-label"
        x={scale.width / 2}
        y={scale.height + 66}
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
function boxOf(element: { x: number; y: number }, scale: MapScale): ConnectorBox {
  return { x: scale.x(element.x), y: scale.y(element.y), width: DOT * 2, height: DOT * 2 };
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
  scale,
  drag,
  selectedId,
  onElementPointerDown,
  onElementContextMenu,
}: {
  model: WardleyModel;
  scale: MapScale;
  drag: { id: string; x: number; y: number } | null;
  selectedId: string | null;
  onElementPointerDown: (event: React.MouseEvent, element: WardleyElement) => void;
  onElementContextMenu: (event: React.MouseEvent, element: WardleyElement) => void;
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
            x={scale.x(Math.min(attitude.x, attitude.opposite.x))}
            y={scale.y(Math.min(attitude.y, attitude.opposite.y))}
            width={scale.x(Math.abs(attitude.opposite.x - attitude.x))}
            height={scale.y(Math.abs(attitude.opposite.y - attitude.y))}
          />
          <text
            className="wardley-attitude-label"
            x={scale.x(Math.min(attitude.x, attitude.opposite.x)) + 8}
            y={scale.y(Math.min(attitude.y, attitude.opposite.y)) + 22}
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
            from={boxOf(source, scale)}
            to={boxOf(target, scale)}
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
              <StraightConnection from={boxOf(element, scale)} to={boxOf(target, scale)} pathClassName="wardley-evolve" />
              <circle
                className="wardley-evolve-target"
                cx={scale.x(target.x)}
                cy={scale.y(target.y)}
                r={DOT}
              />
              {element.evolve!.overrideName ? (
                <text
                  className="wardley-element-label"
                  x={scale.x(target.x) + DOT + 6}
                  y={scale.y(target.y) + 4}
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
          scale={scale}
          dragging={drag?.id === element.id}
          selected={element.id === selectedId}
          onPointerDown={onElementPointerDown}
          onContextMenu={onElementContextMenu}
        />
      ))}

      {[...model.accelerators.values()].map((accelerator) => (
        <g key={accelerator.id} className="wardley-accelerator">
          <path
            className={accelerator.isDeaccelerator ? "wardley-accelerator-back" : "wardley-accelerator-forward"}
            d={straightPath(
              { x: scale.x(accelerator.x) - 16, y: scale.y(accelerator.y) },
              { x: scale.x(accelerator.x) + 16, y: scale.y(accelerator.y) },
            )}
          />
          <text className="wardley-element-label" x={scale.x(accelerator.x) + 22} y={scale.y(accelerator.y) + 4}>
            {accelerator.name}
          </text>
        </g>
      ))}

      {[...model.notes.values()].map((note) => (
        <text key={note.id} className="wardley-note" x={scale.x(note.x)} y={scale.y(note.y)}>
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
            <circle cx={scale.x(occurrence.x)} cy={scale.y(occurrence.y)} r={11} />
            <text x={scale.x(occurrence.x)} y={scale.y(occurrence.y) + 4} textAnchor="middle">
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
  scale,
  dragging,
  selected,
  onPointerDown,
  onContextMenu,
}: {
  element: WardleyElement;
  scale: MapScale;
  dragging: boolean;
  selected: boolean;
  onPointerDown: (event: React.MouseEvent, element: WardleyElement) => void;
  onContextMenu: (event: React.MouseEvent, element: WardleyElement) => void;
}) {
  const x = scale.x(element.x);
  const y = scale.y(element.y);

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
      className={`wardley-element-group wardley-kind-${kindName(element.kind)}${dragging ? " wardley-dragging" : ""}${selected ? " wardley-selected canvas-selected" : ""}`}
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
      onContextMenu={(event) => onContextMenu(event, element)}
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
