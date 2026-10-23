import type { TableGesture } from "@client/table/library/api/tableEvents";
import type { TableColumn } from "@client/table/library/api/tableModel";
import { USES_PREFIX } from "@client/table/library/header/OptionsEditor";
import type { HierarchySurfaceNode, HierarchySurfaceOption } from "../api/hierarchySurfaceModel";

/** CEL's `lowerAscii`, as the specification compares a label's values with its members. */
export const lowerAscii = (text: string) => text.replace(/[A-Z]/g, (letter) => letter.toLowerCase());

/** The values of `label` that `node` carries. */
export function valuesOf(node: HierarchySurfaceNode, label: string): readonly string[] {
  return node.labels.find((values) => values.label === label)?.values ?? [];
}

/** What a label's list is edited by in the table's options editor: an operation for each change, and their params. */
export interface ListOperations {
  rename: string;
  recolour: string;
  move: string;
  delete: string;
  /** A new member of the list, made from the editor's add row; absent when a member is made by carrying it. */
  add?: string;
  params: { name: string; colour: string; after: string };
}

/** The target an operation on the document itself names, as the definition's `for: "document"` has it. */
export const DOCUMENT_TARGET = "document";

/**
 * A label's list as the table's options editor edits a column's: its members in their colours, and under the setting the
 * editor reads, how many nodes carry each, which a delete asks about.
 */
export function listColumn(label: string, name: string, options: readonly HierarchySurfaceOption[], nodes: readonly HierarchySurfaceNode[]): TableColumn {
  return {
    id: label,
    name,
    kind: "options",
    options: options.map((option) => ({ id: option.id, name: option.name, color: option.colour })),
    width: 0,
    visible: true,
    isTitle: false,
    wraps: false,
    settings: Object.fromEntries(
      options.map((option) => [`${USES_PREFIX}${option.id}`, String(nodes.filter((node) => valuesOf(node, label).some((value) => lowerAscii(value) === lowerAscii(option.name))).length)]),
    ),
  };
}

/** One gesture for each change the options editor raises, naming the member it changes. */
export function listRaise(operations: ListOperations, options: readonly HierarchySurfaceOption[], onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown) {
  const { params } = operations;
  return (gesture: TableGesture) => {
    const target = gesture.targetId ?? "";
    switch (gesture.kind) {
      case "addOption":
        if (operations.add !== undefined) {
          onGesture(operations.add, DOCUMENT_TARGET, { [params.name]: gesture.values?.[0] ?? "" });
        }
        break;
      case "renameOption":
        onGesture(operations.rename, target, { [params.name]: gesture.values?.[0] ?? "" });
        break;
      case "recolourOption":
        onGesture(operations.recolour, target, { [params.colour]: gesture.settings?.colour ?? "" });
        break;
      case "moveOption": {
        // A place in the list is the member it goes after, among those that stay where they are.
        const others = options.filter((option) => option.id !== target);
        const at = gesture.index ?? 0;
        onGesture(operations.move, target, { [params.after]: at === 0 ? "" : (others[at - 1]?.id ?? "") });
        break;
      }
      case "deleteOption":
        onGesture(operations.delete, target, {});
        break;
    }
  };
}
