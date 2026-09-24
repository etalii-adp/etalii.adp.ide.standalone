import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useShaclStream } from "./useShaclStream";
import { useViewReport } from "@client/diagrams/useViewReport";
import { shapeHeight, targetWords, type ShaclShape } from "./shaclModel";

/** A card's drawn width, in the module's own canvas units - matching the layout's column pitch. */
export const CARD_WIDTH = 260;

const HEADER_HEIGHT = 30;
const LINE_HEIGHT = 22;


/** An element as the library carries it here: the model element plus what it draws. */
type ShapeElement = DiagramModelElement & { shape: ShaclShape };



/**
 * What a shapes graph draws: cards that drag and select, and references between them as
 * labeled arrows. There is no connect gesture - a reference is stated in the file as
 * `sh:node` or a logical combinator, so it is drawn rather than drawn-on, and the relation
 * type declares no source anchors. The reference's kind is an open string from the document
 * (`node`, `and`, `or`, `xone`, `not`, a row's path), so each connection carries its own
 * kind class rather than the definition enumerating relation types it cannot know.
 */
const SHACL_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "shape",
      shape: "box",
      classNames: [
        { className: "shacl-shape canvas-element" },
        { className: "shacl-shape-blank", when: { path: "payload.blank", is: "true" } },
        { className: "shacl-shape-deactivated", when: { path: "payload.deactivated", is: "true" } },
        { className: "shacl-shape-box canvas-node", on: "shape" },
      ],
      labels: [
        {
          // `display — name` when the shape has a name of its own, `display` when it does not,
          // with no dangling dash: parts, not a template.
          text: { parts: [{ path: "payload.display" }, { path: "payload.name", when: { path: "payload.name", is: "non-empty" } }], join: " — " },
          anchorTo: "top",
          offset: { x: 0, y: HEADER_HEIGHT / 2 + 5 },
          truncate: true,
          className: "shacl-label canvas-node-label",
        },
        {
          text: { path: "payload.badges", each: { path: "text" }, join: " · " },
          when: { path: "payload.badges", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: HEADER_HEIGHT / 2 + 5 },
          align: "end",
          insetX: 8,
          className: "shacl-badges canvas-hint",
        },
        {
          text: { path: "payload.targets", each: { path: "words" } },
          anchorTo: "top",
          offset: { x: 0, y: 0 },
          align: "start",
          insetX: 8,
          stack: { lineHeight: LINE_HEIGHT, start: HEADER_HEIGHT + LINE_HEIGHT - 6 },
          className: "shacl-target",
        },
        {
          // THE THREE-COLUMN ROW, and the reason columns exist: a path on the left, a summary
          // in the middle and a cardinality on the right, once per constraint. Declaring three
          // collections over the same list would let them drift apart the moment one carried a
          // condition - pairing row 2's cardinality with row 3's path, silently.
          text: {
            path: "payload.rows",
            each: {
              parts: [
                { path: "path", when: { path: "sparql", is: "false" } },
                { template: "SPARQL constraint", when: { path: "sparql", is: "true" } },
              ],
              join: "",
            },
          },
          anchorTo: "top",
          offset: { x: 0, y: 0 },
          align: "start",
          insetX: 8,
          // Below the targets, however many this shape declares - which is why a stack's start
          // is bindable rather than a constant.
          stack: { lineHeight: LINE_HEIGHT, start: { path: "payload.rowsStart" } },
          // `shacl-row-path` is the class shacl.css sizes: the migration dropped it with the
          // `<g>` the row used to be, and every path drew at the browser's 16px.
          className: { parts: [{ template: "shacl-row shacl-row-path" }, { template: "shacl-row-sparql", when: { path: "sparql", is: "true" } }], join: " " },
          columns: [
            // Cut at the card's edge: a combinator summary is as long as its operands, and drawn
            // whole it ran out of the card (as it did before the migration, too).
            { text: { path: "summary" }, insetX: 110, align: "start", truncate: true, className: "shacl-row-summary canvas-hint" },
            { text: { path: "cardinality" }, insetX: 8, align: "end", className: "shacl-row-cardinality canvas-hint" },
          ],
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
    },
  ],
  relationTypes: [
    {
      id: "reference",
      route: "straight",
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6 },
      className: "shacl-edge",
      lineClassName: "shacl-edge-line",
      hitClassName: "shacl-edge-hit",
      endpoints: {
        source: { elementTypes: ["shape"], anchors: [] },
        target: { elementTypes: ["shape"], anchors: "edge" },
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
 * The shapes graph: node shapes as cards, their targets as chips and their property shapes as
 * constraint rows, references between shapes as labeled edges - drawn through the central
 * canvas library (shacl-diagram Requirements 1, 3).
 *
 * Two things this canvas deliberately does NOT draw. A target's data end: the data a shapes
 * graph aims at lives in another file, so a chip states what a shape targets and no edge leaves
 * the card - a chip whose term is absent from the file renders exactly like one whose term is
 * present, because absence is this medium's normal case. And a validation result: nothing here
 * runs a shape against anything (Requirement 4).
 */
export function ShaclCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useShaclStream(projectId, path);
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);


  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.shapes.values()].map((shape): ShapeElement => {
      const height = shapeHeight(shape);
      return {
        id: shape.id,
        type: "shape",
        x: shape.x + CARD_WIDTH / 2,
        y: shape.y + height / 2,
        width: CARD_WIDTH,
        height,
        label: shape.display,
        // What the declaration reads. `rowsStart` is the same arithmetic `shapeHeight` already
        // does: the rows begin below however many targets this shape declares.
        payload: {
          display: shape.display,
          name: shape.name,
          blank: shape.blank,
          deactivated: shape.deactivated,
          badges: [shape.deactivated ? "deactivated" : "", shape.closed ? "closed" : "", shape.severity]
            .filter((badge) => badge.length > 0)
            .map((text) => ({ text })),
          targets: shape.targets.map((target) => ({ words: targetWords(target) })),
          rows: shape.rows,
          rowsStart: HEADER_HEIGHT + (shape.targets.length + 1) * LINE_HEIGHT - 6,
        },
        shape,
      };
    });
    const connections = [...model.edges.values()].map((edge) => ({
      id: edge.id,
      type: "reference",
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.label || undefined,
      className: `shacl-edge-${edge.kind}`,
    }));
    return { elements, connections };
  }, [model]);

  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  /** The card whose rectangle holds this canvas point, where one does. */
  const cardAt = (position: ShapePoint): ShaclShape | null => {
    for (const shape of model.shapes.values()) {
      if (
        position.x >= shape.x
        && position.x <= shape.x + CARD_WIDTH
        && position.y >= shape.y
        && position.y <= shape.y + shapeHeight(shape)
      ) {
        return shape;
      }
    }

    return null;
  };

  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection); a menu action it ran and the backend
    // refused comes back here, for the same rejection line every other refusal uses.
    onActionRefused: ({ message }) => setRejection(message),
    onElementMoved: ({ elementId, position }) => {
      setRejection("");
      const shape = model.shapes.get(elementId);
      const height = shape ? shapeHeight(shape) : 0;
      // The authored position, raw. An anonymous shape's refusal comes back from the backend
      // with its sentence - the canvas does not decide it, it reports it (Requirement 3.2).
      void (async () => {
        const error = await moveElementTo(elementId, position.x - CARD_WIDTH / 2, position.y - height / 2);
        if (error) {
          setRejection(error);
        }
      })();
    },
    // A row entry dropped on a card acts on that card; anything else lands as a placement.
    onElementDropped: ({ elementType, position }) => {
      const over = cardAt(position);
      runAction(elementType, over ? over.id : `new:${position.x},${position.y}`);
    },
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


  if (failed) {
    return (
      <div className="shacl-canvas canvas-host shacl-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="shacl-canvas canvas-host" role="application" aria-label="SHACL shapes">
      <DiagramCanvas
        definition={SHACL_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="SHACL shapes"
        className="shacl-surface"
        scrollbarsClassName="shacl-scrollbars"
      />

      {model.truncation ? (
        <p className="shacl-banner canvas-banner" role="status">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} shapes. Edits are withheld while the view is partial.`}
        </p>
      ) : null}

      {rejection ? (
        <p className="shacl-rejection canvas-rejection" role="alert">
          {rejection}
        </p>
      ) : null}

      {loading && model.shapes.size === 0 ? <p className="shacl-status canvas-status">Opening…</p> : null}
    </div>
  );
}

/** A card's height follows its content - the same function the layout uses. */
export { shapeHeight };
