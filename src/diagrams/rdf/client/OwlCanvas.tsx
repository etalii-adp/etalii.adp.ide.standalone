import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { EllipseElement } from "@client/canvas/elements/ellipse/EllipseElement";
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
import { useOwlStream } from "./useOwlStream";
import { useViewReport } from "@client/diagrams/useViewReport";
import { viewportOf } from "./rdfViewport";
import { isCard, isExpression, type OwlDiagramEdge, type OwlModel, type OwlNode } from "./owlModel";

/** A card's drawn width, in the module's own canvas units - matching the backend layout's spacing. */
export const CARD_WIDTH = 220;

/** A class or datatype's drawn size. `owl:Thing` anchors draw smaller, as the notation asks. */
export const SHAPE_WIDTH = 190;
export const SHAPE_HEIGHT = 70;
const THING_SCALE = 0.55;

/** The card's parts, stacked: title, the type badges, then one line per row. */
const HEADER_HEIGHT = 30;
const BADGES_HEIGHT = 16;
const ROW_HEIGHT = 16;
const FOOTER_PADDING = 10;

/** The id of the arrowhead marker this reading defines and its edge stylesheet points at. */
const ARROWHEAD_ID = "owl-arrowhead";

/** Zoom limits, in pixels per canvas unit. */
const MIN_PIXELS_PER_UNIT = 0.05;
const MAX_PIXELS_PER_UNIT = 8;
const ZOOM_STEP = 1.25;

interface OwlView {
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

interface ConnectDrag {
  fromId: string;
  x: number;
  y: number;
  overId?: string;
}

/** A card's height follows its content; every other shape is a fixed silhouette. */
export function nodeSizeOf(node: OwlNode): { width: number; height: number } {
  if (isCard(node)) {
    return {
      width: CARD_WIDTH,
      height:
        HEADER_HEIGHT
        + (node.badges.length > 0 ? BADGES_HEIGHT : 0)
        + node.rows.length * ROW_HEIGHT
        + FOOTER_PADDING,
    };
  }

  if (node.kind === "thing") {
    return { width: SHAPE_WIDTH * THING_SCALE, height: SHAPE_HEIGHT * THING_SCALE };
  }

  return { width: SHAPE_WIDTH, height: SHAPE_HEIGHT };
}

/**
 * The ontology reading: classes as ellipses, datatypes as rectangles, individuals as the
 * family's cards, class expressions as compact nodes beside the class that uses them, and every
 * axiom drawn as the edge its kind asks for - the adopted VOWL vocabulary, drawn through the
 * central canvas library (owl-diagram Requirements 1-3).
 *
 * Kind is carried by shape and edge style with theme-aware colours, never by a fixed palette:
 * the notation's own colours are an identity rather than a semantics, and this canvas has to
 * read in both themes. A drag never writes the ontology file, and an expression node's drag is
 * refused backend-side with the identity boundary's sentence (Requirement 3.2).
 */
export function OwlCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useOwlStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  const [view, setView] = useState<OwlView>(() => ({ startX: -60, startY: -60, pixelsPerUnit: 1 }));

  const viewRef = useRef(view);
  viewRef.current = view;

  // What the reader can see, reported once it settles and on every later change. The report
  // observes the view rather than being wired to any one gesture, so a pan, a scrollbar thumb,
  // a zoom and a programmatic reveal all reach the backend by the same path.
  useViewReport({
    view: { x: view.startX, y: view.startY, w: view.pixelsPerUnit, h: view.pixelsPerUnit },
    report: reportView,
    convert: () => viewportOf(viewRef.current, surfaceRef.current?.getBoundingClientRect() ?? null),
    ready: !loading && !failed,
  });
  const fittedRef = useRef(false);

  const panRef = useRef<{ clientX: number; clientY: number; view: OwlView; moved: boolean } | null>(null);
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

  /** Fits everything into view, with a margin. */
  const fitToView = useCallback(() => {
    const surface = surfaceRef.current;
    const nodes = [...model.nodes.values()];
    if (!surface || nodes.length === 0) {
      return;
    }

    const minX = Math.min(...nodes.map((node) => node.x));
    const maxX = Math.max(...nodes.map((node) => node.x + nodeSizeOf(node).width));
    const span = Math.max(maxX - minX, CARD_WIDTH);
    const width = surface.getBoundingClientRect().width || 1200;

    setView({
      startX: minX - span * 0.1,
      startY: Math.min(...nodes.map((node) => node.y)) - HEADER_HEIGHT,
      pixelsPerUnit: clampZoom(width / (span * 1.2)),
    });
  }, [model]);

