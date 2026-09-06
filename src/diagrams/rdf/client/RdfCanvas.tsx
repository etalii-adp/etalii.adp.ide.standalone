import { useMemo, useState } from "react";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps as ShellCanvasProps } from "@client/shell/panels/diagramCanvas";
import { ContextSelectionAction, type ContextShortcut } from "@client/generated/context_pb";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import type { DiagramDefinition, CustomShapeRef } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { DiagramEventHandlers, DiagramSelection, DiagramViewport } from "@client/canvas/library/api/diagramEvents";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useRdfStream } from "./useRdfStream";
import type { RdfNode } from "./rdfModel";

/** A card's drawn width, in the module's own canvas units - matching the backend layout's spacing. */
export const NODE_WIDTH = 220;

/** The card's parts, stacked: title, the type badges, then one line per literal row. */
const HEADER_HEIGHT = 30;
const BADGES_HEIGHT = 16;
const ROW_HEIGHT = 16;
const FOOTER_PADDING = 10;

/** A card's height follows its content: title, badges when worn, one line per row. */
export function nodeHeightOf(node: RdfNode): number {
  return HEADER_HEIGHT
    + (node.typeBadges.length > 0 ? BADGES_HEIGHT : 0)
    + node.rows.length * ROW_HEIGHT
    + FOOTER_PADDING;
}

