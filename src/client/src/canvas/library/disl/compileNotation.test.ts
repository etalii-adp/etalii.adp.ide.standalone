import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument, DislLabel } from "./disTypes";

/**
 * The compiler's own rules, on a specification small enough to read in one go. The two bundled
 * specifications are held to their modules' hand-written definitions in the modules' tests; this
 * pins down what each mapping does and, as much, what it refuses.
 */

function specOf(overrides: Partial<DislDocument> = {}): DislDocument {
  return {
    disl: "0.2",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: {
        Card: { attributes: { name: { type: "string" }, when: { type: "string" } } },
        Box: { children: { allowed: ["Card"] } },
      },
      relations: { Link: { source: ["Card", "Box"], target: "Card", directed: true, allowSelfLoops: false, allowParallel: false } },
    },
    notation: {
      nodes: {
        Card: {
          shape: "rect",
          labels: [{ id: "name", text: { cel: "self.name + ' (' + since(self) + ')'" }, position: "outside-left", editable: "inline" }],
          tooltip: { attribute: "name" },
          anchors: { mode: "sides", sides: ["left", "right"] },
          size: { default: [80, 40], resizable: true },
        },
        Box: { shape: { type: "hexagon", params: { inset: 0.25 } }, size: { fixed: [40, 40] }, connectable: false, placement: { movable: false } },
      },
      edges: { Link: { line: { routing: "orthogonal" }, targetMarker: "arrow" } },
    },
    toolbox: {
      contextMenus: [
        { for: ["Card"], tools: [{ kind: "editLabel", shortcut: "F2" }, { kind: "delete", shortcut: "Delete" }, { kind: "operation", operation: "pin", shortcut: "P" }] },
        { for: ["Link"], tools: [{ kind: "delete", shortcut: "Delete" }] },
        { for: ["diagram"], tools: [{ kind: "operation", operation: "arrange" }] },
      ],
    },
    "x-test": { actions: { editLabel: "t.rename", delete: "t.remove", "Link/delete": "t.unlink", pin: "t.pin" } },
    ...overrides,
  };
}

const BINDINGS: NotationBindings = {
  wireIds: "x-test",
  celPaths: { "since(self)": "payload.since" },
  classNames: (type) => [{ className: `t-${type.toLowerCase()}`, on: "element" }],
};

