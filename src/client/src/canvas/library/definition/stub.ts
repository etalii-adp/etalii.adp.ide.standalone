import type { ShapeBounds, ShapePoint, StubDeclaration } from "./diagramDefinition";

/** How far off its line a stub's label sits where it does not say: where a line's label sits. */
const STUB_LABEL_DISTANCE = 6;

/** How far past the source a stub's label begins where it does not say. */
const STUB_LABEL_INSET = 8;

/** The height of a connection's text (`.canvas-connection text`), by which a label below a downward stub clears its end. */
const STUB_TEXT_HEIGHT = 11;

/** Where a stub is drawn: its line, and where its label's text is anchored. */
export interface StubGeometry {
  from: ShapePoint;
  to: ShapePoint;
  label: { x: number; y: number; anchor: "start" | "middle" | "end" };
}

/**
 * A stub's line out of the middle of one side of its source's bounds (corner-based, in canvas
 * units), and its label's anchor. Sideways, the label runs along the line from its inset, above or
 * below it; upwards or downwards, it is centred beyond the free end, where the line leaves room.
 */
export function stubGeometry(source: ShapeBounds, stub: StubDeclaration): StubGeometry {
  const centreX = source.x + source.width / 2;
  const centreY = source.y + source.height / 2;
  const distance = stub.label?.distance ?? STUB_LABEL_DISTANCE;
  const inset = stub.label?.inset ?? STUB_LABEL_INSET;
  const off = stub.label?.side === "below" ? distance + STUB_TEXT_HEIGHT : -distance;

  switch (stub.side) {
    case "right": {
      const from = { x: source.x + source.width, y: centreY };
      return { from, to: { x: from.x + stub.length, y: centreY }, label: { x: from.x + inset, y: centreY + off, anchor: "start" } };
    }
    case "left": {
      const from = { x: source.x, y: centreY };
      return { from, to: { x: from.x - stub.length, y: centreY }, label: { x: from.x - inset, y: centreY + off, anchor: "end" } };
    }
    case "top": {
      const from = { x: centreX, y: source.y };
      const to = { x: centreX, y: from.y - stub.length };
      return { from, to, label: { x: centreX, y: to.y - distance, anchor: "middle" } };
    }
    case "bottom": {
      const from = { x: centreX, y: source.y + source.height };
      const to = { x: centreX, y: from.y + stub.length };
      return { from, to, label: { x: centreX, y: to.y + distance + STUB_TEXT_HEIGHT, anchor: "middle" } };
    }
  }
}
