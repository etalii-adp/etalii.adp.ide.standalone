import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  C4BoundaryPayloadSchema,
  C4ElementKindProto,
  C4ElementPayloadSchema,
  C4RelationshipPayloadSchema,
  C4ViewPayloadSchema,
} from "@client/generated/c4_pb";
import type { DiagramDefinition, ElementTypeDefinition, RelationTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl, type DislCustomShape, type DislShapePart } from "@client/canvas/library/disl/disTypes";
import { resolveBox } from "@client/canvas/library/disl/geomExpr";
import component from "../definition/c4-component.dis?raw";
import container from "../definition/c4-container.dis?raw";
import context from "../definition/c4-context.dis?raw";
import deployment from "../definition/c4-deployment.dis?raw";
import dynamic from "../definition/c4-dynamic.dis?raw";
import systemLandscape from "../definition/c4-system-landscape.dis?raw";
import { BOUNDARY_TYPE, C4_BINDINGS } from "./c4Bindings";
import type { C4Model, C4Node } from "./c4Model";
import { HAND_WRITTEN_DEFINITION, handWrittenModel } from "./handWrittenDefinition";

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
const { C4_DEFINITIONS, c4DiagramModel } = await import("./C4Canvas");

/*
 * The C4 canvas's definitions, compiled from the six bundled DISL specifications, draw what the definition
 * the module stated by hand until the switch-over drew (c4-disl-fbl Requirements 9.1 and 9.2).
 *
 * The oracle is that hand-written definition, with the model the canvas built for it, moved unchanged to
 * `handWrittenDefinition.ts`; the title and the key the module drew around it are moved here. The two are
 * not equal key for key, and the differences are each a decision:
 * - A card is typed by its kind (`person` ... `softwaresysteminstance`) and a line by the specifications'
 *   relation where the oracle had one type of each; the canvas cannot tell a stored line from a derived one,
 *   so every line but an interaction is a `relationship`.
 * - A card's type line and description arrive whole, and the library fits them to the card at the size the
 *   specification's styles draw them (`fit`), where the canvas fitted them before handing them over; an
 *   empty line is left out of the payload rather than tested for.
 * - A line's dash is the specification's, drawn inline, where the oracle's stylesheet stated it.
 * - A card without a name is announced by its id, as the specification's `shownName` says; the oracle
 *   announced it by its empty name.
 * - A card is neither connected from nor deleted, and a lifted line, a line between instances and an
 *   interaction are not renamed in place: the specifications' word, where the oracle said nothing.
 * - The actions are the specifications' F2 entries under their wire ids, an element's rename and a line's
 *   relabel; the oracle's Insert and Delete keys, which no backend action answered, are gone, and the empty
 *   canvas offers the specification's menu.
 * So the tests are the drawing, the declarations once those decisions are taken out, and the shapes.
 */

const K = C4ElementKindProto;

function card(id: string, kind: C4ElementKindProto, x: number, y: number, fields: { name: string; typeLine?: string; description?: string; width?: number; height?: number; shape?: string; background?: string; color?: string }): [string, C4Node] {
  return [id, {
    id,
    x,
    y,
    payload: create(C4ElementPayloadSchema, {
      kind,
      name: fields.name,
      typeLine: fields.typeLine ?? "",
      description: fields.description ?? "",
      width: fields.width ?? 160,
      height: fields.height ?? 90,
      style: { background: fields.background ?? "#1168bd", color: fields.color ?? "#ffffff", shape: fields.shape ?? "RoundedBox" },
    }),
  }];
}

