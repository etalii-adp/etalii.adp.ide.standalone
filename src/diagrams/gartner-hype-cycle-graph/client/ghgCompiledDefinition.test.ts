import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import disText from "../definition/gartner-hype-cycle-graph.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ElementTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { RulerRung } from "@client/canvas/library/definition/chrome";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { GHG_DEFINITION, ghgDefinitionFor } from "./GhgCanvas";
import { GHG_BINDINGS } from "./ghgBindings";
import {
  GHG_PHASES,
  GHG_PHASE_TITLES,
  GHG_PHASE_TOOLTIPS,
  GhgActions,
  GhgElementTypes,
  GhgRelationTypes,
  GhgScale,
  GhgShortcuts,
  GhgTimeUnits,
  type GhgTimeUnit,
} from "./ghgIds";

/**
 * The hype cycle's canvas definition, compiled from its bundled DISL specification, is the
 * definition the module stated by hand until the switch-over - for every time unit.
 *
 * <b>The oracle below is that hand-written definition, moved here unchanged</b> from `GhgCanvas.tsx`
 * (`definitionFor`, `TRIGGER_TYPE`, `NOTE_TYPE`, `RULER_RUNGS`). It is what the canvas drew before;
 * the compiled definition must equal it to the last key, so the switch-over changed nothing a user
 * can see. Where the two differed, the code won and the specification was corrected upstream in
 * etalii.adp (the influence's arrowhead: DISL's `arrow` is an open V, the canvas draws a filled
 * triangle, so the specification now says `arrowFilled`).
 */

/**
 * The ruler's rungs, finest first, each with the months it spans. A diagram drawn in a coarser unit
 * keeps only the rungs at least one of its steps wide: a diagram of years never labels a month it
 * cannot snap to. A century and a millennium are ten and a hundred decades, which start on years
 * ending in 00 and 000.
 */
const RULER_RUNGS: readonly { months: number; rung: RulerRung }[] = [
  { months: 1, rung: { every: { calendar: "month" }, label: "MMM yyyy" } },
  { months: 3, rung: { every: { calendar: "quarter" }, label: "MMM yyyy" } },
  { months: 12, rung: { every: { calendar: "year" }, label: "yyyy" } },
  { months: 120, rung: { every: { calendar: "decade" }, label: "yyyy" } },
  { months: 1200, rung: { every: { calendar: "decade", count: 10 }, label: "yyyy" } },
  { months: 12000, rung: { every: { calendar: "decade", count: 100 }, label: "yyyy" } },
];


/**
 * A trigger: a moment in time, drawn as a circle half a trend's height across. Its name and its date
 * are written left of it as a trend's name is, and only the name is edited in place. It offers three
 * handles to start an influence from, and the line leaves its outline facing the target wherever the
 * gesture began, so the document stores nothing for that end. Never resized: every trigger is one size.
 */
const TRIGGER_TYPE: ElementTypeDefinition = {
  id: GhgElementTypes.trigger,
  shape: "ellipse",
  classNames: [
    { className: "canvas-element ghg-trigger", on: "element" },
    { className: "canvas-node ghg-trigger-circle", on: "shape" },
  ],
  labels: [{ text: { template: "{payload.name} · {payload.when}" }, placement: "before", editable: true, className: "canvas-node-label ghg-label" }],
  tooltip: { template: "Trigger: {payload.name}, {payload.whenLong}" },
  // No dots are drawn: the handles still start an influence, but a small circle ringed with dots
  // reads as a different shape (Peter, 2026-09-27).
  anchors: { kind: "compass", positions: ["n", "e", "s"], attachDrawnBy: "edge", visible: false },
  sizing: "model",
};

/**
 * A note: the author's own text in a box, word-wrapped and edited in place across several lines, with
 * no anchors, so no influence starts or ends at one. A note dropped from the toolbox opens its editor.
 */
const NOTE_TYPE: ElementTypeDefinition = {
  id: GhgElementTypes.note,
  shape: "box",
  classNames: [
    { className: "canvas-element ghg-note", on: "element" },
    { className: "canvas-node ghg-note-box", on: "shape" },
  ],
  labels: [{ text: { path: "payload.text" }, wrap: true, editable: true, className: "ghg-note-text" }],
  anchors: { kind: "edge", enabled: false, visible: false },
  sizing: "user",
  resize: "both",
  editOnDrop: true,
};

