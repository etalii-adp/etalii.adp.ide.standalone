import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { StraightConnection } from "@client/canvas/connections/straight/StraightConnection";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import type { ConnectorBox } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useSkosStream } from "./useSkosStream";
import { ALTERNATE, HIERARCHY, IRI_FALLBACK, MAPPING, type SkosEdge, type SkosModel } from "./skosModel";

/** A concept's drawn width, in the module's own canvas units - matching the layout's spacing. */
export const CONCEPT_WIDTH = 200;

/** A concept box is one line of label, with room for the notation badge above it. */
const CONCEPT_HEIGHT = 44;

/** A scheme region's header: the title strip the concepts hang beneath. */
const REGION_WIDTH = 260;
const REGION_HEIGHT = 40;

/** The id of the arrowhead marker this reading defines, worn by mapping edges alone. */
const ARROWHEAD_ID = "skos-arrowhead";

/** Zoom limits, in pixels per canvas unit. */
const MIN_PIXELS_PER_UNIT = 0.05;
const MAX_PIXELS_PER_UNIT = 8;
const ZOOM_STEP = 1.25;

interface SkosView {
  startX: number;
  startY: number;
  pixelsPerUnit: number;
}

interface DragState {
  id: string;
  clientX: number;
  clientY: number;
  x: number;
  y: number;
  moved: boolean;
}

interface DragPreview {
  id: string;
  x: number;
  y: number;
}

/**
 * A gesture in flight. `hierarchy` is what the anchor it started from means: the top anchor
 * files the dragged concept under the one it is released on, the side anchor cross-links them -
 * the two rel: gestures the design names, told apart by where the drag began rather than by a
 * menu the user has to read mid-drag.
 */
interface ConnectDrag {
  fromId: string;
  hierarchy: boolean;
  x: number;
  y: number;
  overId?: string;
}

/**
 * The concept scheme: schemes as titled regions, concepts under their broader concepts in a
 * layered hierarchy, related and mapping links across it, collections as labeled groups - drawn
 * through the central canvas library (skos-diagram Requirements 1, 3, 4, 5).
 *
 * Concept nodes are deliberately rectangular. The VOWL circle is the OWL sibling's tradition;
 * here a node carries a variable-length preferred label, a notation badge and a language chip,
 * and a rectangle is the shape that carries text. A drag never writes the SKOS file:
 * `moveElementTo` lands in the registration's `layout:` block as one undoable command.
 */
