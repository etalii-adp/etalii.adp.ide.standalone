import type { CSSProperties } from "react";

/**
 * THE TWO LOOKS' PAINT, each a ring the library draws just outside an element.
 *
 * SELECTED is a solid outline at a small offset; ACCEPT is a dashed outline at a larger offset in
 * its own colour. The offsets differ so that an element which is both - selected, and under a
 * connection being dragged - shows both at once, one inside the other (centralized-selection
 * Requirements 5.2, 6.1, 6.2). The stroke stays the same width at any zoom, and neither takes the
 * pointer: a press on the ring is a press on the canvas beneath.
 *
 * ## Why the paint is inline, and not a stylesheet rule
 *
 * The ring is a rect inside the element's own group, so a module rule styling its shapes by
 * descendant - `.mindmap-node rect`, `.c4-node rect`, `.sparql-region rect` - matches the ring
 * too, and outranks a single class. It did: on the mind map the selected ring became an opaque
 * surface-coloured box drawn over the label, so the selection did not show and the text could not
 * be read. These looks are the library's and no module restyles them (Requirement 5), so their
 * paint is written where no module rule can reach it - the element's own style - and every
 * property a module rule could set is stated, `none` included, so none is left for one to fill.
 * `ringsSurviveModuleStyles.test.ts` loads every stylesheet in the tree against the rendered
 * rings and names any sheet that still repaints one.
 *
 * Colours stay theme variables, so a theme still decides them.
 */
const RING_BASE: CSSProperties = {
  fill: "none",
  strokeWidth: 2,
  strokeOpacity: 1,
  fillOpacity: 1,
  opacity: 1,
  visibility: "visible",
  display: "inline",
  vectorEffect: "non-scaling-stroke",
  pointerEvents: "none",
};

export const SELECTED_RING_STYLE: CSSProperties = {
  ...RING_BASE,
  stroke: "var(--color-primary, limegreen)",
  strokeDasharray: "none",
};

export const ACCEPT_RING_STYLE: CSSProperties = {
  ...RING_BASE,
  stroke: "var(--color-accept, #0284c7)",
  strokeDasharray: "5 3",
};
