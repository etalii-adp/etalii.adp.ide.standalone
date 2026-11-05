import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ElementTypeDefinition, RelationTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { FDG_OWNERSHIP_RELATIONS, FdgActions, FdgElementTypes, FdgRelationTypes, FdgShortcuts, type FdgElementType, type FdgRelationType } from "./fdgIds";

/*
 * The canvas definition as this module wrote it by hand before it was compiled from the bundled
 * specification (functional-decomposition-graph-disl-fbl task 8), kept verbatim as the oracle the
 * compiled one is compared with. Test only: nothing the module ships imports it.
 */

/** The shape each type is drawn as - the library's built-ins, and nothing of this module's own. */
const SHAPES: Readonly<Record<FdgElementType, ElementTypeDefinition["shape"]>> = {
  [FdgElementTypes.uiElement]: "superellipse",
  [FdgElementTypes.dataElement]: "parallelogram",
  [FdgElementTypes.action]: "trapezoid",
  [FdgElementTypes.function]: "diode",
  [FdgElementTypes.comment]: "box",
};

/**
 * One of the four named types: its shape, its fill class, one editable label bound to the Name,
 * sized by the reader in width. The class is the only thing that tells the four apart in colour,
 * and the colour lives in `fdg.css` as a theme token.
 */
function namedType(type: FdgElementType): ElementTypeDefinition {
  return {
    id: type,
    shape: SHAPES[type],
    classNames: [
      { className: "canvas-element fdg-element", on: "element" },
      { className: `canvas-node fdg-${type}`, on: "shape" },
    ],
    labels: [{ text: { path: "payload.name" }, editable: true, truncate: true, className: "canvas-node-label fdg-label" }],
    anchors: { kind: "edge" },
    sizing: "user",
  };
}

/**
 * The Comment: a box sized in both directions, its text wrapped inside it and edited in the
 * multiline editor, and NO anchors - it is in no relation's endpoints, and showing a handle it
 * could never complete would invite a gesture the notation forbids (Requirement 5.3).
 */
const COMMENT_TYPE: ElementTypeDefinition = {
  id: FdgElementTypes.comment,
  shape: SHAPES[FdgElementTypes.comment],
  classNames: [
    { className: "canvas-element fdg-element", on: "element" },
    { className: `canvas-node fdg-${FdgElementTypes.comment}`, on: "shape" },
  ],
  labels: [{ text: { path: "payload.text" }, editable: true, wrap: true, className: "fdg-comment-text" }],
  anchors: { kind: "edge", enabled: false, visible: false },
  sizing: "user",
  resize: "both",
};

/**
 * One relation, exactly as the rules table states it: its sources, its one target type, and its
 * limit. Every relation draws the same way - a curve with an arrow at the child, and its name at
 * the midpoint, which draws nothing while the name is empty.
 */
function relation(
  id: FdgRelationType,
  sources: readonly FdgElementType[],
  target: FdgElementType,
  cardinality: { maxIntoTarget?: number; maxFromSource?: number },
): RelationTypeDefinition {
  return {
    id,
    route: "cubic-bezier",
    style: { endMarker: "arrow" },
    label: { placement: "midpoint", editable: true },
    className: `fdg-relation fdg-${id}`,
    endpoints: {
      source: { elementTypes: sources },
      target: { elementTypes: [target], anchors: "edge" },
      allowSelf: false,
      cardinality,
    },
  };
}

const { uiElement, dataElement, action, function: fn } = FdgElementTypes;

/**
 * What a functional decomposition graph allows, stated once. The backend states the same table in
 * `FdgRelations`; the canvas refuses under the pointer what the backend would refuse on write, and
 * the backend refuses it anyway, because a request is never trusted to have come from this canvas.
 */
export const HAND_WRITTEN_FDG_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [namedType(uiElement), namedType(dataElement), namedType(action), namedType(fn), COMMENT_TYPE],
  relationTypes: [
    relation(FdgRelationTypes.uiChild, [uiElement], uiElement, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.ownsAction, [uiElement], action, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.ownsData, [uiElement, action, dataElement], dataElement, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.ownsFunction, [uiElement, action, dataElement, fn], fn, { maxIntoTarget: 1 }),
    relation(FdgRelationTypes.shows, [action], uiElement, { maxFromSource: 1 }),
  ],
  // The four ownership relations form a forest; Shows is navigation and may loop (Requirement 5.4).
  acyclic: [{ relationTypes: FDG_OWNERSHIP_RELATIONS }],
  // Every gesture the backend answers, declared, so the library dispatches an id and this module
  // never builds a keystroke. A rename opens the shared inline editor from the backend's prompt; the
  // commit then travels through that prompt, so the canvas sends nothing of its own for it.
  actions: [
    {
      id: FdgActions.rename,
      invokedBy: [{ kind: "shortcut", key: FdgShortcuts.rename }, { kind: "gesture", gesture: "activate" }],
      appliesTo: [{ kind: "element" }],
    },
    {
      id: FdgActions.renameConnection,
      invokedBy: [{ kind: "shortcut", key: FdgShortcuts.rename }],
      appliesTo: [{ kind: "connection" }],
    },
    { id: FdgActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
    { id: FdgActions.disconnect, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // Edge anchors draw no handle to start a connection from, so a relation is drawn by dragging
  // with the right button from one element's body to another's - causal loop's gesture, and the
  // only one an edge-anchored element offers.
  connectOnRightDrag: true,
});

