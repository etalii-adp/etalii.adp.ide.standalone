import type { LayoutDefinition, RowPackedDeclaration, ShapePoint } from "../definition/diagramDefinition";
import type { LayoutAlgorithm, LayoutElement, LayoutInput, LayoutPlacement } from "./layoutAlgorithm";

/** One element as the packing placed it: its left edge in both spaces, and its row. */
interface Packed {
  element: LayoutElement;
  /** The left edge in the model's own space - the element's manual position. */
  manualLeft: number;
  /** The left edge the packing gave it. */
  placedLeft: number;
}

/**
 * The one packing both directions read: every element at the declared width, in the order of
 * its manual left edge, on the row its manual centre sits on.
 *
 * Elements starting at one manual x are placed as a group. The group's x is the larger of where
 * the previous group ended up and where each row it touches is free again (its last element's
 * right edge plus `gap`, plus the room the element needs before it for a label drawn to its left),
 * so a later start is never left of an earlier one, equal starts on
 * different rows share an x, and no two elements on a row come closer than `gap`. A second
 * element of the group on the same row follows the first on that row. Nothing further left
 * satisfies all three, which is what makes the result as narrow as they allow.
 *
 * The first group keeps its own manual x, so a packed diagram starts where its manual one does.
 * The result is returned in placement order, which is also placed-left order.
 */
function pack(input: LayoutInput, declaration: RowPackedDeclaration): Packed[] {
  const byStart = input.elements
    .map((element) => ({ element, manualLeft: element.x - element.width / 2 }))
    .sort((a, b) => a.manualLeft - b.manualLeft || (a.element.id < b.element.id ? -1 : a.element.id > b.element.id ? 1 : 0));

  const rowEnd = new Map<number, number>();
  const packed: Packed[] = [];
  let previous: number | undefined;
  for (let start = 0; start < byStart.length; ) {
    let end = start;
    while (end < byStart.length && byStart[end].manualLeft === byStart[start].manualLeft) {
      end += 1;
    }
    const group = byStart.slice(start, end);

    let groupX = previous ?? group[0].manualLeft;
    for (const { element } of group) {
      const free = rowEnd.get(element.y);
      if (free !== undefined) {
        groupX = Math.max(groupX, free + declaration.gap + (element.leading ?? 0));
      }
    }

    const placedRows = new Set<number>();
    let rightmost = groupX;
    for (const entry of group) {
      const row = entry.element.y;
      const placedLeft = placedRows.has(row) ? rowEnd.get(row)! + declaration.gap + (entry.element.leading ?? 0) : groupX;
      placedRows.add(row);
      rowEnd.set(row, placedLeft + declaration.width);
      rightmost = Math.max(rightmost, placedLeft);
      packed.push({ ...entry, placedLeft });
    }

    previous = rightmost;
    start = end;
  }
  return packed;
}

/**
 * `row-packed`: every element drawn at one declared width, packed along the rows it already
 * sits on in the order of its manual horizontal position. Built for diagrams whose x means
 * time, to be read for their order rather than their durations. A definition allowing the mode
 * without declaring `rowPacked` lays out as manual, the honest fallback.
 */
export const rowPackedLayout: LayoutAlgorithm = {
  mode: "row-packed",
  place: (input: LayoutInput, definition: LayoutDefinition) => {
    const declaration = definition.rowPacked;
    if (declaration === undefined) {
      return null;
    }
    const positions = new Map<string, LayoutPlacement>();
    for (const { element, placedLeft } of pack(input, declaration)) {
      positions.set(element.id, { x: placedLeft + declaration.width / 2, y: element.y, width: declaration.width });
    }
    return positions;
  },
  /**
   * A placed-space x read as a left edge, mapped to the manual left edge it stands for: between
   * the two placed neighbours it falls between, in proportion; beyond either end, at the same
   * distance from the nearest one; with nothing placed, unchanged. The row is kept as it is.
   */
  inverse: (point: ShapePoint, input: LayoutInput, definition: LayoutDefinition) => {
    const declaration = definition.rowPacked;
    if (declaration === undefined) {
      return point;
    }
    const packed = pack(input, declaration);
    if (packed.length === 0) {
      return point;
    }

    let before: Packed | undefined;
    let after: Packed | undefined;
    for (const entry of packed) {
      if (entry.placedLeft <= point.x) {
        before = entry;
      } else {
        after = entry;
        break;
      }
    }

    if (before === undefined) {
      return { x: after!.manualLeft - (after!.placedLeft - point.x), y: point.y };
    }
    if (after === undefined) {
      return { x: before.manualLeft + (point.x - before.placedLeft), y: point.y };
    }
    const fraction = (point.x - before.placedLeft) / (after.placedLeft - before.placedLeft);
    return { x: before.manualLeft + fraction * (after.manualLeft - before.manualLeft), y: point.y };
  },
};