const CORPUS: C4Model = {
  nodes: new Map([
    card("customer", K.C4_ELEMENT_KIND_PERSON, 200, 80, { name: "Personal Banking Customer", typeLine: "[Person]", description: "A customer of the bank, with personal bank accounts.", shape: "Person", background: "#08427b" }),
    card("web", K.C4_ELEMENT_KIND_CONTAINER, 200, 320, { name: "Web Application", typeLine: "[Container: Java and Spring MVC and a very long technology list]", description: "Delivers the static content and the Internet banking single page application.", width: 240, height: 120, background: "#438dd5" }),
    card("db", K.C4_ELEMENT_KIND_CONTAINER, 520, 320, { name: "Database", typeLine: "[Container: Oracle Database Schema]", description: "Stores user registration information, hashed authentication credentials, access logs, etc.", width: 200, height: 120, shape: "Cylinder", background: "#438dd5" }),
    card("mainframe", K.C4_ELEMENT_KIND_SOFTWARE_SYSTEM, 520, 80, { name: "Mainframe Banking System", typeLine: "[Software System]", description: "Supercalifragilisticexpialidociousnessofalongunbrokenword stores everything.", background: "#999999" }),
    card("server", K.C4_ELEMENT_KIND_DEPLOYMENT_NODE, 800, 80, { name: "bigbank-web***", typeLine: "[Deployment Node: Ubuntu 16.04 LTS]", shape: "Box", background: "#ffffff", color: "#000000" }),
    card("dns", K.C4_ELEMENT_KIND_INFRASTRUCTURE_NODE, 800, 320, { name: "DNS", typeLine: "[Infrastructure Node: Route 53]", shape: "Box", background: "#ffffff", color: "#000000" }),
    card("component", K.C4_ELEMENT_KIND_COMPONENT, 1060, 80, { name: "Sign In Controller", typeLine: "[Component: Spring MVC Rest Controller]", description: "Allows users to sign in.", background: "#85bbf0", color: "#000000" }),
    card("instance", K.C4_ELEMENT_KIND_CONTAINER_INSTANCE, 1060, 320, { name: "Web Application", typeLine: "[Container: Java and Spring MVC]", background: "#438dd5" }),
    card("sys-instance", K.C4_ELEMENT_KIND_SOFTWARE_SYSTEM_INSTANCE, 1320, 80, { name: "Internet Banking System", typeLine: "[Software System]" }),
    card("bare", K.C4_ELEMENT_KIND_SOFTWARE_SYSTEM, 1320, 320, { name: "", typeLine: "" }),
  ]),
  relationships: new Map([
    ["customer->web@4", { id: "customer->web@4", payload: create(C4RelationshipPayloadSchema, { sourceId: "customer", destinationId: "web", description: "Visits bigbank.com/ib using", technology: "HTTPS" }) }],
    ["web->db@5", { id: "web->db@5", payload: create(C4RelationshipPayloadSchema, { sourceId: "web", destinationId: "db", description: "Reads from and writes to" }) }],
    ["web->mainframe@6", { id: "web->mainframe@6", payload: create(C4RelationshipPayloadSchema, { sourceId: "web", destinationId: "mainframe", description: "" }) }],
    ["component->db@9", { id: "component->db@9", payload: create(C4RelationshipPayloadSchema, { sourceId: "component", destinationId: "db", description: "Validates credentials", interactionOrder: "2.1" }) }],
  ]),
  boundaries: new Map([
    ["boundary:ibs", { id: "boundary:ibs", x: 360, y: 320, payload: create(C4BoundaryPayloadSchema, { name: "Internet Banking System", kind: "Software System", width: 600, height: 220 }) }],
  ]),
  view: create(C4ViewPayloadSchema, {
    title: "Container diagram for Internet Banking System",
    viewKind: "Container",
    viewKey: "Containers",
    legend: [
      { label: "Person", style: { background: "#08427b" } },
      { label: "Container", style: { background: "#438dd5" } },
      { label: "Software System (external)", style: { background: "#999999" } },
      { label: "Unstyled" },
    ],
  }),
};

