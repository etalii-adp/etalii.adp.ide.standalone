import { useEffect, useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ElementTypeDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import { placementId, relationId } from "@client/canvas/gestureIds";
import {
  ABM_ACTION_IDS,
  ABM_ADD_ACTION_PREFIX,
  ABM_CATEGORY,
  ABM_CHILD_RELATION,
  ABM_NODE_KINDS,
  AbmActions,
  AbmShortcuts,
  type AbmNodeKind,
} from "./abmIds";
import { applyDelta, emptyModel, type AbmModel } from "./abmModel";
import { arrangementOf, previewOf, sameOffsets, type Offset } from "./abmDrag";

/**
 * The shape each kind is drawn as - the library's built-ins, and nothing of this module's own. A
 * composite is a squircle and a wrapper a hexagon, as behavior tree editors in games set the two
 * families apart; among the leaves, a Check is a pill, a Do a box, an Ask the user the
 * parallelogram flowcharts give input, and a Delegate the diode that points onward.
 */
export const ABM_SHAPES: Readonly<Record<AbmNodeKind, ElementTypeDefinition["shape"]>> = {
  sequence: "superellipse",
  fallback: "superellipse",
  parallel: "superellipse",
  retry: "hexagon",
  repeat: "hexagon",
  guard: "hexagon",
  approval: "hexagon",
  check: "pill",
  action: "box",
  ask: "parallelogram",
  delegate: "diode",
};

/** The fill family a kind takes its colour from; `abm.css` maps each to a theme token. */
function familyOf(kind: AbmNodeKind): string {
  if (kind === "check" || kind === "action") {
    return kind;
  }

  return ABM_CATEGORY[kind] === "leaf" ? "other" : ABM_CATEGORY[kind];
}

/**
 * One kind: its shape and fill, the keyword on the top line and the label beneath it. Only the label
 * is editable - the keyword is the kind, changed through the property grid, where it can be refused
 * when the node's children would not fit the new kind.
 */
function nodeType(kind: AbmNodeKind): ElementTypeDefinition {
  return {
    id: kind,
    shape: ABM_SHAPES[kind],
    classNames: [
      { className: "canvas-element abm-node", on: "element" },
      { className: "abm-implicit", on: "element", when: { path: "payload.implicit", is: "true" } },
      { className: `canvas-node abm-${familyOf(kind)}`, on: "shape" },
    ],
    accessibility: { role: "button", label: { template: "{payload.keyword}: {payload.label}" } },
    labels: [
      {
        text: { path: "payload.keyword" },
        anchorTo: "top",
        offset: { x: 0, y: 20 },
        truncate: true,
        className: "abm-keyword",
      },
      {
        text: { path: "payload.label" },
        anchorTo: "top",
        offset: { x: 0, y: 40 },
        truncate: true,
        editable: true,
        editorBox: { top: 27, height: 22 },
        tooltip: { path: "payload.label" },
        className: "canvas-node-label abm-label",
      },
    ],
    // A parent line leaves the middle of the parent's bottom and arrives at the middle of the
    // child's top, as a tree drawn top-down reads; the orthogonal route then runs vertically out
    // and in. A true edge intersection would put both ends wherever the slant between the two
    // centres crossed the outline, and the route would lie along the borders.
    anchors: { kind: "edge", edgeSides: "vertical" },
    sizing: "model",
  };
}

/** The kinds a parent line may start from: the ones that hold children. */
const PARENTS: readonly AbmNodeKind[] = ABM_NODE_KINDS.filter((kind) => ABM_CATEGORY[kind] !== "leaf");

/**
 * What a behavior model allows, stated once. The tree is computed by the backend from the Markdown,
 * so the canvas lays out nothing; a drag moves a node's row and may reorder its siblings, and a
 * parent line drawn from one node to another moves the second under the first, which the cycle rule
 * keeps from ever running a node beneath itself.
 */
export const ABM_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: ABM_NODE_KINDS.map(nodeType),
  relationTypes: [
    {
      id: ABM_CHILD_RELATION,
      route: "orthogonal",
      style: { endMarker: "arrow" },
      className: "abm-child",
      endpoints: {
        source: { elementTypes: PARENTS },
        target: { elementTypes: ABM_NODE_KINDS, anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  acyclic: [{ relationTypes: [ABM_CHILD_RELATION] }],
  actions: [
    {
      id: AbmActions.rename,
      invokedBy: [{ kind: "shortcut", key: AbmShortcuts.rename }, { kind: "gesture", gesture: "activate" }],
      appliesTo: [{ kind: "element" }],
    },
    { id: AbmActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
    { id: AbmActions.moveEarlier, invokedBy: [{ kind: "shortcut", key: AbmShortcuts.moveEarlier }], appliesTo: [{ kind: "element" }] },
    { id: AbmActions.moveLater, invokedBy: [{ kind: "shortcut", key: AbmShortcuts.moveLater }], appliesTo: [{ kind: "element" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // Edge anchors draw no handle, so a parent line is drawn by dragging with the right button from
  // the parent's body to the child's - the gesture every edge-anchored module offers.
  connectOnRightDrag: true,
});

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set(ABM_ACTION_IDS);

function isKind(value: string): value is AbmNodeKind {
  return (ABM_NODE_KINDS as readonly string[]).includes(value);
}

/** Where nodes are drawn other than where the model has them: a drag in flight, or a drop awaiting its answer. */
export interface Rearrangement {
  offsets: ReadonlyMap<string, Offset>;
  /** Whether the gesture is over and these offsets are the drop's, kept until the model places it. */
  settled: boolean;
}

/** The library's elements and connections for a model, with any rearrangement a drag has made. */
export function diagramModelOf(model: AbmModel, rearrangement?: Rearrangement): DiagramModel {
  const elements = [...model.nodes.values()].map((node): DiagramModelElement => {
    const offset = rearrangement?.offsets.get(node.id);
    return {
      id: node.id,
      type: node.kind,
      x: node.x + (offset?.dx ?? 0),
      y: node.y + (offset?.dy ?? 0),
      width: node.payload.width,
      height: node.payload.height,
      label: node.payload.label,
      payload: { keyword: node.payload.keyword, label: node.payload.label, implicit: node.payload.implicit },
    };
  });
  const connections = [...model.lines.values()].flatMap((line): DiagramModelConnection[] =>
    model.nodes.has(line.payload.fromElementId) && model.nodes.has(line.payload.toElementId)
      ? [{ id: line.id, type: ABM_CHILD_RELATION, sourceId: line.payload.fromElementId, targetId: line.payload.toElementId }]
      : [],
  );
  return { elements, connections };
}

/**
 * An agent behavior model: a chat agent's instructions as a behavior tree, drawn top-down through the
 * shared canvas library. A move goes through the stream's `moveElementTo`, everything else through a
 * context action on one target id.
 *
 * <b>A drag carries a subtree, moves a row and reorders it.</b> While a node moves, everything beneath
 * it follows the pointer, its siblings follow it up and down, and the siblings it passes step aside
 * to show where it will land - the Sankey diagram's snap, on its side. On release the backend
 * rewrites the order in the Markdown and keeps the row's height in the `.adp`; until that answer
 * arrives, the drop is drawn where it will be.
 */
export function AbmCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
  const [rearrangement, setRearrangement] = useState<Rearrangement | null>(null);

  // The model's answer replaces whatever the drag drew: it is where everything really is now.
  useEffect(() => setRearrangement(null), [model]);

  const diagramModel = useMemo(() => diagramModelOf(model, rearrangement ?? undefined), [model, rearrangement]);

  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const events: DiagramEventHandlers = {
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, targetId);
      }
    },
    // While a node is dragged, its subtree follows it and its row makes room; nothing is written until the release.
    onElementPreviewed: ({ elementId, bounds }) => {
      if (bounds === null) {
        // The gesture is over. A drop has already settled; an abandoned drag puts everything back.
        setRearrangement((current) => (current?.settled === true ? current : null));
        return;
      }
      const offsets = previewOf(model, elementId, bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
      if (offsets !== null) {
        setRearrangement((current) => (current !== null && !current.settled && sameOffsets(current.offsets, offsets) ? current : { offsets, settled: false }));
      }
    },
    // The library reports the CENTRE it drew the node at; the backend takes the top-left.
    onElementMoved: ({ elementId, position }) => {
      const node = model.nodes.get(elementId);
      const arrangement = arrangementOf(model, elementId, position.x, position.y);
      if (node === undefined || arrangement === null) {
        return;
      }
      // Drawn where it lands at once - a hair off its old place when nothing changes, so the library lets go of the drop.
      const offsets = arrangement.nothing ? new Map([[elementId, { dx: 0, dy: 0.01 }]]) : arrangement.offsets;
      setRearrangement({ offsets, settled: true });
      void moveElementTo(elementId, position.x - node.payload.width / 2, position.y - node.payload.height / 2).then((refusal) => {
        if (refusal !== "" || arrangement.nothing) {
          setRearrangement(null);
        }
      });
    },
    // A parent line, drawn from the parent to the node that belongs under it.
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      runAction(AbmActions.connectChild, relationId(sourceElementId, targetElementId));
    },
    // The backend's toolbox drops its add action (`abm.add.<kind>`); a toolbox the library derived
    // from the definition drops the bare kind. Both mean the same add, placed by where it landed.
    onElementDropped: ({ elementType, position }) => {
      const kind = elementType.startsWith(ABM_ADD_ACTION_PREFIX) ? elementType.slice(ABM_ADD_ACTION_PREFIX.length) : elementType;
      if (!isKind(kind)) {
        return;
      }
      runAction(AbmActions.add(kind), placementId(position.x, position.y));
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

  return (
    <div className="abm-canvas canvas-host" role="application" aria-label="Agent behavior model">
      <DiagramCanvas
        definition={ABM_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Agent behavior model"
        className="abm-surface"
      />
    </div>
  );
}
