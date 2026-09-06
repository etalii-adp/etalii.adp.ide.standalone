import { useMemo, useState } from "react";

import { arcBetween, normalAlong, pointAlong, type ArcBox } from "./causalLoopArc";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomShapeRef, CustomShapeState, DiagramDefinition, RelationTypeDefinition, RouteEnds, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { ContextSelectionAction } from "@client/generated/context_pb";
import {
  LoopPolarityProto,
  loopCaption,
  polarityMark,
  weightStep,
  type CausalLoopLink,
  type CausalLoopLoop,
  type CausalLoopVariable,
} from "./causalLoopModel";
import { useCausalLoopStream } from "./useCausalLoopStream";

export interface CausalLoopCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** An element as the library carries it here: the model element plus what it draws. */
type VariableElement = DiagramModelElement & { variable: CausalLoopVariable };
type LoopElement = DiagramModelElement & { loop: CausalLoopLoop };
type LinkConnection = DiagramModelConnection & { link: CausalLoopLink };

function toArcBox(bounds: ShapeBounds): ArcBox {
  return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
}

/** The arc's own edge answer, for the connector geometry the library asks per attachment. */
function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centreX = bounds.x + bounds.width / 2;
  const centreY = bounds.y + bounds.height / 2;
  const dx = towards.x - centreX;
  const dy = towards.y - centreY;
  const scaleX = dx !== 0 ? bounds.width / 2 / Math.abs(dx) : Number.POSITIVE_INFINITY;
  const scaleY = dy !== 0 ? bounds.height / 2 / Math.abs(dy) : Number.POSITIVE_INFINITY;
  const scale = Math.min(scaleX, scaleY, 1);
  return { x: centreX + dx * scale, y: centreY + dy * scale };
}

/**
 * The chord-bowed arc, and the self-loop's chordless ellipse pair, as one custom route: the
 * geometry anchors on the two end BOXES and bows to the side of travel, which is why the
 * route takes the endpoint bounds the library now hands a drawn connection.
 */
const arcRoute = {
  customRoute: "causal-loop-arc",
  path: (from: ShapePoint, to: ShapePoint, _waypoints: readonly ShapePoint[], ends?: RouteEnds) =>
    ends !== undefined
      ? arcBetween(toArcBox(ends.source), toArcBox(ends.target)).path
      : `M ${from.x} ${from.y} L ${to.x} ${to.y}`,
};

/**
 * The polarity sign beside the arrowhead and the delay strokes across the link - the
 * notation's own adornment, drawn inside the connection's group so the shared selected
 * cascade colours them with the line they describe (the 2026-09-06 adorner styling).
 */
function linkAdornment(route: { ends?: RouteEnds }, rawConnection: unknown) {
  const connection = rawConnection as LinkConnection;
  if (route.ends === undefined) {
    return null;
  }

  const arc = arcBetween(toArcBox(route.ends.source), toArcBox(route.ends.target));
  const mark = polarityMark(connection.link.payload.polarity);
  const markAt = pointAlong(arc, 0.86);
  const markNormal = normalAlong(arc, 0.86);

  return (
    <>
      {connection.link.payload.delayed && (
        // The conventional delay mark: two short strokes ACROSS the link, along the curve's
        // own normal - drawn vertically they would lie along a near-vertical arc.
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
        <text className="causal-loop-polarity" x={markAt.x + markNormal.x * 11} y={markAt.y + markNormal.y * 11} textAnchor="middle">
          {mark}
        </text>
      )}
    </>
  );
}

