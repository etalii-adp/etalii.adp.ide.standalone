import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { fromJson, type JsonValue } from "@bufbuild/protobuf";
import {
  CausalLoopLinkPayloadSchema,
  CausalLoopLoopPayloadSchema,
  CausalLoopVariablePayloadSchema,
  LoopPolarityProto,
} from "@client/generated/causal-loop_pb";
import disText from "../definition/causal-loop-diagram.dis?raw";
import transcriptText from "../backend/EtAlii.Adp.Diagram.CausalLoopDiagram.Tests/Parity/causal-loop.transcript.json?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, RelationTypeDefinition, RouteEnds, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { arcBetween, ArcBow, normalAlong, pointAlong, type ArcBox } from "@client/canvas/library/disl/bowedArc";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { CLD_BINDINGS } from "./cldBindings";
import {
  loopCaption,
  polarityMark,
  type CausalLoopLink,
  type CausalLoopLoop,
  type CausalLoopModel,
  type CausalLoopVariable,
} from "./causalLoopModel";

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
const { CLD_DEFINITION, diagramModelOf } = await import("./CausalLoopCanvas");

/*
 * The causal loop diagram's canvas definition, compiled from its bundled DISL specification, draws what
 * the definition the module stated by hand until the switch-over drew - on every document of the
 * backend's parity transcript, the corpus the backend's own reading is held to, and on the two cases
 * that corpus lacks: a link from a variable to itself and a link that states no polarity.
 *
 * The oracle is that hand-written definition, moved unchanged into this test below, with the model
 * mapping that fed it. The two drawings are not equal byte for byte, and the differences
 * are each a decision, taken back out below rather than hidden:
 *
 * - **The accessible names.** The specification names each variable and loop for a screen reader
 *   (`'Variable ' + self.display`, `'Loop ' + caption + ', ' + computed`); the oracle named none.
 * - **The undecidable loop's turn.** The specification turns a loop's arrow the other way only when it
 *   balances (`flip: self.computed == 'balancing' ? 'horizontal' : 'none'`); the oracle turned every
 *   loop but a reinforcing one that way, so an undecidable loop now turns as a reinforcing one does.
 *   The oracle is given that turn here, so the rest of its drawing is still compared.
 * - **The numbers.** The compiled badge stands at the mean of its members' centres, which the
 *   library computes, where the oracle took the backend's; the two agree to about 1e-14, so every
 *   number is compared to six places.
 *
 * Which relation type a link is (one per weight and bow in the oracle, one with a ladder and a
 * variant now) and how the actions are declared are not drawn, so they are not compared here;
 * `CausalLoopCanvas.test.tsx` holds the gestures.
 */

/*
 * THE ORACLE: the causal loop canvas's definition as `CausalLoopCanvas.tsx` stated it by hand until the
 * definition was compiled from the bundled specification, with the model mapping that fed it. Moved
 * here unchanged but for its arc, which the library now owns, and for being a constant and a function
 * rather than code inside the component. It lives in this test rather than in a file of its own so
 * that nothing outside a test reaches the library's arc: a module source importing it would make the
 * arc a name modules may use, which is the rendering code this conversion took out of the module.
 */

/** An element as the library carries it here: the model element plus what it draws. */
type VariableElement = DiagramModelElement & { variable: CausalLoopVariable };
type LoopElement = DiagramModelElement & { loop: CausalLoopLoop };
type LinkConnection = DiagramModelConnection & { link: CausalLoopLink };

function toArcBox(bounds: ShapeBounds): ArcBox {
  return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
}


/**
 * How far this link bows, and to which side.
 *
 * The magnitude is the notation's own look. The SIGN is the side of travel, and it is normally
 * this module's decision: always the same side, so that `A -> B` and `B -> A` land on opposite
 * sides of the chord and a two-variable loop draws as an ellipse rather than as one line with an
 * arrowhead at each end. A link the author has flipped bows the other way instead - the one
 * escape from that rule, for the case where the chosen side crosses something and the picture
 * stops reading.
 */
function bowOf(flipped: boolean): number {
  return flipped ? -ArcBow : ArcBow;
}

/**
 * The chord-bowed arc, and the self-loop's chordless ellipse pair, as one custom route: the
 * geometry anchors on the two end BOXES and bows to the side of travel, which is why the
 * route takes the endpoint bounds the library now hands a drawn connection.
 */
function arcRouteOf(flipped: boolean) {
  return {
    customRoute: "causal-loop-arc",
    path: (from: ShapePoint, to: ShapePoint, _waypoints: readonly ShapePoint[], ends?: RouteEnds) =>
      ends !== undefined
        ? arcBetween(toArcBox(ends.source), toArcBox(ends.target), bowOf(flipped)).path
        : `M ${from.x} ${from.y} L ${to.x} ${to.y}`,
  };
}