export function SkosCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo } = useSkosStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  const [view, setView] = useState<SkosView>(() => ({ startX: -60, startY: -60, pixelsPerUnit: 1 }));
  const fittedRef = useRef(false);

  const panRef = useRef<{ clientX: number; clientY: number; view: SkosView; moved: boolean } | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const [drag, setDrag] = useState<DragPreview | null>(null);
  const [connect, setConnect] = useState<ConnectDrag | null>(null);
  const connectRef = useRef<ConnectDrag | null>(null);
  const [rejection, setRejection] = useState("");

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  const placed = useMemo(() => placedOf(model), [model]);

  const fitToView = useCallback(() => {
    const surface = surfaceRef.current;
    const all = placedOf(model);
    if (!surface || all.length === 0) {
      return;
    }

    const minX = Math.min(...all.map((item) => item.x));
    const maxX = Math.max(...all.map((item) => item.x + item.width));
    const span = Math.max(maxX - minX, CONCEPT_WIDTH);
    const width = surface.getBoundingClientRect().width || 1200;

    setView({
      startX: minX - span * 0.1,
      startY: Math.min(...all.map((item) => item.y)) - REGION_HEIGHT,
      pixelsPerUnit: clampZoom(width / (span * 1.2)),
    });
  }, [model]);

  // Fitted once, when the first delta lands - refitting on every edit would fight the user's pan.
  useEffect(() => {
    if (!loading && !fittedRef.current && placed.length > 0) {
      fittedRef.current = true;
      fitToView();
    }
  }, [loading, placed, fitToView]);

  const zoomBy = useCallback((factor: number) => {
    setView((current) => {
      const rect = surfaceRef.current?.getBoundingClientRect();
      const width = rect?.width || 1200;
      const height = rect?.height || 600;
      const next = clampZoom(current.pixelsPerUnit * factor);
      const centreX = current.startX + (width / 2) / current.pixelsPerUnit;
      const centreY = current.startY + (height / 2) / current.pixelsPerUnit;
      return {
        startX: centreX - (width / 2) / next,
        startY: centreY - (height / 2) / next,
        pixelsPerUnit: next,
      };
    });
  }, []);

  useRegisterDiagramView(
    useMemo(
      () => ({
        zoomIn: () => zoomBy(ZOOM_STEP),
        zoomOut: () => zoomBy(1 / ZOOM_STEP),
        fitToView,
      }),
      [zoomBy, fitToView],
    ),
  );

  // Non-passive by hand: React's synthetic wheel listener cannot preventDefault, and without it
  // every zoom also scrolls the page.
  useEffect(() => {
    const surface = surfaceRef.current;
    if (!surface) {
      return;
    }

    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      zoomBy(event.deltaY > 0 ? 1 / ZOOM_STEP : ZOOM_STEP);
    };

    surface.addEventListener("wheel", onWheel, { passive: false });
    return () => surface.removeEventListener("wheel", onWheel);
  }, [zoomBy]);

  // Escape abandons whichever gesture is in flight, dispatching nothing.
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") {
        return;
      }

      dragRef.current = null;
      connectRef.current = null;
      setDrag(null);
      setConnect(null);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const toUnitsX = (clientX: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.startX + (clientX - (rect?.left ?? 0)) / view.pixelsPerUnit;
  };

  const toUnitsY = (clientY: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.startY + (clientY - (rect?.top ?? 0)) / view.pixelsPerUnit;
  };

  const xToPx = (x: number): number => (x - view.startX) * view.pixelsPerUnit;
  const yToPx = (y: number): number => (y - view.startY) * view.pixelsPerUnit;

  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    const target = event.target as Element;
    if (target !== event.currentTarget && !target.classList?.contains("skos-content")) {
      return;
    }

    closeMenu();
    if (event.button === 0) {
      select(null);
    }

    panRef.current = { clientX: event.clientX, clientY: event.clientY, view, moved: false };
  };

  const onBoxPointerDown = (event: React.MouseEvent, id: string, x: number, y: number) => {
    event.stopPropagation();
    setRejection("");
    dragRef.current = { id, clientX: event.clientX, clientY: event.clientY, x, y, moved: false };
  };

  const onAnchorPointerDown = (event: React.MouseEvent, id: string, hierarchy: boolean) => {
    event.stopPropagation();
    const start: ConnectDrag = { fromId: id, hierarchy, x: toUnitsX(event.clientX), y: toUnitsY(event.clientY) };
    connectRef.current = start;
    setConnect(start);
  };

  const onPointerMove = (event: React.MouseEvent) => {
    const dragging = dragRef.current;
    if (dragging) {
      dragging.moved ||= Math.abs(event.clientX - dragging.clientX) + Math.abs(event.clientY - dragging.clientY) > 3;
      setDrag({
        id: dragging.id,
        x: dragging.x + (event.clientX - dragging.clientX) / view.pixelsPerUnit,
        y: dragging.y + (event.clientY - dragging.clientY) / view.pixelsPerUnit,
      });
      return;
    }

    const connecting = connectRef.current;
    if (connecting) {
      const next = { ...connecting, x: toUnitsX(event.clientX), y: toUnitsY(event.clientY) };
      connectRef.current = next;
      setConnect(next);
      return;
    }

    const pan = panRef.current;
    if (pan) {
      pan.moved ||= Math.abs(event.clientX - pan.clientX) + Math.abs(event.clientY - pan.clientY) > 3;
      setView({
        ...pan.view,
        startX: pan.view.startX - (event.clientX - pan.clientX) / pan.view.pixelsPerUnit,
        startY: pan.view.startY - (event.clientY - pan.clientY) / pan.view.pixelsPerUnit,
      });
    }
  };

  /** The canvas owns the right button: a drag pans, and the browser's own menu never appears. */
  const onSurfaceContextMenu = (event: React.MouseEvent) => {
    event.preventDefault();
  };

  const onPointerUp = (event: React.MouseEvent) => {
    panRef.current = null;

    const dragging = dragRef.current;
    const landed = drag;
    dragRef.current = null;
    setDrag(null);
    if (dragging && landed && dragging.moved) {
      // The authored position, raw: the layout block stores what the author placed. A blank
      // node's refusal comes back from the backend with its sentence (Requirement 4.4).
      void (async () => {
        const error = await moveElementTo(landed.id, landed.x, landed.y);
        if (error) {
          setRejection(error);
        }
      })();
      return;
    }

    if (dragging && !dragging.moved) {
      // A press with no movement is a click: a selection, never an edit.
      select(elementSelectionOf(entryId, path, dragging.id));
      return;
    }

    const connecting = connectRef.current;
    connectRef.current = null;
    setConnect(null);
    if (connecting) {
      const under = (event.target as Element | null)?.closest?.("[data-element-id]");
      const targetId = connecting.overId ?? under?.getAttribute("data-element-id") ?? null;
      if (!targetId || targetId === connecting.fromId) {
        // Released on nothing, or back onto its own source: a "never mind".
        return;
      }

      // Which anchor the drag began at is which gesture it is - one stateless rel: id either
      // way, and the backend refuses ends that are not both asserted concepts.
      runAction(connecting.hierarchy ? "skos.file-under" : "skos.relate", `rel:${connecting.fromId}->${targetId}`);
    }
  };

  const onBoxPointerEnter = (id: string) => {
    const connecting = connectRef.current;
    if (connecting && id !== connecting.fromId) {
      const next = { ...connecting, overId: id };
      connectRef.current = next;
      setConnect(next);
    }
  };

  const onBoxPointerLeave = () => {
    const connecting = connectRef.current;
    if (connecting?.overId) {
      const next = { ...connecting, overId: undefined };
      connectRef.current = next;
      setConnect(next);
    }
  };

  const openTargetMenuAt = (event: React.MouseEvent, id: string) => {
    if (panRef.current?.moved) {
      return;
    }

    openMenuAt(event, id);
  };

  /** A toolbox entry dropped anywhere: the drop names a placement - `new:{x},{y}` under the pointer. */
  const onSurfaceDrop = (event: React.DragEvent) => {
    const actionId = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    if (!actionId) {
      return;
    }

    event.preventDefault();
    runAction(actionId, `new:${toUnitsX(event.clientX)},${toUnitsY(event.clientY)}`);
  };

  const onDragOver = (event: React.DragEvent) => {
    if (event.dataTransfer.types.includes(TOOLBOX_DRAG_TYPE)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = "copy";
    }
  };

  /** Structural keys travel to the backend as data - the backend owns the key-to-action table. */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2", "Delete"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(selectedId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  if (failed) {
    return (
      <div className="skos-canvas canvas-host skos-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  const boxes = new Map<string, ConnectorBox>();
  for (const item of placed) {
    boxes.set(
      item.id,
      boxFor(at(item, drag), item.width, item.height, xToPx, yToPx, view.pixelsPerUnit),
    );
  }

  return (
    <div className="skos-canvas canvas-host">
      <div
        ref={surfaceRef}
        className="skos-surface canvas-viewport"
        role="application"
        aria-label="SKOS concept scheme"
        tabIndex={0}
        onMouseDown={onSurfacePointerDown}
        onMouseMove={onPointerMove}
        onMouseUp={onPointerUp}
        onMouseLeave={onPointerUp}
        onContextMenu={onSurfaceContextMenu}
        onKeyDown={onKeyDown}
        onDragOver={onDragOver}
        onDrop={onSurfaceDrop}
      >
        <svg className="skos-content canvas-drawing">
          <defs>
            <marker
              id={ARROWHEAD_ID}
              className="skos-arrowhead canvas-arrowhead"
              viewBox="0 0 10 10"
              refX="9"
              refY="5"
              markerWidth="7"
              markerHeight="7"
              orient="auto-start-reverse"
            >
              <path d="M 0 0 L 10 5 L 0 10 z" />
            </marker>
          </defs>

          {[...model.edges.values()].map((edge) =>
            renderEdge(edge, boxes, selectedId, {
              onMouseDown: (event) => {
                // A click on an edge is a selection, never a drag: an edge has no position of
                // its own - but its menu carries the disconnect.
                event.stopPropagation();
                setRejection("");
                select(elementSelectionOf(entryId, path, edge.id));
              },
              onContextMenu: (event) => openTargetMenuAt(event, edge.id),
            }),
          )}

          {connect ? <PendingEdge boxes={boxes} connect={connect} xToPx={xToPx} yToPx={yToPx} /> : null}

          {[...model.schemes.values(), ...model.collections.values()].map((container) => {
            const box = boxes.get(container.id)!;
            const ordered = "ordered" in container && container.ordered;
            const classes = ["skos-region canvas-element"];
            if (container.id === selectedId) {
              classes.push("skos-selected canvas-selected");
            }

            return (
              <BoxElement
                key={container.id}
                className={classes.join(" ")}
                data-element-id={container.id}
                x={box.x - box.width / 2}
                y={box.y - box.height / 2}
                width={box.width}
                height={box.height}
                label={"memberCount" in container ? `${container.label} (${container.memberCount})` : container.label}
                boxClassName="skos-region-box canvas-boundary"
                labelClassName="skos-region-label canvas-node-label"
                labelY={(REGION_HEIGHT / 2 + 5) * view.pixelsPerUnit}
                onMouseDown={(event) => onBoxPointerDown(event, container.id, at(container, drag).x, at(container, drag).y)}
                onContextMenu={(event) => openTargetMenuAt(event, container.id)}
                onDragOver={(event) => event.preventDefault()}
              >
                {ordered ? (
                  <text className="skos-region-kind" x={8 * view.pixelsPerUnit} y={(REGION_HEIGHT - 6) * view.pixelsPerUnit}>
                    ordered
                  </text>
                ) : null}
              </BoxElement>
            );
          })}

          {[...model.concepts.values()].map((concept) => {
            const box = boxes.get(concept.id)!;
            const classes = ["skos-concept canvas-element"];
            if (concept.blank) {
              classes.push("skos-concept-blank");
            }

            if (concept.labelKind === ALTERNATE) {
              classes.push("skos-label-alternate");
            }

            if (concept.labelKind === IRI_FALLBACK) {
              classes.push("skos-label-fallback");
            }

            if (concept.id === selectedId) {
              classes.push("skos-selected canvas-selected");
            }

            if (connect?.overId === concept.id) {
              classes.push("skos-connect-target canvas-connect-target");
            }

            return (
              <BoxElement
                key={concept.id}
                className={classes.join(" ")}
                data-element-id={concept.id}
                x={box.x - box.width / 2}
                y={box.y - box.height / 2}
                width={box.width}
                height={box.height}
                label={concept.label}
                boxClassName="skos-concept-box canvas-node"
                labelClassName="skos-concept-label canvas-node-label"
                labelY={(CONCEPT_HEIGHT / 2 + 8) * view.pixelsPerUnit}
                onMouseDown={(event) => onBoxPointerDown(event, concept.id, at(concept, drag).x, at(concept, drag).y)}
                onMouseEnter={() => onBoxPointerEnter(concept.id)}
                onMouseLeave={onBoxPointerLeave}
                onContextMenu={(event) => openTargetMenuAt(event, concept.id)}
                onDragOver={(event) => event.preventDefault()}
              >
                {concept.notation ? (
                  <text className="skos-notation" x={8 * view.pixelsPerUnit} y={14 * view.pixelsPerUnit}>
                    {concept.notation}
                  </text>
                ) : null}
                {concept.languageChip ? (
                  // A translation gap, visible but quiet: the backend decided this, so the
                  // chip cannot disagree with the label beside it (Requirement 3.3).
                  <text
                    className="skos-language-chip"
                    x={(CONCEPT_WIDTH - 8) * view.pixelsPerUnit}
                    y={14 * view.pixelsPerUnit}
                  >
                    {concept.languageTag}
                  </text>
                ) : null}
                {!concept.blank && concept.id === selectedId ? (
                  <>
                    {/* Top anchor: file under. Side anchor: relate. A visible dot with an
                        invisible fat grab twin, the shared anchor pair. */}
                    <circle className="skos-anchor canvas-anchor" cx={box.width / 2} cy={0} r={4} />
                    <circle className="skos-anchor canvas-anchor" cx={box.width} cy={box.height / 2} r={4} />
                    <circle
                      className="skos-anchor-hit canvas-anchor-hit"
                      cx={box.width / 2}
                      cy={0}
                      r={10}
                      onMouseDown={(event) => onAnchorPointerDown(event, concept.id, true)}
                    />
                    <circle
                      className="skos-anchor-hit canvas-anchor-hit"
                      cx={box.width}
                      cy={box.height / 2}
                      r={10}
                      onMouseDown={(event) => onAnchorPointerDown(event, concept.id, false)}
                    />
                  </>
                ) : null}
              </BoxElement>
            );
          })}
        </svg>
        <CanvasScrollbars
          {...scrollAxesOf(placed, view, surfaceRef.current?.getBoundingClientRect() ?? null)}
          className="skos-scrollbars"
          onPan={(startX, startY) => setView((current) => ({ ...current, startX, startY }))}
        />
      </div>
      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          closeMenu();
          runAction(action.id, selectedId ?? undefined);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={closeMenu}
      />
      {model.truncation ? (
        <p className="skos-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} terms — edits are withheld on this truncated view`}
        </p>
      ) : null}
      {loading ? <p className="skos-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="skos-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

interface Placed {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Everything positioned, in one list - what fitting, scrolling and box geometry all walk. */
function placedOf(model: SkosModel): Placed[] {
  const placed: Placed[] = [];
  for (const scheme of model.schemes.values()) {
    placed.push({ id: scheme.id, x: scheme.x, y: scheme.y, width: REGION_WIDTH, height: REGION_HEIGHT });
  }

  for (const collection of model.collections.values()) {
    placed.push({ id: collection.id, x: collection.x, y: collection.y, width: REGION_WIDTH, height: REGION_HEIGHT });
  }

  for (const concept of model.concepts.values()) {
    placed.push({ id: concept.id, x: concept.x, y: concept.y, width: CONCEPT_WIDTH, height: CONCEPT_HEIGHT });
  }

  return placed;
}

/** The two scroll axes as the shared bars want them, in canvas units. */
function scrollAxesOf(placed: Placed[], view: SkosView, surface: DOMRect | null) {
  const widthPx = surface?.width || 1200;
  const heightPx = surface?.height || 600;

  const horizontalSpan = widthPx / view.pixelsPerUnit;
  const verticalSpan = heightPx / view.pixelsPerUnit;
  const minX = placed.length > 0 ? Math.min(...placed.map((item) => item.x)) : view.startX;
  const maxX = placed.length > 0 ? Math.max(...placed.map((item) => item.x + item.width)) : view.startX + horizontalSpan;
  const minY = placed.length > 0 ? Math.min(...placed.map((item) => item.y)) : view.startY;
  const maxY = placed.length > 0 ? Math.max(...placed.map((item) => item.y + item.height)) : view.startY + verticalSpan;

  return {
    horizontal: {
      viewStart: view.startX,
      viewSpan: horizontalSpan,
      ...scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: CONCEPT_WIDTH }),
    },
    vertical: {
      viewStart: view.startY,
      viewSpan: verticalSpan,
      ...scrollExtentOf(minY, maxY, { factor: 0, minimum: 2 * REGION_HEIGHT }),
    },
  };
}

function at(item: { id: string; x: number; y: number }, drag: DragPreview | null): { x: number; y: number } {
  return drag && drag.id === item.id ? { x: drag.x, y: drag.y } : { x: item.x, y: item.y };
}

/** A box in pixels, centre-based as the shared geometry expects. */
function boxFor(
  position: { x: number; y: number },
  width: number,
  height: number,
  xToPx: (x: number) => number,
  yToPx: (y: number) => number,
  pixelsPerUnit: number,
): ConnectorBox {
  const w = Math.max(width * pixelsPerUnit, 2);
  const h = Math.max(height * pixelsPerUnit, 2);
  return {
    x: xToPx(position.x) + w / 2,
    y: yToPx(position.y) + h / 2,
    width: w,
    height: h,
  };
}

/**
 * One edge in its own language: hierarchy solid and arrowless - the layering carries the
 * direction, broader above narrower, so an arrowhead would only repeat it - related dashed, and
 * a mapping dotted with the arrowhead and its property as the label.
 */
function renderEdge(
  edge: SkosEdge,
  boxes: Map<string, ConnectorBox>,
  selectedId: string | null,
  handlers: {
    onMouseDown: (event: React.MouseEvent) => void;
    onContextMenu: (event: React.MouseEvent) => void;
  },
) {
  const from = boxes.get(edge.fromElementId);
  const to = boxes.get(edge.toElementId);
  if (!from || !to) {
    return null;
  }

  const kindClass =
    edge.kind === HIERARCHY ? "skos-edge-hierarchy" : edge.kind === MAPPING ? "skos-edge-mapping" : "skos-edge-related";

  return (
    <g
      key={edge.id}
      data-element-id={edge.id}
      onMouseDown={handlers.onMouseDown}
      onContextMenu={handlers.onContextMenu}
    >
      {/* The invisible fat grab twin the shared class defines - a thin stroke is no target. */}
      <path className="skos-edge-hit canvas-connection-hit" d={`M ${from.x} ${from.y} L ${to.x} ${to.y}`} />
      <StraightConnection
        from={from}
        to={to}
        className={`skos-edge ${kindClass}${edge.id === selectedId ? " skos-selected canvas-selected" : ""}`}
        pathClassName="skos-edge-line canvas-connection-line"
        markerEnd={edge.kind === MAPPING ? `url(#${ARROWHEAD_ID})` : undefined}
      />
      {edge.predicate ? (
        <text className="skos-edge-label canvas-hint" x={(from.x + to.x) / 2} y={(from.y + to.y) / 2 - 6}>
          {edge.predicate}
        </text>
      ) : null}
    </g>
  );
}

function PendingEdge({
  boxes,
  connect,
  xToPx,
  yToPx,
}: {
  boxes: Map<string, ConnectorBox>;
  connect: ConnectDrag;
  xToPx: (x: number) => number;
  yToPx: (y: number) => number;
}) {
  const from = boxes.get(connect.fromId);
  if (!from) {
    return null;
  }

  const over = connect.overId ? boxes.get(connect.overId) : undefined;
  const end = over ? { x: over.x, y: over.y } : { x: xToPx(connect.x), y: yToPx(connect.y) };

  return (
    <path
      className={`skos-pending-edge canvas-pending-connection${connect.hierarchy ? " skos-pending-hierarchy" : ""}`}
      d={`M ${from.x} ${from.y} L ${end.x} ${end.y}`}
    />
  );
}

function clampZoom(pixelsPerUnit: number): number {
  return Math.min(MAX_PIXELS_PER_UNIT, Math.max(MIN_PIXELS_PER_UNIT, pixelsPerUnit));
}
