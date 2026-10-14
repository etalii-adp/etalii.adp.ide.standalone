import { describe, expect, it, vi } from "vitest";
import { createEvent, fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "../DiagramCanvas";
import type { DiagramDefinition } from "../definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "../api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { arcBetween, normalAlong, pointAlong, type ArcBox } from "./bowedArc";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument, DislEdgeNotation, DislNodeNotation } from "./disTypes";

/**
 * What a notation of curved lines between bodied nodes, with a body-less node among them, compiles
 * to: a bowed arc and what rides along it, a stroke width chosen by thresholds, outline anchors, a
 * circular layout left to the host, and a node with no body drawn as its icon and its line at the
 * centre of the members it lists.
 *
 * Each case is drawn and read back from the markup, because the declarations hold functions, and a
 * function equal to another is a question only its drawing answers.
 */

const NODE: DislNodeNotation = {
  shape: "pill",
  size: { default: [100, 40], resizable: false },
  labels: [{ id: "name", text: { cel: "self.name" }, position: "center" }],
  anchors: { mode: "outline" },
};

const HUB: DislNodeNotation & { icon: unknown } = {
  shape: "none",
  size: { fixed: [60, 40] },
  placement: {
    x: { cel: "self.members.map(m, m.view.bounds.center()[0]).avg()" },
    y: { cel: "self.members.map(m, m.view.bounds.center()[1]).avg()" },
    anchor: "center",
    movable: false,
  } as unknown as DislNodeNotation["placement"],
  icon: { icon: "mdi-sync", position: "top", size: 26, flip: { cel: "self.turn == 'back' ? 'horizontal' : 'none'" } },
  labels: [{ id: "caption", text: { cel: "self.caption" }, position: "bottom" }],
  connectable: false,
};

const ARROW: DislEdgeNotation = {
  line: {
    routing: "curved",
    curvature: 0.2,
    stroke: { width: { cel: "!has(self.weight) ? 1.5 : (self.weight >= 2.0 ? 2.5 : (self.weight <= 0.5 ? 1.0 : 1.5))" } },
  },
  variants: [{ when: "self.flipped", line: { routing: "curved", curvature: -0.2 } }],
  targetMarker: "arrowFilled",
  anchoring: { source: { mode: "outline" }, target: { mode: "outline" } },
  selfLoop: { shape: "arc", size: 30, side: "top" },
  midMarkers: [
    { marker: { type: "bar", params: { width: 16 } }, at: 0.44, orient: "auto", visible: { cel: "self.delayed" } },
    { marker: { type: "bar", params: { width: 16 } }, at: 0.56, orient: "auto", visible: { cel: "self.delayed" } },
  ],
  labels: [
    { id: "sign", text: { cel: "self.sign == 'up' ? '+' : ''" }, at: 0.86, side: "left", distance: 11, editable: false } as unknown as NonNullable<DislEdgeNotation["labels"]>[number],
  ],
} as unknown as DislEdgeNotation;

function specOf(edge: DislEdgeNotation = ARROW, hub: DislNodeNotation = HUB): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: {
        Node: { attributes: { name: { type: "string" } } },
        Hub: { attributes: { caption: { type: "string" }, members: { type: "Node", many: true }, turn: { type: "string" } } },
      },
      relations: { Arrow: { source: "Node", target: "Node", directed: true, allowSelfLoops: true } },
    },
    notation: { nodes: { Node: NODE, Hub: hub }, edges: { Arrow: edge } },
    layout: { algorithms: { ring: { algorithm: "circular" } }, default: "ring" },
  };
}

const BINDINGS: NotationBindings = {
  celPaths: { "self.sign == 'up' ? '+' : ''": "payload.mark" },
  classNames: (type) => [{ className: `t-${type.toLowerCase()}`, on: "element" }],
  relationClassName: () => "t-arrow",
  iconClassName: () => "t-icon",
  midMarkerClassNames: () => ({ group: "t-bars", marker: "t-bar" }),
  edgeLabelClassName: () => "t-mark",
  strokeWidthClassName: (_relation, width) => `t-w-${width}`,
};

