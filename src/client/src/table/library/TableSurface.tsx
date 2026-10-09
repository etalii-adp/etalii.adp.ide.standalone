import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import type { TableEvents, TableGesture } from "./api/tableEvents";
import { cellText, type TableCell, type TableColumn, type TableModel, type TableRow } from "./api/tableModel";
import { CellEditor, type AfterEdit } from "./cells/CellEditor";
import { keyOutcome, tabTarget, type CellPosition } from "./cells/keyboard";
import { OptionTag } from "./cells/OptionTag";
import { DEFAULT_COLUMN_WIDTH, kindOf, type TableDefinition } from "./definition/tableDefinition";
import { AddColumn } from "./header/AddColumn";
import { ColumnHeader } from "./header/ColumnHeader";
import { GroupHeading } from "./rows/GroupHeading";
import { NestedRowToggle } from "./rows/NestedRow";
import { NewRow } from "./rows/NewRow";
import { DEFAULT_MARGIN, DEFAULT_ROW_HEIGHT, indexesOf, sameWindow, spacersOf, windowOf, type RowWindow } from "./rows/windowing";
import { ViewBar } from "./views/ViewBar";
import { ViewTabs } from "./views/ViewTabs";
import "./table.css";

export interface TableSurfaceProps extends TableEvents {
  model: TableModel;
  definition: TableDefinition;
  /**
   * The height the rows are seen through when the surface cannot measure it - before layout, and
   * under a test renderer that lays nothing out. In pixels.
   */
  fallbackViewportHeight?: number;
}

/**
 * A cell's padding and the step a nested row is set in by, in pixels. The padding is the
 * stylesheet's too (`--table-cell-padding`): a nested row's first cell adds its steps to it.
 */
const CELL_PADDING = 8;
const INDENT = 20;

/** The viewport assumed until the surface has been laid out, in pixels: about a screen of rows. */
const FALLBACK_VIEWPORT_HEIGHT = 660;

/**
 * A table: a header row, and the rows of the window in sight.
 *
 * <b>It draws only the window.</b> Every row has one height, so which rows are in sight follows
 * from the scroll offset (`rows/windowing.ts`); the rows before and after are two spacers, and a
 * table of ten thousand rows is a few dozen elements. The window is reported through `onWindow`
 * whenever it becomes other rows, and a line the model does not hold yet is drawn as an empty
 * row of the same height, so the scroll bar never jumps while rows arrive.
 *
 * <b>It is a grid to assistive technology</b> - `grid`, `row`, `columnheader`, `gridcell`, with
 * the row and column counts of the whole view and each drawn row's own index - because the rows
 * that are not drawn exist all the same. One cell at a time takes the Tab key; the arrow keys
 * move between cells, Enter opens a cell's editor and Escape leaves it (`cells/keyboard.ts`).
 *
 * It draws from the model and the module's definition and decides nothing about a document: what
 * the author does is raised as a gesture, and the stream that feeds the model answers it. A
 * gesture the backend refuses changes nothing here - the reason is shown, and the cell is still
 * what the model says it is.
 */
