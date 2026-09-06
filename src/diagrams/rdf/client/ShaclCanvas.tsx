import { useMemo, useState } from "react";

import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomShapeRef,
  CustomShapeState,
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { type ContextShortcut } from "@client/generated/context-contract_pb";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useShaclStream } from "./useShaclStream";
import { useViewReport } from "@client/diagrams/useViewReport";
import { shapeHeight, targetWords, type ShaclShape } from "./shaclModel";

/** A card's drawn width, in the module's own canvas units - matching the layout's column pitch. */
export const CARD_WIDTH = 260;

const HEADER_HEIGHT = 30;
const LINE_HEIGHT = 22;

/** An element as the library carries it here: the model element plus what it draws. */
type ShapeElement = DiagramModelElement & { shape: ShaclShape };

function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/**
 * One node shape as a card: the header names it, the badge strip states what the file states
 * - deactivated, closed, a severity - target chips say what it aims at, and one row per
 * property shape carries the path, the cardinality and the constraint summary.
 */
const cardShape: CustomShapeRef = {
  customShape: "shacl-card",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as ShapeElement;
    const shape = element.shape;
    const width = element.width ?? CARD_WIDTH;
    const height = element.height ?? HEADER_HEIGHT;

    const classes = ["shacl-shape canvas-element"];
    if (shape.blank) {
      classes.push("shacl-shape-blank");
    }

    if (shape.deactivated) {
      classes.push("shacl-shape-deactivated");
    }

    if (state?.selected) {
      classes.push("shacl-selected");
    }

    const badges = [
      shape.deactivated ? "deactivated" : "",
      shape.closed ? "closed" : "",
      shape.severity,
    ].filter((badge) => badge.length > 0);

    return (
      <BoxElement
        className={classes.join(" ")}
        x={element.x - width / 2}
        y={element.y - height / 2}
        width={width}
        height={height}
        label={shape.name.length > 0 ? `${shape.display} — ${shape.name}` : shape.display}
        boxClassName="shacl-shape-box canvas-node"
        labelClassName="shacl-label canvas-node-label"
        labelY={HEADER_HEIGHT / 2 + 5}
      >
        {badges.length > 0 ? (
          <text className="shacl-badges canvas-hint" x={CARD_WIDTH - 8} y={HEADER_HEIGHT / 2 + 5} textAnchor="end">
            {badges.join(" · ")}
          </text>
        ) : null}

        {shape.targets.map((target, index) => (
          <text key={`target-${index}`} className="shacl-target" x={8} y={HEADER_HEIGHT + (index + 1) * LINE_HEIGHT - 6}>
            {targetWords(target)}
          </text>
        ))}

        {shape.rows.map((row, index) => {
          const y = HEADER_HEIGHT + (shape.targets.length + index + 1) * LINE_HEIGHT - 6;
          return (
            <g key={`row-${index}`} className={row.sparql ? "shacl-row shacl-row-sparql" : "shacl-row"}>
              <text className="shacl-row-path" x={8} y={y}>
                {row.sparql ? "SPARQL constraint" : row.path}
              </text>
              <text className="shacl-row-cardinality canvas-hint" x={CARD_WIDTH - 8} y={y} textAnchor="end">
                {row.cardinality}
              </text>
              {row.summary.length > 0 ? (
                <text className="shacl-row-summary canvas-hint" x={110} y={y}>
                  {row.summary}
                </text>
              ) : null}
            </g>
          );
        })}
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/**
 * What a shapes graph draws: cards that drag and select, and references between them as
 * labeled arrows. There is no connect gesture - a reference is stated in the file as
 * `sh:node` or a logical combinator, so it is drawn rather than drawn-on, and the relation
 * type declares no source anchors. The reference's kind is an open string from the document
 * (`node`, `and`, `or`, `xone`, `not`, a row's path), so each connection carries its own
 * kind class rather than the definition enumerating relation types it cannot know.
 */
const SHACL_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [{ id: "shape", shape: cardShape, anchors: { kind: "edge" }, sizing: "model" }],
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
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

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
    onSelectionChanged: ({ selection: next }) =>
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null),
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
      <div className="shacl-canvas canvas-host shacl-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="shacl-canvas canvas-host" role="application" aria-label="SHACL shapes" onKeyDown={onKeyDown}>
      <DiagramCanvas
        definition={SHACL_DEFINITION}
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

      {loading && model.shapes.size === 0 ? <p className="shacl-loading canvas-hint">Loading…</p> : null}
    </div>
  );
}

/** A card's height follows its content - the same function the layout uses. */
export { shapeHeight };