const box = (x: number, y: number): ArcBox => ({ x: x - 50, y: y - 20, width: 100, height: 40 });

const A: DiagramModelElement = { id: "a", type: "node", x: 0, y: 0, width: 100, height: 40, payload: { name: "A" } };
const B: DiagramModelElement = { id: "b", type: "node", x: 300, y: 100, width: 100, height: 40, payload: { name: "B" } };

function arrow(payload: Record<string, unknown>, from = "a", to = "b"): DiagramModelConnection {
  return { id: `${from}-${to}`, type: "arrow", sourceId: from, targetId: to, payload } as DiagramModelConnection;
}

function drawn(definition: DiagramDefinition, model: DiagramModel, events = {}) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={events} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const line = (container: HTMLElement, id: string) => container.querySelector(`[data-connection-id="${id}"] .canvas-connection-line`)!;

describe("compileNotation: curved lines and a body-less node", () => {
  it("draws a body-less node, attaches lines to the outline as an edge, and leaves a circular layout to the host", () => {
    const definition = compileNotation(specOf(), BINDINGS);

    expect(definition.elementTypes.map((type) => [type.id, type.shape, type.anchors.kind])).toEqual([["node", "pill", "edge"], ["hub", "none", "edge"]]);
    expect(definition.relationTypes[0]!.endpoints.source.anchors).toBe("edge");
    expect(definition.relationTypes[0]!.endpoints.target.anchors).toBe("edge");
    expect(definition.layout.modes).toEqual(["manual"]);
  });

  it("draws a curved routing as an arc bowed off the chord between the boxes, to the left of travel", () => {
    const { container } = drawn(compileNotation(specOf(), BINDINGS), { elements: [A, B], connections: [arrow({})] });

    expect(line(container, "a-b").getAttribute("d")).toBe(arcBetween(box(0, 0), box(300, 100), 0.2).path);
  });

  it("bows the other way where a variant's condition holds, and draws a line from a box to itself as the loop the notation sizes", () => {
    const { container } = drawn(compileNotation(specOf(), BINDINGS), { elements: [A, B], connections: [arrow({ flipped: true }), arrow({}, "b", "b")] });

    expect(line(container, "a-b").getAttribute("d")).toBe(arcBetween(box(0, 0), box(300, 100), -0.2).path);
    expect(line(container, "b-b").getAttribute("d")).toBe(arcBetween(box(300, 100), box(300, 100), 0.2, 30).path);
  });

  it("crosses the line with its bars where their condition holds, square to the arc at its apex", () => {
    const { container } = drawn(compileNotation(specOf(), BINDINGS), { elements: [A, B], connections: [arrow({ delayed: true }), arrow({ delayed: false }, "b", "a")] });

    const bars = [...container.querySelectorAll('[data-connection-id="a-b"] .t-bars line.t-bar')];
    const arc = arcBetween(box(0, 0), box(300, 100), 0.2);
    expect(bars.map((bar) => ["x1", "y1", "x2", "y2"].map((name) => Number(bar.getAttribute(name))))).toEqual(
      [0.44, 0.56].map((t) => [
        pointAlong(arc, t).x - arc.apexNormal.x * 8,
        pointAlong(arc, t).y - arc.apexNormal.y * 8,
        pointAlong(arc, t).x + arc.apexNormal.x * 8,
        pointAlong(arc, t).y + arc.apexNormal.y * 8,
      ]),
    );
    expect(container.querySelector('[data-connection-id="b-a"] .t-bars')).toBeNull();
  });

  it("writes a mark beside the line at a fraction along it, on the side it names, and nothing where the mark is empty", () => {
    const { container } = drawn(compileNotation(specOf(), BINDINGS), { elements: [A, B], connections: [arrow({ mark: "+" }), arrow({ mark: "" }, "b", "a")] });

    const mark = container.querySelector('[data-connection-id="a-b"] text.t-mark')!;
    const arc = arcBetween(box(0, 0), box(300, 100), 0.2);
    expect(mark.textContent).toBe("+");
    expect(Number(mark.getAttribute("x"))).toBe(pointAlong(arc, 0.86).x + normalAlong(arc, 0.86).x * 11);
    expect(Number(mark.getAttribute("y"))).toBe(pointAlong(arc, 0.86).y + normalAlong(arc, 0.86).y * 11);
    expect(container.querySelector('[data-connection-id="b-a"] text.t-mark')).toBeNull();
  });

  it("paints the width a stroke ladder chooses with the class the module names for it", () => {
    const { container } = drawn(compileNotation(specOf(), BINDINGS), {
      elements: [A, B],
      connections: [arrow({ weight: 3 }), arrow({ weight: 0.2 }, "b", "a"), arrow({}, "a", "a"), arrow({ weight: 1 }, "b", "b")],
    });

    const classOf = (id: string) => container.querySelector(`[data-connection-id="${id}"]`)!.getAttribute("class");
    expect([classOf("a-b"), classOf("b-a"), classOf("a-a"), classOf("b-b")]).toEqual([
      "canvas-connection library-connection t-arrow t-w-2.5",
      "canvas-connection library-connection t-arrow t-w-1",
      "canvas-connection library-connection t-arrow t-w-1.5",
      "canvas-connection library-connection t-arrow t-w-1.5",
    ]);
  });

  it("draws a body-less node at the centre of the members it lists, its icon above that point and its line on it", () => {
    const hub = (id: string, turn: string): DiagramModelElement => ({ id, type: "hub", x: 999, y: 999, width: 60, height: 40, payload: { caption: id, members: ["a", "b"], turn } });
    const { container } = drawn(compileNotation(specOf(), BINDINGS), { elements: [A, B, hub("h1", "on"), hub("h2", "back")], connections: [] });

    // The line sits where a centred line of an element standing at that point sits.
    const captionOf = (root: HTMLElement, id: string) => [...root.querySelectorAll(`[data-element-id="${id}"] text`)].find((text) => text.textContent === id)!;
    const placed: DiagramDefinition = { elementTypes: [{ id: "hub", shape: "none", labels: [{ text: { path: "payload.caption" } }], anchors: { kind: "edge" }, sizing: "model" }], relationTypes: [], layout: { modes: ["manual"] }, dragging: "disabled" };
    const reference = captionOf(drawn(placed, { elements: [{ ...hub("h1", "on"), x: 150, y: 50 }], connections: [] }).container, "h1");
    const caption = captionOf(container, "h1");
    expect([caption.getAttribute("x"), caption.getAttribute("y")]).toEqual([reference.getAttribute("x"), reference.getAttribute("y")]);

    const at = (angle: number, cx: number, cy: number) => `${(cx + 13 * Math.cos(angle)).toFixed(2)} ${(cy + 13 * Math.sin(angle)).toFixed(2)}`;
    const sweep = (clockwise: boolean) => {
      const start = -Math.PI / 2;
      const end = start + (clockwise ? 1 : -1) * Math.PI * 1.7;
      const flag = clockwise ? 1 : 0;
      return `M ${at(start, 150, 35)} A 13 13 0 0 ${flag} ${at((start + end) / 2, 150, 35)} A 13 13 0 0 ${flag} ${at(end, 150, 35)}`;
    };
    expect(container.querySelector('[data-element-id="h1"] path.t-icon')!.getAttribute("d")).toBe(sweep(true));
    expect(container.querySelector('[data-element-id="h2"] path.t-icon')!.getAttribute("d")).toBe(sweep(false));
  });

  it("tells a toolbox drop which elements it landed on, topmost first", () => {
    const onElementDropped = vi.fn();
    // jsdom lays nothing out: the surface is given a size before it mounts, so the view fits it and a
    // drop at its middle lands at the view's centre.
    const sized = vi.spyOn(Element.prototype, "getBoundingClientRect").mockReturnValue({ left: 0, top: 0, right: 1000, bottom: 1000, width: 1000, height: 1000, x: 0, y: 0, toJSON: () => ({}) });
    // Two nodes stacked at the centre of the view, and one either side to put the centre there.
    const at = (id: string, x: number, size: number): DiagramModelElement => ({ ...A, id, x, width: size, height: size });
    const { container } = drawn(compileNotation(specOf(), BINDINGS), { elements: [at("under", 0, 200), at("over", 0, 100), at("left", -1000, 100), at("right", 1000, 100)], connections: [] }, { onElementDropped });
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const data = new Map([["application/x-adp-toolbox-item", "node"]]);
    const dataTransfer = { getData: (type: string) => data.get(type) ?? "", dropEffect: "", types: [...data.keys()] };

    // jsdom's drag events carry no pointer position, so the drop is given its own.
    const drop = createEvent.drop(surface, { dataTransfer });
    Object.defineProperties(drop, { clientX: { value: 500 }, clientY: { value: 500 } });
    fireEvent(surface, drop);
    sized.mockRestore();

    expect(onElementDropped).toHaveBeenCalledTimes(1);
    const event = onElementDropped.mock.calls[0]![0] as { position: { x: number; y: number }; elementIds: readonly string[] };
    expect(Math.abs(event.position.x)).toBeLessThan(50);
    expect(Math.abs(event.position.y)).toBeLessThan(50);
    expect(event.elementIds).toEqual(["over", "under"]);
  });

  it("binds a whole CEL expression the module names, even one whose terms are joined by +", () => {
    const whole = "self.name != '' ? self.name + ' (named)' : 'unnamed'";
    const spec = specOf();
    const named = { ...spec, notation: { ...spec.notation, nodes: { ...spec.notation.nodes, Node: { ...NODE, labels: [{ id: "name", text: { cel: whole }, position: "center" }] } } } } as DislDocument;
    const definition = compileNotation(named, { ...BINDINGS, celPaths: { ...BINDINGS.celPaths, [whole]: "payload.shown" } });

    const { container } = drawn(definition, { elements: [{ ...A, payload: { name: "A", shown: "A (named)" } }], connections: [] });
    expect(container.querySelector('[data-element-id="a"]')?.textContent).toBe("A (named)");
  });

  it("refuses what it cannot draw: marks along a route that is not curved, an icon it has none of, another placement and a ladder it cannot read", () => {
    expect(() => compileNotation(specOf({ ...ARROW, line: { routing: "straight" } }), BINDINGS)).toThrow(/along a "straight" line/);
    expect(() => compileNotation(specOf(ARROW, { ...HUB, icon: { icon: "mdi-cog", position: "top", size: 26 } } as DislNodeNotation), BINDINGS)).toThrow(/mdi-cog/);
    expect(() => compileNotation(specOf(ARROW, { ...HUB, placement: { x: { cel: "self.x" }, y: { cel: "self.y" } } } as unknown as DislNodeNotation), BINDINGS)).toThrow(/placed at/);
    expect(() => compileNotation(specOf({ ...ARROW, line: { ...ARROW.line, stroke: { width: { cel: "self.weight * 2.0" } } } }), BINDINGS)).toThrow(/stroke width/);
  });

  it("leaves a node placed by its attributes or by the layout to the placements that read those", () => {
    for (const placement of [{ x: { attribute: "x" }, y: { attribute: "y" } }, { x: { layout: true }, y: { layout: true } }]) {
      const hub = compileNotation(specOf(ARROW, { ...HUB, placement } as unknown as DislNodeNotation), BINDINGS).elementTypes.find((type) => type.id === "hub")!;
      expect(hub.centreOf).toBeUndefined();
    }
  });
});
