import type { EdgeAttachment, ShapePoint } from "../definition/diagramDefinition";

/**
 * Everything the library's canvas can tell a module, one typed member per behaviour
 * (diagram-library Requirement 1.2). **Every event is a request, never a mutation**: the
 * canvas performs nothing itself, because the model's truth lives behind the module - a
 * backend-fed module answers by executing an action and waiting for deltas, exactly as the
 * hand-built canvases behave today. A module is free to answer, transform, or ignore any of
 * these; the canvas has already enforced the definition (Requirement 4.3), so an event only
 * ever describes a gesture the definition allows.
 *
 * Where a gesture needs a value the context channel cannot carry - the channel carries no
 * gesture data - the event carries the complete payload instead, so the module can route it
 * through its own transport or the established dialog pattern (Requirement 7.3). No side
 * channel exists to invent.
 */
export type DiagramEvent =
  | ElementDropped
  | ElementDeleted
  | ElementMoved
  | ElementResized
  | SegmentBoundaryMoved
  | ElementPreviewed
  | ConnectionDrawn
  | ConnectionReleasedOnEmpty
  | ConnectionDeleted
  | ConnectionAdjusted
  | ConnectionEndMoved
  | SelectionChanged
  | LabelCommitRequested
  | ViewChanged
  | LayoutModeChanged
  | ActionInvoked
  | ActionRefused;

/** A toolbox item landed on the canvas at a position the definition allows (Requirement 5.3). */
export interface ElementDropped {
  kind: "element-dropped";
  /** The element type the drop carries - a definition `elementTypes` id. */
  elementType: string;
  /** Where it landed, in diagram coordinates. */
  position: ShapePoint;
}

/** A delete gesture reached an element the definition marks deletable. */
export interface ElementDeleted {
  kind: "element-deleted";
  elementId: string;
}

/** A drag released: the element was carried to a new position (Requirement 5.2). */
export interface ElementMoved {
  kind: "element-moved";
  elementId: string;
  position: ShapePoint;
}

/**
 * Which edge a resize moved. `top` and `bottom` arrive only from a type declaring
 * `resize: "both"`; a type that declares nothing offers the two vertical edges only, which is
 * what every user-sizable type did before height resize existed.
 */
export type ResizedSide = "left" | "right" | "top" | "bottom";

/**
 * An edge of a user-sizable element was dragged (sizing: "user"). The bounds are the whole
 * resized rectangle and `side` names the edge that moved, so a module mapping an axis - the
 * timeline's begin and end - knows which end the user meant.
 */
export interface ElementResized {
  kind: "element-resized";
  elementId: string;
  side: ResizedSide;
  bounds: { x: number; y: number; width: number; height: number };
}

/**
 * An inner boundary between two drawn segments was dragged, where the type's segments declare
 * `draggableBoundaries`. `index` is the boundary's (0 lies between segments 0 and 1), and `x` its
 * new canvas position - already snapped with the definition's `snap.x` and clamped so no segment
 * is narrower than one step. What the position MEANS is the module's.
 */
export interface SegmentBoundaryMoved {
  kind: "segment-boundary-moved";
  elementId: string;
  index: number;
  x: number;
}

/**
 * Where an element is drawn while a move, a resize or a boundary drag is still in flight - raised
 * once per applied frame, and once more with `bounds: null` when the gesture is over, after its
 * final event (`element-moved`, `element-resized`, `segment-boundary-moved`) if it had one.
 *
 * <b>The one member that is not a request.</b> Nothing is to be written for it: the gesture has
 * not ended, and a write per frame would be a history entry per frame. It is there so a module
 * can SHOW what the release would write before it writes it - the property grid's Start, Stop and
 * phase ends following the drag - and forget it when `bounds` is null.
 */
export interface ElementPreviewed {
  kind: "element-previewed";
  elementId: string;
  /** The element's drawn rectangle, in canvas units, or null when the gesture is over. */
  bounds: { x: number; y: number; width: number; height: number } | null;
  /** The x of each inner segment boundary as drawn, for a type that declares segments. */
  boundaries?: readonly number[];
}

/**
 * A connect gesture released over empty canvas, where the relation declares that release
 * meaningful (`emptyRelease: "complete"`) - the create-and-relate gesture. Under the default
 * the release raises nothing at all; this member exists only for definitions that opt in.
 */
export interface ConnectionReleasedOnEmpty {
  kind: "connection-released-on-empty";
  relationType: string;
  sourceElementId: string;
  sourceAnchor?: string;
  /** Where the gesture ended, in canvas units. */
  position: ShapePoint;
}

/**
 * A connect gesture completed over a target an endpoint constraint admits (Requirement 5.4).
 * The anchors name what the gesture actually attached to; `undefined` means edge attachment.
 */
export interface ConnectionDrawn {
  kind: "connection-drawn";
  /** The relation type drawn - a definition `relationTypes` id (Requirement 5.5). */
  relationType: string;
  sourceElementId: string;
  targetElementId: string;
  sourceAnchor?: string;
  targetAnchor?: string;
  /**
   * Where the gesture started and ended along an edge, for an end whose element declares
   * `{ kind: "along" }` anchors. Absent for every other end, which attaches as before.
   */
  sourceAttachment?: EdgeAttachment;
  targetAttachment?: EdgeAttachment;
}

