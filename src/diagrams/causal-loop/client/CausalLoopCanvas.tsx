import { useMemo, useRef, useState } from "react";

import { ArcBow, arcBetween, normalAlong, pointAlong, type ArcBox } from "./causalLoopArc";
import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, RelationTypeDefinition, RouteEnds, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { useContextConnection, useContextPrompt } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
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

/**
 * The backend actions this canvas drives by gesture rather than by menu. Each is the same id the
 * provider answers to; the client only decides when to raise it and what source to carry.
 */
const AddVariableActionId = "causal-loop.add-variable";
const RenameVariableActionId = "causal-loop.rename-variable";
const ConnectActionId = "causal-loop.connect";


/** An element as the library carries it here: the model element plus what it draws. */
type VariableElement = DiagramModelElement & { variable: CausalLoopVariable };
type LoopElement = DiagramModelElement & { loop: CausalLoopLoop };
type LinkConnection = DiagramModelConnection & { link: CausalLoopLink };

function toArcBox(bounds: ShapeBounds): ArcBox {
  return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
}


/**
 * How far this link bows, and to which side.
 *
 * The magnitude is the notation's own look. The SIGN is the side of travel, and it is normally
 * this module's decision: always the same side, so that `A -> B` and `B -> A` land on opposite
 * sides of the chord and a two-variable loop draws as an ellipse rather than as one line with an
 * arrowhead at each end. A link the author has flipped bows the other way instead - the one
 * escape from that rule, for the case where the chosen side crosses something and the picture
 * stops reading.
 */
function bowOf(flipped: boolean): number {
  return flipped ? -ArcBow : ArcBow;
}

/**
 * The chord-bowed arc, and the self-loop's chordless ellipse pair, as one custom route: the
 * geometry anchors on the two end BOXES and bows to the side of travel, which is why the
 * route takes the endpoint bounds the library now hands a drawn connection.
 */
function arcRouteOf(flipped: boolean) {
  return {
    customRoute: "causal-loop-arc",
    path: (from: ShapePoint, to: ShapePoint, _waypoints: readonly ShapePoint[], ends?: RouteEnds) =>
      ends !== undefined
        ? arcBetween(toArcBox(ends.source), toArcBox(ends.target), bowOf(flipped)).path
        : `M ${from.x} ${from.y} L ${to.x} ${to.y}`,
  };
}

/**
 * The polarity sign beside the arrowhead and the delay strokes across the link - the
 * notation's own adornment, drawn inside the connection's group so the shared selected
 * cascade colours them with the line they describe (the 2026-09-06 adorner styling).
 */
