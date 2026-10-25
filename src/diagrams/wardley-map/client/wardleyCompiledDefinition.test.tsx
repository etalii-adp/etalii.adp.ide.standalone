import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import { WardleyAttitudeKind, WardleyDecorator, WardleyElementKind } from "@client/generated/wardley-map_pb";
import type { WardleyElement, WardleyModel } from "./wardleyModel";

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
const { wardleyDefinitionFor, wardleyDiagramModelOf } = await import("./WardleyCanvas");
const { handWrittenDefinitionOf, handWrittenModelOf, mapScaleOf, spaceWidthFor } = await import("./handWrittenDefinition");

const STAGES = [
  { label: "Genesis", start: 0, end: 0.175 },
  { label: "Custom Built", start: 0.175, end: 0.4 },
  { label: "Product (+rental)", start: 0.4, end: 0.7 },
  { label: "Commodity (+utility)", start: 0.7, end: 1 },
];

function mark(id: string, name: string, kind: WardleyElementKind, x: number, y: number, more: Partial<WardleyElement> = {}): WardleyElement {
  return { id, name, kind, x, y, visibility: 1 - y, maturity: x, evolutionStage: "", decorators: [], inertia: false, urlAddress: "", pipelineParentId: "", ...more };
}

/** One of everything a map draws: each kind of element, both kinds of link, a dangling one, and the statements. */
const CORPUS: WardleyModel = {
  elements: new Map([
    ["a", mark("a", "Customer", WardleyElementKind.ANCHOR, 0.6, 0.05)],
    ["b", mark("b", "Cup of tea", WardleyElementKind.COMPONENT, 0.7, 0.2, { decorators: [WardleyDecorator.BUY], inertia: true })],
    ["c", mark("c", "Kettle", WardleyElementKind.COMPONENT, 0.4, 0.5, { labelOffset: { x: -57, y: 4 }, evolve: { maturity: 0.8, evolutionStage: "", overrideName: "Electric kettle" } })],
    ["d", mark("d", "Power", WardleyElementKind.SUBMAP, 0.9, 0.8, { evolve: { maturity: 0.95, evolutionStage: "", overrideName: "" } })],
    ["e", mark("e", "Leaf", WardleyElementKind.COMPONENT, 0.3, 0.6, { pipelineParentId: "c" })],
  ]),
  links: new Map([
    ["l1", { id: "l1", sourceId: "a", targetId: "b", isFlow: false, context: "", sourceName: "Customer", targetName: "Cup of tea" }],
    ["l2", { id: "l2", sourceId: "b", targetId: "c", isFlow: true, context: "hot", sourceName: "Cup of tea", targetName: "Kettle" }],
    ["l3", { id: "l3", sourceId: "b", targetId: "gone", isFlow: false, context: "", sourceName: "Cup of tea", targetName: "Gone" }],
  ]),
  notes: new Map([["n1", { id: "n1", x: 0.2, y: 0.9, text: "+a note" }]]),
  annotations: new Map([["an", { id: "an", x: 0.49, y: 0.57, number: 1, text: "Standardising power", occurrences: [{ x: 0.49, y: 0.57 }, { x: 0.79, y: 0.92 }] }]]),
  accelerators: new Map([
    ["ac", { id: "ac", x: 0.5, y: 0.3, name: "Faster", isDeaccelerator: false }],
    ["de", { id: "de", x: 0.5, y: 0.4, name: "Slower", isDeaccelerator: true }],
  ]),
  attitudes: new Map([["at", { id: "at", x: 0.2, y: 0.3, kind: WardleyAttitudeKind.SETTLERS, opposite: { x: 0.55, y: 0.8 } }]]),
};

const DRAWN = ["circle", "rect", "line", "path", "text", "title"];
const POSITIONS: Record<string, "x" | "y"> = { x: "x", cx: "x", x1: "x", x2: "x", y: "y", cy: "y", y1: "y", y2: "y" };