/**
 * The polarity sign beside the arrowhead and the delay strokes across the link - the
 * notation's own adornment, drawn inside the connection's group so the shared selected
 * cascade colours them with the line they describe (the 2026-09-06 adorner styling).
 */
function linkAdornment(route: { ends?: RouteEnds; highlighted?: boolean }, rawConnection: unknown) {
  const connection = rawConnection as LinkConnection;
  if (route.ends === undefined) {
    return null;
  }

  // The same bow the route drew, read from the same link: an adornment computed against the
  // unflipped arc would sit beside the line rather than on it.
  const arc = arcBetween(toArcBox(route.ends.source), toArcBox(route.ends.target), bowOf(connection.link.payload.flipped));
  const mark = polarityMark(connection.link.payload.polarity);
  const markAt = pointAlong(arc, 0.86);
  const markNormal = normalAlong(arc, 0.86);

  return (
    <>
      {connection.link.payload.delayed && (
        // The conventional delay mark: two short strokes ACROSS the link, along the curve's
        // own normal - drawn vertically they would lie along a near-vertical arc.
        // Highlighted, the group carries the selection colour inline - but the strokes' own class
        // rule outranks an inherited stroke, so today they stay in the text colour (measured in
        // Chromium on 2026-09-26; see causal-loop.css). Kept as rendered by client-centralization.
        <g className="causal-loop-delay" style={route.highlighted === true ? { stroke: "var(--color-selected, #7c3aed)" } : undefined}>
          <line
            className="causal-loop-delay-stroke"
            x1={pointAlong(arc, 0.44).x - arc.apexNormal.x * 8}
            y1={pointAlong(arc, 0.44).y - arc.apexNormal.y * 8}
            x2={pointAlong(arc, 0.44).x + arc.apexNormal.x * 8}
            y2={pointAlong(arc, 0.44).y + arc.apexNormal.y * 8}
          />
          <line
            className="causal-loop-delay-stroke"
            x1={pointAlong(arc, 0.56).x - arc.apexNormal.x * 8}
            y1={pointAlong(arc, 0.56).y - arc.apexNormal.y * 8}
            x2={pointAlong(arc, 0.56).x + arc.apexNormal.x * 8}
            y2={pointAlong(arc, 0.56).y + arc.apexNormal.y * 8}
          />
        </g>
      )}
      {mark !== "" && (
        <text
          className="causal-loop-polarity"
          style={route.highlighted === true ? { fill: "var(--color-selected, #7c3aed)" } : undefined}
          x={markAt.x + markNormal.x * 11}
          y={markAt.y + markNormal.y * 11}
          textAnchor="middle"
        >
          {mark}
        </text>
      )}
    </>
  );
}

/**
 * A near-complete circle with a gap for its arrowhead - the loop marker the notation draws at
 * the centre of a feedback loop. Two arcs rather than one, because a single SVG elliptical arc
 * cannot exceed a half turn without the large-arc flag.
 */
function loopMarkerPath(centreX: number, centreY: number, radius: number, clockwise: boolean): string {
  const sweep = clockwise ? 1 : 0;
  const start = -Math.PI / 2;
  const end = start + (clockwise ? 1 : -1) * Math.PI * 1.7;
  const middle = (start + end) / 2;

  const at = (angle: number) => `${(centreX + radius * Math.cos(angle)).toFixed(2)} ${(centreY + radius * Math.sin(angle)).toFixed(2)}`;

  return `M ${at(start)} A ${radius} ${radius} 0 0 ${sweep} ${at(middle)} A ${radius} ${radius} 0 0 ${sweep} ${at(end)}`;
}

/**
 * One weight step's relation type, in each bow direction: the weight steps keep the stylesheet's
 * classes as they are, and the direction is a type of its own because a route is declared by the
 * definition rather than computed per connection - the same reason the weight steps are three
 * types rather than one parameterised at draw time.
 */
function linkRelation(step: "light" | "normal" | "heavy", flipped: boolean): RelationTypeDefinition {
  return {
    id: flipped ? `link-${step}-flipped` : `link-${step}`,
    route: arcRouteOf(flipped),
    style: { endMarker: "arrow" },
    className: `causal-loop-link causal-loop-weight-${step}`,
    adorn: linkAdornment,
    endpoints: {
      source: { elementTypes: ["variable"] },
      target: { elementTypes: ["variable"], anchors: "edge" },
      // A link may state its own variable on both ends; the self-loop draws as the
      // chordless ellipse pair the arc route already knows.
      allowSelf: true,
    },
  };
}