describe("compileNotation", () => {
  it("maps nodes, labels, anchors, sizes and placement", () => {
    const definition = compileNotation(specOf(), BINDINGS);
    expect(definition.elementTypes).toEqual([
      {
        id: "card",
        shape: "box",
        classNames: [{ className: "t-card", on: "element" }],
        labels: [{ text: { template: "{payload.name} ({payload.since})" }, placement: "before", editable: true }],
        tooltip: { path: "payload.name" },
        anchors: { kind: "edge", edgeSides: "horizontal" },
        sizing: "user",
        resize: "both",
      },
      {
        id: "box",
        shape: "hexagon",
        classNames: [{ className: "t-box", on: "element" }],
        anchors: { kind: "edge", enabled: false, visible: false },
        sizing: "model",
        draggable: false,
      },
    ]);
  });

  it("compiles a relation end's max to the library's limit at that end, beside one per direction", () => {
    const spec = specOf();
    spec.metamodel.relations = {
      Link: { source: { types: ["Card", "Box"], max: 2 }, target: { types: ["Card"], max: 1 }, directed: true, allowSelfLoops: false, allowParallel: false },
    };
    const [link] = compileNotation(spec, BINDINGS).relationTypes;
    expect(link?.endpoints?.cardinality).toEqual({ perPair: "ordered", maxFromSource: 2, maxIntoTarget: 1 });
  });

  it("compiles acyclic on an abstract relation type to one rule over the subtypes it draws", () => {
    const spec = specOf();
    spec.metamodel.relations = {
      Owns: { abstract: true, acyclic: true },
      Holds: { extends: "Owns", source: "Card", target: "Card" },
      Grips: { extends: "Owns", source: "Card", target: "Card", acyclic: true },
      Link: { source: ["Card", "Box"], target: "Card" },
    };
    spec.notation.edges = { Holds: {}, Grips: {}, Link: {} };
    expect(compileNotation(spec, BINDINGS).acyclic).toEqual([{ relationTypes: ["holds", "grips"] }]);
  });

  it("maps the relation: route, DISL's open arrow to the library's, one per direction", () => {
    const [link] = compileNotation(specOf(), BINDINGS).relationTypes;
    expect(link).toEqual({
      id: "link",
      route: "orthogonal",
      style: { endMarker: "open-arrow" },
      endpoints: { source: { elementTypes: ["card", "box"] }, target: { elementTypes: ["card"] }, allowSelf: false, cardinality: { perPair: "ordered" } },
    });
  });

  it("declares the actions a gesture or a key invokes, gestures first, with their wire ids", () => {
    const definition = compileNotation(specOf(), BINDINGS);
    expect(definition.actions).toEqual([
      { id: "t.rename", invokedBy: [{ kind: "shortcut", key: "F2" }, { kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element", elementTypes: ["card"] }] },
      { id: "t.remove", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element", elementTypes: ["card"] }] },
      { id: "t.unlink", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
      { id: "t.pin", invokedBy: [{ kind: "shortcut", key: "P" }], appliesTo: [{ kind: "element", elementTypes: ["card"] }] },
    ]);
    expect(definition.backgroundMenu).toBe(true);
    expect(definition.layout).toEqual({ modes: ["manual"] });
    expect(definition.dragging).toBe("enabled");
  });

  it("refuses a CEL term it has no payload path for, naming it", () => {
    const spec = specOf();
    const card = spec.notation.nodes.Card!;
    const nodes = { ...spec.notation.nodes, Card: { ...card, tooltip: { cel: "self.name + upper(self.when)" } } };
    expect(() => compileNotation({ ...spec, notation: { ...spec.notation, nodes } }, BINDINGS)).toThrow(/"upper\(self\.when\)".*celPaths/);
  });

  it("refuses a shape it cannot draw, a marker it has none for and a missing wire-id block", () => {
    const spec = specOf();
    const withShape = (shape: unknown) => ({ ...spec, notation: { ...spec.notation, nodes: { ...spec.notation.nodes, Box: { ...spec.notation.nodes.Box!, shape } } } }) as DislDocument;
    expect(() => compileNotation(withShape("cloud"), BINDINGS)).toThrow(/"cloud", which is no built-in/);
    expect(() => compileNotation(withShape({ type: "hexagon", params: { inset: 0.3 } }), BINDINGS)).toThrow(/inset 0.25/);
    const edges = { Link: { ...spec.notation.edges!.Link!, targetMarker: "erMany" } };
    expect(() => compileNotation({ ...spec, notation: { ...spec.notation, edges } }, BINDINGS)).toThrow(/no marker/);
    expect(() => compileNotation(spec, { ...BINDINGS, wireIds: "x-other" })).toThrow(/no "x-other" block/);
  });

  it("refuses a label position the library has no placement for", () => {
    const spec = specOf();
    const card = spec.notation.nodes.Card!;
    const nodes = { ...spec.notation.nodes, Card: { ...card, labels: [{ ...card.labels![0]!, position: "outside-top-left" }] } };
    expect(() => compileNotation({ ...spec, notation: { ...spec.notation, nodes } }, BINDINGS)).toThrow(/"outside-top-left"/);
  });

  it("places a label outside on the right beside its node, and refuses a distance of its own", () => {
    const beside = compileNotation(withLabel({ position: "outside-right" }), BINDINGS).elementTypes[0]!.labels![0];
    expect(beside).toEqual({ text: { path: "payload.name" }, placement: "beside" });
    expect(() => compileNotation(withLabel({ position: "outside-right", distance: 4 }), BINDINGS)).toThrow(/sits 4 beside/);
  });
  it("places a label outside on the top above its node and outside on the bottom below it, and refuses a distance of its own", () => {
    const above = compileNotation(withLabel({ position: "outside-top" }), BINDINGS).elementTypes[0]!.labels![0];
    expect(above).toEqual({ text: { path: "payload.name" }, placement: "above" });
    const below = compileNotation(withLabel({ position: "outside-bottom" }), BINDINGS).elementTypes[0]!.labels![0];
    expect(below).toEqual({ text: { path: "payload.name" }, placement: "below" });
    expect(() => compileNotation(withLabel({ position: "outside-top", distance: 4 }), BINDINGS)).toThrow(/sits 4 above/);
    expect(() => compileNotation(withLabel({ position: "outside-bottom", distance: 4 }), BINDINGS)).toThrow(/sits 4 below/);
  });
  /** The spec with Card's one label replaced by `label`. */
  function withLabel(label: Partial<DislLabel>): DislDocument {
    const spec = specOf();
    const card = spec.notation.nodes.Card!;
    const nodes = { ...spec.notation.nodes, Card: { ...card, labels: [{ id: "name", text: { attribute: "name" }, ...label } as DislLabel] } };
    return { ...spec, notation: { ...spec.notation, nodes } };
  }

  it("maps a label anchored at an edge to the library's aligned line, inset by the offset inward", () => {
    const right = compileNotation(withLabel({ position: { anchor: [1, 0], offset: [-4, 12], align: "end" } }), BINDINGS).elementTypes[0]!.labels![0];
    expect(right).toEqual({ text: { path: "payload.name" }, anchorTo: "top", align: "end", insetX: 4, offset: { x: 0, y: 12 } });

    const left = compileNotation(withLabel({ position: { anchor: [0, 1], offset: [3, -2], align: "start" } }), BINDINGS).elementTypes[0]!.labels![0];
    expect(left).toEqual({ text: { path: "payload.name" }, anchorTo: "bottom", align: "start", insetX: 3, offset: { x: 0, y: -2 } });
  });

  it("refuses a label at an edge aligned other than toward it, and one anchored off the middle and the edges", () => {
    expect(() => compileNotation(withLabel({ position: { anchor: [1, 0], align: "start" } }), BINDINGS)).toThrow(/right edge aligned "start"/);
    expect(() => compileNotation(withLabel({ position: { anchor: [1, 0] } }), BINDINGS)).toThrow(/right edge with no align/);
    expect(() => compileNotation(withLabel({ position: { anchor: [0.25, 0] } }), BINDINGS)).toThrow(/anchors at \[0.25,0\]/);
  });

  it("draws a label only while the text it is visible on is non-empty, and refuses any other condition", () => {
    const drawn = compileNotation(withLabel({ visible: { cel: "since(self) != ''" } }), BINDINGS).elementTypes[0]!.labels![0];
    expect(drawn).toEqual({ text: { path: "payload.name" }, when: { path: "payload.since", is: "non-empty" } });
    const attribute = compileNotation(withLabel({ visible: "self.when != ''" }), BINDINGS).elementTypes[0]!.labels![0];
    expect(attribute).toEqual({ text: { path: "payload.name" }, when: { path: "payload.when", is: "non-empty" } });
    const always = compileNotation(withLabel({ visible: true }), BINDINGS).elementTypes[0]!.labels![0];
    expect(always).toEqual({ text: { path: "payload.name" } });

    expect(() => compileNotation(withLabel({ visible: { cel: "self.when == 'x'" } }), BINDINGS)).toThrow(/visible when .*non-empty/);
    expect(() => compileNotation(withLabel({ visible: false }), BINDINGS)).toThrow(/visible when false/);
  });

  it("gives a relation its line's class and makes it unselectable where the notation says so", () => {
    const spec = specOf();
    const edges = { Link: { ...spec.notation.edges!.Link!, selectable: false } };
    const [link] = compileNotation({ ...spec, notation: { ...spec.notation, edges } }, { ...BINDINGS, relationLineClassName: () => "t-line" }).relationTypes;
    expect(link).toMatchObject({ lineClassName: "t-line", selectable: false });
    expect(compileNotation(specOf(), BINDINGS).relationTypes[0]).not.toHaveProperty("selectable");
  });

  it("matches DISL's Space as the key it is, and hands each action to the module with its entry's shortcut", () => {
    const spec = specOf();
    const contextMenus = [{ for: ["Card"], tools: [{ kind: "operation", operation: "pin", shortcut: "Space" }, { kind: "delete", shortcut: "Delete" }] }];
    const seen: [string, { shortcut?: string }][] = [];
    const definition = compileNotation({ ...spec, toolbox: { contextMenus } } as DislDocument, {
      ...BINDINGS,
      action: (action, entry) => {
        seen.push([action.id, entry]);
        return { ...action, backendKey: entry.shortcut };
      },
    });

    expect(definition.actions).toEqual([
      { id: "t.remove", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element", elementTypes: ["card"] }] },
      { id: "t.pin", backendKey: " ", invokedBy: [{ kind: "shortcut", key: " " }], appliesTo: [{ kind: "element", elementTypes: ["card"] }] },
    ]);
    expect(seen).toEqual([
      ["t.remove", { shortcut: "Delete" }],
      ["t.pin", { shortcut: " " }],
    ]);
  });

  /**
   * A spec drawn as the timeline is: two moving types with named begin and end anchors on their sides,
   * one relation started from either, labelled above its middle, created on an empty release by its tool.
   */
  function spanSpec(): DislDocument {
    const anchors = { mode: "fixed" as const, points: [{ id: "begin", x: 0, y: 0.5 }, { id: "end", x: 1, y: 0.5 }] };
    return specOf({
      metamodel: {
        types: { Item: { abstract: true }, Span: { extends: "Item" }, Point: { extends: "Item" } },
        relations: { Next: { source: { types: ["Item"], role: "earlier" }, target: { types: "Item" }, directed: true, allowSelfLoops: false, allowParallel: true } } as unknown as DislDocument["metamodel"]["relations"],
      },
      notation: {
        nodes: {
          Span: { shape: "roundedRect", anchors, placement: { movable: { x: true, y: true } } },
          Point: { shape: "diamond", anchors, size: { fixed: [18, 18] } },
        },
        edges: {
          Next: {
            line: { routing: "bezier" },
            targetMarker: "none",
            connect: { from: { end: "source", begin: "target" }, tool: "relate" },
            labels: [{ id: "label", text: { attribute: "label" }, at: "middle", side: "above", distance: 6, editable: "inline" }],
          },
        },
      },
      coordinates: {
        axes: { x: { kind: "linear", scale: 1 }, rows: { kind: "linear", scale: 60 } },
        systems: { canvas: { kind: "cartesian", x: "x", y: "rows", snapping: { x: "none", y: { grid: { spacing: 1 } }, feedback: { showValue: true } } } },
        default: "canvas",
      } as unknown as DislDocument["coordinates"],
      toolbox: { tools: { relate: { id: "relate", creates: "Next", mode: "drag", createTarget: { type: "Span" } } } },
    });
  }

  const HINT = { text: { template: "here" }, when: { path: "state.dragging", is: "true" } } as const;
  const SPAN_BINDINGS: NotationBindings = { ...BINDINGS, wireIds: "x-test", dragHint: HINT, builtInShapes: { roundedRect: "span", diamond: "moment" } };

  it("names fixed anchors on the sides they lie on, and narrows a line's other end to those sides", () => {
    const [span] = compileNotation(spanSpec(), SPAN_BINDINGS).elementTypes;
    expect(span!.anchors).toEqual({
      kind: "sides",
      fractions: [{ side: "left", at: 0.5, name: "begin" }, { side: "right", at: 0.5, name: "end" }],
      edgeSides: "horizontal",
    });

    const spec = spanSpec();
    const off = { ...spec, notation: { ...spec.notation, nodes: { ...spec.notation.nodes, Span: { ...spec.notation.nodes.Span!, anchors: { mode: "fixed" as const, points: [{ id: "mid", x: 0.5, y: 0.5 }] } } } } };
    expect(() => compileNotation(off, SPAN_BINDINGS)).toThrow(/"mid" at \(0.5, 0.5\) lies on no side/);
  });

  it("draws a built-in shape as the module states, ahead of the library's own drawing of it", () => {
    expect(compileNotation(spanSpec(), SPAN_BINDINGS).elementTypes.map((type) => type.shape)).toEqual(["span", "moment"]);
    expect(compileNotation(spanSpec(), { ...SPAN_BINDINGS, builtInShapes: { roundedRect: "span" } }).elementTypes[1]!.shape).toBe("diamond");
  });

  it("maps an edge's label, its starting anchors and its tool's release on empty canvas", () => {
    const [next] = compileNotation(spanSpec(), { ...SPAN_BINDINGS, relationHitClassName: () => "t-hit" }).relationTypes;
    expect(next).toEqual({
      id: "next",
      route: "cubic-bezier",
      style: { endMarker: "none" },
      hitClassName: "t-hit",
      label: { placement: "midpoint", offset: -6, editable: true },
      endpoints: { source: { elementTypes: ["span", "point"], anchors: ["begin", "end"] }, target: { elementTypes: ["span", "point"] }, allowSelf: false },
      emptyRelease: "complete",
    });

    const spec = spanSpec();
    const withEdge = (edge: Partial<NonNullable<DislDocument["notation"]["edges"]>[string]>) =>
      ({ ...spec, notation: { ...spec.notation, edges: { Next: { ...spec.notation.edges!.Next!, ...edge } } } }) as DislDocument;
    // A tool that creates no end leaves an empty release ignored; a tool the toolbox lacks is refused.
    expect(compileNotation({ ...spec, toolbox: { tools: { relate: { id: "relate", creates: "Next" } } } }, SPAN_BINDINGS).relationTypes[0]).not.toHaveProperty("emptyRelease");
    expect(() => compileNotation(withEdge({ connect: { tool: "absent" } }), SPAN_BINDINGS)).toThrow(/"absent", which the toolbox does not declare/);
    expect(compileNotation(withEdge({ labels: [{ id: "l", text: { attribute: "label" }, at: "end", side: "below", distance: 4 }] }), SPAN_BINDINGS).relationTypes[0]!.label).toEqual({ placement: "target", offset: 4 });
    expect(() => compileNotation(withEdge({ labels: [{ id: "l", text: { cel: "upper(self.label)" } }] }), SPAN_BINDINGS)).toThrow(/connection's own label/);
    expect(() => compileNotation(withEdge({ labels: [{ id: "l", text: { attribute: "label" }, at: 0.25 }] }), SPAN_BINDINGS)).toThrow(/labels at a fraction along a "bezier" line/);
  });

  it("snaps only the axes that snap, takes an axis the module binds, and adds the drag hint to what moves", () => {
    const definition = compileNotation(spanSpec(), SPAN_BINDINGS);
    expect(definition.snap).toEqual({ y: { step: 60 } });
    expect(definition.elementTypes.map((type) => type.labels)).toEqual([[HINT], [HINT]]);

    const day = { step: { path: "payload.day" }, origin: { path: "payload.dayOrigin" } };
    expect(compileNotation(spanSpec(), { ...SPAN_BINDINGS, snapAxes: { x: day } }).snap).toEqual({ y: { step: 60 }, x: day });
    expect(() => compileNotation(spanSpec(), { ...SPAN_BINDINGS, dragHint: undefined })).toThrow(/no dragHint/);
  });

  it("leaves the rulers to a module that draws its own", () => {
    const spec = specOf({
      coordinates: {
        axes: { time: { kind: "time", valueType: "datetime", scale: { unit: "day", size: 20 }, ruler: { visible: true, position: "bottom", levels: [] } }, rows: { kind: "linear", scale: 60 } },
        systems: { timeline: { kind: "cartesian", x: "time", y: "rows" } },
        default: "timeline",
      } as unknown as DislDocument["coordinates"],
    });
    expect(() => compileNotation(spec, BINDINGS)).toThrow(/only a view-fixed bottom ruler/);
    expect(compileNotation(spec, { ...BINDINGS, ownRulers: true }).chrome).toBeUndefined();
  });
});

/**
 * A relation without a target (DISL 4.9, an `optional` target end) and the stub it is drawn as
 * (DISL 6.10 *Stubs*), a line's label bound to the relation's own attribute through CEL, and the
 * accessible name DISL gives a node that states none (6.15).
 */
describe("compileNotation, for a relation that may have no target", () => {
  function openSpec(stub: Readonly<Record<string, unknown>> | undefined, label: Readonly<Record<string, unknown>> = { attribute: "label" }): DislDocument {
    return {
      disl: "0.3",
      language: { id: "test", version: "1.0.0" },
      metamodel: {
        types: {
          File: { label: "File", labelAttribute: "path", attributes: { path: { type: "string" } } },
          Part: { label: "Part", extends: "File" },
        },
        relations: { Uses: { source: "File", target: { types: ["Part"], optional: true }, attributes: { label: { type: "string" } } } },
      },
      notation: {
        nodes: { File: { shape: "rect", connectable: false }, Part: { shape: "rect", connectable: false } },
        edges: {
          Uses: {
            line: { routing: "straight" },
            targetMarker: "arrowFilled",
            labels: [{ id: "label", text: label, at: "middle", side: "above", distance: 6 }],
            ...(stub !== undefined ? { stub } : {}),
          },
        },
      },
    } as unknown as DislDocument;
  }

  const OPEN_BINDINGS: NotationBindings = {
    celPaths: {},
    classNames: () => [],
    stubClassName: (relation) => `t-${relation.toLowerCase()}-open`,
    edgeLabelClassName: (relation, label) => `t-${relation.toLowerCase()}-${label}`,
  };

  const STUB = { length: 48, side: "right", style: "missing", label: { id: "asWritten", text: { cel: "self.label + ' (missing)'" }, side: "above" } };

  it("reads the stub: its length, its side, its style's class and its label", () => {
    const [uses] = compileNotation(openSpec(STUB), OPEN_BINDINGS).relationTypes;

    expect(uses?.stub).toEqual({
      length: 48,
      side: "right",
      className: "t-uses-open",
      label: { text: { template: "{payload.label} (missing)" }, side: "above", className: "t-uses-asWritten" },
    });
  });

  it("keeps a stated distance, leaves out what the stub does not state, and states no stub where there is none", () => {
    const [stated] = compileNotation(openSpec({ length: 30, side: "left", label: { id: "n", text: { attribute: "label" }, side: "below", distance: 4 } }), { celPaths: {}, classNames: () => [] }).relationTypes;
    expect(stated?.stub).toEqual({ length: 30, side: "left", label: { text: { path: "payload.label" }, side: "below", distance: 4 } });

    const [none] = compileNotation(openSpec(undefined), OPEN_BINDINGS).relationTypes;
    expect(none?.stub).toBeUndefined();
  });

  it("refuses a stub side it cannot place and a label beside a stub it cannot write", () => {
    expect(() => compileNotation(openSpec({ ...STUB, side: "auto" }), OPEN_BINDINGS)).toThrow(/stub leaves "auto"/);
    expect(() => compileNotation(openSpec({ ...STUB, label: { ...STUB.label, side: "on" } }), OPEN_BINDINGS)).toThrow(/stub label .* "on"/);
  });

  it("reads a term the module's celPaths names from its path, even where the term is an attribute of its own", () => {
    // A module whose payload carries one name for what the specification computes several ways.
    const spec = openSpec(undefined);
    const nodes = { ...spec.notation.nodes, File: { ...spec.notation.nodes.File!, tooltip: { cel: "'File ' + self.path" } } };
    const [file] = compileNotation({ ...spec, notation: { ...spec.notation, nodes } }, { ...OPEN_BINDINGS, celPaths: { "self.path": "payload.name" } }).elementTypes;

    expect(file?.tooltip).toEqual({ template: "File {payload.name}" });
  });

  it("draws a line's label from the relation's own attribute when the specification binds it as CEL", () => {
    const [uses] = compileNotation(openSpec(undefined, { cel: "self.label" }), OPEN_BINDINGS).relationTypes;

    expect(uses?.label).toEqual({ placement: "midpoint", offset: -6 });
    expect(() => compileNotation(openSpec(undefined, { cel: "self.label + '!'" }), OPEN_BINDINGS)).toThrow(/connection's own label/);
  });

  it("draws a line's label the module composes into the connection's own label, where its celPaths names the CEL", () => {
    const composed = "self.when != '' ? self.label + ' when ' + self.when : self.label";
    const [uses] = compileNotation(openSpec(undefined, { cel: composed }), { ...OPEN_BINDINGS, celPaths: { [composed]: "label" } }).relationTypes;

    expect(uses?.label).toEqual({ placement: "midpoint", offset: -6 });
  });

  it("names a node that states no accessible name as DISL does: its type's label and its label attribute", () => {
    const announced: NotationBindings = { ...OPEN_BINDINGS, focusable: true, accessibleRole: "button" };
    const [file, part] = compileNotation(openSpec(undefined), announced).elementTypes;

    expect(file?.accessibility).toEqual({ role: "button", focusable: true, label: { template: "File {payload.path}" } });
    // The label attribute is inherited from the type it extends.
    expect(part?.accessibility).toEqual({ role: "button", focusable: true, label: { template: "Part {payload.path}" } });
    // A module that asks for neither a role nor a tab order is left as it was.
    expect(compileNotation(openSpec(undefined), OPEN_BINDINGS).elementTypes[0]?.accessibility).toBeUndefined();
  });

  it("names a node that states its role and no name as DISL does too, and keeps a name it states", () => {
    // `accessibility.name`'s default is the type label plus `labelAttribute` (6.15), whatever else is stated.
    const spec = openSpec(undefined);
    const nodes = {
      File: { ...spec.notation.nodes.File!, accessibility: { role: "button" } },
      Part: { ...spec.notation.nodes.Part!, accessibility: { role: "link", name: { attribute: "path" } } },
    };
    const [file, part] = compileNotation({ ...spec, notation: { ...spec.notation, nodes } }, { ...OPEN_BINDINGS, focusable: true }).elementTypes;

    expect(file?.accessibility).toEqual({ role: "button", focusable: true, label: { template: "File {payload.path}" } });
    expect(part?.accessibility).toEqual({ role: "link", focusable: true, label: { path: "payload.path" } });
  });
});

describe("compileNotation, for a specification whose notation is inherited", () => {
  /**
   * A node type and a relation type with notation of their own on an abstract supertype only, and two
   * subtypes that state a part of theirs: DISL 4.7 looks notation up along the supertype chain, and the
   * resolution of a visual property (6.x, step 6) merges it per key.
   */
  function inheritingSpec(): DislDocument {
    return {
      disl: "0.3",
      language: { id: "test", version: "1.0.0" },
      metamodel: {
        types: {
          Thing: { abstract: true, attributes: { name: { type: "string" } } },
          Card: { extends: "Thing", label: "Card" },
          Note: { extends: "Thing", label: "Note" },
        },
        relations: {
          Link: { abstract: true, source: ["Thing"], target: { types: ["Thing"], optional: true } },
          Uses: { extends: "Link", source: ["Card"], target: { types: ["Note"], optional: true } },
          Owns: { extends: "Link", source: ["Card"], target: ["Card"] },
        },
      },
      notation: {
        nodes: {
          Thing: { shape: "rect", labels: [{ id: "name", text: { attribute: "name" }, position: "center" }], size: { default: [72, 32] }, deletable: false },
          Card: { tooltip: { attribute: "name" } },
          Note: { shape: "ellipse" },
        },
        edges: {
          Link: { line: { routing: "straight", stroke: { width: 1.5 } }, targetMarker: "arrowFilled" },
          Owns: { line: { stroke: { width: 2 } }, targetMarker: "none" },
        },
      },
    } as unknown as DislDocument;
  }

  const BINDINGS: NotationBindings = { celPaths: {}, classNames: (type) => [{ className: `t-${type.toLowerCase()}` }] };

  it("draws no abstract type, and gives each subtype its supertype's notation under its own", () => {
    const definition = compileNotation(inheritingSpec(), BINDINGS);

    expect(definition.elementTypes.map((type) => type.id)).toEqual(["card", "note"]);
    const [card, note] = definition.elementTypes;
    expect(card).toMatchObject({ shape: "box", labels: [{ text: { path: "payload.name" } }], tooltip: { path: "payload.name" }, deletable: false });
    expect(note).toMatchObject({ shape: "ellipse", labels: [{ text: { path: "payload.name" } }], deletable: false });
    expect(note?.tooltip).toBeUndefined();
  });

  it("draws a relation with no notation of its own as its supertype's, and merges the one it states", () => {
    const definition = compileNotation(inheritingSpec(), BINDINGS);

    expect(definition.relationTypes.map((relation) => relation.id)).toEqual(["uses", "owns"]);
    const [uses, owns] = definition.relationTypes;
    expect(uses?.style).toEqual({ endMarker: "arrow" });
    expect(uses?.endpoints).toMatchObject({ source: { elementTypes: ["card"] }, target: { elementTypes: ["note"] } });
    expect(owns?.style).toEqual({ endMarker: "none" });
    expect(owns?.route).toEqual(uses?.route);
  });

  it("gives a relation that states no ends of its own its supertype's, and keeps the ones it states", () => {
    const spec = inheritingSpec();
    const relations = spec.metamodel.relations!;
    const shared = { ...spec, metamodel: { ...spec.metamodel, relations: { ...relations, Owns: { extends: "Link" } } } } as unknown as DislDocument;
    const definition = compileNotation(shared, BINDINGS);

    const [uses, owns] = definition.relationTypes;
    expect(owns?.endpoints).toMatchObject({ source: { elementTypes: ["card", "note"] }, target: { elementTypes: ["card", "note"] } });
    expect(uses?.endpoints).toMatchObject({ source: { elementTypes: ["card"] }, target: { elementTypes: ["note"] } });
  });
});

describe("compileNotation, for the dash of a line", () => {
  function dashedSpec(dash: unknown, stubStyle?: unknown): DislDocument {
    return {
      disl: "0.3",
      language: { id: "test", version: "1.0.0" },
      metamodel: {
        types: { Card: { attributes: { name: { type: "string" } } } },
        relations: { Link: { source: ["Card"], target: { types: ["Card"], optional: true }, attributes: { dynamic: { type: "bool" } } } },
      },
      notation: {
        styles: {
          missing: { stroke: { width: 1.5, dash: [3, 3] } },
          missingDependency: { extends: "missing", stroke: { width: 2, dash: [2, 6] } },
        },
        nodes: { Card: { shape: "rect" } },
        edges: {
          Link: {
            line: { routing: "straight", stroke: { width: 1.5, ...(dash !== undefined ? { dash } : {}) } },
            targetMarker: "arrowFilled",
            ...(stubStyle !== undefined ? { stub: { length: 48, side: "right", style: stubStyle } } : {}),
          },
        },
      },
    } as unknown as DislDocument;
  }

  const BINDINGS: NotationBindings = { celPaths: {}, classNames: () => [] };

  it("draws a line with the dash its type states", () => {
    const [link] = compileNotation(dashedSpec([1, 4]), BINDINGS).relationTypes;

    expect(link?.style).toEqual({ endMarker: "arrow", dash: [1, 4] });
    expect(compileNotation(dashedSpec([]), BINDINGS).relationTypes[0]?.style).toEqual({ endMarker: "arrow" });
    expect(compileNotation(dashedSpec(undefined), BINDINGS).relationTypes[0]?.style).toEqual({ endMarker: "arrow" });
  });

  it("chooses a line's dash per connection by an attribute of its own, or a condition the module names", () => {
    const [link] = compileNotation(dashedSpec({ cel: "self.dynamic ? [5.0, 4.0] : []" }), BINDINGS).relationTypes;
    expect(link?.dashWhen).toEqual({ when: { path: "payload.dynamic", is: "true" }, dash: [5, 4] });
    expect(link?.style).toEqual({ endMarker: "arrow" });

    const [named] = compileNotation(dashedSpec({ cel: "self.mode == 'late' ? [2, 2] : [6, 2]" }), { ...BINDINGS, celConditions: { "self.mode == 'late'": { path: "payload.late", is: "true" } } }).relationTypes;
    expect(named?.dashWhen).toEqual({ when: { path: "payload.late", is: "true" }, dash: [2, 2] });
    expect(named?.style).toEqual({ endMarker: "arrow", dash: [6, 2] });

    expect(() => compileNotation(dashedSpec({ cel: "self.weight > 2 ? [1, 1] : []" }), BINDINGS)).toThrow(/dash .*self\.weight > 2/);
  });

  it("draws a stub with the dash of its own style, its extends resolved", () => {
    expect(compileNotation(dashedSpec([1, 4], "missing"), BINDINGS).relationTypes[0]?.stub?.dash).toEqual([3, 3]);
    expect(compileNotation(dashedSpec([1, 4], "missingDependency"), BINDINGS).relationTypes[0]?.stub?.dash).toEqual([2, 6]);
    expect(compileNotation(dashedSpec([1, 4], { stroke: { dash: [4, 1] } }), BINDINGS).relationTypes[0]?.stub?.dash).toEqual([4, 1]);
    expect(compileNotation(dashedSpec([1, 4], { stroke: { width: 3 } }), BINDINGS).relationTypes[0]?.stub?.dash).toBeUndefined();
  });
});

describe("compileNotation, for a colour chosen by index", () => {
  function tintedSpec(fill: string): DislDocument {
    return {
      disl: "0.3",
      language: { id: "test", version: "1.0.0" },
      metamodel: { types: { Step: { attributes: { index: { type: "int" } } }, Gate: {} } },
      notation: {
        nodes: {
          Step: { shape: "rect", style: { fill: { cel: fill }, stroke: { color: { cel: "token('color.hue' + string(self.index % 4))" } } } },
          Gate: { shape: "rect", style: { fill: { token: "color.surface" } } },
        },
      },
    } as unknown as DislDocument;
  }

  const BINDINGS: NotationBindings = {
    celPaths: {},
    classNames: (type) => [{ className: `t-${type.toLowerCase()}`, on: "shape" }],
    indexedColourClassName: (_type, token) => (token === "color.hue" ? "t-hue-" : undefined),
  };

  it("paints the slot an index wraps into, by the class the module names for the token's family", () => {
    const [step, gate] = compileNotation(tintedSpec("color(token('color.hue' + string(self.index % 4))).mix(token('color.surface'), 0.82)"), BINDINGS).elementTypes;

    expect(step?.classNames).toEqual([
      { className: "t-step", on: "shape" },
      { className: { parts: [{ template: "t-hue-" }, { path: "payload.index", number: { modulo: 4 } }], join: "" }, on: "shape", when: { path: "payload.index", is: "present" } },
    ]);
    expect(gate?.classNames).toEqual([{ className: "t-gate", on: "shape" }]);
  });

  it("refuses a colour chosen by index the module names no class for", () => {
    expect(() => compileNotation(tintedSpec("token('color.hue' + string(self.index % 4))"), { ...BINDINGS, indexedColourClassName: undefined })).toThrow(/color\.hue.*class/);
  });
});

describe("compileNotation, for an operation a double-click runs", () => {
  function revealingSpec(operation: Readonly<Record<string, unknown>>): DislDocument {
    return {
      disl: "0.3",
      language: { id: "test", version: "1.0.0" },
      metamodel: { types: { Card: {} } },
      notation: { nodes: { Card: { shape: "rect", doubleClick: "reveal" } } },
      behavior: { operations: { reveal: operation } },
    } as unknown as DislDocument;
  }

  it("invokes it by the key the operation states as well as by the double-click", () => {
    expect(compileNotation(revealingSpec({ label: "Reveal", shortcut: "Enter" }), { celPaths: {}, classNames: () => [] }).actions).toEqual([
      { id: "reveal", invokedBy: [{ kind: "shortcut", key: "Enter" }, { kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
    ]);
    expect(compileNotation(revealingSpec({ label: "Reveal" }), { celPaths: {}, classNames: () => [] }).actions).toEqual([
      { id: "reveal", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
    ]);
  });
});

describe("compileNotation, for a canvas with nothing to draw", () => {
  function emptySpec(empty: unknown): DislDocument {
    return {
      disl: "0.3",
      language: { id: "test", version: "1.0.0" },
      metamodel: { types: { Card: {} } },
      notation: { nodes: { Card: { shape: "rect" } }, ...(empty !== undefined ? { canvas: { empty } } : {}) },
    } as unknown as DislDocument;
  }

  const BINDINGS: NotationBindings = { celPaths: {}, classNames: () => [] };

  it("states the canvas's empty sentence as its chrome", () => {
    expect(compileNotation(emptySpec({ text: "Nothing to draw here." }), BINDINGS).chrome).toEqual({ empty: { text: { template: "Nothing to draw here." } } });
    expect(compileNotation(emptySpec(undefined), BINDINGS).chrome).toBeUndefined();
  });

  it("refuses a sentence a template would read a placeholder in", () => {
    expect(() => compileNotation(emptySpec({ text: "Nothing in {folder}." }), BINDINGS)).toThrow(/empty.*\{folder\}/);
  });
});
