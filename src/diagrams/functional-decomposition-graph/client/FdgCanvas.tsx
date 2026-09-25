import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  DiagramDefinition,
  ElementTypeDefinition,
  RelationTypeDefinition,
  ShapeBounds,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { useContextConnection, useContextPrompt } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import {
  FDG_ACTION_IDS,
  FDG_ADD_ACTION_PREFIX,
  FDG_ELEMENT_TYPES,
  FDG_OWNERSHIP_RELATIONS,
  FdgActions,
  FdgElementTypes,
  FdgProperties,
  FdgRelationTypes,
  FdgShortcuts,
  type FdgElementType,
  type FdgRelationType,
} from "./fdgIds";
import { applyDelta, emptyModel } from "./fdgModel";

/** The shape each type is drawn as - the library's built-ins, and nothing of this module's own. */
const SHAPES: Readonly<Record<FdgElementType, ElementTypeDefinition["shape"]>> = {
  [FdgElementTypes.uiElement]: "superellipse",
  [FdgElementTypes.dataElement]: "parallelogram",
  [FdgElementTypes.action]: "trapezoid",
  [FdgElementTypes.function]: "diode",
  [FdgElementTypes.comment]: "box",
};

/**
 * One of the four named types: its shape, its fill class, one editable label bound to the Name,
 * sized by the reader in width. The class is the only thing that tells the four apart in colour,
 * and the colour lives in `fdg.css` as a theme token.
 */
function namedType(type: FdgElementType): ElementTypeDefinition {
  return {
    id: type,
    shape: SHAPES[type],
    classNames: [
      { className: "canvas-element fdg-element", on: "element" },
      { className: `canvas-node fdg-${type}`, on: "shape" },
    ],
    labels: [{ text: { path: "payload.name" }, editable: true, truncate: true, className: "canvas-node-label fdg-label" }],
    anchors: { kind: "edge" },
    sizing: "user",
  };
}

/**
 * The Comment: a box sized in both directions, its text wrapped inside it and edited in the
 * multiline editor, and NO anchors - it is in no relation's endpoints, and showing a handle it
 * could never complete would invite a gesture the notation forbids (Requirement 5.3).
 */
const COMMENT_TYPE: ElementTypeDefinition = {
  id: FdgElementTypes.comment,
  shape: SHAPES[FdgElementTypes.comment],
  classNames: [
    { className: "canvas-element fdg-element", on: "element" },
    { className: `canvas-node fdg-${FdgElementTypes.comment}`, on: "shape" },
  ],
  labels: [{ text: { path: "payload.text" }, editable: true, wrap: true, className: "fdg-comment-text" }],
  anchors: { kind: "edge", enabled: false, visible: false },
  sizing: "user",
  resize: "both",
};

/**
 * One relation, exactly as the rules table states it: its sources, its one target type, and its
 * limit. Every relation draws the same way - a curve with an arrow at the child, and its name at
 * the midpoint, which draws nothing while the name is empty.
 */
function relation(
  id: FdgRelationType,
  sources: readonly FdgElementType[],
  target: FdgElementType,
  cardinality: { maxIntoTarget?: number; maxFromSource?: number },
): RelationTypeDefinition {
  return {
    id,
    route: "cubic-bezier",
    style: { endMarker: "arrow" },
    label: { placement: "midpoint", editable: true },
    className: `fdg-relation fdg-${id}`,
    endpoints: {
      source: { elementTypes: sources },
      target: { elementTypes: [target], anchors: "edge" },
      allowSelf: false,
      cardinality,
    },
  };
}

const { uiElement, dataElement, action, function: fn } = FdgElementTypes;

/**
 * What a functional decomposition graph allows, stated once. The backend states the same table in
 * `FdgRelations`; the canvas refuses under the pointer what the backend would refuse on write, and
 * the backend refuses it anyway, because a request is never trusted to have come from this canvas.
 */