function polarityWord(computed: LoopPolarityProto): string {
  switch (computed) {
    case LoopPolarityProto.REINFORCING:
      return "reinforcing";
    case LoopPolarityProto.BALANCING:
      return "balancing";
    default:
      return "undecidable";
  }
}

/**
 * What a causal loop diagram allows, stated once: pill variables that drag and select, links
 * as chord-bowed arcs with polarity and delay adornment (self-loops included), and loop
 * badges that select but never move - the identifier's marker and caption drawn at the
 * centre of the variables the loop runs through. Renaming stays the dialog it always was;
 * Arrange stays a backend action.
 */
const HAND_WRITTEN: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    // The label is the visible name and the one editable thing: renaming edits it in place,
    // centred over the pill. "inside" opens the editor at the element's centre.
    {
      id: "variable",
      // A pill: a box whose corner radius is half its height, which is what `rx={height / 2}`
      // said in the renderer and what the built-in says by name.
      shape: "pill",
      classNames: [
        { className: "canvas-element causal-loop-variable", on: "element" },
        { className: "canvas-node", on: "shape" },
      ],
      labels: [{ text: { path: "payload.display" }, editable: true, className: "canvas-node-label" }],
      anchors: { kind: "edge" },
      sizing: "model",
    },
    {
      id: "loop",
      // A BADGE WITH NO BODY: the polarity sweep and its caption, and nothing else. `none` is
      // what made this an element type rather than a renderer.
      shape: "none",
      classNames: [
        { className: "causal-loop-loop", on: "element" },
        { className: "causal-loop-disagrees", on: "element", when: { path: "payload.disagrees", is: "true" } },
      ],
      decorations: [
        {
          // The sweep follows the polarity: reinforcing clockwise, balancing anticlockwise -
          // how the reference tools distinguish them before anyone reads the letter. Two
          // declarations with a condition, not one function with a boolean argument.
          glyph: "path",
          d: { path: "payload.markerPath" },
          markerEnd: "arrow",
          className: "causal-loop-marker",
        },
      ],
      labels: [
        {
          text: { path: "payload.caption" },
          className: { template: "causal-loop-badge causal-loop-{payload.polarityWord}" },
          underline: { style: "wavy", when: { path: "payload.disagrees", is: "true" } },
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
      draggable: false,
    },
  ],
  relationTypes: [
    linkRelation("light", false),
    linkRelation("normal", false),
    linkRelation("heavy", false),
    linkRelation("light", true),
    linkRelation("normal", true),
    linkRelation("heavy", true),
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // The right-button drag draws a link, in the shared gesture layer - so a causal loop links
  // by dragging between variables, and this module keeps no gesture state of its own.
  connectOnRightDrag: true,
  // The diagram's own menu - Arrange diagram, a new variable where the user right-clicked - on
  // empty canvas. The library opens it for the point clicked; this canvas used to build it by
  // hand, and was the only one that did (centralized-selection, design A, "The background menu").
  backgroundMenu: true,
});

/**
 * The thickness step a weight is drawn at: a short ladder rather than a continuous stroke width,
 * because a weight is an author's annotation and not a measurement.
 */
function weightStep(link: CausalLoopLink): "light" | "normal" | "heavy" {
  if (!link.payload.hasWeight) {
    return "normal";
  }

  if (link.payload.weight >= 2) {
    return "heavy";
  }

  return link.payload.weight <= 0.5 ? "light" : "normal";
}

/** The library's elements and connections for a model, as the hand-written canvas built them. */
function handWrittenModelOf(model: CausalLoopModel): DiagramModel {
  const variables = [...model.variables.values()].map((variable): VariableElement => ({
    id: variable.id,
    type: "variable",
    x: variable.x,
    y: variable.y,
    width: variable.payload.width,
    height: variable.payload.height,
    label: variable.payload.display,
    payload: { display: variable.payload.display },
    variable,
  }));
  const loops = [...model.loops.values()].map((loop): LoopElement => ({
    id: loop.id,
    type: "loop",
    x: loop.x,
    y: loop.y,
    // The badge and its marker together, roughly: enough box for a press to land on.
    width: 60,
    height: 48,
    // What the declaration reads. The sweep's path is DATA the module computes - a string a
    // binding reads - which is the distinction `d` was written as a binding for.
    payload: {
      caption: loopCaption(loop),
      polarityWord: polarityWord(loop.payload.computed),
      disagrees: loop.payload.disagrees,
      markerPath: loopMarkerPath(loop.x, loop.y - 15, 13, loop.payload.computed === LoopPolarityProto.REINFORCING),
    },
    loop,
  }));
  const links = [...model.links.values()].flatMap((link) =>
    model.variables.has(link.payload.fromElementId) && model.variables.has(link.payload.toElementId)
      ? [{
          id: link.id,
          type: link.payload.flipped ? `link-${weightStep(link)}-flipped` : `link-${weightStep(link)}`,
          sourceId: link.payload.fromElementId,
          targetId: link.payload.toElementId,
          link,
        } satisfies LinkConnection]
      : [], // the far end is not held - off screen, or never declared; a line to nothing is worse than none
  );
  return { elements: [...variables, ...loops], connections: links };
}

