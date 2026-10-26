import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import { placementId, relationId } from "@client/canvas/gestureIds";
import disText from "../definition/supply-chain.dis?raw";
import { barWidthOf, CARD_INSET, STAGE_TYPE, STEPPER, STEPPER_GAP, SUPPLY_CHAIN_BINDINGS } from "./supplyChainBindings";
import { bandWidthOf, flowMiddleOf, type FlowConnection } from "./supplyChainFlow";
import {
  FLOW_TYPE,
  GROUP_TYPE,
  StepperTypes,
  SUPPLY_CHAIN_ADD_ACTION_PREFIX,
  SUPPLY_CHAIN_STAGES,
  SupplyChainActions,
  SupplyChainTraces,
  type SupplyChainStage,
} from "./supplyChainIds";
import { applyDelta, emptyModel, formatAmount, type SupplyChainModel } from "./supplyChainModel";

/** The word each stage's header shows. */
const STAGE_TITLES: Readonly<Record<SupplyChainStage, string>> = {
  source: "Source",
  processor: "Processor",
  producer: "Producer",
  integrator: "Integrator",
  hub: "Hub",
  outlet: "Outlet",
  consumer: "Consumer",
};

/**
 * What a supply chain diagram draws, and what each gesture on it means: compiled from the bundled
 * specification, with what the library cannot read from it in `SUPPLY_CHAIN_BINDINGS`.
 */
export const SUPPLY_CHAIN_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), SUPPLY_CHAIN_BINDINGS));

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set([
  SupplyChainActions.increase,
  SupplyChainActions.decrease,
  SupplyChainActions.rename,
  SupplyChainActions.remove,
]);

function isStage(value: string): value is SupplyChainStage {
  return (SUPPLY_CHAIN_STAGES as readonly string[]).includes(value);
}

/** Roughly how wide the amount is drawn, so the unit can sit just after it. */
function amountWidthOf(text: string): number {
  return [...text].reduce((width, character) => width + (/[0-9]/.test(character) ? 12 : 6), 0);
}

/** The library's elements and connections for a model: frames, cards, the steppers of what is selected, and flows. */
export function diagramModelOf(model: SupplyChainModel): { diagram: DiagramModel; stepperOwners: ReadonlyMap<string, string> } {
  const nodes = [...model.nodes.values()];
  const stepperOwners = new Map<string, string>();

  const groups = [...model.groups.values()].map((group): DiagramModelElement => ({
    id: group.id,
    type: GROUP_TYPE,
    x: group.x,
    y: group.y,
    width: group.payload.width,
    height: group.payload.height,
    label: group.payload.name,
    payload: {
      name: group.payload.name,
      members: group.payload.members === 1 ? "1 node" : `${group.payload.members} nodes`,
      trace: group.payload.trace,
    },
  }));

  const cards = nodes.map((node): DiagramModelElement => {
    const { width, height, quantity, hasQuantity, unit, share } = node.payload;
    const amount = hasQuantity ? formatAmount(quantity) : "—";
    const top = -height / 2;
    return {
      id: node.id,
      type: STAGE_TYPE,
      x: node.x,
      y: node.y,
      width,
      height,
      label: node.payload.name,
      payload: {
        name: node.payload.name,
        stage: node.stage,
        stageTitle: STAGE_TITLES[node.stage].toUpperCase(),
        amount,
        unit,
        hasQuantity,
        trace: node.payload.trace,
        tooltip: `${STAGE_TITLES[node.stage]}: ${node.payload.name}${hasQuantity ? ` — ${amount}${unit ? ` ${unit}` : ""}` : ""}`,
        amountBaseline: top + 74,
        unitX: -width / 2 + CARD_INSET + amountWidthOf(amount) + 6,
        share,
        barWidth: barWidthOf(share),
      },
    };
  });

  const steppers: DiagramModelElement[] = [];
  const addSteppers = (ownerId: string, at: ShapePoint, value: number, what: string) => {
    for (const [type, sign, offset, verb] of [
      [StepperTypes.down, "−", -(STEPPER + STEPPER_GAP) / 2, "Decrease"],
      [StepperTypes.up, "+", (STEPPER + STEPPER_GAP) / 2, "Increase"],
    ] as const) {
      const id = `${ownerId}::${type}`;
      stepperOwners.set(id, ownerId);
      steppers.push({
        id,
        type,
        x: at.x + offset,
        y: at.y,
        width: STEPPER,
        height: STEPPER,
        payload: { sign, enabled: type === StepperTypes.up || value > 0, tooltip: `${verb} the ${what}` },
      });
    }
  };

  for (const node of nodes.filter((candidate) => candidate.payload.trace === SupplyChainTraces.selected)) {
    // Beside the amount, at the card's right edge.
    const right = node.x + node.payload.width / 2 - CARD_INSET - STEPPER - STEPPER_GAP / 2;
    addSteppers(node.id, { x: right, y: node.y - node.payload.height / 2 + 68 }, node.payload.quantity, "quantity");
  }

  const flows = [...model.flows.values()].flatMap((flow): FlowConnection[] => {
    const from = model.nodes.get(flow.payload.fromElementId);
    const to = model.nodes.get(flow.payload.toElementId);
    if (from === undefined || to === undefined) {
      return []; // an end is not held - off screen, or never declared; a line to nothing is worse than none
    }

    const lanes = flow.payload.lanes.map((lane) => ({ x: lane.x, y: lane.y }));
    if (flow.payload.trace === SupplyChainTraces.selected) {
      // Under the volume pill, at the flow's middle - its middle lane, or the middle of its two ends.
      const forward = to.x >= from.x;
      const startX = from.x + (forward ? 1 : -1) * from.payload.width / 2;
      const endX = to.x - (forward ? 1 : -1) * to.payload.width / 2;
      const mid = flowMiddleOf({ x: startX, y: from.y }, { x: endX, y: to.y }, lanes);
      addSteppers(flow.id, { x: mid.x, y: mid.y + bandWidthOf(flow.payload.weight) / 2 + 36 }, flow.payload.volume, "volume");
    }

    return [{
      id: flow.id,
      type: FLOW_TYPE,
      sourceId: flow.payload.fromElementId,
      targetId: flow.payload.toElementId,
      ...(lanes.length > 0 ? { waypoints: lanes } : {}),
      label: flow.payload.product || undefined,
      className: flow.payload.trace ? `supply-chain-trace-${flow.payload.trace}` : undefined,
      style: { strokeWidth: bandWidthOf(flow.payload.weight) },
      title: flow.payload.product,
      flow,
    }];
  });

  // Frames first so they lie beneath everything; steppers last so they lie on top of their card.
  return { diagram: { elements: [...groups, ...cards, ...steppers], connections: flows }, stepperOwners };
}

