import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";
import { FdgElementTypes, FdgRelationTypes } from "./fdgIds";

/**
 * What the functional decomposition graph's canvas needs beside its bundled specification, because the
 * library cannot read it from there. `compileNotation` derives everything else - the five shapes, the
 * labels, the sizes, the relations' ends, limits and cycle rule, the right-button body drag and the
 * actions - from `definition/functional-decomposition-graph.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The class names.** They tie the drawing to `fdg.css`, which paints the theme tokens per type; the
 *   specification names its fills by token, and the stylesheet is where the tokens become colours.
 * - **The target end's anchors.** Every relation ends on the target's outline, where the line meets it,
 *   which the library's edge anchors draw; the specification's `outline` anchoring compiles to that
 *   for a node, and this states it for the relation's target end, as today's canvas does.
 * - **The diode.** DISL draws it as a custom path (6.7); the library has the same shape as its
 *   built-in `diode`, so the custom shape is bound to it rather than drawn again here.
 */

/** A metamodel type's name as the document spells it: `UiElement` is `ui-element`. */
const DOCUMENT_TYPES: Readonly<Record<string, string>> = {
  UiElement: FdgElementTypes.uiElement,
  DataElement: FdgElementTypes.dataElement,
  Action: FdgElementTypes.action,
  Function: FdgElementTypes.function,
  Comment: FdgElementTypes.comment,
  UiChild: FdgRelationTypes.uiChild,
  OwnsAction: FdgRelationTypes.ownsAction,
  OwnsData: FdgRelationTypes.ownsData,
  OwnsFunction: FdgRelationTypes.ownsFunction,
  Shows: FdgRelationTypes.shows,
};

function documentType(type: string): string {
  const spelled = DOCUMENT_TYPES[type];
  if (spelled === undefined) {
    throw new Error(`fdgBindings: "${type}" is no type of the functional decomposition graph.`);
  }

  return spelled;
}

export const FDG_BINDINGS: NotationBindings = {
  wireIds: "x-fdg",
  celPaths: {},
  classNames: (type) => [
    { className: "canvas-element fdg-element", on: "element" },
    { className: `canvas-node fdg-${documentType(type)}`, on: "shape" },
  ],
  labelClassName: (type) => (type === "Comment" ? "fdg-comment-text" : "canvas-node-label fdg-label"),
  relationClassName: (relation) => `fdg-relation fdg-${documentType(relation)}`,
  endpointAnchors: (_relation, end) => (end === "target" ? "edge" : undefined),
  customShapes: { diode: { shape: "diode" } },
};
