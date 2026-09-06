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
  /** The user's bends, where the type is adjustable (Requirement 3.5). */
  waypoints?: readonly ShapePoint[];
  /** Per-connection override of the type's style (Requirement 3.2). */
  style?: ConnectionStyle;
}