/** The sum of the plain translations of an element's groups, which the compiled canvas draws a box inside. */
function offsetOf(node: Element): { x: number; y: number } {
  let x = 0;
  let y = 0;
  for (let parent = node.parentElement; parent !== null; parent = parent.parentElement) {
    const translation = /^translate\((-?[\d.]+)[ ,]+(-?[\d.]+)\)$/.exec(parent.getAttribute("transform") ?? "");
    if (translation !== null) {
      x += Number(translation[1]);
      y += Number(translation[2]);
    }
  }
  return { x, y };
}

function rounded(value: number): string {
  return String(Math.round(value * 10) / 10);
}

/**
 * What the eye sees of a rendered map, one line per drawn primitive: its classes without the library's own,
 * its absolute position, its anchor however it is set, and a connection as the pair of points it joins,
 * which is the same line whichever end it starts from.
 */
function primitives(definition: DiagramDefinition, model: DiagramModel): string[] {
  const { container, unmount } = render(
    <DiagramCanvas definition={definition} model={model} events={{}} source={{ entryId: new Uint8Array(16), path: ["map.owm"] }} ariaLabel="Wardley map" className="wardley-surface" />,
  );
  const centres = [...container.querySelectorAll("svg circle")].map((circle) => {
    const offset = offsetOf(circle);
    return { x: Number(circle.getAttribute("cx")) + offset.x, y: Number(circle.getAttribute("cy")) + offset.y };
  });
  const lines: string[] = [];
  for (const node of container.querySelectorAll("svg *")) {
    const tag = node.tagName.toLowerCase();
    const content = node.textContent ?? "";
    if (tag === "g") {
      // What a group is painted as, which everything drawn in it inherits.
      const classes = (node.getAttribute("class") ?? "").split(/\s+/).filter((name) => name.startsWith("wardley-"));
      if (classes.length > 0) {
        lines.push(`group class=${classes.sort().join(" ")}`);
      }
      continue;
    }

    if (!DRAWN.includes(tag) || (tag === "text" && content === "")) {
      continue;
    }

    const offset = offsetOf(node);
    const attributes: string[] = [];
    for (const { name, value } of node.attributes) {
      if (name === "class") {
        const classes = value.split(/\s+/).filter((name) => name !== "" && !name.startsWith("library-"));
        if (classes.length > 0) {
          attributes.push(`class=${classes.join(" ")}`);
        }
      } else if (name in POSITIONS && !node.hasAttribute("transform")) {
        attributes.push(`${name}=${rounded(Number(value) + offset[POSITIONS[name]])}`);
      } else if (name === "d" && node.getAttribute("class")?.includes("canvas-connection") === true) {
        attributes.push(`d=${joinedPoints(value, centres)}`);
      } else if (!["style", "data-element-id", "rx", "text-anchor"].includes(name)) {
        attributes.push(`${name}=${value}`);
      }
    }

    if (tag === "text") {
      attributes.push(`anchor=${node.getAttribute("text-anchor") ?? (node as SVGElement).style.textAnchor ?? "start"}`);
    }

    lines.push(`${tag} ${attributes.sort().join(" ")}${tag === "text" || tag === "title" ? ` "${content}"` : ""}`);
  }
  unmount();
  return lines.sort();
}

/** A straight connection's two ends, each as the centre of the mark it meets, in a fixed order. */
function joinedPoints(path: string, centres: { x: number; y: number }[]): string {
  const numbers = path.match(/-?[\d.]+/g)?.map(Number) ?? [];
  const ends = [
    { x: numbers[0], y: numbers[1] },
    { x: numbers[numbers.length - 2], y: numbers[numbers.length - 1] },
  ].map((end) => {
    const centre = centres.find((candidate) => Math.hypot(candidate.x - end.x, candidate.y - end.y) <= 14);
    return centre === undefined ? `${rounded(end.x)},${rounded(end.y)}` : `@${centre.x},${centre.y}`;
  });
  return ends.sort().join(" ");
}

/**
 * Where the definition compiled from the specification draws a map differently from the hand-written definition
 * it replaced, oracle line to compiled line; `undefined` on the left is a primitive only the compiled canvas draws,
 * and on the right one only the hand-written canvas drew.
 * Each is the specification's own geometry rather than a defect of the compile.
 */
