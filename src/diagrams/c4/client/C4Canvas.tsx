import { useMemo, useState } from "react";

import { StyledBoxElement } from "@client/canvas/elements/styled-box/StyledBoxElement";
import { FrameElement } from "@client/canvas/elements/frame/FrameElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementSelectionOf, elementSourceOf, selectedElementIdOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomShapeRef, CustomShapeState, DiagramDefinition, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { ContextSelectionAction, type ContextShortcut } from "@client/generated/context_pb";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { C4Model, C4Node, C4BoundaryBox } from "./c4Model";
import { useC4Stream } from "./useC4Stream";

/**
 * Where an element's name sits inside its box, in canvas units: `StyledBoxElement` draws the
 * name's baseline 22 below the box's top, so a 20-tall editor starting 6 below the top covers
 * that line and nothing else. The inset label rule carries these as definition data.
 */
const NAME_TOP = 6;
const NAME_HEIGHT = 20;
const NAME_INSET = 4;

export interface C4CanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** An element as the library carries it here: the model element plus the node it draws. */
type C4NodeElement = DiagramModelElement & { node: C4Node };
type C4BoundaryElement = DiagramModelElement & { boundary: C4BoundaryBox };

function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/**
 * One element card as a first-class custom shape: the shared styled box with the palette the
 * backend resolved - shape, background, colour, the three text lines - while hit-testing,
 * edge attachment and dragging stay the library's.
 */
const cardShape: CustomShapeRef = {
  customShape: "c4-card",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as C4NodeElement;
    const { name, typeLine, description, width, height, style } = element.node.payload;
    return (
      <StyledBoxElement
        className={`c4-node${state?.selected ? " c4-node-focused" : ""}${state?.dragging ? " c4-node-dragging" : ""}`}
        x={element.x}
        y={element.y}
        width={width}
        height={height}
        shape={style?.shape ?? "RoundedBox"}
        background={style?.background ?? "#1168bd"}
        color={style?.color ?? "#ffffff"}
        name={name}
        typeLine={typeLine}
        description={description}
        nameClassName="c4-node-name"
        typeClassName="c4-node-type"
        descriptionClassName="c4-node-description"
        role="button"
        aria-label={name}
      />
    );
  },
  edgePoint: boxEdgePoint,
};

/** The dashed rectangle around a system's containers or a container's components - inert. */
const boundaryShape: CustomShapeRef = {
  customShape: "c4-boundary",
  render: (raw) => {
    const element = raw as C4BoundaryElement;
    const { name, kind, width, height } = element.boundary.payload;
    return (
      <FrameElement
        className="c4-boundary"
        x={element.x}
        y={element.y}
        width={width}
        height={height}
        label={`${name} [${kind}]`}
        labelClassName="c4-boundary-label"
      />
    );
  },
  edgePoint: boxEdgePoint,
};

/**
 * What a C4 view allows, stated once: element cards that drag, rename their NAME line in
 * place and speak F2/Delete/Insert; boundaries that enclose and ignore every gesture; one
 * dashed, arrowed, labelled relationship whose editor opens with the authored description
 * rather than the drawn "description [technology]" string. Everything drawn was decided by
 * the backend - the canvas stays a renderer, not a second opinion about what C4 is.
 */
