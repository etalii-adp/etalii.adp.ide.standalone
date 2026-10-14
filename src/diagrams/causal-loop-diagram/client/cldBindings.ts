import type { ClassDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the causal loop diagram's canvas needs beside its bundled specification, because the library
 * cannot read it from there. `compileNotation` derives everything else from
 * `definition/causal-loop-diagram.dis`: the pill variables with their label, accessible name and
 * outline anchors; the loop badge with no body, drawn at the centre of the variables it runs through,
 * its circular arrow above that point turning the way its polarity says and its caption on it; the
 * causal link as a bowed arc with its flipped variant, its loop to its own variable, its delay strokes,
 * its polarity mark and its weight ladder; the right-button connect, the double-click rename, the
 * Delete key and the background menu.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** The library has no CEL. The polarity mark is a choice between `+`, `−` and
 *   nothing, which the canvas reads from the payload as `polarityMark` (`diagramModelOf`). The two
 *   tooltips name a path no payload carries yet, so no tooltip is drawn, as none was before: the
 *   backend's payload would have to compute them, which this conversion leaves to a change of its own.
 * - **The class names.** They tie the drawing to `causal-loop.css`, which paints the theme tokens;
 *   `moduleThemeContrast.test.ts` holds those tokens to readable values. The loop badge's colour
 *   follows its computed polarity through its caption's class, and a disagreeing loop carries the
 *   class the wavy underline is painted by, which DISL cannot say (the companion's gap 4).
 * - **The weight ladder's classes.** The specification computes a link's width from its weight; each
 *   width it gives is painted by the class the stylesheet already has for it.
 * - **The arrowhead.** The specification says DISL's `arrow`, which is the open V; the canvas has
 *   always drawn the filled triangle. Kept as drawn until the specification says `arrowFilled`, as the
 *   hype cycle graph's was corrected.
 */
const ELEMENT_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Variable: [
    { className: "canvas-element causal-loop-variable", on: "element" },
    { className: "canvas-node", on: "shape" },
  ],
  Loop: [
    { className: "causal-loop-loop", on: "element" },
    { className: "causal-loop-disagrees", on: "element", when: { path: "payload.disagrees", is: "true" } },
  ],
};

/** The class each width of the weight ladder is painted by. */
const WEIGHT_CLASSES: Readonly<Record<string, string>> = {
  "1": "causal-loop-weight-light",
  "1.5": "causal-loop-weight-normal",
  "2.5": "causal-loop-weight-heavy",
};

export const CLD_BINDINGS: NotationBindings = {
  wireIds: "x-cld",
  celPaths: {
    "self.polarity == 'positive' ? '+' : (self.polarity == 'negative' ? '−' : '')": "payload.polarityMark",
    "self.label != '' && self.label != self.name ? self.label + ' (' + self.name + ')' : self.name": "payload.tooltip",
    "self.caption + ': computed ' + self.computed + (self.disagrees ? ', but labelled ' + self.stated : '')": "payload.tooltip",
  },
  classNames: (type) => ELEMENT_CLASSES[type] ?? [],
  labelClassName: (type) => (type === "Loop" ? { template: "causal-loop-badge causal-loop-{payload.computed}" } : "canvas-node-label"),
  relationClassName: () => "causal-loop-link",
  iconClassName: () => "causal-loop-marker",
  midMarkerClassNames: () => ({ group: "causal-loop-delay", marker: "causal-loop-delay-stroke" }),
  edgeLabelClassName: () => "causal-loop-polarity",
  strokeWidthClassName: (_relation, width) => WEIGHT_CLASSES[String(width)],
  builtInMarkers: { arrow: "arrow" },
};