export const FDG_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [namedType(uiElement), namedType(dataElement), namedType(action), namedType(fn), COMMENT_TYPE],
  relationTypes: [
    relation(FdgRelationTypes.uiChild, [uiElement], uiElement, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.ownsAction, [uiElement], action, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.ownsData, [uiElement, action, dataElement], dataElement, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.ownsFunction, [uiElement, action, dataElement, fn], fn, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.shows, [action], uiElement, { maxFromSource: 1 }),
  ],
  // The four ownership relations form a forest; Shows is navigation and may loop (Requirement 5.4).
  acyclic: [{ relationTypes: FDG_OWNERSHIP_RELATIONS }],
  // Every gesture the backend answers, declared, so the library dispatches an id and this module
  // never builds a keystroke. A rename opens the shared inline editor from the backend's prompt; the
  // commit then travels through that prompt, so the canvas sends nothing of its own for it.
  actions: [
    {
      id: FdgActions.rename,
      invokedBy: [{ kind: "shortcut", key: FdgShortcuts.rename }, { kind: "gesture", gesture: "activate" }],
      appliesTo: [{ kind: "element" }],
    },
    {
      id: FdgActions.renameConnection,
      invokedBy: [{ kind: "shortcut", key: FdgShortcuts.rename }],
      appliesTo: [{ kind: "connection" }],
    },
    { id: FdgActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
    { id: FdgActions.disconnect, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // Edge anchors draw no handle to start a connection from, so a relation is drawn by dragging
  // with the right button from one element's body to another's - causal loop's gesture, and the
  // only one an edge-anchored element offers.
  connectOnRightDrag: true,
});

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set(FDG_ACTION_IDS);

function isElementType(value: string): value is FdgElementType {
  return (FDG_ELEMENT_TYPES as readonly string[]).includes(value);
}

/**
 * A functional decomposition graph: screens, actions, data and functions, each owned by one parent,
 * drawn through the shared canvas library. Every library event takes the one route that can carry
 * it (the user's chat ruling of 2026-09-25): a move through the stream's `moveElementTo`, a resize
 * through `setProperty`, and everything else through a context action on one target id.
 */
export function FdgCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { watchId, executeAction, setProperty } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const { prompt, onPropose, onSubmit, onCancel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.elements.values()].map((element): DiagramModelElement => ({
      id: element.id,
      type: element.type,
      x: element.x,
      y: element.y,
      width: element.payload.width,
      height: element.payload.height,
      label: element.type === FdgElementTypes.comment ? element.payload.text : element.payload.name,
      payload: { name: element.payload.name, text: element.payload.text },
    }));
    const connections = [...model.connections.values()].flatMap((connection): DiagramModelConnection[] =>
      model.elements.has(connection.payload.fromElementId) && model.elements.has(connection.payload.toElementId)
        ? [{
            id: connection.id,
            type: connection.type,
            sourceId: connection.payload.fromElementId,
            targetId: connection.payload.toElementId,
            label: connection.payload.name || undefined,
          }]
        : [], // an end is not held - off screen, or never declared; a line to nothing is worse than none
    );
    return { elements, connections };
  }, [model]);

  /** Surfaces a refusal, whichever route it came back on. */
  const report = (error: string) => {
    if (error) {
      setRejection(error);
    }
  };

  const runAction = (actionId: string, targetId: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, elementSourceOf(targetId));
      if (!outcome.accepted) {
        report(outcome.error);
      }
    })();
  };

  const runProperty = async (propertyId: string, value: number, targetId: string): Promise<boolean> => {
    const outcome = await setProperty(propertyId, String(Math.round(value)), elementSourceOf(targetId));
    if (!outcome.accepted) {
      report(outcome.error);
    }
    return outcome.accepted;
  };

  // A move takes the stream's one `moveElementTo` (client-centralization task 8) and reports what it
  // resolves to - the backend's refusal, or "" when accepted. The position passed is the TOP-LEFT,
  // which is what the document holds; a position is what makes this an arrangement rather than a
  // re-parenting. This built its own `MoveElement` request until task 8 landed after FDG task 14 -
  // the same request and the same catch text the shared call now carries, so nothing a user sees
  // changes.
  const moveTo = async (elementId: string, left: number, top: number) => {
    report(await moveElementTo(elementId, left, top));
  };

  const events: DiagramEventHandlers = {
    // Rename, delete and disconnect, as the library dispatched them from their declarations.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        setRejection("");
        runAction(actionId, targetId);
      }
    },
    onActionRefused: ({ message }) => setRejection(message),
    // The library reports the CENTRE it drew the element at; the document holds the top-left.
    onElementMoved: ({ elementId, position }) => {
      const element = model.elements.get(elementId);
      if (element === undefined) {
        return;
      }
      setRejection("");
      void moveTo(elementId, position.x - element.payload.width / 2, position.y - element.payload.height / 2);
    },
    // A resize is a size. Dragging the left or top edge also moves the top-left, because the far
    // edge stays put - and a property carries one value, so that edge is a size and then a move.
    onElementResized: ({ elementId, side, bounds }) => {
      setRejection("");
      const horizontal = side === "left" || side === "right";
      void (async () => {
        const sized = await runProperty(
          horizontal ? FdgProperties.width : FdgProperties.height,
          horizontal ? bounds.width : bounds.height,
          elementId,
        );
        if (sized && (side === "left" || side === "top")) {
          await moveTo(elementId, bounds.x, bounds.y);
        }
      })();
    },
    // The whole gesture in one stateless call: the relation in the action id, both ends in the target.
    onConnectionDrawn: ({ relationType, sourceElementId, targetElementId }) => {
      setRejection("");
      runAction(FdgActions.connect(relationType as FdgRelationType), `rel:${sourceElementId}->${targetElementId}`);
    },
    // A toolbox drop carries the element type; the placement is the drop's centre.
    // The backend's toolbox drops its add action (`fdg.add.<type>`), as every module's does; a toolbox
    // the library derived from the definition drops the bare type. Both mean the same add.
    onElementDropped: ({ elementType, position }) => {
      const type = elementType.startsWith(FDG_ADD_ACTION_PREFIX) ? elementType.slice(FDG_ADD_ACTION_PREFIX.length) : elementType;
      if (!isElementType(type)) {
        return;
      }
      setRejection("");
      runAction(FdgActions.add(type), `new:${position.x},${position.y}`);
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: viewReportOf(client, projectId, watchId, path),
    convert: () => ({
      minX: viewport?.x ?? 0,
      minY: viewport?.y ?? 0,
      maxX: (viewport?.x ?? 0) + (viewport?.width ?? 0),
      maxY: (viewport?.y ?? 0) + (viewport?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  if (failed) {
    return (
      <div className="fdg-canvas canvas-host canvas-host-message">
        <p>This functional decomposition graph could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="fdg-canvas canvas-host" role="application" aria-label="Functional decomposition graph">
      <DiagramCanvas
        definition={FDG_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        editing={{ editingId, onPropose, onSubmit, onCancel }}
        ariaLabel="Functional decomposition graph"
        className="fdg-surface"
      />
      {loading ? <p className="fdg-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="fdg-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}