/**
 * A supply chain: sources, processors, producers, integrators, hubs, outlets and consumers in
 * the regions they belong to, joined by flows as broad as the goods they carry.
 * Selecting anything lights the whole chain through it - what feeds it, and what it feeds - and
 * dims the rest; the backend works that out, so it arrives as ordinary deltas.
 */
export function SupplyChainCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const { diagram, stepperOwners } = useMemo(() => diagramModelOf(model), [model]);

  // Every refusal reaches the one line the library draws around every canvas, and clears when sent.
  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const events: DiagramEventHandlers = {
    // A stepper names the node or flow it belongs to; everything else names itself.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, stepperOwners.get(targetId) ?? targetId);
      }
    },
    // The library reports the CENTRE it drew the element at; the document holds the top-left. A
    // group's frame moves every member with it, and a node dropped inside another group's frame
    // joins that group - the backend does both, given the top-left.
    onElementMoved: ({ elementId, position }) => {
      const node = model.nodes.get(elementId);
      const group = model.groups.get(elementId);
      const size = node?.payload ?? group?.payload;
      if (size !== undefined) {
        // Returned, so a refusal lets the library put the element back where it was (ruled 2026-10-19).
        return moveElementTo(elementId, position.x - size.width / 2, position.y - size.height / 2);
      }
    },
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      runAction(SupplyChainActions.connect, relationId(sourceElementId, targetElementId));
    },
    // The backend's toolbox drops its add action; a toolbox derived from the definition drops the bare stage.
    onElementDropped: ({ elementType, position }) => {
      if (elementType === SupplyChainActions.addGroup || elementType === GROUP_TYPE) {
        runAction(SupplyChainActions.addGroup, placementId(position.x, position.y));
        return;
      }
      const stage = elementType.startsWith(SUPPLY_CHAIN_ADD_ACTION_PREFIX) ? elementType.slice(SUPPLY_CHAIN_ADD_ACTION_PREFIX.length) : elementType;
      if (isStage(stage)) {
        runAction(SupplyChainActions.add(stage), placementId(position.x, position.y));
      }
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
    <div className="supply-chain-canvas canvas-host" role="application" aria-label="Supply chain diagram">
      <DiagramCanvas
        definition={SUPPLY_CHAIN_DEFINITION}
        model={diagram}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Supply chain diagram"
        className="supply-chain-surface"
      />
    </div>
  );
}