/** The Delete key as the backend shortcut it has always travelled as. */
function deleteShortcut(): ContextShortcut {
  return { key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut;
}

/** An element as the library carries it here: the model element plus the card it draws. */
type CardElement = DiagramModelElement & { card: RdfNode };

/**
 * The resource card as a first-class custom shape: title, type badges, one line per literal
 * row - content only this module understands, rendered by it, while hit-testing, anchoring
 * and connecting stay the library's (diagram-library Requirement 2.1). The card is the proof
 * that a custom shape is a citizen, not a decorated box.
 */
const cardShape: CustomShapeRef = {
  customShape: "resource-card",
  render: (raw) => {
    const element = raw as CardElement;
    const node = element.card;
    const height = nodeHeightOf(node);
    const left = element.x - NODE_WIDTH / 2;
    const top = element.y - height / 2;
    const rowsStart = HEADER_HEIGHT + (node.typeBadges.length > 0 ? BADGES_HEIGHT : 0);

    return (
      <BoxElement
        className={`rdf-node canvas-element${node.blank ? " rdf-node-blank" : ""}`}
        x={left}
        y={top}
        width={NODE_WIDTH}
        height={height}
        label={node.display}
        boxClassName="rdf-node-box canvas-node"
        labelClassName="rdf-label canvas-node-label"
        labelY={HEADER_HEIGHT / 2 + 5}
      >
        {node.typeBadges.length > 0 ? (
          <text className="rdf-badges" x={8} y={HEADER_HEIGHT - 6 + BADGES_HEIGHT}>
            {node.typeBadges.join(" · ")}
          </text>
        ) : null}
        {node.rows.map((row, index) => (
          <text key={`${row.predicate}-${index}`} className="rdf-row" x={8} y={rowsStart + (index + 1) * ROW_HEIGHT - 4}>
            {`${row.predicate}: ${row.value}${row.annotation ? ` ${row.annotation}` : ""}`}
          </text>
        ))}
      </BoxElement>
    );
  },
  edgePoint: (bounds, towards) => {
    const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
    return edgePointOf(
      { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
      towards.x - centre.x,
      towards.y - centre.y,
    );
  },
};

/**
 * What an RDF data graph allows, stated once: resource cards that drag and connect from
 * their side anchors, blank cards that connect to but never from (the identity boundary
 * starts at the gesture - a blank offers no anchors), and one straight, arrowed,
 * predicate-labelled statement between them. Manual layout only: the backend lays out, and
 * the drawn positions are the registration's `layout:` block.
 */
const RDF_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "resource",
      shape: cardShape,
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "w" },
          { side: "right", at: 0.5, name: "e" },
        ],
      },
      sizing: "model",
    },
    { id: "blank", shape: cardShape, anchors: { kind: "edge" }, sizing: "model" },
  ],
  relationTypes: [
    {
      id: "statement",
      route: "straight",
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6 },
      endpoints: {
        source: { elementTypes: ["resource"] },
        target: { elementTypes: ["resource", "blank"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/**
 * The data graph, drawn through the diagram library: the graph-shaped reference canvas
 * (diagram-library Requirement 9.3). The module supplies the definition above, folds the
 * stream into the library's model, and answers events - reposition through the layout path
 * (blank nodes refused backend-side), anchor drags as one stateless `rel:` gesture, toolbox
 * drops as `new:` placements, the shared menu and selection seams unchanged. Everything
 * gestural, geometric and rendered-but-generic left this file with the migration.
 */
export function RdfCanvas({ projectId, entryId, path }: ShellCanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useRdfStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<DiagramViewport | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.nodes.values()].map((node): CardElement => ({
      id: node.id,
      type: node.blank ? "blank" : "resource",
      x: node.x + NODE_WIDTH / 2,
      y: node.y + nodeHeightOf(node) / 2,
      width: NODE_WIDTH,
      height: nodeHeightOf(node),
      label: node.display,
      card: node,
    }));
    const connections = [...model.edges.values()].map((edge) => ({
      id: edge.id,
      type: "statement",
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.predicate,
    }));
    return { elements, connections };
  }, [model]);

  /** The backend's push is the selection; the canvas renders it and never decides (Requirement 7.1). */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (selectedId === null) {
      return [];
    }
    return [{ kind: model.edges.has(selectedId) ? "connection" : "element", id: selectedId }];
  }, [selectedId, model.edges]);

  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(sourceId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) =>
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null),
    onElementMoved: ({ elementId, position }) => {
      const node = model.nodes.get(elementId);
      if (node === undefined) {
        return;
      }
      setRejection("");
      // The authored position, raw: the layout block stores what the author placed, and a
      // blank node's refusal comes back from the backend with its sentence.
      void (async () => {
        const error = await moveElementTo(elementId, position.x - NODE_WIDTH / 2, position.y - nodeHeightOf(node) / 2);
        if (error) {
          setRejection(error);
        }
      })();
    },
    // The whole gesture in one stateless rel: call; the predicate is asked in a dialog -
    // the event carries the payload the context channel cannot (Requirement 7.3).
    onConnectionDrawn: ({ sourceElementId, targetElementId }) =>
      runAction("rdf.connect", `rel:${sourceElementId}->${targetElementId}`),
    // A drop carries the backend's own action id; the drop point becomes its placement.
    onElementDropped: ({ elementType, position }) => runAction(elementType, `new:${position.x},${position.y}`),
    // Deletion stays the backend's: the key travels as data against the selection, exactly
    // the shortcut the old keyboard path sent - the backend owns the key-to-action table.
    onElementDeleted: ({ elementId }) => runShortcut(deleteShortcut(), elementId),
    onConnectionDeleted: ({ connectionId }) => runShortcut(deleteShortcut(), connectionId),
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  // What the reader can see, reported once it settles - the same debounce discipline as
  // before the migration, now observing the library's one view-changed signal.
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

  /** F2 travels to the backend as data; Delete is the library's event, handled above. */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }
    const shortcut = structuralShortcutFor(event, ["F2"]);
    if (!shortcut) {
      return;
    }
    event.preventDefault();
    runShortcut(shortcut, selectedId);
  };

  if (failed) {
    return (
      <div className="rdf-canvas canvas-host rdf-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="rdf-canvas canvas-host" role="application" aria-label="RDF graph" onKeyDown={onKeyDown}>
      <DiagramCanvas
        definition={RDF_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        context={{
          selectionKey: selectionKey ?? undefined,
          actions,
          selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
          executeAction: (actionId) => runAction(actionId, selectedId ?? undefined),
        }}
        ariaLabel="RDF graph"
        className="rdf-surface"
      />
      {model.truncation ? (
        <p className="rdf-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} resources — edits are withheld on this truncated view`}
        </p>
      ) : null}
      {loading ? <p className="rdf-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="rdf-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}
