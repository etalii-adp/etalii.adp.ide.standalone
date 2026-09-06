import { useMemo, useState } from "react";

import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { EllipseElement } from "@client/canvas/elements/ellipse/EllipseElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomShapeRef,
  CustomShapeState,
  DiagramDefinition,
  RelationTypeDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { ContextSelectionAction, type ContextShortcut } from "@client/generated/context_pb";
import { useOwlStream } from "./useOwlStream";
import { useViewReport } from "@client/diagrams/useViewReport";
import { isCard, isExpression, type OwlEdgeKind, type OwlModel, type OwlNode } from "./owlModel";

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

/** An element as the library carries it here: the model element plus what it draws. */
type OwlElement = DiagramModelElement & { node: OwlNode; doubled: boolean };

function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/** Where a connector approaching from `towards` touches an ellipse whose box is `bounds`. */
function ellipseEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  const dx = towards.x - centre.x;
  const dy = towards.y - centre.y;
  const rx = Math.max(bounds.width / 2, 1);
  const ry = Math.max(bounds.height / 2, 1);
  const scale = Math.sqrt((dx / rx) ** 2 + (dy / ry) ** 2);
  if (scale === 0) {
    return centre;
  }

  return { x: centre.x + dx / scale, y: centre.y + dy / scale };
}

/** The classes a node wears: its kind, its dimming, and whatever state it is in. */
function classesFor(node: OwlNode, state?: CustomShapeState): string {
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

  if (state?.selected) {
    classes.push("owl-selected");
  }

  if (state?.connectTarget) {
    classes.push("owl-connect-target canvas-connect-target");
  }

  return classes.join(" ");
}