/** The canvas's markup, with the ids React generates per mount taken out. */
function drawn(definition: DiagramDefinition, model: DiagramModel, around: (canvas: JSX.Element) => JSX.Element = (canvas) => canvas): string {
  const { container, unmount } = render(around(
    <DiagramCanvas
      definition={definition}
      model={model}
      events={{}}
      source={{ entryId: new Uint8Array(16), path: ["bank.dsl"] }}
      ariaLabel="C4 diagram"
      className="c4-canvas-host"
      scrollbarsClassName="c4-scrollbars"
    />,
  ));
  const markup = container.innerHTML.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");
  unmount();
  return markup;
}

/** The title and the key as the module drew them around the hand-written definition's canvas, moved here unchanged. */
function handWrittenChrome(model: C4Model) {
  return (canvas: JSX.Element) => (
    <>
      {model.view && (
        <div className="c4-canvas-title" data-testid="c4-title">
          {model.view.title}
        </div>
      )}
      {canvas}
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
  );
}

const SPECIFICATIONS: Readonly<Record<string, string>> = {
  SystemLandscape: systemLandscape,
  SystemContext: context,
  Container: container,
  Component: component,
  Dynamic: dynamic,
  Deployment: deployment,
};

/** The oracle with the dash its stylesheet drew stated on its line, as the specifications state it. */
const ORACLE_WITH_DASH: DiagramDefinition = {
  ...HAND_WRITTEN_DEFINITION,
  relationTypes: HAND_WRITTEN_DEFINITION.relationTypes.map((relation) => ({ ...relation, style: { ...relation.style, dash: [6, 4] } })),
};

