import type { ClassDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";
import { GHG_PHASES } from "./ghgIds";

/**
 * What the hype cycle's canvas needs beside its bundled specification, because the library cannot
 * read it from there. `compileNotation` derives everything else - shapes, labels, anchors, sizes,
 * the influence's ends and rules, the shortcuts, the snap, the ruler, the filter and legend, and the
 * Compact viewpoint - from `definition/gartner-hype-cycle-graph.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** A trigger's label is `self.name + ' · ' + formatWhen(self.date, diagram.unit, false)`:
 *   the library has no CEL, so the backend computes `formatWhen(...)` into the payload as `when`
 *   (`GhgElementMapper`), and this table names that path. The same holds for the long date in the
 *   tooltip, a trend's visible phases and its compact width.
 * - **The class names.** They tie the drawing to `ghg.css`, which paints the theme tokens; a test
 *   proves those tokens equal the specification's.
 * - **The banner.** `phasedBanner` is drawn as the library's `arrow-banner` cut into segments, which
 *   `ghgShapeGeometry.test.ts` proves is the same geometry. Whether its boundaries are the stored
 *   ones, and so can be dragged, is read from the CEL its parameters bind: a viewpoint that spreads
 *   them evenly (`n / visiblePhases(self)`) stores none, and its handles' `visible` CEL hides them.
 * - **The snap origins.** Each element snaps from its own origin, which for a trigger puts its
 *   CENTRE on a step line and a row's middle - geometry constants the backend sends in the payload.
 */

/** Whether a banner parameter binds the stored boundary `n`, or an even spread over the visible phases. */
function boundarySource(value: unknown, index: number): "stored" | "even" {
  const cel = typeof value === "object" && value !== null && "cel" in value ? String(value.cel) : undefined;
  if (cel === `boundaryFraction(self, ${index})`) {
    return "stored";
  }

  if (cel === `${index}.0 / double(visiblePhases(self))`) {
    return "even";
  }

  throw new Error(`ghgBindings: phasedBanner b${index} binds ${JSON.stringify(value)}, which is neither the stored boundary nor an even spread.`);
}

const ELEMENT_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Trend: [{ className: "canvas-element ghg-trend", on: "element" }],
  Trigger: [
    { className: "canvas-element ghg-trigger", on: "element" },
    { className: "canvas-node ghg-trigger-circle", on: "shape" },
  ],
  Note: [
    { className: "canvas-element ghg-note", on: "element" },
    { className: "canvas-node ghg-note-box", on: "shape" },
  ],
};

export const GHG_BINDINGS: NotationBindings = {
  wireIds: "x-ghg",
  celPaths: {
    "visiblePhases(self)": "payload.phases",
    "formatWhen(self.date, diagram.unit, false)": "payload.when",
    "formatWhen(self.date, diagram.unit, true)": "payload.whenLong",
    "24.0 * double(visiblePhases(self))": "payload.compactWidth",
  },
  classNames: (type) => ELEMENT_CLASSES[type] ?? [],
  labelClassName: (type) => (type === "Note" ? "ghg-note-text" : "canvas-node-label ghg-label"),
  relationClassName: () => "ghg-influence",
  customShapes: {
    phasedBanner: {
      shape: "arrow-banner",
      segments: ({ shape, params, bind }) => {
        const sources = new Set([1, 2, 3].map((index) => boundarySource(params[`b${index}`], index)));
        if (sources.size !== 1) {
          throw new Error("ghgBindings: phasedBanner mixes stored and evenly spread boundaries.");
        }

        const stored = sources.has("stored");
        return {
          count: bind(params.count),
          max: shape.params?.count?.max ?? GHG_PHASES.length,
          boundaries: stored ? "payload.boundaries" : undefined,
          classNames: GHG_PHASES.map((phase) => `ghg-${phase}`),
          tooltips: (shape.parts ?? []).flatMap((part) => (typeof part["x-part.tooltip"] === "string" ? [part["x-part.tooltip"]] : [])),
          divider: "chevron",
          draggableBoundaries: stored,
        };
      },
    },
  },
  snapOrigins: { x: "payload.snapX", y: "payload.snapY" },
  legendSwatchClass: (_enumName, value) => `ghg-${value}`,
};
