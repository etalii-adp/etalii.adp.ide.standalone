import type { ActionDeclaration } from "@client/canvas/library/definition/actions";
import type { ClassDeclaration, ElementTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the C4 canvas needs beside its six bundled specifications, because the library cannot read it from
 * there. `compileNotation` derives the rest - the eight element types and their three lines, the four
 * line types with their dash, arrowhead and label, which of them rename in place, the F2 actions, the
 * title above the drawing surface and the key below it - from `definition/c4-*.dis`, which state the same
 * notation for every view kind.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** The library has no CEL. The backend composes a card's name, type line and
 *   description, and a line's label, and the canvas carries them in the element's payload and the
 *   connection's label; a card is announced by `shownName`, its name or else its id, which the canvas
 *   computes. The key's CEL runs over each element drawn: the backend sends the entries it gave, each with
 *   its label and its style.
 * - **The class names.** They tie the drawing to `c4.css`.
 * - **The shapes and the colours.** DISL draws a person as `c4Person`, a data store as `c4Cylinder` and
 *   the rest as a `roundedRect` or a `rect`; this module draws every one as the library's `styled-box`,
 *   whose silhouette and colours are the element's own, because a C4 author sets both and the backend
 *   resolves them from its theme (`C4Theme`) - the `Database` tag that makes a cylinder among them.
 *   `c4ShapeGeometry.test.ts` holds each silhouette to the specification's geometry.
 * - **The drops.** The backend answers every drop, on an element or on the canvas, and refuses the ones
 *   that cannot land with a sentence it computes; the canvas sends each to it.
 * - **The keys the backend is sent.** The backend resolves a C4 action by its key, so each action sends
 *   the key its menu entry writes.
 */

/** The class every card carries, and the one it adds while it is dragged. */
const CARD_CLASSES: readonly ClassDeclaration[] = [
  { className: "c4-node" },
  { className: "c4-node-dragging", when: { path: "state.dragging", is: "true" } },
];

/** Each of a card's three lines by the class `c4.css` sizes it with. */
const LINE_CLASSES: Readonly<Record<string, string>> = { name: "c4-node-name", type: "c4-node-type", description: "c4-node-description" };

/**
 * The dashed rectangle around a container view's system or a hosting deployment node, which is the
 * host's rather than the specifications' (finding A16): a frame with one composed label, which ignores
 * every gesture - a press on it is a press on the background.
 */
export const BOUNDARY_TYPE: ElementTypeDefinition = {
  id: "boundary",
  shape: "frame",
  // On the ELEMENT rather than the shape: a boundary is its frame AND its label, and the test that reads
  // its text reads the whole thing.
  classNames: [
    { className: "c4-boundary", on: "element" },
    // The dashed outline's own class, so the stylesheet reaches the frame and nothing else the library
    // draws in the boundary's group.
    { className: "c4-boundary-outline" },
  ],
  labels: [{ text: { template: "{payload.name} [{payload.kind}]" }, placement: "above", className: "c4-boundary-label" }],
  anchors: { kind: "edge" },
  sizing: "model",
  draggable: false,
  selectable: false,
};

/** An action sending the key its menu entry writes, which is how the backend resolves it. */
function dispatched(action: ActionDeclaration, entry: { shortcut?: string }): ActionDeclaration {
  return entry.shortcut !== undefined ? { ...action, backendKey: entry.shortcut } : action;
}

export const C4_BINDINGS: NotationBindings = {
  wireIds: "x-c4",
  celPaths: {
    "displayedName(self)": "payload.name",
    "typeLine(self)": "payload.typeLine",
    "displayedDescription(self)": "payload.description",
    "shownName(self)": "payload.shownName",
    "lineLabel(self.description, self.technology)": "connection.label",
    "self.description == '' ? '' : (self.order != '' ? self.order + '. ' + self.description : self.description)": "connection.label",
  },
  rowPaths: {
    "self.legendLabel": "label",
    "self.external ? token('color.external') : token({'Person': 'color.person', 'SoftwareSystem': 'color.softwareSystem', 'Container': 'color.container', 'Component': 'color.component', 'DeploymentNode': 'color.deploymentNode', 'InfrastructureNode': 'color.deploymentNode', 'ContainerInstance': 'color.container', 'SoftwareSystemInstance': 'color.softwareSystem'}[self.type])": "style.background",
  },
  classNames: () => CARD_CLASSES,
  labelClassName: (_type, label) => LINE_CLASSES[label.id],
  relationClassName: () => "c4-relationship-group",
  relationLineClassName: () => "c4-relationship-line",
  endpointAnchors: (_relation, end) => (end === "target" ? "edge" : undefined),
  customShapes: { c4Person: { shape: "styled-box" }, c4Cylinder: { shape: "styled-box" } },
  builtInShapes: { roundedRect: "styled-box", rect: "styled-box" },
  boundStyle: () => ({ silhouette: { path: "payload.shape" }, fill: { path: "payload.background" }, labelColor: { path: "payload.color" } }),
  labelFit: "style",
  toolboxDrops: "module",
  canvasChrome: {
    title: { className: "c4-canvas-title", data: { testid: "c4-title" } },
    legend: {
      entries: "payload.legend",
      className: "c4-canvas-legend",
      titleClassName: "c4-legend-title",
      entryClassName: "c4-legend-entry",
      swatchClassName: "c4-legend-swatch",
      data: { testid: "c4-legend" },
    },
  },
  action: dispatched,
};