describe("the C4 canvas's compiled definitions", () => {
  it.each(Object.keys(SPECIFICATIONS))("draw what the hand-written definition drew, for a %s view", (viewKind) => {
    const compiled = drawn(C4_DEFINITIONS[viewKind]!, c4DiagramModel(CORPUS));
    const oracle = drawn(ORACLE_WITH_DASH, handWrittenModel(CORPUS), handWrittenChrome(CORPUS));

    // The cases the comparison would pass vacuously on: a canvas that drew nothing for either.
    for (const drawnPart of ["c4-node-name", "c4-node-type", "c4-node-description", "c4-boundary-label", "c4-relationship-line", "2.1. Validates credentials", "c4-legend-entry", 'data-testid="c4-title"', "<circle", "<ellipse"]) {
      expect(compiled).toContain(drawnPart);
    }
    // The one card without a name, announced by its id rather than by nothing.
    expect(compiled).toContain('data-element-id="bare" role="button" aria-label="bare"');
    expect(oracle).toContain('data-element-id="bare" role="button" aria-label=""');
    expect(compiled).toBe(oracle.replace('data-element-id="bare" role="button" aria-label=""', 'data-element-id="bare" role="button" aria-label="bare"'));
  });

  it("draw neither a title nor a key before the view has arrived", () => {
    const compiled = drawn(C4_DEFINITIONS.Container!, c4DiagramModel({ ...CORPUS, view: null }));

    expect(compiled).not.toContain("c4-canvas-title");
    expect(compiled).not.toContain("c4-canvas-legend");
    expect(compiled).toBe(drawn(ORACLE_WITH_DASH, handWrittenModel({ ...CORPUS, view: null })).replace('aria-label=""', 'aria-label="bare"'));
  });

  it("are one notation, whichever view kind's specification they are compiled from", () => {
    for (const viewKind of Object.keys(SPECIFICATIONS)) {
      expect(C4_DEFINITIONS[viewKind]).toEqual(C4_DEFINITIONS.SystemContext);
    }
  });

  it("declare each card as the hand-written definition declared its element, but for the decisions named above", () => {
    const [element, boundary] = HAND_WRITTEN_DEFINITION.elementTypes as [ElementTypeDefinition, ElementTypeDefinition];
    const [name, typeLine, description] = element.labels!;
    const card = (id: string): ElementTypeDefinition => ({
      ...element,
      id,
      labels: [
        name!,
        { text: { path: "payload.typeLine" }, anchorTo: "top", offset: typeLine!.offset, truncate: true, fit: { fontSize: 10, inset: 12 }, className: typeLine!.className },
        { text: { path: "payload.description" }, anchorTo: "top", offset: description!.offset, wrap: true, fit: { fontSize: 10, inset: 12 }, stack: description!.stack, className: description!.className },
      ],
      accessibility: { role: "button", label: { path: "payload.shownName" } },
      anchors: { kind: "edge", enabled: false, visible: false },
      deletable: false,
    });
    const cards = ["person", "softwaresystem", "container", "component", "deploymentnode", "infrastructurenode", "containerinstance", "softwaresysteminstance"];

    expect(C4_DEFINITIONS.Container!.elementTypes).toEqual([...cards.map(card), boundary]);
  });

  it("declare each line as the hand-written definition declared its relationship, but for the decisions named above", () => {
    const [relationship] = HAND_WRITTEN_DEFINITION.relationTypes as [RelationTypeDefinition];
    const cards = C4_DEFINITIONS.Container!.elementTypes.filter((type) => type.id !== "boundary").map((type) => type.id);
    const line = (id: string, renamed: boolean, allowSelf: boolean): RelationTypeDefinition => ({
      ...relationship,
      id,
      style: { endMarker: "arrow", dash: [6, 4] },
      label: { placement: "midpoint", offset: -6, ...(renamed ? { editable: true } : {}) },
      endpoints: { source: { elementTypes: cards, anchors: "edge" }, target: { elementTypes: cards, anchors: "edge" }, allowSelf },
    });

    expect(C4_DEFINITIONS.Container!.relationTypes).toEqual([
      line("relationship", true, true),
      line("liftedrelationship", false, false),
      line("instanceline", false, false),
      line("interactionline", false, true),
    ]);
  });

  it("offer the specifications' actions, each sending the key its menu entry writes", () => {
    expect(C4_DEFINITIONS.Container!.actions).toEqual([
      { id: "c4.rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
      { id: "c4.relabel", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "connection" }] },
    ]);
    // Every key the oracle sent but the two no backend action answered.
    const sent = new Set(C4_DEFINITIONS.Container!.actions!.map((action) => action.backendKey));
    expect((HAND_WRITTEN_DEFINITION.actions ?? []).map((action) => action.backendKey).filter((key) => !sent.has(key))).toEqual(["Insert", "Delete"]);
    expect(C4_DEFINITIONS.Container!.toolbox).toBeUndefined();
    expect(C4_DEFINITIONS.Container!.backgroundMenu).toBe(true);
  });

  it("are what the canvas draws: the module's definitions are compiled from the bundled specifications", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    for (const [viewKind, text] of Object.entries(SPECIFICATIONS)) {
      const compiled = compileNotation(parseDisl(text), C4_BINDINGS);
      expect(C4_DEFINITIONS[viewKind]).toEqual(assertValidDiagramDefinition({ ...compiled, elementTypes: [...compiled.elementTypes, BOUNDARY_TYPE] }));
    }
  });
});

/**
 * The specifications' shapes are the silhouettes the canvas draws them as.
 *
 * The canvas never reads a shape's geometry: `c4Bindings.ts` says every card is the library's `styled-box`,
 * whose silhouette the element names. This holds the two to one drawing - each part of `c4Person`,
 * `c4Cylinder`, the `roundedRect` of radius 8 and the `rect` in the order drawn, at the box it stands in -
 * at a grid of card sizes, so neither can be changed without the other.
 */
