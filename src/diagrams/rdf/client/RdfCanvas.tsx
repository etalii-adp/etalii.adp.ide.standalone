import { useMemo, useState } from "react";
import { elementSourceOf } from "@client/canvas/selection";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps as ShellCanvasProps } from "@client/shell/panels/diagramCanvas";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { DiagramEventHandlers, DiagramViewport } from "@client/canvas/library/api/diagramEvents";
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



/** An element as the library carries it here: the model element plus the card it draws. */
type CardElement = DiagramModelElement & { card: RdfNode };

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
      shape: "box",
      // THE CARD THE COLLECTION BINDING WAS DESIGNED FOR. A header, a badge line when the
      // resource wears types, and ONE LINE PER LITERAL ROW - which is the case a fixed set of
      // named slots cannot reach, and the reason `labels` binds to a collection at all.
      classNames: [
        // On the SHAPE rather than the element group: the module's tests read these as
        // descendants of the element, which is where the renderer put them.
        { className: "rdf-node canvas-element" },
        { className: "rdf-node-blank", when: { path: "payload.blank", is: "true" } },
        { className: "rdf-node-box canvas-node", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.display" },
          anchorTo: "top",
          offset: { x: 0, y: HEADER_HEIGHT / 2 + 5 },
          truncate: true,
          className: "rdf-label canvas-node-label",
        },
        {
          text: { path: "payload.typeBadges", each: { path: "text" }, join: " · " },
          when: { path: "payload.typeBadges", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: HEADER_HEIGHT - 6 + BADGES_HEIGHT },
          align: "start",
          insetX: 8,
          className: "rdf-badges",
        },
        {
          text: { path: "payload.rows", each: { template: "{predicate}: {value} {annotation}" } },
          anchorTo: "top",
          offset: { x: 0, y: 0 },
          align: "start",
          insetX: 8,
          // Where the rows begin depends on whether this card wears badges - which the module
          // already computes to size the card, and which a fixed start would get wrong by one
          // row on every card that has them.
          stack: { lineHeight: ROW_HEIGHT, start: { path: "payload.rowsStart" } },
          className: "rdf-row",
        },
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "w" },
          { side: "right", at: 0.5, name: "e" },
        ],
      },
      sizing: "model",
    },
    {
      id: "blank",
      shape: "box",
      // THE CARD THE COLLECTION BINDING WAS DESIGNED FOR. A header, a badge line when the
      // resource wears types, and ONE LINE PER LITERAL ROW - which is the case a fixed set of
      // named slots cannot reach, and the reason `labels` binds to a collection at all.
      classNames: [
        // On the SHAPE rather than the element group: the module's tests read these as
        // descendants of the element, which is where the renderer put them.
        { className: "rdf-node canvas-element" },
        { className: "rdf-node-blank", when: { path: "payload.blank", is: "true" } },
        { className: "rdf-node-box canvas-node", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.display" },
          anchorTo: "top",
          offset: { x: 0, y: HEADER_HEIGHT / 2 + 5 },
          truncate: true,
          className: "rdf-label canvas-node-label",
        },
        {
          text: { path: "payload.typeBadges", each: { path: "text" }, join: " · " },
          when: { path: "payload.typeBadges", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: HEADER_HEIGHT - 6 + BADGES_HEIGHT },
          align: "start",
          insetX: 8,
          className: "rdf-badges",
        },
        {
          text: { path: "payload.rows", each: { template: "{predicate}: {value} {annotation}" } },
          anchorTo: "top",
          offset: { x: 0, y: 0 },
          align: "start",
          insetX: 8,
          // Where the rows begin depends on whether this card wears badges - which the module
          // already computes to size the card, and which a fixed start would get wrong by one
          // row on every card that has them.
          stack: { lineHeight: ROW_HEIGHT, start: { path: "payload.rowsStart" } },
          className: "rdf-row",
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
    },
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
  // WHAT THIS READING OFFERS, AND WHAT INVOKES IT. F2 was a hand-written key list in this
  // canvas and the delete was a keystroke it built by hand to describe a gesture the library
  // had already handed it. Declared, the library derives the key set and dispatches an action
  // id; the backend still holds the key-to-action table, which is why the handler says which
  // shortcut each action travels as.
  actions: [
    { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
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
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<DiagramViewport | null>(null);


  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.nodes.values()].map((node): CardElement => ({
      id: node.id,
      type: node.blank ? "blank" : "resource",
      x: node.x + NODE_WIDTH / 2,
      y: node.y + nodeHeightOf(node) / 2,
      width: NODE_WIDTH,
      height: nodeHeightOf(node),
      label: node.display,
      // What the declaration reads. `rowsStart` is the same number `nodeHeightOf` already uses
      // to size the card: where the rows begin, below the badges when there are any.
      payload: {
        display: node.display,
        blank: node.blank,
        typeBadges: node.typeBadges.map((text) => ({ text })),
        rows: node.rows,
        rowsStart: HEADER_HEIGHT + (node.typeBadges.length > 0 ? BADGES_HEIGHT : 0) + ROW_HEIGHT - 4,
      },
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

  const runAction = (actionId: string, sourceId?: string) => {
    // A refusal needs nothing here: the call reports it to the library's refusal line.
    void executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
  };

  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection), and so is the refusal line: every call
    // here, and every menu action the library runs, reports its own refusal there
    // (client-centralization Requirement 2).
    onElementMoved: ({ elementId, position }) => {
      const node = model.nodes.get(elementId);
      if (node === undefined) {
        return;
      }
      // The authored position, raw: the layout block stores what the author placed, and a
      // blank node's refusal comes back from the backend with its sentence.
      void moveElementTo(elementId, position.x - NODE_WIDTH / 2, position.y - nodeHeightOf(node) / 2);
    },
    // The whole gesture in one stateless rel: call; the predicate is asked in a dialog -
    // the event carries the payload the context channel cannot (Requirement 7.3).
    onConnectionDrawn: ({ sourceElementId, targetElementId }) =>
      runAction("rdf.connect", `rel:${sourceElementId}->${targetElementId}`),
    // A drop carries the backend's own action id; the drop point becomes its placement.
    onElementDropped: ({ elementType, position }) => runAction(elementType, `new:${position.x},${position.y}`),
    // Deletion stays the backend's: the key travels as data against the selection, exactly
    // the shortcut the old keyboard path sent - the backend owns the key-to-action table.
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


  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div className="rdf-canvas canvas-host" role="application" aria-label="RDF graph">
      <DiagramCanvas
        definition={RDF_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="RDF graph"
        className="rdf-surface"
      />
      {model.truncation ? (
        <p className="rdf-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} resources — edits are withheld on this truncated view`}
        </p>
      ) : null}
    </div>
  );
}