/** An individual's or the ontology header's card: title, type badges, then one line per row. */
const cardShape: CustomShapeRef = {
  customShape: "owl-card",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as OwlElement;
    const node = element.node;
    const width = element.width ?? CARD_WIDTH;
    const height = element.height ?? HEADER_HEIGHT + FOOTER_PADDING;
    const rowsStart = HEADER_HEIGHT + (node.badges.length > 0 ? BADGES_HEIGHT : 0);

    return (
      <BoxElement
        className={classesFor(node, state)}
        x={element.x - width / 2}
        y={element.y - height / 2}
        width={width}
        height={height}
        label={node.display}
        boxClassName="owl-card-box canvas-node"
        labelClassName="owl-label canvas-node-label"
        labelY={HEADER_HEIGHT / 2 + 5}
      >
        {node.badges.length > 0 ? (
          <text className="owl-badges" x={8} y={HEADER_HEIGHT - 6 + BADGES_HEIGHT}>
            {fit(node.badges.join(" · "), width)}
          </text>
        ) : null}
        {node.rows.map((row, index) => (
          <text key={`${row.predicate}-${index}`} className="owl-row" x={8} y={rowsStart + (index + 1) * ROW_HEIGHT - 4}>
            {fit(`${row.predicate}: ${row.value}${row.annotation ? ` ${row.annotation}` : ""}`, width)}
          </text>
        ))}
        <title>{node.display}</title>
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/** A datatype rectangle: the schema half of the literal-node position (Requirement 1.1). */
const datatypeShape: CustomShapeRef = {
  customShape: "owl-datatype",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as OwlElement;
    const width = element.width ?? SHAPE_WIDTH;
    const height = element.height ?? SHAPE_HEIGHT;

    return (
      <BoxElement
        className={classesFor(element.node, state)}
        x={element.x - width / 2}
        y={element.y - height / 2}
        width={width}
        height={height}
        label={element.node.display}
        boxClassName="owl-datatype-box canvas-node"
        labelClassName="owl-label canvas-node-label"
        labelY={height / 2 + 4}
      >
        <title>{element.node.display}</title>
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/**
 * Everything round: classes, Thing anchors, and the expression shapes - whose label is the
 * Manchester form, with the elision marker when it hides depth. An equivalence doubles the
 * outline, as the notation draws it.
 */
const roundShape: CustomShapeRef = {
  customShape: "owl-round",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as OwlElement;
    const width = element.width ?? SHAPE_WIDTH;
    const height = element.height ?? SHAPE_HEIGHT;

    return (
      <EllipseElement
        className={classesFor(element.node, state)}
        x={element.x}
        y={element.y}
        radiusX={width / 2}
        radiusY={height / 2}
        text={labelFor(element.node, width)}
        doubled={element.doubled}
        ellipseClassName="owl-shape canvas-node"
        innerClassName="owl-shape-inner"
        labelClassName="owl-label canvas-node-label"
      >
        <title>{element.node.display}</title>
      </EllipseElement>
    );
  },
  edgePoint: ellipseEdgePoint,
};

/** The node kinds a connect gesture may start from or land on: everything with an identity. */
const CONNECTABLE = ["class", "datatype", "individual", "thing", "ontology"] as const;

/** Every drawn kind, expression shapes included - what an edge may point at. */
const ALL_KINDS = [...CONNECTABLE, "operator", "restriction"] as const;

/** The two side anchors an axiom gesture starts from, shared by every connectable kind. */
const AXIOM_ANCHORS = {
  kind: "sides",
  fractions: [
    { side: "left", at: 0.5, name: "axiom-left" },
    { side: "right", at: 0.5, name: "axiom-right" },
  ],
} as const;

/**
 * One axiom kind as a relation type: a straight line styled by what it states - dotted for
 * subclass, the dedicated mark for disjointness, a labelled arrow for a property. Only the
 * subclass gesture starts from an anchor; every other kind is drawn from the document alone,
 * because the backend offers the subclass axiom for the whole between-two-nodes gesture and
 * asks for a predicate itself where the ends want more (Requirement 6.1).
 */
function edgeType(kind: OwlEdgeKind, options: { arrow: boolean; gesture?: boolean; labelled?: boolean }): RelationTypeDefinition {
  return {
    id: kind,
    route: "straight",
    style: { endMarker: options.arrow ? "arrow" : "none" },
    ...(options.labelled ? { label: { placement: "midpoint" as const, offset: -6 } } : {}),
    className: `owl-edge owl-edge-${kind}`,
    lineClassName: "owl-edge-line",
    hitClassName: "owl-edge-hit",
    endpoints: {
      source: { elementTypes: [...CONNECTABLE], anchors: options.gesture ? ["axiom-left", "axiom-right"] : [] },
      target: { elementTypes: [...ALL_KINDS], anchors: "edge" },
      allowSelf: false,
    },
  };
}

/**
 * The ontology reading: classes as ellipses, datatypes as rectangles, individuals as the
 * family's cards, class expressions as compact nodes beside the class that uses them, and every
 * axiom drawn as the edge its kind asks for - the adopted VOWL vocabulary (Requirements 1-3).
 *
 * Kind is carried by shape and edge style with theme-aware colours, never by a fixed palette.
 * An expression node drags like anything else, and the backend refuses the move with the
 * identity boundary's sentence (Requirement 3.2) - so it stays a full element type here, just
 * one no relation sources from.
 */
const OWL_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    { id: "class", shape: roundShape, anchors: AXIOM_ANCHORS, sizing: "model" },
    { id: "thing", shape: roundShape, anchors: AXIOM_ANCHORS, sizing: "model" },
    { id: "datatype", shape: datatypeShape, anchors: AXIOM_ANCHORS, sizing: "model" },
    { id: "individual", shape: cardShape, anchors: AXIOM_ANCHORS, sizing: "model" },
    { id: "ontology", shape: cardShape, anchors: AXIOM_ANCHORS, sizing: "model" },
    { id: "operator", shape: roundShape, anchors: { kind: "edge" }, sizing: "model" },
    { id: "restriction", shape: roundShape, anchors: { kind: "edge" }, sizing: "model" },
  ],
  relationTypes: [
    edgeType("subclass", { arrow: true, gesture: true }),
    edgeType("equivalent", { arrow: false }),
    edgeType("disjoint", { arrow: false }),
    edgeType("object-property", { arrow: true, labelled: true }),
    edgeType("datatype-property", { arrow: true, labelled: true }),
    edgeType("assertion", { arrow: true, labelled: true }),
    edgeType("expression", { arrow: false }),
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/**
 * The ontology, drawn through the diagram library. A drag never writes the ontology file:
 * `moveElementTo` lands in the registration's `layout:` block as one undoable command, and an
 * anchor drag between two nodes becomes the one stateless rel: gesture the backend answers -
 * between two classes the subclass axiom, anything else a predicate prompt (Requirement 6.1).
 */
export function OwlCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useOwlStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.nodes.values()].map((node): OwlElement => {
      const size = nodeSizeOf(node);
      return {
        id: node.id,
        type: node.kind,
        x: node.x + size.width / 2,
        y: node.y + size.height / 2,
        width: size.width,
        height: size.height,
        label: node.display,
        node,
        doubled: hasEquivalence(model, node.id),
      };
    });
    const connections = [...model.edges.values()].map((edge) => ({
      id: edge.id,
      type: edge.kind,
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.label || undefined,
    }));
    return { elements, connections };
  }, [model]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
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
      setRejection("");
      const node = model.nodes.get(elementId);
      const size = node ? nodeSizeOf(node) : { width: 0, height: 0 };
      // The authored position, raw. An expression node's refusal comes back from the backend
      // carrying the identity boundary's sentence (Requirement 3.2).
      void (async () => {
        const error = await moveElementTo(elementId, position.x - size.width / 2, position.y - size.height / 2);
        if (error) {
          setRejection(error);
        }
      })();
    },
    // The whole gesture in one stateless rel: call. Between two classes the backend offers
    // the subclass axiom; anything else asks for a predicate (Requirement 6.1).
    onConnectionDrawn: ({ sourceElementId, targetElementId }) =>
      runAction("owl.subclass", `rel:${sourceElementId}->${targetElementId}`),
    // A toolbox drop names a placement - `new:{x},{y}` under the pointer.
    onElementDropped: ({ elementType, position }) => runAction(elementType, `new:${position.x},${position.y}`),
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
      <div className="owl-canvas canvas-host owl-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="owl-canvas canvas-host" role="application" aria-label="OWL ontology" onKeyDown={onKeyDown}>
      <DiagramCanvas
        definition={OWL_DEFINITION}
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
        ariaLabel="OWL ontology"
        className="owl-surface"
        scrollbarsClassName="owl-scrollbars"
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

/** Whether an equivalence axiom touches this node - what doubles its outline, per the notation. */
function hasEquivalence(model: OwlModel, id: string): boolean {
  for (const edge of model.edges.values()) {
    if (edge.kind === "equivalent" && (edge.fromElementId === id || edge.toElementId === id)) {
      return true;
    }
  }

  return false;
}