interface TranscriptDocument {
  document: string;
  payloads: string[];
}

const TRANSCRIPT = JSON.parse(transcriptText) as { documents: TranscriptDocument[] };

/** One line of a document's payloads: `id | type | x, y | PayloadName {json}`, or null for a document that did not read. */
function parsed(line: string): { id: string; type: string; x: number; y: number; json: JsonValue } | null {
  const parts = line.split(" | ");
  if (parts.length < 4) {
    return null;
  }

  const [id, type, position, payload] = parts as [string, string, string, string];
  const [x, y] = position.split(",").map((value) => Number(value.trim())) as [number, number];
  return { id, type, x, y, json: JSON.parse(payload.slice(payload.indexOf("{"))) as JsonValue };
}

/** The client's model of one document, built from the payloads the backend sends for it. */
function modelOf(payloads: readonly string[]): CausalLoopModel {
  const variables = new Map<string, CausalLoopVariable>();
  const links = new Map<string, CausalLoopLink>();
  const loops = new Map<string, CausalLoopLoop>();
  for (const line of payloads) {
    const row = parsed(line);
    if (row === null) {
      continue;
    }

    const { id, x, y, json } = row;
    switch (row.type) {
      case "systems/causal-loop+variable":
        variables.set(id, { id, x, y, payload: fromJson(CausalLoopVariablePayloadSchema, json) });
        break;
      case "systems/causal-loop+link":
        links.set(id, { id, x, y, payload: fromJson(CausalLoopLinkPayloadSchema, json) });
        break;
      case "systems/causal-loop+loop":
        loops.set(id, { id, x, y, payload: fromJson(CausalLoopLoopPayloadSchema, json) });
        break;
    }
  }
  return { variables, links, loops };
}

/** What the corpus lacks: a link from a variable to itself, and one that states no polarity. */
const SUPPLEMENT: readonly string[] = [
  'variable:a | systems/causal-loop+variable | 0, 0 | CausalLoopVariablePayload { "display": "A", "id": "a", "width": 90, "height": 39.6 }',
  'variable:b | systems/causal-loop+variable | 260, 90 | CausalLoopVariablePayload { "display": "B", "id": "b", "width": 90, "height": 39.6 }',
  'link:a|a | systems/causal-loop+link | 0, -40 | CausalLoopLinkPayload { "fromElementId": "variable:a", "toElementId": "variable:a", "polarity": "CAUSAL_LOOP_POLARITY_PROTO_NEGATIVE", "delayed": true }',
  'link:a|b | systems/causal-loop+link | 130, 45 | CausalLoopLinkPayload { "fromElementId": "variable:a", "toElementId": "variable:b" }',
  'link:b|a | systems/causal-loop+link | 130, 45 | CausalLoopLinkPayload { "fromElementId": "variable:b", "toElementId": "variable:a", "polarity": "CAUSAL_LOOP_POLARITY_PROTO_POSITIVE", "weight": 0.25, "hasWeight": true, "flipped": true }',
];

const CASES: readonly { name: string; payloads: readonly string[] }[] = [
  ...TRANSCRIPT.documents.map((document) => ({ name: document.document.split("/").slice(-1)[0]!, payloads: document.payloads })),
  { name: "a self-link and an unstated link", payloads: SUPPLEMENT },
];

/** The oracle's model, with the undecidable loop turned as the specification turns it. */
function oracleModelOf(model: CausalLoopModel): DiagramModel {
  const drawn = handWrittenModelOf(model);
  return {
    ...drawn,
    elements: drawn.elements.map((element) => {
      const payload = element.payload as { polarityWord?: string } | undefined;
      return element.type === "loop" && payload?.polarityWord === "undecidable"
        ? { ...element, payload: { ...payload, markerPath: loopMarkerPath(element.x, element.y - 15, 13, true) } }
        : element;
    }),
  };
}