const C4_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "element",
      shape: cardShape,
      anchors: { kind: "edge" },
      sizing: "model",
      label: { placement: "inset", editable: true, insetTop: NAME_TOP, insetHeight: NAME_HEIGHT, insetX: NAME_INSET },
    },
    { id: "boundary", shape: boundaryShape, anchors: { kind: "edge" }, sizing: "model", draggable: false },
  ],
  relationTypes: [
    {
      id: "relationship",
      route: "straight",
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6, editable: true },
      className: "c4-relationship-group",
      lineClassName: "c4-relationship-line",
      endpoints: {
        source: { elementTypes: ["element"] },
        target: { elementTypes: ["element"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/**
 * Renders one C4 view, through the diagram library. Everything it draws was decided by the
 * backend - the boxes at the sizes it measured, the palette it resolved, the title and the
 * key it composed - and every gesture answers with the same backend calls the hand-built
 * canvas made: a drag as `moveElementTo`, a drop as the entry's own action with the target
 * element as parent, the keys as data against the selection.
 */
export function C4Canvas({ projectId, entryId, path }: C4CanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useC4Stream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = useMemo(() => selectedElementIdOf(selection), [selection]);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

  const diagramModel = useMemo<DiagramModel>(() => {
    const boundaries = [...model.boundaries.values()].map((boundary): C4BoundaryElement => ({
      id: boundary.id,
      type: "boundary",
      x: boundary.x,
      y: boundary.y,
      width: boundary.payload.width,
      height: boundary.payload.height,
      label: `${boundary.payload.name} [${boundary.payload.kind}]`,
      boundary,
    }));
    const nodes = [...model.nodes.values()].map((node): C4NodeElement => ({
      id: node.id,
      type: "element",
      x: node.x,
      y: node.y,
      width: node.payload.width,
      height: node.payload.height,
      label: node.payload.name,
      node,
    }));
    const relationships = [...model.relationships.values()].map((relationship) => {
      const p = relationship.payload;
      const label = p.technology ? `${p.description} [${p.technology}]` : p.description;
      return {
        id: relationship.id,
        type: "relationship",
        sourceId: p.sourceId,
        targetId: p.destinationId,
        // The drawn string decorates the one authored value; the editor opens with the value.
        label: label ? (p.interactionOrder ? `${p.interactionOrder}. ${label}` : label) : undefined,
        editValue: p.description,
      };
    });
    // Boundaries first, so everything they enclose draws on top of them.
    return { elements: [...boundaries, ...nodes], connections: relationships };
  }, [model]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (!selectedId) {
      return [];
    }
    if (model.relationships.has(selectedId)) {
      return [{ kind: "connection", id: selectedId }];
    }
    return model.nodes.has(selectedId) ? [{ kind: "element", id: selectedId }] : [];
  }, [selectedId, model]);

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void executeShortcut(shortcut, elementSourceOf(sourceId));
  };

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) => {
      // A press on a boundary was always a press on the background - the frame never had a
      // hit surface of its own - so it deselects rather than selecting the inert box.
      if (next.length > 0 && model.boundaries.has(next[0].id)) {
        select(null);
        return;
      }
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null);
    },
    onElementMoved: ({ elementId, position }) => {
      if (!model.nodes.has(elementId)) {
        return;
      }
      // Nothing optimistic: the element stays where it was until the backend's delta says
      // otherwise, so what is drawn is always what was recorded.
      void moveElementTo(elementId, position.x, position.y);
    },
    onElementDropped: ({ elementType, position }) => {
      // The entry carries the backend's own action id. Dropped on an element, that element
      // becomes the new one's parent - which is how C4's containment gets decided by the
      // gesture; on empty canvas only what stands alone can land, and the backend refuses
      // the rest with a sentence.
      const target = [...model.nodes.values()].reverse().find((node) => {
        const { width, height } = node.payload;
        return Math.abs(position.x - node.x) <= width / 2 && Math.abs(position.y - node.y) <= height / 2;
      });
      void executeAction(elementType, target !== undefined ? elementSourceOf(target.id) : undefined);
    },
    // Delete travels as the backend shortcut it always was, raised by the library's key path.
    onElementDeleted: ({ elementId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, elementId),
    onConnectionDeleted: ({ connectionId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, connectionId),
    onViewChanged: ({ viewport: next }) => setViewport(next),
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

  /** F2 and Insert travel to the backend as data; Delete is the library's event, handled above. */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }
    const shortcut = structuralShortcutFor(event, ["F2", "Insert"]);
    if (!shortcut) {
      return;
    }
    event.preventDefault();
    runShortcut(shortcut, selectedId);
  };

  if (failed) {
    return (
      <div className="c4-canvas" data-testid="c4-canvas">
        <div className="c4-canvas-unavailable" role="alert">
          This diagram is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  return (
    <div className="c4-canvas" data-testid="c4-canvas" onKeyDown={onKeyDown}>
      {loading ? (
        <div className="c4-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <>
          {/* C4 requires every diagram to carry a title describing its type and scope. */}
          {model.view && (
            <div className="c4-canvas-title" data-testid="c4-title">
              {model.view.title}
            </div>
          )}
          <DiagramCanvas
            definition={C4_DEFINITION}
            model={diagramModel}
            events={events}
            selection={librarySelection}
            toolboxItems={toolboxItems}
            context={{
              selectionKey: selectionKey ?? undefined,
              actions,
              selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
              executeAction: (actionId) => void executeAction(actionId, selectedId ? elementSourceOf(selectedId) : undefined),
            }}
            editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
            className="c4-canvas-host"
            scrollbarsClassName="c4-scrollbars"
            ariaLabel={model.view?.title ?? "C4 diagram"}
          />

          {/* C4 requires a key explaining every shape and colour the diagram uses, so it can be
              read without accompanying narrative. Built from what the backend actually drew. */}
          {model.view && model.view.legend.length > 0 && (
            <div className="c4-canvas-legend" data-testid="c4-legend">
              <span className="c4-legend-title">Key</span>
              {model.view.legend.map((entry) => (
                <span className="c4-legend-entry" key={entry.label}>
                  <span
                    className="c4-legend-swatch"
                    style={{ background: entry.style?.background, borderColor: entry.style?.background }}
                  />
                  {entry.label}
                </span>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

export type { C4Model };
