import type { ContextMenuGroup, ContextMenuItem } from "../../../shell/context/ContextMenu";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn } from "../api/tableModel";
import { kindOf, type ColumnAction, type TableDefinition } from "../definition/tableDefinition";

/**
 * A column's menu, as data: which entries the module's definition offers, what each is called
 * for this column, and the gesture it raises. Plain functions, so every entry is a line in a
 * test and the menu component only has to show them.
 */

/** A column's narrowest width, in pixels: wide enough for its icon and a few letters. */
export const MINIMUM_COLUMN_WIDTH = 60;

/**
 * The types a new property's panel lists: every kind that may be added, once under its own name
 * or once per entry it is offered under, each with the settings a property made from it starts with.
 */
export function offeredKinds(definition: TableDefinition): { id: string; kind: string; label: string; icon: string; settings: Readonly<Record<string, string>> }[] {
  return addableKinds(definition).flatMap(({ kind, label, icon }) => {
    const offers = kindOf(definition, kind).offeredAs ?? [];
    return offers.length === 0 ? [{ id: kind, kind, label, icon, settings: {} }] : offers.map((offer, index) => ({ id: `${kind}:${index}`, kind, ...offer }));
  });
}

/** The kinds a new column, or a column changing its type, may be given: all but those the definition holds back. */
export function addableKinds(definition: TableDefinition): { kind: string; label: string; icon: string }[] {
  return Object.entries(definition.kinds)
    .filter(([, kind]) => kind.addable !== false)
    .map(([name, kind]) => ({ kind: name, label: kind.label, icon: kind.icon }));
}

/** The kind a column added beside another starts as: the definition's choice, else the first it lets be added. */
export function defaultKind(definition: TableDefinition): string {
  return definition.defaultKind ?? addableKinds(definition)[0]?.kind ?? "";
}

/**
 * Where a dragged column lands: its place in the new order, from where the pointer was let go
 * and where the columns are. A column lands after every other column whose middle the pointer
 * has passed.
 */
export function dropIndex(from: number, pointerX: number, columns: readonly { left: number; right: number }[]): number {
  return columns.filter((column, index) => index !== from && (column.left + column.right) / 2 < pointerX).length;
}

/** A width a drag may leave a column with: whole pixels, and never narrower than the minimum. */
export function resizedWidth(start: number, dx: number): number {
  return Math.max(MINIMUM_COLUMN_WIDTH, Math.round(start + dx));
}

/** The order the menu shows its entries in, and where a line separates them. */
const MENU_ORDER: readonly (readonly ColumnAction[])[] = [
  ["rename", "changeType", "options", "relation"],
  ["filter", "sortAscending", "sortDescending", "group", "nest"],
  ["hide", "wrap", "freeze"],
  ["insertLeft", "insertRight", "duplicate", "delete"],
];

const TITLE_STAYS = "The column that names a row is always there.";

/** The setting a column names the other side of its two-way relation under, and a delete says what becomes of that side under. */
export const OTHER_SIDE = "otherSide";

/** The setting a column says under that its rows can be nested by it, and the one it says under that they are. */
const NESTABLE = "nestable";
const NESTS = "parent";

export interface ColumnMenuHandlers {
  raise: (gesture: TableGesture) => void;
  /** Renaming is done in the header itself; the menu only starts it. */
  startRename: () => void;
  /** A column inserted beside this one is asked for in a panel, name first; the menu only opens it. Left out, the column is made at once under its type's name. */
  insertBeside?: (side: "left" | "right") => void;
  /** A relation's limit and its other side are edited in a panel of their own; the menu only opens it. Left out, the menu has no such entry. */
  editRelation?: () => void;
  /** True for the column the view's columns are frozen up to: its entry then unfreezes them. */
  frozenUpToThis?: boolean;
  /** A column's options are edited in a panel of their own; the menu only opens it. Left out, the menu has no such entry. */
  editOptions?: () => void;
}

