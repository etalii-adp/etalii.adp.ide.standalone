import { fitToWidth, LABEL_FONT_SIZE } from "../../label/textMetrics";
import {
  resolveOneAt,
  valueAtPath,
  type BindingPath,
  type BindingSource,
  type FieldBinding,
  type PartsBinding,
  type TemplateBinding,
} from "./binding";
import type { ShapeBounds } from "./diagramDefinition";

/**
 * `compartments` - lists INSIDE an element, whose rows are things in their own right.
 *
 * A label bound to a collection already draws one line per entry, and that is all it does: its
 * lines cannot be pointed at, selected or folded away, because a line is text and has no identity
 * beneath it. A compartment's rows are the model's own child entries - a specification's tasks, a
 * location's pull requests - each with an id, so a row can be selected, opened for its menu and
 * changed without rewriting the list it is in.
 *
 * Rows may be grouped by a field, under one heading per value in a declared order; a heading shows
 * its title and how many rows it holds, and folds its rows away. Which headings are folded is the
 * MODEL's to say - the library holds no collapse state, so a fold survives a reload exactly when
 * the module stores it.
 *
 * <b>The element's height follows its rows.</b> A row is one line, shortened with an ellipsis, so
 * the height is a sum of declared line heights and needs no text measurement - which is what lets
 * `elementBounds`, a layout pass and the drawing all agree on it without asking the browser.
 *
 * Resolution is pure, like `layoutLabels`, so the acceptance can be asserted without a canvas.
 */
export interface CompartmentDeclaration {
  /** Names the compartment: the key of its one heading when it is not grouped, and part of every row's key. */
  id: string;
  /** A path to the rows: an array of objects. Anything else draws no rows. */
  rows: BindingPath;
  /** A path, rooted at a ROW, to its id. A row without one is drawn and cannot be selected. */
  rowId: BindingPath;
  /** What a row says, resolved against the row. */
  text: FieldBinding | TemplateBinding | PartsBinding;
  /** A path, rooted at a row, to a link. A row that has one draws the link symbol at its end. */
  link?: BindingPath;
  /**
   * The order of rows within a group: by the value at this path, rooted at a row, compared as
   * text. Rows without a value come after those with one, in the order the model gave them.
   * Omitted, rows keep the model's order.
   */
  orderBy?: { path: BindingPath; direction: "ascending" | "descending" };
  /**
   * One heading per value of a field, in the order written here. Omitted, the compartment is one
   * group under {@link title}.
   */
  groupBy?: {
    /** A path, rooted at a row, to the value it is grouped by. */
    path: BindingPath;
    groups: readonly CompartmentGroup[];
    /** The heading for rows whose value is none of the above; such rows are never dropped. */
    otherTitle: string;
  };
  /** The heading of an ungrouped compartment. */
  title?: string;
  /**
   * A path, rooted at the ELEMENT, to the keys of the headings that are folded: an array of
   * strings. A grouped compartment's keys are its groups' values, an ungrouped one's is its
   * {@link id}. The model resolves defaults; the library draws what it is told.
   */
  collapsed: BindingPath;
  /** Where the first heading's top sits, measured down from the element's top. */
  top: number;
  /** The height of a heading line and of a row line. */
  headingHeight: number;
  rowHeight: number;
  /** The space left below the last line, inside the element. */
  bottom: number;
  /** The inset of a heading from the element's sides, and the further indent of a row. */
  insetX: number;
  rowIndent: number;
}

/** One group of a grouped compartment. */
export interface CompartmentGroup {
  /** The field value this group collects, and the group's key. */
  value: string;
  title: string;
}

/** One heading, resolved and ready to draw. Geometry is absolute canvas units. */
export interface LaidOutHeading {
  compartmentId: string;
  /** The key the model's collapsed list names this heading by. */
  key: string;
  title: string;
  count: number;
  collapsed: boolean;
  box: ShapeBounds;
}

/** One row, resolved and ready to draw. */
export interface LaidOutRow {
  compartmentId: string;
  groupKey: string;
  /** The row's own id, or undefined for a row the model gave none. */
  id?: string;
  /** The text as drawn - shortened to fit - and in full. */
  text: string;
  fullText: string;
  link?: string;
  box: ShapeBounds;
  /** Where the text starts. */
  textX: number;
}

export interface LaidOutCompartments {
  headings: readonly LaidOutHeading[];
  rows: readonly LaidOutRow[];
  /** The bottom of the last compartment, inset included - what the element must be tall enough for. */
  bottom: number;
}

/** The width kept free at a row's end for its link symbol, whether or not this row has one. */
export const ROW_LINK_WIDTH = 16;

/**
 * Lays every declared compartment out inside `bounds`, top down, each beneath the one before.
 *
 * <b>Nothing here throws</b>, for the reason no binding does: a path that names nothing, or names
 * something that is not a list, draws no rows rather than taking the canvas down.
 */
