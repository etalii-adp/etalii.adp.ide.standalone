import type { LayoutDefinition, LayoutMode, ShapePoint } from "../definition/diagramDefinition";
import { rowPackedLayout } from "./rowPackedLayout";

/** What a layout pass reads: positions, sizes, and the two structures a hierarchy can hang on. */
export interface LayoutInput {
  elements: readonly LayoutElement[];
  connections: readonly { sourceId: string; targetId: string }[];
}

export interface LayoutElement {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  /** The tree parent, where the notation nests structurally. */
  parentId?: string;
}

/**
 * Where each element goes, by id, and - for a layout that sizes as well as places - the width
 * it is drawn with. `null` means: the model's own positions, untouched.
 */
export type LayoutPositions = ReadonlyMap<string, LayoutPlacement> | null;

/** One element's placement: its centre, and its width where the layout decides that too. */
export interface LayoutPlacement extends ShapePoint {
  width?: number;
}

/**
 * One layout mode behind one seam (diagram-library Requirement 8.1). A further algorithm is
 * an addition to {@link LAYOUT_ALGORITHMS}, never a change to the canvas - the canvas asks
 * for the active mode and applies whatever comes back.
 *
 * **No layout computation crosses the wire** (Requirement 8.3): everything here runs on the
 * client, only where a definition opted into the mode, and where the backend lays out today
 * the mode is manual/external and these functions never run.
 */
export interface LayoutAlgorithm {
  readonly mode: LayoutMode;
  place(input: LayoutInput, definition: LayoutDefinition): LayoutPositions;
  /**
   * The point in the model's own space that `point`, in the placed space, stands for - what a
   * drop under this layout means to a module that places by the model's positions. A layout
   * without one leaves a drop's position as the pointer's.
   */
  inverse?(point: ShapePoint, input: LayoutInput, definition: LayoutDefinition): ShapePoint;
}

/**
 * Manual/external: the model's own positions, untouched - the mode every backend-laid-out
 * diagram uses today, and the default for any mode no algorithm answers for. Returning null
 * rather than a copied map keeps "untouched" literal: there is no second position store to
 * drift from the model.
 */
export const manualLayout: LayoutAlgorithm = {
  mode: "manual",
  place: () => null,
};

const TREE_MAIN_GAP = 60;
const TREE_CROSS_GAP = 20;

/**
 * A directional hierarchy: children fan out from their parent along the declared direction,
 * each subtree given the cross-axis room its leaves need - the shape a mindmap draws. The
 * hierarchy hangs on `parentId` where the model states one, and on the connections
 * (source as parent) otherwise, so both structural and drawn trees lay out.
 */
export const treeLayout: LayoutAlgorithm = {
  mode: "tree",
  place: (input, definition) => {
    const direction = definition.treeDirection ?? "left-to-right";
    const parents = new Map<string, string>();
    for (const element of input.elements) {
      if (element.parentId !== undefined) {
        parents.set(element.id, element.parentId);
      }
    }
    if (parents.size === 0) {
      for (const connection of input.connections) {
        if (!parents.has(connection.targetId)) {
          parents.set(connection.targetId, connection.sourceId);
        }
      }
    }

    const children = new Map<string, LayoutElement[]>();
    const byId = new Map(input.elements.map((element) => [element.id, element]));
    const roots: LayoutElement[] = [];
    for (const element of input.elements) {
      const parent = parents.get(element.id);
      if (parent !== undefined && byId.has(parent)) {
        const siblings = children.get(parent) ?? [];
        siblings.push(element);
        children.set(parent, siblings);
      } else {
        roots.push(element);
      }
    }

    const horizontal = direction === "left-to-right" || direction === "right-to-left";
    const mainSign = direction === "left-to-right" || direction === "top-down" ? 1 : -1;
    const crossSize = (element: LayoutElement) => (horizontal ? element.height : element.width);
    const mainSize = (element: LayoutElement) => (horizontal ? element.width : element.height);

    /** The cross-axis room a subtree needs: its own size, or its children's sum, whichever is larger. */
    const spanOf = (element: LayoutElement): number => {
      const kids = children.get(element.id) ?? [];
      if (kids.length === 0) {
        return crossSize(element);
      }
      const kidsSpan = kids.reduce((sum, kid) => sum + spanOf(kid), 0) + TREE_CROSS_GAP * (kids.length - 1);
      return Math.max(crossSize(element), kidsSpan);
    };

    const positions = new Map<string, ShapePoint>();
    const placeSubtree = (element: LayoutElement, main: number, crossStart: number) => {
      const span = spanOf(element);
      const centreCross = crossStart + span / 2;
      positions.set(element.id, horizontal ? { x: main, y: centreCross } : { x: centreCross, y: main });

      const kids = children.get(element.id) ?? [];
      let cursor = crossStart + (span - childrenSpan(element)) / 2;
      for (const kid of kids) {
        placeSubtree(kid, main + mainSign * (mainSize(element) / 2 + TREE_MAIN_GAP + mainSize(kid) / 2), cursor);
        cursor += spanOf(kid) + TREE_CROSS_GAP;
      }
    };

    const childrenSpan = (element: LayoutElement): number => {
      const kids = children.get(element.id) ?? [];
      return kids.length === 0 ? 0 : kids.reduce((sum, kid) => sum + spanOf(kid), 0) + TREE_CROSS_GAP * (kids.length - 1);
    };

    let rootCursor = 0;
    for (const root of roots) {
      placeSubtree(root, 0, rootCursor);
      rootCursor += spanOf(root) + TREE_CROSS_GAP * 2;
    }

    return positions;
  },
};

/**
 * The registered algorithms. Horizontal-flow, vertical-flow and layered-graph are declared
 * in the schema and land here as `diagram-library-adoption` reaches a module needing each -
 * an addition to this list, no change anywhere else. A mode with no entry lays out as
 * manual: the honest fallback, because inventing placements for an unimplemented mode would
 * be worse than leaving the model's own.
 */
export const LAYOUT_ALGORITHMS: readonly LayoutAlgorithm[] = [manualLayout, treeLayout, rowPackedLayout];

export function layoutAlgorithmFor(mode: LayoutMode): LayoutAlgorithm | undefined {
  return LAYOUT_ALGORITHMS.find((algorithm) => algorithm.mode === mode);
}