  // Fitted once, when the first delta lands - refitting on every edit would fight the user's pan.
  useEffect(() => {
    if (!loading && !fittedRef.current && model.nodes.size > 0) {
      fittedRef.current = true;
      fitToView();
    }
  }, [loading, model, fitToView]);

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

  // Non-passive by hand: React's synthetic wheel listener cannot preventDefault.
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
    if (target !== event.currentTarget && !target.classList?.contains("owl-content")) {
      return;
    }

    closeMenu();
    if (event.button === 0) {
      select(null);
    }

    panRef.current = { clientX: event.clientX, clientY: event.clientY, view, moved: false };
  };

  const onShapePointerDown = (event: React.MouseEvent, id: string, x: number, y: number) => {
    event.stopPropagation();
    setRejection("");
    dragRef.current = { id, clientX: event.clientX, clientY: event.clientY, x, y, moved: false };
  };

  const onAnchorPointerDown = (event: React.MouseEvent, id: string) => {
    event.stopPropagation();
    const start: ConnectDrag = { fromId: id, x: toUnitsX(event.clientX), y: toUnitsY(event.clientY) };
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
      // The authored position, raw. An expression node's refusal comes back from the backend
      // carrying the identity boundary's sentence (Requirement 3.2).
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
        return;
      }

      // The whole gesture in one stateless rel: call. Between two classes the backend offers
      // the subclass axiom; anything else asks for a predicate (Requirement 6.1).
      runAction("owl.subclass", `rel:${connecting.fromId}->${targetId}`);
    }
  };

  const onShapePointerEnter = (id: string) => {
    const connecting = connectRef.current;
    if (connecting && id !== connecting.fromId) {
      const next = { ...connecting, overId: id };
      connectRef.current = next;
      setConnect(next);
    }
  };

  const onShapePointerLeave = () => {
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

  /** A toolbox entry dropped anywhere on the canvas names a placement - `new:{x},{y}`. */
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
      <div className="owl-canvas canvas-host owl-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  const boxes = new Map<string, ConnectorBox>();
  for (const node of model.nodes.values()) {
    const size = nodeSizeOf(node);
    boxes.set(node.id, boxFor(at(node, drag), size.width, size.height, xToPx, yToPx, view.pixelsPerUnit));
  }

  return (
    <div className="owl-canvas canvas-host">
      <div
        ref={surfaceRef}
        className="owl-surface canvas-viewport"
        role="application"
        aria-label="OWL ontology"
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
        <svg className="owl-content canvas-drawing">
          <defs>
            <marker
              id={ARROWHEAD_ID}
              className="owl-arrowhead canvas-arrowhead"
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
                event.stopPropagation();
                setRejection("");
                select(elementSelectionOf(entryId, path, edge.id));
              },
              onContextMenu: (event) => openTargetMenuAt(event, edge.id),
            }),
          )}

          {[...model.nodes.values()].map((node) => {
            const box = boxes.get(node.id)!;
            const shared = {
              key: node.id,
              className: classesFor(node, selectedId, connect?.overId),
              "data-element-id": node.id,
              onMouseDown: (event: React.MouseEvent) =>
                onShapePointerDown(event, node.id, at(node, drag).x, at(node, drag).y),
              onMouseEnter: () => onShapePointerEnter(node.id),
              onMouseLeave: onShapePointerLeave,
              onContextMenu: (event: React.MouseEvent) => openTargetMenuAt(event, node.id),
              onDragOver: (event: React.DragEvent) => event.preventDefault(),
            };

            if (isCard(node)) {
              const badgesY = HEADER_HEIGHT - 6 + BADGES_HEIGHT;
              const rowsStart = HEADER_HEIGHT + (node.badges.length > 0 ? BADGES_HEIGHT : 0);
              return (
                <BoxElement
                  {...shared}
                  x={box.x - box.width / 2}
                  y={box.y - box.height / 2}
                  width={box.width}
                  height={box.height}
                  label={node.display}
                  boxClassName="owl-card-box canvas-node"
                  labelClassName="owl-label canvas-node-label"
                  labelY={(HEADER_HEIGHT / 2 + 5) * view.pixelsPerUnit}
                >
                  {node.badges.length > 0 ? (
                    <text className="owl-badges" x={8 * view.pixelsPerUnit} y={badgesY * view.pixelsPerUnit}>
                      {fit(node.badges.join(" · "), box.width)}
                    </text>
                  ) : null}
                  {node.rows.map((row, index) => (
                    <text
                      key={`${row.predicate}-${index}`}
                      className="owl-row"
                      x={8 * view.pixelsPerUnit}
                      y={(rowsStart + (index + 1) * ROW_HEIGHT - 4) * view.pixelsPerUnit}
                    >
                      {fit(`${row.predicate}: ${row.value}${row.annotation ? ` ${row.annotation}` : ""}`, box.width)}
                    </text>
                  ))}
                  <title>{node.display}</title>
                  {anchorsFor(node, box, selectedId, onAnchorPointerDown)}
                </BoxElement>
              );
            }

            if (node.kind === "datatype") {
              // A datatype is a rectangle: the schema half of the literal-node position, where
              // the datatype IS the axiom's point (Requirement 1.1).
              return (
                <BoxElement
                  {...shared}
                  x={box.x - box.width / 2}
                  y={box.y - box.height / 2}
                  width={box.width}
                  height={box.height}
                  label={node.display}
                  boxClassName="owl-datatype-box canvas-node"
                  labelClassName="owl-label canvas-node-label"
                  labelY={(SHAPE_HEIGHT / 2 + 4) * view.pixelsPerUnit}
                />
              );
            }

            // Everything else is round: classes, Thing anchors, and the expression shapes -
            // whose label is the Manchester form, with the elision marker when it hides depth.
            return (
              <EllipseElement
                {...shared}
                x={box.x}
                y={box.y}
                radiusX={box.width / 2}
                radiusY={box.height / 2}
                text={labelFor(node, box.width)}
                doubled={hasEquivalence(model, node.id)}
                ellipseClassName="owl-shape canvas-node"
                innerClassName="owl-shape-inner"
                labelClassName="owl-label canvas-node-label"
              >
                <title>{node.display}</title>
                {anchorsFor(node, box, selectedId, onAnchorPointerDown)}
              </EllipseElement>
            );
          })}
        </svg>
        <CanvasScrollbars
          {...scrollAxesOf(model, view, surfaceRef.current?.getBoundingClientRect() ?? null)}
          className="owl-scrollbars"
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
        <p className="owl-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} elements — edits are withheld on this truncated view`}
        </p>
      ) : null}
      {loading ? <p className="owl-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="owl-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

/**
 * A label that stays inside its shape. The drawn names are IRIs' local names and Manchester
 * expressions, both of which run long; left whole they smear across their neighbours and turn a
 * real ontology into a thicket. The full name is one hover or one selection away - the shape
 * carries it as a title, and the property grid shows it in full.
 */
export function labelFor(node: OwlNode, widthPx: number): string {
  return fit(isExpression(node) && node.elided ? `${node.display} …` : node.display, widthPx);
}

/**
 * One line of text, cut to what its element can hold. Rows carry annotation values - a comment,
 * a contributor's address - that are sentences rather than names, and drawn whole they run
 * across half the canvas.
 */
export function fit(text: string, widthPx: number): string {
  // ~0.55em per character at the label's size, with a little padding inside the outline.
  const budget = Math.max(6, Math.floor((widthPx - 16) / 6.2));
  return text.length <= budget ? text : `${text.slice(0, budget - 1).trimEnd()}…`;
}

/** The classes a node wears: its kind, its dimming, and whatever state it is in. */
function classesFor(node: OwlNode, selectedId: string | null, connectTargetId: string | undefined): string {
  const classes = ["owl-node canvas-element", `owl-${node.kind}`];
  if (node.deprecated) {
    classes.push("owl-deprecated");
  }

  if (node.external) {
    classes.push("owl-external");
  }

  if (node.malformed) {
    classes.push("owl-malformed");
  }

  if (node.elided) {
    classes.push("owl-elided");
  }

  if (node.id === selectedId) {
    classes.push("owl-selected canvas-selected");
  }

  if (connectTargetId === node.id) {
    classes.push("owl-connect-target canvas-connect-target");
  }

  return classes.join(" ");
}

/**
 * The connection anchors, on a selected node that can carry an axiom. An expression node never
 * wears them: it has no identity an edit could key off (Requirement 3.2).
 */
function anchorsFor(
  node: OwlNode,
  box: ConnectorBox,
  selectedId: string | null,
  onAnchorPointerDown: (event: React.MouseEvent, id: string) => void,
) {
  if (node.id !== selectedId || isExpression(node)) {
    return null;
  }

  const left = isCard(node) ? 0 : -box.width / 2;
  const right = isCard(node) ? box.width : box.width / 2;
  const y = isCard(node) ? box.height / 2 : 0;
  return (
    <>
      {/* A visible dot with an invisible fat grab twin - the shared anchor pair. */}
      <circle className="owl-anchor canvas-anchor" cx={left} cy={y} r={4} />
      <circle className="owl-anchor canvas-anchor" cx={right} cy={y} r={4} />
      <circle
        className="owl-anchor-hit canvas-anchor-hit"
        cx={left}
        cy={y}
        r={10}
        onMouseDown={(event) => onAnchorPointerDown(event, node.id)}
      />
      <circle
        className="owl-anchor-hit canvas-anchor-hit"
        cx={right}
        cy={y}
        r={10}
        onMouseDown={(event) => onAnchorPointerDown(event, node.id)}
      />
    </>
  );
}

/** Whether an equivalence axiom touches this node - what doubles its outline, per the notation. */
function hasEquivalence(model: OwlModel, id: string): boolean {
  for (const edge of model.edges.values()) {
    if (edge.kind === "equivalent" && (edge.fromElementId === id || edge.toElementId === id)) {
      return true;
    }
  }

  return false;
}

/** The two scroll axes as the shared bars want them - everything in canvas units. */
function scrollAxesOf(model: OwlModel, view: OwlView, surface: DOMRect | null) {
  const widthPx = surface?.width || 1200;
  const heightPx = surface?.height || 600;
  const nodes = [...model.nodes.values()];

  const horizontalSpan = widthPx / view.pixelsPerUnit;
  const verticalSpan = heightPx / view.pixelsPerUnit;
  const minX = nodes.length > 0 ? Math.min(...nodes.map((node) => node.x)) : view.startX;
  const maxX = nodes.length > 0
    ? Math.max(...nodes.map((node) => node.x + nodeSizeOf(node).width))
    : view.startX + horizontalSpan;
  const minY = nodes.length > 0 ? Math.min(...nodes.map((node) => node.y)) : view.startY;
  const maxY = nodes.length > 0
    ? Math.max(...nodes.map((node) => node.y + nodeSizeOf(node).height))
    : view.startY + verticalSpan;

  return {
    horizontal: {
      viewStart: view.startX,
      viewSpan: horizontalSpan,
      ...scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: CARD_WIDTH }),
    },
    vertical: {
      viewStart: view.startY,
      viewSpan: verticalSpan,
      ...scrollExtentOf(minY, maxY, { factor: 0, minimum: 2 * HEADER_HEIGHT }),
    },
  };
}