export function TableSurface({ model, definition, onWindow, onView, onGesture, fallbackViewportHeight = FALLBACK_VIEWPORT_HEIGHT }: TableSurfaceProps) {
  const rowHeight = definition.rowHeight ?? DEFAULT_ROW_HEIGHT;
  const columns = useMemo(() => model.columns.filter((column) => column.visible), [model.columns]);
  const scrollerRef = useRef<HTMLDivElement>(null);
  const [scroll, setScroll] = useState({ top: 0, height: 0 });
  const [active, setActive] = useState<CellPosition | null>(null);
  const [editing, setEditing] = useState<{ replace?: string } | null>(null);
  const [refusal, setRefusal] = useState("");
  // The width a column is being dragged to, until the drag ends: a preview, and nothing written.
  const [resizing, setResizing] = useState<{ columnId: string; width: number } | null>(null);
  // Whether the active cell should take the focus when it next renders: after the keyboard or a
  // click moved it, and not when the focus has left the table on its own.
  const focusWantedRef = useRef(false);

  const measure = useCallback(() => {
    const scroller = scrollerRef.current;
    if (scroller !== null) {
      // The header row is inside the scroller and stays in sight, so it is not room for rows.
      setScroll({ top: scroller.scrollTop, height: Math.max(0, scroller.clientHeight - rowHeight) });
    }
  }, [rowHeight]);

  useLayoutEffect(measure, [measure]);
  useEffect(() => {
    const scroller = scrollerRef.current;
    if (scroller === null || typeof ResizeObserver === "undefined") {
      return undefined;
    }
    const observer = new ResizeObserver(measure);
    observer.observe(scroller);
    return () => observer.disconnect();
  }, [measure]);

  const viewportHeight = scroll.height > 0 ? scroll.height : fallbackViewportHeight;
  const window = useMemo(
    () => windowOf({ scrollTop: scroll.top, viewportHeight, rowHeight, rowCount: model.rowCount, margin: DEFAULT_MARGIN }),
    [scroll.top, viewportHeight, rowHeight, model.rowCount],
  );

  // Reported when the window becomes other rows, not on every pixel scrolled.
  const reportedRef = useRef<RowWindow | null>(null);
  useEffect(() => {
    if (reportedRef.current === null || !sameWindow(reportedRef.current, window)) {
      reportedRef.current = window;
      onWindow?.(window);
    }
  }, [window, onWindow]);

  // The active cell is kept in sight and takes the focus, once it is drawn.
  useEffect(() => {
    const scroller = scrollerRef.current;
    if (active === null || scroller === null || editing !== null) {
      return;
    }

    const top = active.row * rowHeight;
    if (top < scroller.scrollTop) {
      scroller.scrollTop = top;
      measure();
    } else if (top + rowHeight > scroller.scrollTop + viewportHeight) {
      scroller.scrollTop = top + rowHeight - viewportHeight;
      measure();
    }

    if (focusWantedRef.current) {
      const cell = scroller.querySelector<HTMLElement>(`[data-cell="${active.row}:${active.column}"]`);
      if (cell !== null) {
        focusWantedRef.current = false;
        cell.focus();
      }
    }
  }, [active, editing, rowHeight, viewportHeight, measure, window]);

  const moveTo = useCallback((to: CellPosition) => {
    focusWantedRef.current = true;
    setActive(to);
  }, []);

  /** Raises a gesture made outside an editor, and shows a refusal on the table's own line. */
  const raise = useCallback(
    (gesture: TableGesture) => {
      setRefusal("");
      void onGesture?.(gesture).then(setRefusal);
    },
    [onGesture],
  );

  const onResizePreview = useCallback((columnId: string, width: number | null) => setResizing(width === null ? null : { columnId, width }), []);

  /** Sends a cell's new value. The refusal, if any, is the caller's to show. */
  const commitCell = useCallback(
    (row: TableRow, column: TableColumn, values: readonly string[], settings?: Readonly<Record<string, string>>): Promise<string> =>
      onGesture?.({ kind: "setCell", rowId: row.id, columnId: column.id, values, settings }) ?? Promise.resolve(""),
    [onGesture],
  );

  /** The row and column at a position when the cell there can be edited now; undefined otherwise. */
  const editableAt = (at: CellPosition) => {
    const row = model.rows.get(at.row);
    const column = columns[at.column];
    const editor = column === undefined ? undefined : kindOf(definition, column.kind).editor;
    if (row === undefined || row.isGroup || row.isNewRow || column === undefined || editor === undefined || model.readOnlyReason !== "") {
      return undefined;
    }
    return { row, column, editor, cell: row.cells.find((candidate) => candidate.columnId === column.id) };
  };

  /** Sends a value from outside an editor, and shows a refusal on the table's own line. */
  const sendDirectly = (target: NonNullable<ReturnType<typeof editableAt>>, values: readonly string[]) => {
    setRefusal("");
    void commitCell(target.row, target.column, values).then(setRefusal);
  };

  const startEdit = (at: CellPosition, replace?: string) => {
    const target = editableAt(at);
    if (target === undefined) {
      return;
    }

    setRefusal("");
    if (target.editor === "checkbox") {
      // A tick has no editor to open: the gesture is the edit.
      sendDirectly(target, [target.cell?.values[0] === "true" ? "false" : "true"]);
      return;
    }

    setActive(at);
    setEditing({ replace });
  };

  const closeEdit = (then: AfterEdit) => {
    setEditing(null);
    if (then === "leave" || active === null) {
      return;
    }

    const bounds = { rows: model.rowCount, columns: columns.length };
    moveTo(then === "stay" ? active : tabTarget(active, bounds, then === "previous"));
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (active === null || editing !== null) {
      return;
    }

    const outcome = keyOutcome(event, active, { rows: model.rowCount, columns: columns.length });
    switch (outcome.kind) {
      case "move":
        // A Tab that has nowhere to go inside the table is left to the browser: it leaves the table.
        if (outcome.to.row === active.row && outcome.to.column === active.column) {
          return;
        }
        event.preventDefault();
        moveTo(outcome.to);
        return;
      case "edit":
        event.preventDefault();
        startEdit(active, outcome.replace);
        return;
      case "newRow":
        event.preventDefault();
        raise({ kind: "addRow", targetId: model.rows.get(active.row)?.id });
        return;
      case "clear": {
        const target = editableAt(active);
        if (target !== undefined && target.cell !== undefined && target.cell.values.length > 0) {
          event.preventDefault();
          sendDirectly(target, target.editor === "checkbox" ? ["false"] : []);
        }
        return;
      }
      case "none":
        return;
    }
  };

  const spacers = spacersOf(window, model.rowCount, rowHeight);
  const widths = columns.map((column) =>
    resizing?.columnId === column.id ? resizing.width : column.width > 0 ? column.width : (definition.columnWidth ?? DEFAULT_COLUMN_WIDTH),
  );
  const editable = model.readOnlyReason === "" && onGesture !== undefined;
  const template = widths.map((width) => `${width}px`).join(" ");
  const rowStyle = { gridTemplateColumns: template, height: rowHeight };
  // The header has one more track than the rows: the room of the button that adds a column.
  const headerStyle = editable ? { gridTemplateColumns: `${template} 40px`, height: rowHeight } : rowStyle;
  // Until a cell is active, the first one drawn takes the Tab key, so the table can be entered.
  const entry: CellPosition = active ?? { row: window.first, column: 0 };

  return (
    <div className="table-frame">
      {model.views.length > 0 && <ViewTabs views={model.views} activeViewId={model.settings.viewId} editable={editable} onView={onView} raise={raise} />}
      {model.views.length > 0 && <ViewBar model={model} definition={definition} editable={editable} raise={raise} />}
    <div
      ref={scrollerRef}
      className="table-surface"
      role="grid"
      aria-label={model.title}
      aria-rowcount={model.rowCount + 1}
      aria-colcount={columns.length}
      aria-readonly={model.readOnlyReason !== "" ? true : undefined}
      onScroll={measure}
      onKeyDown={onKeyDown}
    >
      <div className="table-header-row" role="row" aria-rowindex={1} style={headerStyle}>
        {columns.map((column, index) => (
          <ColumnHeader
            key={column.id}
            column={column}
            index={index}
            width={widths[index]!}
            definition={definition}
            editable={editable}
            raise={raise}
            onResizePreview={onResizePreview}
          />
        ))}
        {editable && <AddColumn definition={definition} raise={raise} />}
      </div>
      {spacers.before > 0 && <div className="table-spacer" style={{ height: spacers.before }} aria-hidden="true" />}
      {indexesOf(window).map((index) => {
        const row = model.rows.get(index);
        // The header is row 1 of the grid, so the view's line 0 is row 2.
        const rowIndex = index + 2;

        if (row === undefined) {
          return <div key={index} className="table-row table-row-loading" role="row" aria-rowindex={rowIndex} aria-busy="true" style={rowStyle} />;
        }

        if (row.isGroup) {
          return (
            <div key={index} className="table-row table-group-row" role="row" aria-rowindex={rowIndex} data-row-id={row.id} style={{ height: rowHeight }}>
              <GroupHeading row={row} columnCount={columns.length} editable={editable} raise={raise} />
            </div>
          );
        }

        if (row.isNewRow) {
          // A table that cannot be edited has nowhere to add a row: the line keeps its height and is empty.
          return (
            <div key={index} className="table-row table-new-row" role="row" aria-rowindex={rowIndex} data-new-row-of={row.id} style={{ height: rowHeight }}>
              {editable && <NewRow row={row} columnCount={columns.length} raise={raise} />}
            </div>
          );
        }

        const byColumn = new Map(row.cells.map((cell) => [cell.columnId, cell]));
        return (
          <div key={index} className="table-row" role="row" aria-rowindex={rowIndex} data-row-id={row.id} style={rowStyle}>
            {columns.map((column, columnIndex) => {
              const cell = byColumn.get(column.id);
              const kind = kindOf(definition, column.kind);
              const isActive = active?.row === index && active.column === columnIndex;
              const editor = isActive && editing !== null && kind.editor !== "checkbox" ? kind.editor : undefined;
              const classes = [
                "table-cell",
                columnIndex === 0 ? "table-cell-first" : "",
                column.wraps ? "table-cell-wraps" : "",
                cell?.pending ? "table-cell-pending" : "",
                isActive ? "table-cell-active" : "",
                editor !== undefined ? "table-cell-editing" : "",
              ]
                .filter(Boolean)
                .join(" ");
              return (
                <div
                  key={column.id}
                  className={classes}
                  role="gridcell"
                  aria-colindex={columnIndex + 1}
                  aria-selected={isActive}
                  aria-readonly={kind.editor === undefined || model.readOnlyReason !== "" ? true : undefined}
                  data-column-id={column.id}
                  data-cell={`${index}:${columnIndex}`}
                  tabIndex={entry.row === index && entry.column === columnIndex ? 0 : -1}
                  style={column.isTitle && row.depth > 0 ? { paddingInlineStart: CELL_PADDING + row.depth * INDENT } : undefined}
                  onFocus={() => {
                    if (!isActive) {
                      setActive({ row: index, column: columnIndex });
                    }
                  }}
                  onDoubleClick={() => startEdit({ row: index, column: columnIndex })}
                >
                  {editor !== undefined ? (
                    <CellEditor
                      editor={editor}
                      column={column}
                      cell={cell}
                      replace={editing?.replace}
                      commit={(values, settings) => commitCell(row, column, values, settings)}
                      onClose={closeEdit}
                    />
                  ) : (
                    <>
                      {column.isTitle && <NestedRowToggle row={row} title={cellText(cell)} editable={editable} raise={raise} />}
                      <CellValue cell={cell} column={column} shows={kind.editor === "checkbox" ? "tick" : kind.editor === "option" || kind.editor === "options" ? "tags" : "text"} />
                    </>
                  )}
                </div>
              );
            })}
          </div>
        );
      })}
      {spacers.after > 0 && <div className="table-spacer" style={{ height: spacers.after }} aria-hidden="true" />}
      {refusal !== "" && (
        <p className="table-refusal" role="alert">
          {refusal}
        </p>
      )}
    </div>
    </div>
  );
}

/**
 * What a cell shows while it is not being edited: its text, a tick for a kind that is one, or a
 * tag per option for a kind that chooses among options.
 */
function CellValue({ cell, column, shows }: { cell: TableCell | undefined; column: TableColumn; shows: "text" | "tick" | "tags" }) {
  if (shows === "tick") {
    const ticked = cell?.values[0] === "true";
    return <span className={`mdi ${ticked ? "mdi-checkbox-marked" : "mdi-checkbox-blank-outline"} table-cell-tick`} role="checkbox" aria-checked={ticked} />;
  }

  if (shows === "tags" && cell !== undefined) {
    return (
      <>
        {cell.values.map((value, index) => {
          // An option the column no longer has is still shown, by the label its cell carries or by its id.
          const option = column.options.find((candidate) => candidate.id === value);
          return <OptionTag key={value} label={option?.name ?? (cell.labels[index] || value)} color={option?.color ?? ""} />;
        })}
      </>
    );
  }

  return <>{cellText(cell)}</>;
}
