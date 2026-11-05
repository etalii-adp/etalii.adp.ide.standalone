import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import disText from "../definition/supply-chain.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { ActionDeclaration } from "@client/canvas/library/definition/actions";
import type { CustomRouteRef, DiagramDefinition, RelationTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { SupplyChainFlowPayloadSchema, SupplyChainGroupPayloadSchema, SupplyChainNodePayloadSchema } from "@client/generated/supply-chain_pb";
import { CARD_INSET, HEADER, STAGE_TYPE, SUPPLY_CHAIN_BINDINGS } from "./supplyChainBindings";
import { HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION } from "./supplyChainHandWrittenDefinition";
import { flowAdornment } from "./supplyChainFlow";
import { GROUP_TYPE, SUPPLY_CHAIN_STAGES, SupplyChainActions, SupplyChainTraces, type SupplyChainStage } from "./supplyChainIds";
import type { SupplyChainModel } from "./supplyChainModel";

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextConnection: () => connection,
    useContextSelection: () => ({ selection: null }),
  };
});

const connection = fakeContextConnection({ select: vi.fn(), revealPath: vi.fn() });

const { DiagramCanvas } = await import("@client/canvas/library/DiagramCanvas");
const { SUPPLY_CHAIN_DEFINITION, diagramModelOf } = await import("./SupplyChainCanvas");

/*
 * The supply chain's canvas definition, compiled from its bundled DISL specification, draws what the
 * definition the module stated by hand until the switch-over drew (supply-chain-disl-fbl, task 8).
 *
 * The oracle is that hand-written definition, moved unchanged to `supplyChainHandWrittenDefinition.ts`.
 * The drawing is compared over a corpus of models that reaches every case the drawing distinguishes:
 * every stage, a node with a quantity, with a unit, with neither and with a long name; a group of one
 * node, of several and of none; every mark of the trace on a node, a group and a flow; a selected node
 * and a selected flow with their steppers, one of them at zero; flows forward, backward, through lanes,
 * with a volume and without, with a product and without.
 *
 * Where the two differ, each difference is a decision, and the test takes it back out:
 * - **Seven element types became one.** The oracle had a type per stage; the compiled definition has the
 *   specification's one `Stage`, its colour a class bound to the payload's stage. The oracle's model names
 *   each card's stage as its type, and the markup's element types are compared with that taken out.
 * - **The card's ornaments are measured from its centre** rather than bound to payload fields that held the
 *   same numbers; the oracle's model carries those fields, which the canvas no longer sends.
 * - **The actions are the specification's menu entries**, one per kind of target where the oracle joined
 *   the cards, the frames and the flows in one; each still answers the keys and gestures it answered.
 * - **A second flow between the same two nodes in the same direction is refused by the backend**, in the
 *   sentence of the definition's `noSecondFlowWhileDrawing`, where the oracle's canvas declined the gesture
 *   itself (`cardinality.perPair`): DISL states that rule as a gesture constraint, which the library does not
 *   evaluate, and a file holding two is drawn and not reported, so the metamodel cannot forbid it.
 * - **A frame states its corner**, 8, which is the rounded rectangle's own.
 * - **A double-click on a flow is not declared.** The oracle declared one renaming the product, which the
 *   library never raised: it raises `activate` on an element alone, so a double-click on a flow did nothing
 *   (seen in the browser pass of this task), and DISL gives an edge no `doubleClick` to compile it from.
 *   F2 and the menu's *Rename product…* still rename it. The oracle's row is taken out of the comparison.
 */

/** A node's payload, as the backend sends it, with a card's size. */
function nodePayload(name: string, stage: SupplyChainStage, trace: string, quantity?: number, unit = "", share = 0.5) {
  return create(SupplyChainNodePayloadSchema, {
    name,
    stage,
    quantity: quantity ?? 0,
    hasQuantity: quantity !== undefined,
    unit,
    share,
    width: 192,
    height: 96,
    trace,
  });
}

function flowPayload(fromElementId: string, toElementId: string, product: string, trace: string, volume?: number, lanes: readonly { x: number; y: number }[] = []) {
  return create(SupplyChainFlowPayloadSchema, {
    fromElementId,
    toElementId,
    product,
    volume: volume ?? 0,
    hasVolume: volume !== undefined,
    unit: volume === undefined ? "" : "t",
    weight: volume === undefined ? 0 : Math.min(1, volume / 10),
    trace,
    lanes: lanes.map((lane) => ({ ...lane })),
  });
}

