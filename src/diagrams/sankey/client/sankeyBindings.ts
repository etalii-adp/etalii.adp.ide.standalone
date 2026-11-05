import type { ActionInvocation } from "@client/canvas/library/definition/actions";
import type { ClassDeclaration, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the Sankey canvas needs beside its bundled specification, because the library cannot read it
 * from there. `compileNotation` derives the rest from `definition/sankey.dis`: the bar, its six side
 * labels and their type, the anchors along its left and right edges, the drag up and down a column,
 * the flow's ends and its right-button drag, and the context menus' shortcuts.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** The library has no CEL, so the backend computes what the notation's functions
 *   return - a node's place from the layout, its name and value as written, its side, its paint, a
 *   band's thickness - and this table names where each lands. `sankeyColumn(self) > 0` and `== 0`
 *   choose a label's side, which the payload carries as `side`, and `noteSide` where there is a note.
 * - **The class names.** They tie the drawing to `sankey.css`, which paints the label halo, the band's
 *   opacity and the hidden centre line.
 * - **The band's route and ribbon** (finding A15). DISL strokes every edge and its bezier reaches at
 *   least 30; the band is a filled ribbon as thick as its value measured vertically, over a horizontal S
 *   whose control points sit `max(40, half the distance)` from each end. The route is {@link centrePath};
 *   the ribbon is the library's, swept along it.
 * - **The second keys of + and -.** DISL gives a menu entry one shortcut: `+` is also typed with Shift
 *   on some layouts and `=` sits beside it, and `-` is `_` with Shift.
 */

/** How strongly a band's curve pulls: half the distance it travels, at least 40, so it leaves and arrives level. */
function pullOf(from: ShapePoint, to: ShapePoint): number {
  return Math.max(40, Math.abs(to.x - from.x) / 2);
}

/**
 * The centre line of a band: a horizontal S from where it leaves its source's right edge to where it
 * reaches its target's left edge. It is what the library hit-tests, what a connect preview draws, and
 * what the ribbon is swept along.
 */
export function centrePath(from: ShapePoint, to: ShapePoint): string {
  const pull = pullOf(from, to);
  return `M ${from.x} ${from.y} C ${from.x + pull} ${from.y} ${to.x - pull} ${to.y} ${to.x} ${to.y}`;
}

/** The keys beyond each entry's one shortcut: `+` with Shift and `=` beside it, and `-` as `_` with Shift. */
const SECOND_KEYS: Readonly<Record<string, readonly ActionInvocation[]>> = {
  "+": [{ kind: "shortcut", key: "+", shift: true }, { kind: "shortcut", key: "=" }],
  "-": [{ kind: "shortcut", key: "_", shift: true }],
};

const NODE_CLASSES: readonly ClassDeclaration[] = [
  { className: "canvas-element sankey-node", on: "element" },
  { className: "sankey-bar", on: "shape" },
];

/** A label's class by the style it is drawn in. */
const LABEL_CLASSES: Readonly<Record<string, string>> = {
  nodeName: "sankey-label sankey-name",
  nodeValue: "sankey-label sankey-value",
  nodeNote: "sankey-label sankey-note",
};

export const SANKEY_BINDINGS: NotationBindings = {
  wireIds: "x-sankey",
  celPaths: {
    "sankeyColumn(self) * 320.0": "element.x",
    "sankeyTop(self)": "element.y",
    "labelOf(self)": "payload.name",
    "nodeDisplay(self)": "payload.displayValue",
    "labelOf(self) + ': ' + nodeDisplay(self) + (self.note != '' ? ' (' + self.note + ')' : '')": "payload.tooltip",
    "paint(flowColor(self))": "payload.paint",
    "bandThickness(self)": "payload.thickness",
  },
  celConditions: {
    "sankeyColumn(self) > 0": { path: "payload.side", equals: "right" },
    "sankeyColumn(self) == 0": { path: "payload.side", equals: "left" },
    "sankeyColumn(self) > 0 && self.note != ''": { path: "payload.noteSide", equals: "right" },
    "sankeyColumn(self) == 0 && self.note != ''": { path: "payload.noteSide", equals: "left" },
  },
  classNames: () => NODE_CLASSES,
  labelClassName: (_type, label) => (typeof label.style === "string" ? LABEL_CLASSES[label.style] : undefined),
  relationClassName: () => "sankey-flow",
  relationLineClassName: () => "sankey-flow-line",
  customShapes: { sankeyBar: { shape: "box" } },
  customRoutes: { Flow: { customRoute: "sankey-band", path: (from, to) => centrePath(from, to) } },
  ribbons: { Flow: { className: "sankey-band" } },
  boundStyle: () => ({ fill: { path: "payload.paint" }, labelColor: { path: "payload.paint" } }),
  stylesFromNotation: true,
  action: (action, entry) => {
    const more = entry.shortcut === undefined ? undefined : SECOND_KEYS[entry.shortcut];
    return more === undefined ? action : { ...action, invokedBy: [...action.invokedBy, ...more] };
  },
};
