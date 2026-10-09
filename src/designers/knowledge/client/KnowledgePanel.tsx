import { useCallback } from "react";
import { useTableStream } from "@client/designers/useTableStream";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import type { TableGesture } from "@client/table/library/api/tableEvents";
import { TableSurface } from "@client/table/library/TableSurface";
import { knowledgeTable } from "./knowledgeDefinition";

/**
 * A knowledge file, shown as its table. The module's whole client is this: the table's stream
 * handed to the table library with the designer's declarations. It draws nothing itself - no
 * cell, no header, no status line - so a knowledge table looks and behaves as the library's
 * tables do. That it is opening, that it cannot be opened and that an edit was refused are said
 * by the frame the shell puts around every tool, which the stream reports to.
 */
export function KnowledgePanel({ path }: ToolContentProps) {
  const table = useTableStream(path);
  const viewId = table.model.settings.viewId;
  const { edit } = table;

  // Every edit says which view it was made in. A view's own settings are the view's, and an
  // edit of the table made while looking through a view - a row added under a filter, a column
  // hidden - means something only together with that view.
  const onGesture = useCallback((gesture: TableGesture) => edit({ ...gesture, viewId: gesture.viewId ?? viewId }), [edit, viewId]);

  return (
    <TableSurface
      model={table.model}
      definition={knowledgeTable}
      pendingEdits={table.pendingEdits}
      onWindow={table.setWindow}
      onView={table.setView}
      onGesture={onGesture}
    />
  );
}