/**
 * What a hype cycle graph is, stated once for each time unit a document may name. Every piece of it
 * is a library declaration: the phased banner, the circle and the note, the attachments anywhere
 * along a phase's edge, one influence per direction, the ruler and the tag filter. The unit changes only the ruler's scale
 * and rungs; a snap is always one step of four units, whatever a step is. The backend states the
 * same rules in `GhgRuleSet` and refuses what the canvas refuses anyway, because a request is never
 * trusted to have come from this canvas.
 */
function handWrittenDefinitionFor(unit: GhgTimeUnit): DiagramDefinition {
  const months = GhgTimeUnits[unit];
  const trendType: ElementTypeDefinition = {
    id: GhgElementTypes.trend,
    shape: "arrow-banner",
    classNames: [{ className: "canvas-element ghg-trend", on: "element" }],
    segments: {
      count: { path: "payload.phases" },
      max: GHG_PHASES.length,
      boundaries: "payload.boundaries",
      classNames: GHG_PHASES.map((phase) => `ghg-${phase}`),
      tooltips: GHG_PHASE_TOOLTIPS,
      divider: "chevron",
      draggableBoundaries: true,
    },
    labels: [{ text: { path: "payload.name" }, placement: "before", editable: true, className: "canvas-node-label ghg-label" }],
    // An influence attaches anywhere along a phase's top or bottom edge; no dot is drawn, because
    // the whole edge is the handle.
    anchors: { kind: "along", edges: ["top", "bottom"], regions: "segments", visible: false },
    sizing: "user",
  };
  // Compact: every trend one width, its phases even, and nothing that would change a date offered -
  // no move, no resize, no boundary drag - while influences, renaming and the toolbox still work.
  const compactTrendType: ElementTypeDefinition = {
    ...trendType,
    sizing: "model",
    draggable: false,
    segments: { ...trendType.segments!, boundaries: undefined, draggableBoundaries: false },
  };
  return assertValidDiagramDefinition({
    elementTypes: [trendType, TRIGGER_TYPE, NOTE_TYPE],
    relationTypes: [
      {
        id: GhgRelationTypes.influence,
        route: "cubic-bezier",
        style: { endMarker: "arrow" },
        className: "ghg-influence",
        // A selected influence shows a handle on each end, slid along its trend's edge to move it.
        movableEnds: true,
        hideWhenAttachmentHidden: true,
        // A trigger sets trends off and is never set off itself: it is a source, never a target.
        endpoints: {
          source: { elementTypes: [GhgElementTypes.trend, GhgElementTypes.trigger] },
          target: { elementTypes: [GhgElementTypes.trend] },
          allowSelf: false,
          cardinality: { perPair: "ordered" },
        },
      },
    ],
    actions: [
      {
        id: GhgActions.rename,
        invokedBy: [{ kind: "shortcut", key: GhgShortcuts.rename }, { kind: "gesture", gesture: "activate" }],
        appliesTo: [{ kind: "element" }],
      },
      { id: GhgActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
      { id: GhgActions.disconnect, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
    ],
    // A step of the unit is four units wide, so a snap of four lands every edge on the start of a
    // month, a year, a decade or a century; a row is 56, and the snap rests the trend's top on it,
    // which puts its middle on the row. The origin, 1900-01, starts all four. Each element carries
    // its own origins: 0 for a trend and a note, and for a trigger the offsets that put its CENTRE
    // on a step line and a row's middle.
    snap: {
      x: { step: GhgScale.unitsPerMonth, origin: { path: "payload.snapX" } },
      y: { step: GhgScale.rowStep, origin: { path: "payload.snapY" } },
    },
    chrome: {
      rulers: [
        {
          orientation: "horizontal",
          edge: "bottom",
          scale: { unit: "month", unitsPerStep: GhgScale.unitsPerMonth / months, origin: GhgScale.origin },
          ladder: RULER_RUNGS.filter((entry) => entry.months >= months).map((entry) => entry.rung),
          minSpacingPx: 64,
        },
      ],
    },
    filter: {
      field: "payload.tags",
      label: "Filter by tags",
      // Notes carry no tags and stay on the canvas under every filter.
      elementTypes: [GhgElementTypes.trend, GhgElementTypes.trigger],
      // The key to the phase colours, each swatch painted by the rule that paints its phase.
      legend: GHG_PHASES.map((phase, index) => ({ caption: GHG_PHASE_TITLES[index], swatchClass: `ghg-${phase}` })),
    },
    // True-time by default; the Compact toggle under the legend packs the trends along their rows
    // at one width, in the order they start, and takes away the time axis with the gestures.
    layout: {
      modes: ["manual", "row-packed"],
      toggle: { caption: "Compact", on: "row-packed" },
      // Only a trend takes the compact width: a trigger keeps its circle and a note its box, and a
      // note two rows tall keeps both rows clear, because an element covers every row line it spans.
      // Each trend's width is its share of the compact width, and an influence's target starts after
      // the middle of its source, so causes read to the left of their effects.
      rowPacked: {
        width: { path: "payload.compactWidth" },
        gap: GhgScale.unitsPerMonth,
        types: [GhgElementTypes.trend],
        rowStep: GhgScale.rowStep,
        followConnections: true,
      },
      modeOverrides: {
        "row-packed": {
          // Nothing that would change a date is offered: no trigger is dragged, and a note is
          // neither dragged nor resized, while influences from a trigger are still drawn.
          elementTypes: [compactTrendType, { ...TRIGGER_TYPE, draggable: false }, { ...NOTE_TYPE, sizing: "model", draggable: false }],
          dragging: "disabled",
          chrome: { rulers: [] },
          // A compact x is no date, so empty canvas offers nothing there.
          backgroundMenu: false,
        },
      },
    },
    dragging: "enabled",
    // Arrange diagram and "Add … here" on empty canvas, from the backend's own list.
    backgroundMenu: true,
  });
}


const UNITS = Object.keys(GhgTimeUnits) as GhgTimeUnit[];

describe("the hype cycle's compiled definition", () => {
  it("is the hand-written definition, for every time unit", () => {
    for (const unit of UNITS) {
      expect(ghgDefinitionFor(unit), unit).toEqual(handWrittenDefinitionFor(unit));
    }
  });

  it("is what the canvas draws: the module's definitions are compiled from the bundled specification", () => {
    // The canary for the test above: were the canvas still holding its own definition, the
    // equality would compare the oracle with a copy of itself.
    const spec = parseDisl(disText);
    for (const unit of UNITS) {
      expect(ghgDefinitionFor(unit), unit).toEqual(assertValidDiagramDefinition(compileNotation(spec, GHG_BINDINGS, { diagram: { unit } })));
    }
    expect(GHG_DEFINITION).toBe(ghgDefinitionFor("month"));
  });
});

/** The CSS custom property each theme token of the specification is painted with. */
const TOKEN_PROPERTIES: Readonly<Record<string, string>> = {
  "ghg.peak": "--color-diagram-hype-peak",
  "ghg.trough": "--color-diagram-hype-trough",
  "ghg.slope": "--color-diagram-hype-slope",
  "ghg.plateau": "--color-diagram-hype-plateau",
  "ghg.chevron": "--color-diagram-hype-chevron",
  "ghg.trigger": "--color-diagram-hype-trigger",
  "ghg.note": "--color-diagram-hype-note",
  "ghg.noteText": "--color-diagram-hype-note-text",
  "ghg.border": "--color-border",
  "ghg.influence": "--color-text-muted",
};

/** The custom properties a block of `index.css` declares, by name. */
function customPropertiesIn(block: string): Record<string, string> {
  return Object.fromEntries([...block.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((match) => [match[1]!, match[2]!.trim().toLowerCase()]));
}

/** The light theme (`:root`) and the dark one (`:root` under `prefers-color-scheme: dark`) of `index.css`. */
function themes(): { light: Record<string, string>; dark: Record<string, string> } {
  const css = readFileSync(join(__dirname, "..", "..", "..", "client", "src", "index.css"), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
  const light = /^:root\s*\{([^}]*)\}/m.exec(css);
  const dark = /@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{([^}]*)\}/.exec(css);
  if (light === null || dark === null) {
    throw new Error("index.css no longer has the light :root block and the dark one this test reads.");
  }

  return { light: customPropertiesIn(light[1]!), dark: customPropertiesIn(dark[1]!) };
}

describe("the hype cycle's theme tokens", () => {
  const theme = parseDisl(disText).notation.theme!;
  const css = themes();

  it("are each painted by a custom property", () => {
    expect(Object.keys(theme.tokens).sort()).toEqual(Object.keys(TOKEN_PROPERTIES).sort());
  });

  it("equal the custom properties in the light theme", () => {
    const light = { ...theme.tokens, ...theme.modes?.light };
    for (const [token, property] of Object.entries(TOKEN_PROPERTIES)) {
      expect(css.light[property], `${token} (${property})`).toBe(light[token]!.toLowerCase());
    }
  });

  it("equal the custom properties in the dark theme", () => {
    const dark = { ...theme.tokens, ...theme.modes?.dark };
    for (const [token, property] of Object.entries(TOKEN_PROPERTIES)) {
      expect(css.dark[property], `${token} (${property})`).toBe(dark[token]!.toLowerCase());
    }
  });
});
