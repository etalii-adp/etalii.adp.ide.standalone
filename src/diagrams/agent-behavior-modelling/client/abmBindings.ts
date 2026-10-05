import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the behavior model's canvas needs beside its bundled specification, because the library
 * cannot read it from there. `compileNotation` derives everything else - the eleven kinds and their
 * shapes, the keyword and label lines, the accessible names, the top and bottom anchors, the parent
 * line with its route, arrowhead, ends and cycle rule, and the shortcuts - from
 * `definition/agent-behavior-modelling.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL path.** The keyword line is `keyword(self)`, a function of the specification: the
 *   backend computes it into the payload as `keyword` (`AbmElementMapper`), and the library has no CEL.
 * - **The class names.** They tie the drawing to `abm.css`: a node's fill family is its notation
 *   style (`abm-composite`, `abm-decorator`, `abm-check`, `abm-action`, `abm-other`), and a test
 *   proves the theme tokens equal the custom properties `abm.css` paints with. The dashed
 *   `abm-implicit` class is carried by every kind, as the canvas always has: the specification states
 *   it on a Do alone, because only a Do has the `implicit` attribute, and on any other kind the
 *   condition never holds.
 * - **The label editor's box.** Where the inline editor opens over the label line, which the
 *   specification does not state.
 * - **The target's anchor rule.** The parent line's target end attaches by edge intersection, as the
 *   canvas has always declared it; the specification's `sides` anchors say the same of every node.
 * - **The diode.** The specification's custom `diode` is drawn as the library's diode, which
 *   `abmShapeGeometry.test.ts` proves is the same outline.
 * - **The right-button connect.** DISL 0.2 has no pointer-button gesture; the specification proposes
 *   one as `x-abm-connectGesture`, which this reads.
 */
export const ABM_BINDINGS: NotationBindings = {
  wireIds: "x-abm",
  celPaths: { "keyword(self)": "payload.keyword" },
  classNames: (_type, node) => [
    { className: "canvas-element abm-node", on: "element" },
    { className: "abm-implicit", on: "element", when: { path: "payload.implicit", is: "true" } },
    { className: `canvas-node abm-${String(node.style)}`, on: "shape" },
  ],
  labelClassName: (_type, label) => (label.style === "keyword" ? "abm-keyword" : "canvas-node-label abm-label"),
  labelEditorBox: (_type, label) => (label.editable === "inline" ? { top: 27, height: 22 } : undefined),
  relationClassName: () => "abm-child",
  endpointAnchors: (_relation, end) => (end === "target" ? "edge" : undefined),
  customShapes: { diode: { shape: "diode" } },
  extras: (spec) => {
    const gesture = spec.notation.edges?.Child?.["x-abm-connectGesture"] as { button?: string } | undefined;
    return gesture?.button === "secondary" ? { connectOnRightDrag: true } : {};
  },
};
