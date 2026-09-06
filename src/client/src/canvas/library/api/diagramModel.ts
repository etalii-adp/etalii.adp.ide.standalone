import type { ConnectionStyle, ElementStyle, ShapePoint } from "../definition/diagramDefinition";

/**
 * What a module hands the canvas to draw: elements and connections in the definition's own
 * terms, with positions. The model's truth lives behind the module - typically folded from
 * the backend's delta stream - and the canvas never changes it: every gesture becomes an
 * event, and the drawing changes when this object does (diagram-library Requirement 1.2).
 */
export interface DiagramModel {
  elements: readonly DiagramModelElement[];
  connections: readonly DiagramModelConnection[];
}

export interface DiagramModelElement {
  id: string;
  /** A definition `elementTypes` id. An element naming no declared type renders as a visible
   *  fallback box - a mapping bug shown, not hidden (design, Error Handling 1). */
  type: string;
  /** The element's centre, in canvas units. */
  x: number;
  y: number;
  /** The measured size, where the type's sizing is "model" or "user". */
  width?: number;
  height?: number;
  label?: string;
  /**
   * Where the drawn label BEGINS, in canvas units, for a notation whose labels sit at an
   * authored offset from their mark (wardley's `label [-57, 4]`). A beside-placed editable
   * label opens its editor here rather than at the shape's default gap; omitted, the default
   * gap applies. Model data, not definition data, because the offset is per element and the
   * author's.
   */
  labelAt?: ShapePoint;
  /** The enclosing element, where the notation nests - a c4 boundary, a sparql region. */
  parentId?: string;
  /** Per-element override of the type's style (Requirement 2.2). */
  style?: ElementStyle;
}

export interface DiagramModelConnection {
  id: string;
  /** A definition `relationTypes` id. */
  type: string;
  sourceId: string;
  targetId: string;
  /** The anchor names the connection attaches to; omitted means edge attachment. */
  sourceAnchor?: string;
  targetAnchor?: string;
  label?: string;
  /**
   * Extra class names for the connection's group - for kinds a definition cannot enumerate
   * as relation types, like shacl's open edge-kind strings.
   */
  className?: string;
  /** A hover tooltip for the whole connection - a wardley link's context sentence. */
  title?: string;
  /**
   * The one authored value the label DECORATES, where the drawn string is not it - c4 draws
   * "description [technology]", sometimes numbered, and its editor opens with the description
   * alone. Omitted, the editor opens with the label as drawn.
   */
  editValue?: string;
  /** The user's bends, where the type is adjustable (Requirement 3.5). */
  waypoints?: readonly ShapePoint[];
  /** Per-connection override of the type's style (Requirement 3.2). */
  style?: ConnectionStyle;
}