/** One weight step's relation type; three of them keep the stylesheet's weight classes as they are. */
function linkRelation(step: "light" | "normal" | "heavy"): RelationTypeDefinition {
  return {
    id: `link-${step}`,
    route: arcRoute,
    style: { endMarker: "arrow" },
    className: `causal-loop-link causal-loop-weight-${step}`,
    adorn: linkAdornment,
    endpoints: {
      source: { elementTypes: ["variable"] },
      target: { elementTypes: ["variable"], anchors: "edge" },
      // A link may state its own variable on both ends; the self-loop draws as the
      // chordless ellipse pair the arc route already knows.
      allowSelf: true,
    },
  };
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

/**
 * A near-complete circle with a gap for its arrowhead - the loop marker the notation draws at
 * the centre of a feedback loop. Two arcs rather than one, because a single SVG elliptical arc
 * cannot exceed a half turn without the large-arc flag.
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
 * What a causal loop diagram allows, stated once: pill variables that drag and select, links
 * as chord-bowed arcs with polarity and delay adornment (self-loops included), and loop
 * badges that select but never move - the identifier's marker and caption drawn at the
 * centre of the variables the loop runs through. Renaming stays the dialog it always was;
 * Arrange stays a backend action.
 */
function definitionOf(onActivate: (id: string) => void): DiagramDefinition {
  const variableShape: CustomShapeRef = {
    customShape: "causal-loop-variable",
    render: (raw, state?: CustomShapeState) => {
      const element = raw as VariableElement;
      const { width, height } = element.variable.payload;
      return (
        <BoxElement
          className={`canvas-element causal-loop-variable${state?.selected ? " canvas-selected" : ""}`}
          x={element.x - width / 2}
          y={element.y - height / 2}
          width={width}
          height={height}
          rx={height / 2}
          label={element.variable.payload.display}
          boxClassName="canvas-node"
          labelClassName="canvas-node-label"
          labelX={width / 2}
          onDoubleClick={() => onActivate(element.id)}
        />
      );
    },
    edgePoint: boxEdgePoint,
  };

  const loopShape: CustomShapeRef = {
    customShape: "causal-loop-badge",
    render: (raw, state?: CustomShapeState) => {
      const element = raw as LoopElement;
      const loop = element.loop;
      return (
        <g className={`causal-loop-loop${loop.payload.disagrees ? " causal-loop-disagrees" : ""}${state?.selected ? " canvas-selected" : ""}`}>
          {/* The sweep follows the polarity: reinforcing clockwise, balancing anticlockwise -
              how the reference tools distinguish them before anyone reads the letter. */}
          <path
            className="causal-loop-marker"
            d={loopMarkerPath(element.x, element.y - 15, 13, loop.payload.computed === LoopPolarityProto.REINFORCING)}
            markerEnd="url(#library-arrow)"
          />
          <text
            className={`causal-loop-badge causal-loop-${polarityWord(loop.payload.computed)}`}
            x={element.x}
            y={element.y}
            textAnchor="middle"
          >
            {loopCaption(loop)}
          </text>
        </g>
      );
    },
    edgePoint: boxEdgePoint,
  };

  return assertValidDiagramDefinition({
    elementTypes: [
      { id: "variable", shape: variableShape, anchors: { kind: "edge" }, sizing: "model" },
      { id: "loop", shape: loopShape, anchors: { kind: "edge" }, sizing: "model", draggable: false },
    ],
    relationTypes: [linkRelation("light"), linkRelation("normal"), linkRelation("heavy")],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  });
}

/**
 * Draws a causal loop diagram, through the diagram library, in the notation the world uses:
 * variables joined by polarised arrows, delays marked with strokes across the link, and each
 * loop carrying its R or B identifier among the variables it runs through. Every gesture
 * answers with the same backend calls the hand-built canvas made.
 */
export function CausalLoopCanvas({ projectId, entryId, path }: CausalLoopCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useCausalLoopStream(projectId, path);
  const { select, executeAction } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState<string | null>(null);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = selectedElementIdOf(selection);

  const definition = useMemo(
    () => definitionOf((id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.ACTIVATE))),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [entryId, path, select],
  );

  const diagramModel = useMemo<DiagramModel>(() => {
    const variables = [...model.variables.values()].map((variable): VariableElement => ({
      id: variable.id,
      type: "variable",
      x: variable.x,
      y: variable.y,
      width: variable.payload.width,
      height: variable.payload.height,
      label: variable.payload.display,
      variable,
    }));
    const loops = [...model.loops.values()].map((loop): LoopElement => ({
      id: loop.id,
      type: "loop",
      x: loop.x,
      y: loop.y,
      // The badge and its marker together, roughly: enough box for a press to land on.
      width: 60,
      height: 48,
      loop,
    }));
    const links = [...model.links.values()].flatMap((link) =>
      model.variables.has(link.payload.fromElementId) && model.variables.has(link.payload.toElementId)
        ? [{
            id: link.id,
            type: `link-${weightStep(link)}`,
            sourceId: link.payload.fromElementId,
            targetId: link.payload.toElementId,
            link,
          } satisfies LinkConnection]
        : [], // the far end is not held - off screen, or never declared; a line to nothing is worse than none
    );
    return { elements: [...variables, ...loops], connections: links };
  }, [model]);

  const librarySelection = useMemo<DiagramSelection>(() => {
    if (!selectedId) {
      return [];
    }
    if (model.links.has(selectedId)) {
      return [{ kind: "connection", id: selectedId }];
    }
    return model.variables.has(selectedId) || model.loops.has(selectedId) ? [{ kind: "element", id: selectedId }] : [];
  }, [selectedId, model]);

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) =>
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null),
    onElementMoved: ({ elementId, position }) => {
      if (!model.variables.has(elementId)) {
        return; // a loop badge has no position of its own; the definition already refuses the drag
      }
      void (async () => {
        const error = await moveElementTo(elementId, position.x, position.y);
        if (error) {
          setRejection(error);
        }
      })();
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
    // Deliberately unanswered: drops (the hand-built canvas never wired them) and deletions
    // (Delete was never a causal-loop key; removal lives in the menu the backend pushes).
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
      minX: viewport?.x ?? 0,
      minY: viewport?.y ?? 0,
      maxX: (viewport?.x ?? 0) + (viewport?.width ?? 0),
      maxY: (viewport?.y ?? 0) + (viewport?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  /**
   * The menu for the diagram itself, opened on empty canvas: with nothing selected the
   * diagram is the subject - Arrange diagram lives there - and the point is carried as a
   * placement id so the backend can put a new variable where the user actually right-clicked.
   * Element and link menus are the library's, through the context integration below.
   */
  const surfaceMenu = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  const onSurfaceContextMenu = (event: React.MouseEvent) => {
    const target = event.target as Element;
    if (target.closest("[data-element-id]") !== null || target.closest("[data-connection-id]") !== null) {
      return; // an element's or a link's own menu; the library answers it
    }
    const svg = target.closest("svg");
    if (svg === null || viewport === null) {
      return;
    }
    const rect = svg.getBoundingClientRect();
    const scale = viewport.width / Math.max(rect.width, 1);
    const x = viewport.x + (event.clientX - rect.left) * scale;
    const y = viewport.y + (event.clientY - rect.top) * scale;
    surfaceMenu.openMenuAt(event, `new:${x},${y}`);
  };

  if (failed) {
    return <div className="causal-loop-message">This causal loop diagram could not be opened.</div>;
  }

  if (loading) {
    return <div className="causal-loop-message">Reading the diagram…</div>;
  }

  return (
    <div className="causal-loop-frame" onContextMenu={onSurfaceContextMenu}>
      {rejection !== null && (
        <div className="canvas-rejection" role="status" onClick={() => setRejection(null)}>
          {rejection}
        </div>
      )}
      {model.variables.size === 0 ? (
        <div className="causal-loop-message">This causal loop diagram states no variables yet.</div>
      ) : (
        <DiagramCanvas
          definition={definition}
          model={diagramModel}
          events={events}
          selection={librarySelection}
          toolboxItems={toolboxItems}
          context={{
            selectionKey: selectionKey ?? undefined,
            actions,
            selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
            executeAction: (actionId) => void executeAction(actionId),
          }}
          className="causal-loop-canvas-host"
          ariaLabel="Causal loop diagram"
        />
      )}

      <ContextMenu
        open={surfaceMenu.menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          surfaceMenu.closeMenu();
          void executeAction(action.id);
        })}
        position={surfaceMenu.menuPosition ?? { x: 0, y: 0 }}
        onClose={surfaceMenu.closeMenu}
      />
    </div>
  );
}

export type { CausalLoopLink };