export function layoutCompartments(
  declarations: readonly CompartmentDeclaration[] | undefined,
  source: BindingSource,
  bounds: ShapeBounds,
): LaidOutCompartments {
  const headings: LaidOutHeading[] = [];
  const rows: LaidOutRow[] = [];
  let bottom = bounds.y;
  let cursor: number | null = null;

  for (const declaration of declarations ?? []) {
    const collapsed = collapsedKeys(declaration, source);
    const groups = groupsOf(declaration, source);
    let y: number = cursor ?? bounds.y + declaration.top;
    for (const group of groups) {
      const isCollapsed = collapsed.has(group.key);
      headings.push({
        compartmentId: declaration.id,
        key: group.key,
        title: group.title,
        count: group.rows.length,
        collapsed: isCollapsed,
        box: { x: bounds.x + declaration.insetX, y, width: bounds.width - 2 * declaration.insetX, height: declaration.headingHeight },
      });
      y += declaration.headingHeight;
      if (isCollapsed) {
        continue;
      }
      for (const row of group.rows) {
        const x = bounds.x + declaration.insetX + declaration.rowIndent;
        const width = Math.max(0, bounds.width - 2 * declaration.insetX - declaration.rowIndent);
        const fullText = resolveOneAt(declaration.text, row) ?? "";
        rows.push({
          compartmentId: declaration.id,
          groupKey: group.key,
          id: textAt(row, declaration.rowId),
          text: fitToWidth(fullText, Math.max(0, width - ROW_LINK_WIDTH), LABEL_FONT_SIZE),
          fullText,
          link: declaration.link === undefined ? undefined : textAt(row, declaration.link),
          box: { x, y, width, height: declaration.rowHeight },
          textX: x,
        });
        y += declaration.rowHeight;
      }
    }
    // A compartment that drew nothing takes no room and leaves no inset behind.
    if (groups.length > 0) {
      cursor = y;
      bottom = y + declaration.bottom;
    }
  }

  return { headings, rows, bottom };
}

/**
 * How tall an element must be for its compartments, or null when it declares none or they hold
 * nothing - an element's own height then stands.
 */
export function compartmentsHeight(
  declarations: readonly CompartmentDeclaration[] | undefined,
  source: BindingSource,
): number | null {
  if (declarations === undefined || declarations.length === 0) {
    return null;
  }
  // Laid out from y = 0 at any width: heights do not depend on the width, only shortening does.
  const laidOut = layoutCompartments(declarations, source, { x: 0, y: 0, width: 0, height: 0 });
  return laidOut.headings.length === 0 ? null : laidOut.bottom;
}

/**
 * Whether one of an element's compartments holds a row with this id - folded away or not, since
 * a row that is not drawn is still in the model and may still be what is selected.
 */
export function hasRow(declarations: readonly CompartmentDeclaration[] | undefined, source: BindingSource, id: string): boolean {
  return (declarations ?? []).some((declaration) => {
    const value = valueAtPath(declaration.rows, source);
    return Array.isArray(value) && value.some((row) => textAt(row, declaration.rowId) === id);
  });
}

interface ResolvedGroup {
  key: string;
  title: string;
  rows: readonly unknown[];
}

function groupsOf(declaration: CompartmentDeclaration, source: BindingSource): ResolvedGroup[] {
  const value = valueAtPath(declaration.rows, source);
  const all = Array.isArray(value) ? (value as readonly unknown[]) : [];
  if (all.length === 0) {
    return [];
  }

  const grouping = declaration.groupBy;
  if (grouping === undefined) {
    return [{ key: declaration.id, title: declaration.title ?? "", rows: ordered(all, declaration) }];
  }

  const known = new Set(grouping.groups.map((group) => group.value));
  const groups: ResolvedGroup[] = grouping.groups.map((group) => ({
    key: group.value,
    title: group.title,
    rows: ordered(all.filter((row) => textAt(row, grouping.path) === group.value), declaration),
  }));
  const others = all.filter((row) => !known.has(textAt(row, grouping.path) ?? ""));
  if (others.length > 0) {
    groups.push({ key: "", title: grouping.otherTitle, rows: ordered(others, declaration) });
  }
  // A group with no row is not drawn.
  return groups.filter((group) => group.rows.length > 0);
}

function ordered(rows: readonly unknown[], declaration: CompartmentDeclaration): readonly unknown[] {
  const order = declaration.orderBy;
  if (order === undefined) {
    return rows;
  }
  const sign = order.direction === "ascending" ? 1 : -1;
  // Decorated with the model's own position, so rows that compare equal - and rows with no value
  // at all - keep the order they came in whatever the engine's sort does with ties.
  return rows
    .map((row, index) => ({ row, index, key: textAt(row, order.path) }))
    .sort((left, right) => {
      if (left.key === undefined || right.key === undefined) {
        return left.key === right.key ? left.index - right.index : left.key === undefined ? 1 : -1;
      }
      const compared = left.key < right.key ? -1 : left.key > right.key ? 1 : 0;
      return compared === 0 ? left.index - right.index : sign * compared;
    })
    .map((entry) => entry.row);
}

function collapsedKeys(declaration: CompartmentDeclaration, source: BindingSource): ReadonlySet<string> {
  const value = valueAtPath(declaration.collapsed, source);
  return new Set(Array.isArray(value) ? value.filter((key): key is string => typeof key === "string") : []);
}

/** The text at a path rooted at a row; undefined for anything missing or empty. */
function textAt(row: unknown, path: BindingPath): string | undefined {
  const text = resolveOneAt({ path }, row);
  return text === null || text === "" ? undefined : text;
}