/** One model per trace: everything unmarked, then the chain through a selected node, then through a selected flow. */
function corpus(): SupplyChainModel[] {
  const marks: readonly (readonly [string, string, string, string])[] = [
    ["", "", "", ""],
    [SupplyChainTraces.selected, SupplyChainTraces.downstream, SupplyChainTraces.related, SupplyChainTraces.dimmed],
    [SupplyChainTraces.upstream, SupplyChainTraces.selected, SupplyChainTraces.dimmed, SupplyChainTraces.related],
  ];
  return marks.map(([first, second, group, rest]) => ({
    groups: new Map([
      ["north", { id: "north", x: 116, y: 270, payload: create(SupplyChainGroupPayloadSchema, { name: "North", width: 232, height: 300, members: 2, trace: group }) }],
      ["east", { id: "east", x: 900, y: 120, payload: create(SupplyChainGroupPayloadSchema, { name: "East", width: 232, height: 156, members: 1, trace: rest }) }],
      ["empty", { id: "empty", x: 1300, y: 120, payload: create(SupplyChainGroupPayloadSchema, { name: "", width: 232, height: 156, members: 0, trace: "" }) }],
    ]),
    nodes: new Map(
      SUPPLY_CHAIN_STAGES.map((stage, index) => {
        const id = `n${index}`;
        const trace = index === 0 ? first : index === 1 ? second : rest;
        const quantity = index === 2 ? undefined : index === 3 ? 0 : 1200 * index + 0.5;
        const name = index === 4 ? "A hub with a name far longer than its card can hold" : `Node ${index}`;
        return [id, { id, stage, x: 116 + 300 * index, y: 188 + (index % 2) * 164, payload: nodePayload(name, stage, trace, quantity, index === 5 ? "" : "kt") }];
      }),
    ),
    flows: new Map([
      ["f1", { id: "f1", payload: flowPayload("n0", "n1", "Ore", first === SupplyChainTraces.selected ? "" : second, 6) }],
      ["f2", { id: "f2", payload: flowPayload("n1", "n4", "Parts", second, 0, [{ x: 716, y: 270 }, { x: 1016, y: 270 }]) }],
      ["f3", { id: "f3", payload: flowPayload("n6", "n2", "", rest) }],
      ["f4", { id: "f4", payload: flowPayload("n3", "n5", "Goods", rest, 12) }],
    ]),
  }));
}

/** The oracle's model of a model: each card typed by its stage, with the payload fields its ornaments were bound to. */
function oracleModelOf(diagram: DiagramModel): DiagramModel {
  const elements = diagram.elements.map((element): DiagramModelElement => {
    if (element.type !== STAGE_TYPE) {
      return element;
    }

    const payload = element.payload as Record<string, unknown>;
    const width = element.width!;
    const height = element.height!;
    const top = -height / 2;
    return {
      ...element,
      type: payload.stage as string,
      payload: {
        ...payload,
        left: -width / 2,
        top,
        width,
        headerSeam: top + HEADER - 10,
        barLeft: -width / 2 + CARD_INSET,
        barTop: height / 2 - 12,
        barTrack: width - CARD_INSET * 2,
      },
    };
  });
  return { ...diagram, elements };
}

/** The canvas's markup, with the ids React generates per mount taken out. */
function drawn(definition: DiagramDefinition, model: DiagramModel): string {
  const { container, unmount } = render(
    <DiagramCanvas definition={definition} model={model} events={{}} source={{ entryId: new Uint8Array(16), path: ["chain.supply"] }} ariaLabel="Supply chain diagram" className="supply-chain-surface" />,
  );
  const markup = container.innerHTML.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");
  unmount();
  return markup;
}

/** The markup with the element types taken out, which are the one decision the drawing shows. */
function withoutTypes(markup: string): string {
  return markup.replace(new RegExp(`data-element-type="(${[STAGE_TYPE, ...SUPPLY_CHAIN_STAGES].join("|")})"`, "g"), 'data-element-type="«stage»"');
}


/** Each action as one row per kind of target and element type, the oracle's stage types named as the compiled one. */
function offered(actions: readonly ActionDeclaration[] | undefined): string[] {
  const typeOf = (type: string) => ((SUPPLY_CHAIN_STAGES as readonly string[]).includes(type) ? STAGE_TYPE : type);
  return (actions ?? [])
    .flatMap((action) =>
      action.appliesTo.flatMap((target) => {
        const types = target.kind === "element" ? [...new Set((target.elementTypes ?? [STAGE_TYPE, GROUP_TYPE]).map(typeOf))] : ["connection"];
        return types.flatMap((type) => action.invokedBy.map((invocation) => `${action.id} ${type} ${JSON.stringify(invocation)}`));
      }),
    )
    .sort();
}

