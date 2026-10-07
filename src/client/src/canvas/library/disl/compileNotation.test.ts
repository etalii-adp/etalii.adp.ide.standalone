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
    const nodes = { ...spec.notation.nodes, Card: { ...card, labels: [{ ...card.labels![0]!, position: "outside-right" }] } };
    expect(() => compileNotation({ ...spec, notation: { ...spec.notation, nodes } }, BINDINGS)).toThrow(/"outside-right"/);
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
});
