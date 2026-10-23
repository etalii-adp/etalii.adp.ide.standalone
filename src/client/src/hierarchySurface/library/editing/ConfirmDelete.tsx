import { ConfirmDialog } from "@client/components/ConfirmDialog";
import type { HierarchySurfaceModel } from "../api/hierarchySurfaceModel";
import type { HierarchyIndex } from "../layout";

export interface ConfirmDeleteProps {
  /** The question the definition words, or empty while nothing is being deleted. */
  question: string;
  onConfirm: () => void;
  onCancel: () => void;
}

/** The question a delete asks first, when something concerns the node (FR-014): cancel is the default. */
export function ConfirmDelete({ question, onConfirm, onCancel }: ConfirmDeleteProps) {
  return (
    <ConfirmDialog
      open={question !== ""}
      icon="mdi-delete-outline"
      title="Delete"
      message={question}
      confirmLabel="Delete"
      confirmColor="danger"
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

/**
 * How much concerns a node a delete would take (the count of a delete's `confirm`): what is nested directly in it, the
 * notes about it or naming it, and the other nodes whose texts name it. A note or a node counts once, however often
 * it names the node; the names are the model's references, so the page needs no syntax of a designer's own.
 */
export function deleteCount(model: HierarchySurfaceModel, index: HierarchyIndex, nodeId: string): number {
  const names = (references: readonly { targetId: string }[]) => references.some((reference) => reference.targetId === nodeId);
  const children = index.children.get(nodeId)?.length ?? 0;
  const notes = model.notes.filter((note) => note.aboutId === nodeId || names(note.references)).length;
  const texts = model.nodes.filter((node) => node.id !== nodeId && names(node.references)).length;
  return children + notes + texts;
}