describe("the C4 specifications' shapes", () => {
  const SPEC = parseDisl(container);
  const SIZES: readonly (readonly [number, number])[] = [[160, 90], [240, 120], [200, 71.6], [173.5, 39.6]];

  /** What a part draws, as the element it is drawn as and the box it stands in, from the card's top left. */
  interface Drawn { element: string; x: number; y: number; w: number; h: number; radius: number }

  const round = (value: number) => Math.round(value * 1e9) / 1e9 + 0;

  function specified(parts: readonly DislShapePart[], w: number, h: number): Drawn[] {
    return parts.map((part) => {
      const box = resolveBox(part.box!, { w, h, p: {} });
      const shape = typeof part.shape === "string" ? { type: part.shape, params: {} } : (part.shape as { type: string; params?: { radius?: number } });
      const element = shape.type === "circle" ? "circle" : shape.type === "ellipse" ? "ellipse" : "rect";
      return { element, x: round(box.x), y: round(box.y), w: round(box.w), h: round(box.h), radius: shape.params?.radius ?? 0 };
    });
  }

  /** The shape group's children as the canvas draws a card of the silhouette, at the size given. */
  function drawnAs(silhouette: string, w: number, h: number): Drawn[] {
    const model: C4Model = { nodes: new Map([card("a", K.C4_ELEMENT_KIND_CONTAINER, 0, 0, { name: "A", width: w, height: h, shape: silhouette })]), relationships: new Map(), boundaries: new Map(), view: null };
    const { container: root, unmount } = render(
      <DiagramCanvas definition={C4_DEFINITIONS.Container!} model={c4DiagramModel(model)} events={{}} source={{ entryId: new Uint8Array(16), path: ["bank.dsl"] }} ariaLabel="C4 diagram" />,
    );
    const group = root.querySelector('[data-element-id="a"] g.c4-node')!;
    const number = (element: Element, name: string) => Number(element.getAttribute(name) ?? 0);
    const parts = [...group.children].filter((child) => child.tagName !== "text").map((child): Drawn => {
      const tag = child.tagName;
      if (tag === "circle") {
        const r = number(child, "r");
        return { element: tag, x: round(number(child, "cx") - r + w / 2), y: round(number(child, "cy") - r + h / 2), w: 2 * r, h: 2 * r, radius: 0 };
      }
      if (tag === "ellipse") {
        const rx = number(child, "rx");
        const ry = number(child, "ry");
        return { element: tag, x: round(number(child, "cx") - rx + w / 2), y: round(number(child, "cy") - ry + h / 2), w: round(2 * rx), h: 2 * ry, radius: 0 };
      }
      return { element: tag, x: round(number(child, "x") + w / 2), y: round(number(child, "y") + h / 2), w: round(number(child, "width")), h: round(number(child, "height")), radius: number(child, "rx") };
    });
    unmount();
    return parts;
  }

  const custom = (name: string) => (SPEC.notation.shapes![name] as DislCustomShape).parts!;
  const whole = (type: string, radius?: number): DislShapePart[] => [{ id: "body", shape: radius === undefined ? type : { type, params: { radius } }, box: { x: 0, y: 0, w: "w", h: "h" } } as DislShapePart];

  it.each([
    ["Person", "c4Person", () => custom("c4Person")],
    ["Cylinder", "c4Cylinder", () => custom("c4Cylinder")],
    ["RoundedBox", "a roundedRect of radius 8", () => whole("roundedRect", 8)],
    ["Box", "a rect", () => whole("rect")],
  ] as const)("draw the %s silhouette as %s states it, part by part", (silhouette, _shape, parts) => {
    for (const [w, h] of SIZES) {
      expect(drawnAs(silhouette, w, h)).toEqual(specified(parts(), w, h));
    }
  });

  it("are the shapes the specifications draw their cards with", () => {
    const shapes = Object.fromEntries(Object.entries(SPEC.notation.nodes).map(([name, node]) => [name, typeof node.shape === "string" ? node.shape : (node.shape as { type: string; params?: unknown })]));
    expect(shapes).toEqual({
      Person: "c4Person",
      SoftwareSystem: { type: "roundedRect", params: { radius: 8 } },
      Container: { type: "roundedRect", params: { radius: 8 } },
      Component: { type: "roundedRect", params: { radius: 8 } },
      DeploymentNode: "rect",
      InfrastructureNode: "rect",
      ContainerInstance: { type: "roundedRect", params: { radius: 8 } },
      SoftwareSystemInstance: { type: "roundedRect", params: { radius: 8 } },
    });
  });
});