/** A delete gesture reached a connection. */
export interface ConnectionDeleted {
  kind: "connection-deleted";
  connectionId: string;
}

/**
 * One end of a connection was dragged along the edge it is attached to, where the relation
 * declares `movableEnds`. The attachment is the whole new one - same edge, possibly another
 * segment - measured exactly as a connect gesture measures its ends.
 */
export interface ConnectionEndMoved {
  kind: "connection-end-moved";
  connectionId: string;
  end: "source" | "target";
  attachment: EdgeAttachment;
}

/** Waypoints or control points were dragged on an adjustable route (Requirement 3.5). */
export interface ConnectionAdjusted {
  kind: "connection-adjusted";
  connectionId: string;
  waypoints: readonly ShapePoint[];
}

/**
 * What is selected on the canvas. **Set-shaped from the start** and constrained to one
 * member until `multi-select` lands (Requirement 7.4) - the canvas never puts a second item
 * in today, so adopting the set later changes the canvas, not the modules reading this.
 */
export type DiagramSelection = readonly SelectedItem[];

export interface SelectedItem {
  kind: "element" | "connection";
  id: string;
}

export interface SelectionChanged {
  kind: "selection-changed";
  selection: DiagramSelection;
}

/**
 * A label edit committed in the shared inline editor (Requirement 6.1). A request like every
 * other member: the text on screen changes when the module's model does, not before.
 */
export interface LabelCommitRequested {
  kind: "label-commit-requested";
  target: SelectedItem;
  value: string;
}

/** The visible rectangle, in diagram coordinates. */
export interface DiagramViewport {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Panning or zooming settled on a new viewport. */
export interface ViewChanged {
  kind: "view-changed";
  viewport: DiagramViewport;
}

/**
 * A declared action fired - by its shortcut, its menu entry, or a canvas gesture.
 *
 * <b>The action arrives by ID, never as a keystroke.</b> That is the whole point of task 5: nine
 * canvases synthesise `{ key: "Delete", ... }` today to say "delete this", and a module
 * manufacturing a fake key event to name an action is the sharpest evidence available that the
 * action had nowhere to be declared. Only the handler for this event stays imperative.
 */
export interface ActionInvoked {
  kind: "action-invoked";
  actionId: string;
  targetKind: "element" | "connection" | "canvas";
  targetId?: string;
}

/**
 * The backend refused an action run from the shared context menu, with the message to show.
 *
 * <b>The one member that is not a request.</b> Where a canvas owns its selection
 * (`DiagramCanvas`'s `source`), it also runs the shared menu's actions against the backend, so
 * the refusal comes back to the library rather than to the module. The library has no place of
 * its own to show it and each module already has one - its rejection line - so the library
 * hands the message on (centralized-selection, design A). Showing a refused action is event
 * handling for an action, which the declarative rule leaves to the module.
 */
export interface ActionRefused {
  kind: "action-refused";
  actionId: string;
  message: string;
}

/** The user switched between the definition's allowed layout modes (Requirement 8.2). */
export interface LayoutModeChanged {
  kind: "layout-mode-changed";
  mode: string;
}

/**
 * One optional handler per event kind a MODULE answers - the shape a module hands to the canvas.
 * Optional, because a read-only diagram legitimately answers nothing: an unhandled request is a
 * gesture the module chose to ignore, not an error.
 *
 * <b>`selection-changed` is not among them.</b> Selection is the library's: it reads the
 * backend's, pushes a press's and wires the shared menu, and a module writes none of it
 * (centralized-selection Requirement 1). So a module cannot name `onSelectionChanged` here - a
 * handler map that tries is a type error, which is what keeps selection glue from coming back.
 */
export type DiagramEventHandlers = {
  [Kind in ModuleEventKind as EventName<Kind>]?: (event: Extract<DiagramEvent, { kind: Kind }>) => void;
};

/** Every event kind but the one the library keeps for itself. */
type ModuleEventKind = Exclude<DiagramEvent["kind"], "selection-changed">;

/**
 * Every event kind's handler: the module's, plus the library's own `selection-changed`, which the
 * canvas raises to the library's selection wrapper and to nobody else. <b>Library-internal.</b>
 */
export type LibraryEventHandlers = {
  [Kind in DiagramEvent["kind"] as EventName<Kind>]?: (event: Extract<DiagramEvent, { kind: Kind }>) => void;
};

/** `"element-dropped"` becomes `onElementDropped`, and so on for every member. */
type EventName<Kind extends string> = `on${PascalCase<Kind>}`;

type PascalCase<Value extends string> = Value extends `${infer Head}-${infer Tail}`
  ? `${Capitalize<Head>}${PascalCase<Tail>}`
  : Capitalize<Value>;

/**
 * Routes one event to its handler, if the module supplied one. The canvas calls this and
 * nothing else with an event, which is what keeps "every event is a request" true by
 * construction - there is no second path on which the canvas could act on its own raise.
 */
export function dispatchDiagramEvent(handlers: LibraryEventHandlers, event: DiagramEvent): void {
  const name = `on${event.kind.replace(/(^|-)([a-z])/g, (_, __, letter: string) => letter.toUpperCase())}` as keyof LibraryEventHandlers;
  const handler = handlers[name] as ((raised: DiagramEvent) => void) | undefined;
  handler?.(event);
}
