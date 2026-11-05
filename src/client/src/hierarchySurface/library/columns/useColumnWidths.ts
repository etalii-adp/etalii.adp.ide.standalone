import { useCallback, useEffect, useState } from "react";
import type { HierarchySurfaceColumnWidth } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceWidths } from "../definition/hierarchySurfaceDefinition";
import { DOCUMENT_TARGET } from "../labels/listEditing";
import { LEVEL_WIDTH } from "../layout";

export interface ColumnWidths {
  /** The width of the column of levels at `level`, 1 for the top ones: as it is being dragged, as kept, or the page's own. */
  widthOf: (level: number) => number;
  /** Shows the column at `level` at `width` while its edge is dragged, without sending anything. */
  preview: (level: number, width: number) => void;
  /** Keeps `width` for the column at `level`: its first width, or a change of the kept one. */
  commit: (level: number, width: number) => void;
  /** Gives the column at `level` the page's own width again, when it has a kept one. */
  reset: (level: number) => void;
  /** The range a width is held to. */
  min: number;
  max: number;
}

/**
 * The widths of the columns of levels (DESL section 7.7.1): those the document keeps, the one being dragged, and the
 * page's own for the rest. A drag shows its width at once and sends one gesture when it ends; the width it showed stays
 * until the document's next model, or until the gesture is refused. Each column's is its own, so changing one while
 * another's gesture is on its way leaves the other's as it was sent.
 */
export function useColumnWidths(
  kept: readonly HierarchySurfaceColumnWidth[],
  widths: HierarchySurfaceWidths | undefined,
  onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown,
): ColumnWidths {
  // The widths shown before the document has them, by depth.
  const [changing, setChanging] = useState<ReadonlyMap<number, number>>(new Map());
  // The model that answers a gesture holds the width it kept; what the drag showed is then the document's.
  useEffect(() => setChanging(new Map()), [kept]);
  const show = useCallback((level: number, width: number | null) => setChanging((current) => withWidth(current, level, width)), []);
  const min = widths?.min ?? LEVEL_WIDTH;
  const max = widths?.max ?? LEVEL_WIDTH;

  const widthOf = useCallback(
    (level: number) => changing.get(level) ?? kept.find((column) => column.level === level)?.width ?? LEVEL_WIDTH,
    [changing, kept],
  );
  const preview = useCallback((level: number, width: number) => show(level, clamp(width, min, max)), [show, min, max]);
  const send = useCallback(
    (level: number, operation: string, targetId: string, params: Readonly<Record<string, string>>) => {
      // A refusal leaves the document as it was, so the column goes back to the width it keeps.
      void Promise.resolve(onGesture(operation, targetId, params)).then((refusal) => {
        if (typeof refusal === "string" && refusal !== "") {
          show(level, null);
        }
      });
    },
    [onGesture, show],
  );
  const commit = useCallback(
    (level: number, width: number) => {
      const stored = kept.find((column) => column.level === level);
      const wanted = clamp(Math.round(width), min, max);
      if (widths === undefined || wanted === (stored?.width ?? LEVEL_WIDTH)) {
        show(level, null);
        return;
      }
      show(level, wanted);
      if (stored === undefined) {
        send(level, widths.operations.add, DOCUMENT_TARGET, { [widths.params.level]: String(level), [widths.params.width]: String(wanted) });
      } else {
        send(level, widths.operations.set, stored.id, { [widths.params.width]: String(wanted) });
      }
    },
    [kept, widths, min, max, show, send],
  );
  const reset = useCallback(
    (level: number) => {
      const stored = kept.find((column) => column.level === level);
      if (widths !== undefined && stored !== undefined) {
        show(level, LEVEL_WIDTH);
        send(level, widths.operations.reset, stored.id, {});
      }
    },
    [kept, widths, show, send],
  );
  return { widthOf, preview, commit, reset, min, max };
}

/** `widths` with `level` shown at `width`, or as the document keeps it for null. */
function withWidth(widths: ReadonlyMap<number, number>, level: number, width: number | null): ReadonlyMap<number, number> {
  const next = new Map(widths);
  if (width === null) {
    next.delete(level);
  } else {
    next.set(level, width);
  }
  return next;
}

function clamp(width: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, width));
}
