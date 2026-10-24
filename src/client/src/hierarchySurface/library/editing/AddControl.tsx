import type { HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceAdd } from "../definition/hierarchySurfaceDefinition";

export interface AddControlProps {
  /** What can be added here, each its own button, in this order. */
  adds: readonly HierarchySurfaceAdd[];
  /** The number of the level the nodes go into, for a screen reader; empty at the end of the page. */
  within: string;
  onAdd: (add: HierarchySurfaceAdd) => void;
}

/**
 * The place a node is added: at the end of every level's content, and at the end of the page (FR-012), each control
 * its icon alone with its label as the tooltip. It asks nothing:
 * the node is added at once under the operation's own title, and the page opens that title for typing, as the table's
 * `NewRow` adds a row without a dialog.
 */
export function AddControl({ adds, within, onAdd }: AddControlProps) {
  return (
    <div className="hs-add" role="group" aria-label={within === "" ? "Add" : `Add to ${within}`}>
      {adds.map((add) => (
        <button
          key={add.operation}
          type="button"
          className="hs-add-button"
          aria-label={within === "" ? add.label : `${add.label} in ${within}`}
          title={add.label}
          onClick={(event) => {
            event.stopPropagation();
            onAdd(add);
          }}
        >
          <span className={`mdi ${add.icon}`} aria-hidden="true" />
        </button>
      ))}
    </div>
  );
}

/**
 * The node an add brought, between the page before it and the page after: the one node of `after` that is not in
 * `before`. A node without a number of its own is named by its line, so an edit that numbers a placeholder or moves
 * the lines below it gives that node a new id too; such a node is matched to the one it was by its role and title,
 * and what is left is the node the add made. Undefined when the add made none.
 */
export function addedNode(before: readonly HierarchySurfaceNode[], after: readonly HierarchySurfaceNode[]): HierarchySurfaceNode | undefined {
  const present = new Set(after.map((node) => node.id));
  const known = new Set(before.map((node) => node.id));
  const renamed = before.filter((node) => !present.has(node.id));
  const candidates = after.filter((node) => !known.has(node.id));
  for (const was of renamed) {
    const at = candidates.findIndex((node) => node.role === was.role && node.title === was.title);
    if (at >= 0) {
      candidates.splice(at, 1);
    }
  }
  return candidates[0];
}
