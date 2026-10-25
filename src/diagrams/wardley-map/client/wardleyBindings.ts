import type { ActionDeclaration } from "@client/canvas/library/definition/actions";
import type { Binding } from "@client/canvas/library/definition/binding";
import type { ClassDeclaration, LabelTypography } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the Wardley map's canvas needs beside its bundled specification, because the library cannot read
 * it from there. `compileNotation` derives everything else from `definition/wardley-map.dis`: the map's
 * bounds stretched across to the pane within their margin, the four evolution stages as bands with their
 * dashed boundaries, both axes with their titles and end labels, the drag clamped at the map's edge, the
 * marks and their labels at the document's own offset, the inertia bar, the links and flows, an
 * evolution's ring and its dashed join, the pipelines' components moving along their row only, the
 * notes, the annotations' pins, the accelerators, the attitudes behind everything, and the menus.
 *
 * <b>Why each entry is code.</b>
 * - **The wire ids**, and **the type ids**: the specification's `x-wardley` block gives the actions and
 *   tools their wire ids, and gives the component, the anchor, the submap and a pipeline's component one
 *   wire type, which the canvas tells apart by the specification's names, as `WardleyCanvas` builds it.
 * - **The CEL paths.** The library has no CEL: a label's offset, the badges' text, a coordinate that
 *   comes from another element, an annotation's number and an attitude's kind are what `WardleyCanvas`
 *   computes into the payload, and this table names where.
 * - **The class names.** They tie the drawing to `wardley.css`, which paints the styles and tokens the
 *   specification names (`wardleyCompiledDefinition.test.tsx` holds the two to each other).
 * - **The submap's double ring.** DISL draws it as a custom shape of two circles; the library's symbol
 *   draws that ring, chosen per type.
 * - **The keys the backend is sent.** The backend resolves a Wardley action by its key, so each action
 *   sends the key its menu entry writes.
 */

/** The axes' and the bands' text: a readable size whatever the zoom, within limits. */
export const WARDLEY_CHROME_TYPOGRAPHY: LabelTypography = { fontSize: 20, scaleWithView: { min: 0.35, max: 2.5 } };

/** The classes of a mark the reader can move: a component, an anchor, a submap or a pipeline's component. */
function markClasses(kind: string): readonly ClassDeclaration[] {
  return [
    { className: "wardley-element-group", on: "element" },
    { className: `wardley-kind-${kind}`, on: "element" },
    { className: "wardley-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
    { className: "wardley-element", on: "shape" },
    { className: "wardley-element-outer", on: "shape-inner" },
  ];
}

const ELEMENT_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Component: markClasses("component"),
  Anchor: markClasses("anchor"),
  Submap: markClasses("submap"),
  PipelineComponent: markClasses("component"),
  Evolution: [{ className: "wardley-evolve-target", on: "shape" }],
  AnnotationPin: [
    { className: "wardley-annotation", on: "element" },
    { className: "wardley-annotation-glyph", on: "shape" },
  ],
  Note: [{ className: "wardley-note", on: "element" }],
  Accelerator: [{ className: { template: "wardley-accelerator-{payload.direction}-glyph" }, on: "shape" }],
  Attitude: [{ className: { template: "wardley-attitude wardley-attitude-{payload.attitude}" }, on: "shape" }],
};

const LABEL_CLASSES: Readonly<Record<string, string | Binding>> = {
  name: "wardley-element-label",
  renamedTo: "wardley-element-label",
  badges: "wardley-element-badges",
  text: "wardley-note-label",
  number: "wardley-annotation-label",
  kind: { template: "wardley-attitude-label wardley-attitude-{payload.attitude}-label" },
};

/** An accelerator's name, in the class of its direction. */
const ACCELERATOR_LABEL: Binding = { template: "wardley-accelerator-{payload.direction}-label" };

const RELATION_LINE_CLASSES: Readonly<Record<string, string>> = {
  Dependency: "wardley-link",
  Flow: "wardley-link wardley-link-flow",
  "Evolution.component": "wardley-evolve",
};

/** An action sending the key its menu entry writes, which is how the backend resolves it. */
function dispatched(action: ActionDeclaration, entry: { shortcut?: string }): ActionDeclaration {
  return entry.shortcut !== undefined ? { ...action, backendKey: entry.shortcut } : action;
}

export const WARDLEY_BINDINGS: NotationBindings = {
  wireIds: "x-wardley",
  typeIds: "specification",
  celPaths: {
    "has(self.labelOffset) ? [self.labelOffset.dx, self.labelOffset.dy] : [15.0, 4.0]": "payload.labelOffset",
    "has(self.labelOffset) ? [self.labelOffset.dx, self.labelOffset.dy + 14.0] : [15.0, 18.0]": "payload.badgesOffset",
    "(decoratorNames().filter(d, decorated(self, d)) + (self.inertia ? ['inertia'] : [])).join(' · ')": "payload.badges",
    "has(self.component) ? self.component.visibility : 0.0": "element.y",
    "has(self.component) ? self.component.maturity : 0.0": "element.x",
    "has(self.parent.component) ? self.parent.component.visibility : 0.0": "element.y",
    "string(self.number)": "payload.number",
    "string(self.attitude)": "payload.attitude",
  },
  celConditions: { "decoratorNames().exists(d, decorated(self, d)) || self.inertia": { path: "payload.badges", is: "non-empty" } },
  classNames: (type) => ELEMENT_CLASSES[type] ?? [],
  labelClassName: (type, label) => (type === "Accelerator" ? ACCELERATOR_LABEL : LABEL_CLASSES[label.id]),
  badgeClassName: () => "wardley-inertia",
  relationLineClassName: (relation) => RELATION_LINE_CLASSES[relation],
  customShapes: { doubleRing: { shape: "symbol" } },
  boundStyle: (type) => (type === "Submap" ? { silhouette: { template: "double-circle" } } : undefined),
  axisChrome: {
    className: "wardley-chrome",
    axisClassName: "wardley-axis",
    rangeClassName: (_axis, _range, index) => `wardley-band wardley-band-${index}`,
    typography: WARDLEY_CHROME_TYPOGRAPHY,
  },
  action: dispatched,
};