/** The canvas's markup, with the ids React generates per mount taken out and every number to six places. */
function drawn(definition: DiagramDefinition, model: DiagramModel): string {
  const { container, unmount } = render(
    <DiagramCanvas
      definition={definition}
      model={model}
      events={{}}
      source={{ entryId: new Uint8Array(16), path: ["feedback.cld"] }}
      ariaLabel="Causal loop diagram"
      className="causal-loop-canvas-host"
    />,
  );
  const markup = container.innerHTML
    .replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»")
    .replace(/-?\d+\.\d+(e-?\d+)?|-?\d+e-?\d+/g, (number) => {
      const rounded = Number(Number(number).toFixed(6));
      return String(Object.is(rounded, -0) ? 0 : rounded);
    });
  unmount();
  return markup;
}

/** The compiled drawing with the accessible names the specification adds taken back out. */
function withoutAccessibleNames(markup: string): string {
  return markup.replace(/ aria-label="(Variable|Loop) [^"]*"/g, "");
}

describe("the causal loop diagram's compiled definition", () => {
  it("draws what the hand-written definition drew, on every document of the corpus", () => {
    const covered = new Set<string>();
    for (const { name, payloads } of CASES) {
      const model = modelOf(payloads);
      const compiled = drawn(CLD_DEFINITION, diagramModelOf(model));
      for (const word of ["causal-loop-variable", "causal-loop-link", "causal-loop-delay", "causal-loop-polarity", "causal-loop-weight-light", "causal-loop-weight-heavy", "causal-loop-balancing", "causal-loop-reinforcing", "causal-loop-undecidable", "causal-loop-disagrees", 'aria-label="Variable ']) {
        if (compiled.includes(word)) {
          covered.add(word);
        }
      }
      if ([...model.loops.values()].some((loop) => loop.payload.computed === LoopPolarityProto.UNDECIDABLE)) {
        covered.add("an undecidable loop");
      }

      expect(withoutAccessibleNames(compiled), name).toBe(drawn(HAND_WRITTEN, oracleModelOf(model)));
    }

    // The cases the comparison would pass vacuously on: a corpus that drew nothing the notation
    // distinguishes, or a decision taken out that was never there.
    expect([...covered].sort()).toEqual([
      "an undecidable loop",
      'aria-label="Variable ',
      "causal-loop-balancing",
      "causal-loop-delay",
      "causal-loop-disagrees",
      "causal-loop-link",
      "causal-loop-polarity",
      "causal-loop-reinforcing",
      "causal-loop-undecidable",
      "causal-loop-variable",
      "causal-loop-weight-heavy",
      "causal-loop-weight-light",
    ]);
    expect(CASES.length).toBeGreaterThanOrEqual(16);
  });

  it("turns an undecidable loop as a reinforcing one, which is the one drawing the specification changes", () => {
    const model = modelOf(SUPPLEMENT);
    const undecidable: CausalLoopLoop = {
      id: "loop:B1",
      x: 130,
      y: 45,
      payload: fromJson(CausalLoopLoopPayloadSchema, { identifier: "B1", memberElementIds: ["variable:a", "variable:b"] }),
    };
    const withLoop: CausalLoopModel = { ...model, loops: new Map([[undecidable.id, undecidable]]) };

    // The oracle as it was turns it the other way; given the specification's turn, it agrees.
    expect(withoutAccessibleNames(drawn(CLD_DEFINITION, diagramModelOf(withLoop)))).not.toBe(drawn(HAND_WRITTEN, handWrittenModelOf(withLoop)));
    expect(withoutAccessibleNames(drawn(CLD_DEFINITION, diagramModelOf(withLoop)))).toBe(drawn(HAND_WRITTEN, oracleModelOf(withLoop)));
  });

  it("offers the specification's double-click rename and its Delete gesture, each the backend action of its id", () => {
    expect(CLD_DEFINITION.actions).toEqual([
      { id: "causal-loop.rename-variable", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element", elementTypes: ["variable"] }] },
      { id: "causal-loop.remove-variable", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element", elementTypes: ["variable"] }] },
      { id: "causal-loop.remove-link", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
      { id: "causal-loop.remove-loop", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element", elementTypes: ["loop"] }] },
    ]);
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    // A route and an adornment are made afresh by each compile, so a function is compared by its source.
    const declared = (definition: DiagramDefinition) => JSON.stringify(definition, (_key, value: unknown) => (typeof value === "function" ? value.toString() : value));
    expect(declared(CLD_DEFINITION)).toBe(declared(assertValidDiagramDefinition(compileNotation(parseDisl(disText), CLD_BINDINGS))));
    expect(CLD_DEFINITION).not.toBe(HAND_WRITTEN);
  });
});
