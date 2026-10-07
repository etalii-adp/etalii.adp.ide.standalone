import { forwardBezierPath, horizontalBezierPath } from "@client/canvas/connectors";
import type { ActionDeclaration } from "@client/canvas/library/definition/actions";
import type { ClassDeclaration, CustomRouteRef, LabelDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the timeline's canvas needs beside its bundled specification, because the library cannot read
 * it from there. `compileNotation` derives everything else - the two node types, their labels, their
 * named `begin` and `end` anchors, sizing and moves, the relation with its ends, label and empty-canvas
 * release, the row snap, the shortcuts and the background menu - from `definition/timeline.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL path.** A label is `shownLabel(self)`, the label or else the id: the library has no CEL,
 *   so the canvas computes it into the element's `label` (`TimelineCanvas`), and this table names it.
 * - **The class names.** They tie the drawing to `timeline.css`.
 * - **The span and the moment.** DISL draws a period as a `roundedRect` and a moment as a `diamond`;
 *   this module draws them as the library's shared `span` and its `moment` marker.
 * - **The route.** DISL's `bezier` says the curve's shape; that it loops forward and back when the target
 *   begins before the source ends is this route's geometry.
 * - **A relation's end at its target.** It attaches on the side facing its source, the library's `edge`
 *   rule; the specification names the target's `begin` anchor, which is where the model pins it.
 * - **The day snap.** A date-only element's begin rests on the start of a day. A day's width in canvas
 *   units follows the scale the canvas froze at its first fit, so it rides each element's payload; an
 *   element with a time of day carries none and moves freely in x.
 * - **The drag hint.** The specification asks the drag to show the value it would rest on
 *   (`feedback.showValue`); that value is the time under the left edge and the row under the top,
 *   arithmetic over the live bounds that no payload can carry, because the model still holds the
 *   position from before the drag.
 * - **The ruler.** The time ruler is drawn beside the canvas (`TimelineRuler`), over the frozen scale.
 * - **The keys the backend is sent.** The backend resolves a timeline action by its key, so each action
 *   sends the key its menu entry writes.
 */

/**
 * The vertical distance between adjacent rows, in the module's own y units. Mirrors
 * `TimelineRows.Height` in the backend and must stay equal to it, as it does the rows axis's scale.
 */
export const ROW_HEIGHT = 60;

/** How tall an element's box is drawn, leaving a gutter between rows. */
export const ELEMENT_HEIGHT = 36;

/** One canvas unit as a fraction of a row, for the hint's declared row arithmetic. */
const ROWS_PER_UNIT = 1 / ROW_HEIGHT;

/** How far above the box the drag hint sits, matching where the shared span drew it. */
const HINT_ABOVE = ELEMENT_HEIGHT / 2 + 8;

/**
 * The drag hint both element types show.
 *
 * <b>The one row in the tree whose text is arithmetic rather than a field</b>: the time under the
 * element's left edge, and the row its top would land on. Both read the LIVE bounds, which is why
 * `bounds` is a binding root at all - the model still holds the PRE-drag position while this is on
 * screen, so no payload could carry either number.
 */
export const DRAG_HINT: LabelDeclaration = {
  text: {
    parts: [
      {
        path: "bounds.left",
        number: { times: "payload.secondsPerUnit", plus: "payload.originSeconds", format: "yyyy-MM-ddTHH:mm:ss" },
      },
      {
        // `row N`: a literal word beside a computed number, which is why parts nest.
        parts: [
          { template: "row" },
          { path: "bounds.top", number: { times: ROWS_PER_UNIT, plus: "payload.originRows", round: "nearest" } },
        ],
        join: " ",
      },
    ],
    join: " · ",
  },
  when: { path: "state.dragging", is: "true" },
  offset: { x: 0, y: -HINT_ABOVE },
  className: "timeline-hint canvas-hint",
};

/**
 * The relation's curve. The loop is decided by geometry: a target beginning before the source ends gets
 * the forward-and-back curve, exactly as the interactive bezier drew it.
 */
export const timelineRoute: CustomRouteRef = {
  customRoute: "timeline-bezier",
  path: (from, to) => (to.x < from.x ? forwardBezierPath(from, to) : horizontalBezierPath(from, to)),
};

const ELEMENT_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Period: [{ className: "timeline-period canvas-node", on: "shape" }],
  Moment: [{ className: "timeline-moment", on: "shape" }],
};

/** An action sending the key its menu entry writes, which is how the backend resolves it. */
function dispatched(action: ActionDeclaration, entry: { shortcut?: string }): ActionDeclaration {
  return entry.shortcut !== undefined ? { ...action, backendKey: entry.shortcut } : action;
}

export const TIMELINE_BINDINGS: NotationBindings = {
  wireIds: "x-timeline",
  celPaths: { "shownLabel(self)": "element.label" },
  classNames: (type) => ELEMENT_CLASSES[type] ?? [],
  labelClassName: () => "timeline-label canvas-node-label",
  relationClassName: () => "timeline-connection",
  relationLineClassName: () => "timeline-connection-line",
  relationHitClassName: () => "timeline-connection-hit",
  endpointAnchors: (_relation, end) => (end === "target" ? "edge" : undefined),
  builtInShapes: { roundedRect: "span", diamond: "moment" },
  customRoutes: { Connection: timelineRoute },
  snapAxes: { x: { step: { path: "payload.dayUnits" }, origin: { path: "payload.dayOriginUnits" } } },
  dragHint: DRAG_HINT,
  ownRulers: true,
  action: dispatched,
};