/** A route's drawing at a few points, forward and back, through a lane and between two cards, which is what makes two routes the same. */
function drawingOf(route: RelationTypeDefinition["route"]): unknown {
  const ref = route as CustomRouteRef;
  const ends = { source: { x: 0, y: 0, width: 192, height: 96 }, target: { x: 500, y: 200, width: 192, height: 96 } };
  return {
    name: ref.customRoute,
    paths: [
      ref.path({ x: 96, y: 48 }, { x: 596, y: 248 }, [], ends),
      ref.path({ x: 596, y: 248 }, { x: 96, y: 48 }, [], { source: ends.target, target: ends.source }),
      ref.path({ x: 96, y: 48 }, { x: 596, y: 248 }, [{ x: 300, y: 150 }], ends),
    ],
  };
}

/** A relation type with its route drawn, its element types named as the compiled one and its per-pair rule taken out. */
function comparable(relation: RelationTypeDefinition): unknown {
  const typesOf = (types: readonly string[] | undefined) => [...new Set((types ?? []).map((type) => ((SUPPLY_CHAIN_STAGES as readonly string[]).includes(type) ? STAGE_TYPE : type)))];
  const { cardinality: _perPair, ...endpoints } = relation.endpoints;
  return {
    ...relation,
    route: drawingOf(relation.route),
    endpoints: {
      ...endpoints,
      source: { ...relation.endpoints.source, elementTypes: typesOf(relation.endpoints.source.elementTypes) },
      target: { ...relation.endpoints.target, elementTypes: typesOf(relation.endpoints.target.elementTypes) },
    },
  };
}

describe("the supply chain's compiled definition", () => {
  it("draws what the hand-written definition drew, for every model of the corpus", () => {
    for (const model of corpus()) {
      const { diagram } = diagramModelOf(model);
      const compiled = drawn(SUPPLY_CHAIN_DEFINITION, diagram);

      // The cases the comparison would pass vacuously on: a canvas that drew nothing for either.
      expect(compiled).toContain("supply-chain-stage-integrator");
      expect(compiled).toContain("supply-chain-header");
      expect(compiled).toContain("supply-chain-group-frame");
      expect(compiled).toContain("supply-chain-flow-goods");
      expect(withoutTypes(compiled)).toBe(withoutTypes(drawn(HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION, oracleModelOf(diagram))));
    }
  });

  it("offers what the hand-written definition offered, each action to the targets and on the keys it was", () => {
    const flowDoubleClick = `${SupplyChainActions.rename} connection ${JSON.stringify({ kind: "gesture", gesture: "activate" })}`;
    const oracle = offered(HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION.actions);

    expect(oracle).toContain(flowDoubleClick);
    expect(offered(SUPPLY_CHAIN_DEFINITION.actions)).toEqual(oracle.filter((row) => row !== flowDoubleClick));
  });

  it("declares the flow the hand-written definition declared, but for the per-pair rule", () => {
    expect(SUPPLY_CHAIN_DEFINITION.relationTypes.map(comparable)).toEqual(HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION.relationTypes.map(comparable));
    expect(SUPPLY_CHAIN_DEFINITION.relationTypes[0]!.adorn).toBe(flowAdornment);
    expect(HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION.relationTypes[0]!.endpoints.cardinality).toEqual({ perPair: "ordered" });
    expect(SUPPLY_CHAIN_DEFINITION.relationTypes[0]!.endpoints.cardinality).toBeUndefined();
  });

  it("declares the steppers, the layout, the drag and the menus the hand-written definition declared", () => {
    const rest = (definition: DiagramDefinition) => ({ ...definition, elementTypes: undefined, relationTypes: undefined, actions: undefined });

    expect(SUPPLY_CHAIN_DEFINITION.elementTypes.slice(-2)).toEqual(HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION.elementTypes.slice(-2));
    expect(rest(SUPPLY_CHAIN_DEFINITION)).toEqual(rest(HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION));
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the test above: were the canvas still holding its own definition, it would
    // compare the oracle with a copy of itself.
    expect(SUPPLY_CHAIN_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), SUPPLY_CHAIN_BINDINGS)));
  });
});