function at(placed: { id: string; x: number; y: number }, drag: DragPreview | null): { x: number; y: number } {
  return drag && drag.id === placed.id ? { x: drag.x, y: drag.y } : { x: placed.x, y: placed.y };
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
 * One axiom: a straight line styled by what it states - dotted for subclass, the dedicated mark
 * for disjointness, a labelled arrow for a property. An edge whose end is missing draws nothing.
 */
function renderEdge(
  edge: OwlDiagramEdge,
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

  const selected = edge.id === selectedId ? " owl-selected canvas-selected" : "";
  return (
    <g
      key={edge.id}
      data-element-id={edge.id}
      onMouseDown={handlers.onMouseDown}
      onContextMenu={handlers.onContextMenu}
    >
      {/* The invisible fat grab twin the shared class defines - a thin stroke is no target. */}
      <path className="owl-edge-hit canvas-connection-hit" d={`M ${from.x} ${from.y} L ${to.x} ${to.y}`} />
      <StraightConnection
        from={from}
        to={to}
        className={`owl-edge owl-edge-${edge.kind}${selected}`}
        pathClassName="owl-edge-line canvas-connection-line"
        markerEnd={arrowFor(edge) ? `url(#${ARROWHEAD_ID})` : undefined}
      />
      {edge.label ? (
        <text className="owl-edge-label canvas-hint" x={(from.x + to.x) / 2} y={(from.y + to.y) / 2 - 6}>
          {edge.label}
        </text>
      ) : null}
    </g>
  );
}

/** Which axioms point: properties and hierarchy do, symmetric statements do not. */
function arrowFor(edge: OwlDiagramEdge): boolean {
  return edge.kind !== "equivalent" && edge.kind !== "disjoint" && edge.kind !== "expression";
}

function clampZoom(pixelsPerUnit: number): number {
  return Math.min(MAX_PIXELS_PER_UNIT, Math.max(MIN_PIXELS_PER_UNIT, pixelsPerUnit));
}