/** The menu of one column: the entries the definition offers, grouped, each bound to its gesture. */
export function columnMenuGroups(column: TableColumn, definition: TableDefinition, { raise, startRename, editOptions, editRelation, insertBeside, frozenUpToThis = false }: ColumnMenuHandlers): ContextMenuGroup[] {
  const offered = new Set(definition.columnActions ?? []);
  // Only a column whose values are chosen from options has options to edit.
  const editor = kindOf(definition, column.kind).editor;
  if (editOptions === undefined || (editor !== "option" && editor !== "options")) {
    offered.delete("options");
  }
  // Only a relation that holds its own values has a limit and another side to edit: the side another table shows here is edited there.
  if (editRelation === undefined || kindOf(definition, column.kind).needsTarget !== true || column.settings.computed === "true") {
    offered.delete("relation");
  }
  // Only a column that says its rows can be nested by it has the entry that does so.
  if (column.settings[NESTABLE] !== "true") {
    offered.delete("nest");
  }
  const nests = column.settings[NESTS] === "true";
  const entry = (action: ColumnAction): ContextMenuItem => {
    switch (action) {
      case "rename":
        return { id: action, label: "Rename", icon: "mdi-pencil-outline", onSelect: startRename };
      case "changeType":
        return {
          id: action,
          label: "Change type",
          icon: "mdi-swap-horizontal",
          items: [
            // A kind that relates to another table is given to a new property only: nothing is changed into it.
            addableKinds(definition).filter((kind) => kindOf(definition, kind.kind).needsTarget !== true).map((kind) => ({
              id: `changeType:${kind.kind}`,
              label: kind.label,
              icon: kind.icon,
              disabled: kind.kind === column.kind,
              disabledReason: kind.kind === column.kind ? "It is this type already." : undefined,
              onSelect: () => raise({ kind: "setColumnType", columnId: column.id, settings: { type: kind.kind } }),
            })),
          ],
        };
      case "relation":
        return { id: action, label: "Edit relation", icon: "mdi-arrow-top-right", onSelect: () => editRelation?.() };
      case "options":
        return { id: action, label: "Edit options", icon: "mdi-format-list-bulleted-square", onSelect: () => editOptions?.() };
      case "filter":
        return { id: action, label: "Filter", icon: "mdi-filter-outline", onSelect: () => raise({ kind: "addFilter", columnId: column.id }) };
      case "sortAscending":
        return { id: action, label: "Sort ascending", icon: "mdi-sort-ascending", onSelect: () => raise({ kind: "addSort", columnId: column.id, settings: { direction: "ascending" } }) };
      case "sortDescending":
        return { id: action, label: "Sort descending", icon: "mdi-sort-descending", onSelect: () => raise({ kind: "addSort", columnId: column.id, settings: { direction: "descending" } }) };
      case "group":
        return { id: action, label: "Group", icon: "mdi-format-list-group", onSelect: () => raise({ kind: "groupBy", columnId: column.id }) };
      case "hide":
        return {
          id: action,
          label: "Hide in view",
          icon: "mdi-eye-off-outline",
          disabled: column.isTitle,
          disabledReason: column.isTitle ? TITLE_STAYS : undefined,
          onSelect: () => raise({ kind: "hideColumn", columnId: column.id }),
        };
      case "wrap":
        return {
          id: action,
          label: column.wraps ? "Do not wrap" : "Wrap text",
          icon: "mdi-wrap",
          onSelect: () => raise({ kind: "setColumnWrap", columnId: column.id, settings: { wrap: column.wraps ? "false" : "true" } }),
        };
      case "freeze":
        // As in Notion: the columns up to and including this one stay in sight when the table scrolls sideways.
        return {
          id: action,
          label: frozenUpToThis ? "Unfreeze columns" : "Freeze up to column",
          icon: frozenUpToThis ? "mdi-pin-off-outline" : "mdi-pin-outline",
          onSelect: () => raise(frozenUpToThis ? { kind: "freezeColumns" } : { kind: "freezeColumns", columnId: column.id }),
        };
      case "nest":
        return {
          id: action,
          label: nests ? "Do not nest rows by this" : "Nest rows by this",
          icon: "mdi-file-tree-outline",
          onSelect: () => raise({ kind: "setColumnParent", columnId: column.id, settings: { parent: nests ? "false" : "true" } }),
        };
      case "insertLeft":
        return { id: action, label: "Insert left", icon: "mdi-table-column-plus-before", onSelect: () => (insertBeside === undefined ? raise({ kind: "addColumn", targetId: column.id, settings: { side: "left", type: defaultKind(definition) } }) : insertBeside("left")) };
      case "insertRight":
        return { id: action, label: "Insert right", icon: "mdi-table-column-plus-after", onSelect: () => (insertBeside === undefined ? raise({ kind: "addColumn", targetId: column.id, settings: { side: "right", type: defaultKind(definition) } }) : insertBeside("right")) };
      case "duplicate":
        return { id: action, label: "Duplicate", icon: "mdi-content-duplicate", onSelect: () => raise({ kind: "duplicateColumn", columnId: column.id }) };
      case "delete": {
        // One side of a two-way relation is not deleted on one press: its author chooses what becomes of the other side.
        const otherSide = column.settings[OTHER_SIDE];
        if (otherSide !== undefined && otherSide !== "") {
          return {
            id: action,
            label: "Delete",
            icon: "mdi-trash-can-outline",
            items: [
              [
                { id: "delete:both", label: `Delete '${otherSide}' too`, icon: "mdi-trash-can-outline", onSelect: () => raise({ kind: "deleteColumn", columnId: column.id, settings: { [OTHER_SIDE]: "delete" } }) },
                { id: "delete:keep", label: `Keep '${otherSide}' as a one-way relation`, icon: "mdi-arrow-right-thin", onSelect: () => raise({ kind: "deleteColumn", columnId: column.id, settings: { [OTHER_SIDE]: "keep" } }) },
              ],
            ],
          };
        }
        return {
          id: action,
          label: "Delete",
          icon: "mdi-trash-can-outline",
          disabled: column.isTitle,
          disabledReason: column.isTitle ? TITLE_STAYS : undefined,
          onSelect: () => raise({ kind: "deleteColumn", columnId: column.id }),
        };
      }
    }
  };

  return MENU_ORDER.map((group) => group.filter((action) => offered.has(action)).map(entry)).filter((group) => group.length > 0);
}
