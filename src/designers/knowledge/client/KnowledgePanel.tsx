import { useCallback, useEffect, useState } from "react";
import { elementSelectionOf } from "@client/canvas/selection";
import { useTableStream } from "@client/designers/useTableStream";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { classifyLink, openLink } from "@client/shell/links/openLink";
import type { TableColumn } from "@client/table/library/api/tableModel";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import type { TableGesture } from "@client/table/library/api/tableEvents";
import { TableSurface } from "@client/table/library/TableSurface";
import { knowledgeTable } from "./knowledgeDefinition";

/**
 * A knowledge file, shown as its table. The module's whole client is this: the table's stream
 * handed to the table library with the designer's declarations. It draws nothing itself - no
 * cell, no header, no status line - so a knowledge table looks and behaves as the library's
 * tables do, and tells the shell which row is selected. That it is opening, that it cannot be opened and that an edit was refused are said
 * by the frame the shell puts around every tool, which the stream reports to.
 */
/**
 * A row that is being opened in another table: which file it is in and which row it is. The
 * table that opens the row and the table that shows it are two panels, and the shell opens a file
 * by its path only, so the row waits here for the panel of its file to take it.
 */
let rowToOpen: { file: string; rowId: string } | null = null;
const rowWaiting = new EventTarget();

/** The file a panel shows, as a relation names it: its data file, whichever of its two files the panel was opened at. */
const fileOf = (path: readonly string[]) => path.join("/").replace(/\.[^./]+$/, "");

export function KnowledgePanel({ path, entryId }: ToolContentProps) {
  const table = useTableStream(path);
  const viewId = table.model.settings.viewId;
  const { edit } = table;

  // Every edit says which view it was made in. A view's own settings are the view's, and an
  // edit of the table made while looking through a view - a row added under a filter, a column
  // hidden - means something only together with that view.
  const onGesture = useCallback((gesture: TableGesture) => edit({ ...gesture, viewId: gesture.viewId ?? viewId }), [edit, viewId]);

  // One selected row is the selection of the whole application, as an element of a diagram is: the
  // property grid then shows that row's values. Several rows, or none, are no selection of this kind.
  const { select, revealPath } = useContextConnection();
  const onSelectRows = useCallback((rowIds: readonly string[]) => select(rowIds.length === 1 ? elementSelectionOf(entryId, path, rowIds[0]!) : null), [select, entryId, path]);

  // A related row is opened where it lives: a row of this table becomes the selection, and a row of
  // another table opens that table's file, found from this one as the relation names it.
  const onOpenValue = useCallback(
    (column: TableColumn, rowId: string) => {
      const target = column.settings.targetFile ?? "";
      if (target === "" || target === ".") {
        select(elementSelectionOf(entryId, path, rowId));
      } else {
        // The row waits for its table: one that is open takes it at once, one that opens now when it has loaded.
        const file = classifyLink(target, path);
        rowToOpen = file.kind === "project" ? { file: fileOf(file.segments), rowId } : null;
        openLink(target, path, { revealPath });
        rowWaiting.dispatchEvent(new Event("row"));
      }
    },
    [select, revealPath, entryId, path],
  );

  // A row opened from another table is taken here once this table is there: it becomes the
  // selection, as if its handle had been pressed.
  const [opened, setOpened] = useState("");
  const loaded = !table.loading;
  useEffect(() => {
    const take = () => {
      if (rowToOpen !== null && loaded && rowToOpen.file === fileOf(path)) {
        const rowId = rowToOpen.rowId;
        rowToOpen = null;
        setOpened(rowId);
        select(elementSelectionOf(entryId, path, rowId));
      }
    };
    take();
    rowWaiting.addEventListener("row", take);
    return () => rowWaiting.removeEventListener("row", take);
  }, [loaded, path, entryId, select]);

  return (
    <TableSurface
      model={table.model}
      definition={knowledgeTable}
      pendingEdits={table.pendingEdits}
      onWindow={table.setWindow}
      onView={table.setView}
      onGesture={onGesture}
      selectedRow={opened}
      onSelectRows={onSelectRows}
      onOpenValue={onOpenValue}
    />
  );
}