function linkAdornment(route: { ends?: RouteEnds; highlighted?: boolean }, rawConnection: unknown) {
  const connection = rawConnection as LinkConnection;
  if (route.ends === undefined) {
    return null;
  }

  // The same bow the route drew, read from the same link: an adornment computed against the
  // unflipped arc would sit beside the line rather than on it.
  const arc = arcBetween(toArcBox(route.ends.source), toArcBox(route.ends.target), bowOf(connection.link.payload.flipped));
  const mark = polarityMark(connection.link.payload.polarity);
  const markAt = pointAlong(arc, 0.86);
  const markNormal = normalAlong(arc, 0.86);

  return (
    <>
      {connection.link.payload.delayed && (
        // The conventional delay mark: two short strokes ACROSS the link, along the curve's
        // own normal - drawn vertically they would lie along a near-vertical arc.
        // Highlighted, the delay strokes take the selection colour with the line they cross: the
        // library paints its highlight inline, so an adorner it does not draw itself must carry it.
        <g className="causal-loop-delay" style={route.highlighted === true ? { stroke: "var(--color-selected, #7c3aed)" } : undefined}>
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
          style={route.highlighted === true ? { fill: "var(--color-selected, #7c3aed)" } : undefined}
          x={markAt.x + markNormal.x * 11}
          y={markAt.y + markNormal.y * 11}
          textAnchor="middle"
        >
          {mark}
        </text>
      )}
    </>
  );
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
 * One weight step's relation type, in each bow direction: the weight steps keep the stylesheet's
 * classes as they are, and the direction is a type of its own because a route is declared by the
 * definition rather than computed per connection - the same reason the weight steps are three
 * types rather than one parameterised at draw time.
 */
function linkRelation(step: "light" | "normal" | "heavy", flipped: boolean): RelationTypeDefinition {
  return {
    id: flipped ? `link-${step}-flipped` : `link-${step}`,
    route: arcRouteOf(flipped),
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
 * What a causal loop diagram allows, stated once: pill variables that drag and select, links
 * as chord-bowed arcs with polarity and delay adornment (self-loops included), and loop
 * badges that select but never move - the identifier's marker and caption drawn at the
 * centre of the variables the loop runs through. Renaming stays the dialog it always was;
 * Arrange stays a backend action.
 */
function definitionOf(): DiagramDefinition {
  return assertValidDiagramDefinition({
    elementTypes: [
      // The label is the visible name and the one editable thing: renaming edits it in place,
      // centred over the pill. "inside" opens the editor at the element's centre.
      {
        id: "variable",
        // A pill: a box whose corner radius is half its height, which is what `rx={height / 2}`
        // said in the renderer and what the built-in says by name.
        shape: "pill",
        classNames: [
          { className: "canvas-element causal-loop-variable", on: "element" },
          { className: "canvas-node", on: "shape" },
        ],
        labels: [{ text: { path: "payload.display" }, editable: true, className: "canvas-node-label" }],
        anchors: { kind: "edge" },
        sizing: "model",
      },
      {
        id: "loop",
        // A BADGE WITH NO BODY: the polarity sweep and its caption, and nothing else. `none` is
        // what made this an element type rather than a renderer.
        shape: "none",
        classNames: [
          { className: "causal-loop-loop", on: "element" },
          { className: "causal-loop-disagrees", on: "element", when: { path: "payload.disagrees", is: "true" } },
        ],
        decorations: [
          {
            // The sweep follows the polarity: reinforcing clockwise, balancing anticlockwise -
            // how the reference tools distinguish them before anyone reads the letter. Two
            // declarations with a condition, not one function with a boolean argument.
            glyph: "path",
            d: { path: "payload.markerPath" },
            markerEnd: "arrow",
            className: "causal-loop-marker",
          },
        ],
        labels: [
          {
            text: { path: "payload.caption" },
            className: { template: "causal-loop-badge causal-loop-{payload.polarityWord}" },
          },
        ],
        anchors: { kind: "edge" },
        sizing: "model",
        draggable: false,
      },
    ],
    relationTypes: [
      linkRelation("light", false),
      linkRelation("normal", false),
      linkRelation("heavy", false),
      linkRelation("light", true),
      linkRelation("normal", true),
      linkRelation("heavy", true),
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    // The right-button drag draws a link, in the shared gesture layer - so a causal loop links
    // by dragging between variables, and this module keeps no gesture state of its own.
    connectOnRightDrag: true,
    // The diagram's own menu - Arrange diagram, a new variable where the user right-clicked - on
    // empty canvas. The library opens it for the point clicked; this canvas used to build it by
    // hand, and was the only one that did (centralized-selection, design A, "The background menu").
    backgroundMenu: true,
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
  const { executeAction } = useContextConnection();
  const { prompt, onPropose, onSubmit, onCancel } = useContextPrompt();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState<string | null>(null);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const editingId = inlineLabelElementIdOf(prompt);

  /** Runs a backend action, threading its source and surfacing any refusal. */
  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId !== undefined ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const definition = useMemo(() => definitionOf(), []);

  const diagramModel = useMemo<DiagramModel>(() => {
    const variables = [...model.variables.values()].map((variable): VariableElement => ({
      id: variable.id,
      type: "variable",
      x: variable.x,
      y: variable.y,
      width: variable.payload.width,
      height: variable.payload.height,
      label: variable.payload.display,
      payload: { display: variable.payload.display },
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
      // What the declaration reads. The sweep's path is DATA the module computes - a string a
      // binding reads - which is the distinction `d` was written as a binding for.
      payload: {
        caption: loopCaption(loop),
        polarityWord: polarityWord(loop.payload.computed),
        disagrees: loop.payload.disagrees,
        markerPath: loopMarkerPath(loop.x, loop.y - 15, 13, loop.payload.computed === LoopPolarityProto.REINFORCING),
      },
      loop,
    }));
    const links = [...model.links.values()].flatMap((link) =>
      model.variables.has(link.payload.fromElementId) && model.variables.has(link.payload.toElementId)
        ? [{
            id: link.id,
            type: link.payload.flipped ? `link-${weightStep(link)}-flipped` : `link-${weightStep(link)}`,
            sourceId: link.payload.fromElementId,
            targetId: link.payload.toElementId,
            link,
          } satisfies LinkConnection]
        : [], // the far end is not held - off screen, or never declared; a line to nothing is worse than none
    );
    return { elements: [...variables, ...loops], connections: links };
  }, [model]);

  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection); a menu action it ran and the backend
    // refused comes back here, for the rejection line.
    onActionRefused: ({ message }) => setRejection(message),
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
    // A toolbox drop carries the backend action its item names; the placement carries where it
    // landed. "Add variable" then adds one there with a calculated name and no dialog; a link
    // or loop item drops onto whatever variable it was released on, the same as its menu entry.
    onElementDropped: ({ elementType, position }) => {
      if (elementType === AddVariableActionId) {
        runAction(elementType, `new:${position.x},${position.y}`);
        return;
      }
      // A link or loop item is dropped ONTO a variable; find which one it landed on.
      const variableId = variableAtCanvas(position);
      if (variableId !== null) {
        runAction(elementType, variableId);
      } else {
        setRejection("Drop a link or a loop onto a variable.");
      }
    },
    // A link drawn by right-dragging between two variables: the shared gesture layer raises this,
    // and the module states the link (and claims the loops it closes) with no dialog.
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      const from = sourceElementId.slice("variable:".length);
      const to = targetElementId.slice("variable:".length);
      runAction(ConnectActionId, `rel:${from}->${to}`);
    },
    // Deletions stay unanswered: Delete was never a causal-loop key; removal lives in the menu
    // the backend pushes.
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

  /** The `variable:…` id of the topmost variable whose box contains a canvas point, or null. */
  const variableAtCanvas = (point: { x: number; y: number }): string | null => {
    const variables = [...model.variables.values()];
    for (let index = variables.length - 1; index >= 0; index--) {
      const variable = variables[index];
      const halfWidth = variable.payload.width / 2;
      const halfHeight = variable.payload.height / 2;
      if (
        point.x >= variable.x - halfWidth && point.x <= variable.x + halfWidth &&
        point.y >= variable.y - halfHeight && point.y <= variable.y + halfHeight
      ) {
        return variable.id;
      }
    }
    return null;
  };


  // A double-click detected by hand rather than by the browser's `dblclick`: the library
  // re-renders the pressed element on selection, so the second click lands on a fresh DOM node
  // and the browser, seeing two clicks on different nodes, never fires `dblclick`. The frame is
  // stable, so counting clicks on it is reliable.
  const lastClickRef = useRef<{ id: string; at: number }>({ id: "", at: 0 });
  const onFrameClick = (event: React.MouseEvent<HTMLDivElement>) => {
    const id = (event.target as Element).closest("[data-element-id]")?.getAttribute("data-element-id") ?? "";
    if (!id.startsWith("variable:")) {
      lastClickRef.current = { id: "", at: 0 };
      return;
    }
    const now = event.timeStamp;
    const previous = lastClickRef.current;
    if (previous.id === id && now - previous.at < 400) {
      lastClickRef.current = { id: "", at: 0 };
      // Rename the variable in place: the action returns an inline prompt the library opens over
      // the pill, rather than the dialog it used to.
      runAction(RenameVariableActionId, id);
      return;
    }
    lastClickRef.current = { id, at: now };
  };

  if (failed) {
    return <div className="causal-loop-message">This causal loop diagram could not be opened.</div>;
  }

  if (loading) {
    return <div className="causal-loop-message">Reading the diagram…</div>;
  }

  return (
    <div
      className="causal-loop-frame canvas-host"
      onClick={onFrameClick}
    >
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
          source={{ entryId, path }}
          toolboxItems={toolboxItems}
          editing={{ editingId, onPropose, onSubmit, onCancel }}
          className="causal-loop-canvas-host"
          ariaLabel="Causal loop diagram"
        />
      )}
    </div>
  );
}

export type { CausalLoopLink };
