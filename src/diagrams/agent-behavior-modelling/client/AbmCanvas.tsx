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
  ABM_CHILD_RELATION,
  ABM_NODE_KINDS,
  AbmActions,
  type AbmNodeKind,
} from "./abmIds";
import { applyDelta, emptyModel, type AbmModel } from "./abmModel";
import { arrangementOf, previewOf, sameOffsets, type Offset } from "./abmDrag";
import { ABM_BINDINGS } from "./abmBindings";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import disText from "../definition/agent-behavior-modelling.dis?raw";

/** The bundled specification: the same bytes the backend loads, read by Vite as text. */
const SPEC = parseDisl(disText);

/**
 * What a behavior model allows, stated once - compiled from the bundled DISL specification
 * (`definition/agent-behavior-modelling.dis`), with the few things the library cannot read from it
 * in `abmBindings.ts`. The tree is computed by the backend from the Markdown, so the canvas lays out
 * nothing; a drag moves a node's row and may reorder its siblings, and a parent line drawn from one
 * node to another moves the second under the first, which the cycle rule keeps from ever running a
 * node beneath itself. `abmCompiledDefinition.test.ts` holds this to the definition once written
 * here by hand.
 */
export const ABM_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(SPEC, ABM_BINDINGS));

/**
 * The shape each kind is drawn as - the library's built-ins, and nothing of this module's own: a
 * composite is a squircle and a wrapper a hexagon; among the leaves a Check is a pill, a Do a box,
 * an Ask the user a parallelogram and a Delegate a diode. Read from the compiled definition.
 */
export const ABM_SHAPES: Readonly<Record<AbmNodeKind, ElementTypeDefinition["shape"]>> = Object.fromEntries(
  ABM_DEFINITION.elementTypes.map((type) => [type.id, type.shape]),
) as Record<AbmNodeKind, ElementTypeDefinition["shape"]>;

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