const NAMED: [string | undefined, string | undefined][] = [
  // The badges start under the name, as the label's `beside` and offset say; the hand-written canvas centred
  // them under the mark and lost the offset.
  ['text anchor=middle class=wardley-element-badges x=700 y=214 "buy · inertia"', 'text anchor=start class=wardley-element-badges x=715 y=218 "buy · inertia"'],
  // The new name of an evolution sits on the same line as every other name.
  ['text anchor=start class=wardley-element-label x=815 y=508 "Electric kettle"', 'text anchor=start class=wardley-element-label x=815 y=504 "Electric kettle"'],
  // A note's text starts at its position, as before, and is centred on that line rather than standing on it.
  ['text anchor=start class=wardley-note-label x=200 y=900 "+a note"', 'text anchor=start class=wardley-note-label x=200 y=904 "+a note"'],
  // An attitude's label is inset by the specification's 8 and set on its 20-unit line.
  ['text anchor=start class=wardley-attitude-label wardley-attitude-settlers-label x=206 y=316 "settlers"', 'text anchor=start class=wardley-attitude-label wardley-attitude-settlers-label x=208 y=320 "settlers"'],
  // The inertia mark says what it is when pointed at.
  [undefined, 'title  "Inertia"'],
  // An accelerator's rule is painted by its own class rather than by its group's, so its name is no longer
  // stroked three units wide as if it were part of the rule.
  ["group class=wardley-accelerator-back", undefined],
  ["group class=wardley-accelerator-forward", undefined],
];

function withNamedDifferences(oracle: string[]): string[] {
  const lines = [...oracle];
  for (const [from, to] of NAMED) {
    if (from === undefined) {
      lines.push(...(to === undefined ? [] : [to]));
      continue;
    }

    const at = lines.indexOf(from);
    expect(at, `the hand-written canvas draws ${from}`).toBeGreaterThanOrEqual(0);
    lines.splice(at, 1, ...(to === undefined ? [] : [to]));
  }
  return lines.sort();
}

describe("the Wardley map definition compiled from its specification", () => {
  it("draws the map the hand-written definition drew, but for the differences it names", () => {
    const scale = mapScaleOf(spaceWidthFor(null));
    const oracle = primitives(handWrittenDefinitionOf(scale), handWrittenModelOf(CORPUS, STAGES, scale));
    const definition = wardleyDefinitionFor(null);
    const compiled = primitives(definition, wardleyDiagramModelOf(CORPUS, definition.dragBounds!));

    expect(compiled).toEqual(withNamedDifferences(oracle));
  });

  it("draws the square and the attitude's area with sharp corners, as the hand-written ones were", () => {
    const stylesheet = readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "wardley.css"), "utf8");

    expect(stylesheet.replace(/\s+/g, " ")).toContain(".wardley-kind-anchor .wardley-element, .wardley-attitude { rx: 0; }");
  });

  it("paints an accelerator's rule by the classes the compiled canvas writes on it, and its name as text", () => {
    const stylesheet = readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "wardley.css"), "utf8").replace(/\s+/g, " ");

    expect(stylesheet).toContain(".wardley-accelerator-forward-glyph, .wardley-accelerator-back-glyph { fill: none; stroke: var(--color-text); stroke-width: 3; }");
    expect(stylesheet).toContain(".wardley-accelerator-back-glyph { stroke-dasharray: 5 4; }");
    expect(stylesheet).toContain(".wardley-accelerator-forward-label, .wardley-accelerator-back-label { fill: var(--color-text); }");
  });

  it("sends each action to the backend by the shortcut the specification gives it", () => {
    const actions = wardleyDefinitionFor(null).actions ?? [];

    expect(actions.map((action) => [action.id, action.backendKey])).toEqual([
      ["wardley.remove", "Delete"],
      ["wardley.unlink", "Delete"],
      ["wardley.remove-from-pipeline", "Delete"],
      ["wardley.rename", "F2"],
    ]);
  });
});
