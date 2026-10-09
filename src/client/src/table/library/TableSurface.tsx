import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import type { TableEvents } from "./api/tableEvents";
import { cellText, type TableColumn, type TableModel, type TableRow } from "./api/tableModel";
import { DEFAULT_COLUMN_WIDTH, kindOf, type TableDefinition } from "./definition/tableDefinition";
import { DEFAULT_MARGIN, DEFAULT_ROW_HEIGHT, indexesOf, sameWindow, spacersOf, windowOf, type RowWindow } from "./rows/windowing";
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
 * that are not drawn exist all the same.
 *
 * It draws from the model and the module's definition and decides nothing about a document: what
 * the author does is raised as an event, and the stream that feeds the model answers it.
 */
export function TableSurface({ model, definition, onWindow, fallbackViewportHeight = FALLBACK_VIEWPORT_HEIGHT }: TableSurfaceProps) {
  const rowHeight = definition.rowHeight ?? DEFAULT_ROW_HEIGHT;
  const columns = useMemo(() => model.columns.filter((column) => column.visible), [model.columns]);
  const scrollerRef = useRef<HTMLDivElement>(null);
  const [scroll, setScroll] = useState({ top: 0, height: 0 });

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

  const window = useMemo(
    () =>
      windowOf({
        scrollTop: scroll.top,
        viewportHeight: scroll.height > 0 ? scroll.height : fallbackViewportHeight,
        rowHeight,
        rowCount: model.rowCount,
        margin: DEFAULT_MARGIN,
      }),
    [scroll, fallbackViewportHeight, rowHeight, model.rowCount],
  );

  // Reported when the window becomes other rows, not on every pixel scrolled.
  const reportedRef = useRef<RowWindow | null>(null);
  useEffect(() => {
    if (reportedRef.current === null || !sameWindow(reportedRef.current, window)) {
      reportedRef.current = window;
      onWindow?.(window);
    }
  }, [window, onWindow]);

  const spacers = spacersOf(window, model.rowCount, rowHeight);
  const template = columns.map((column) => `${column.width > 0 ? column.width : (definition.columnWidth ?? DEFAULT_COLUMN_WIDTH)}px`).join(" ");
  const rowStyle = { gridTemplateColumns: template, height: rowHeight };

  return (
    <div
      ref={scrollerRef}
      className="table-surface"
      role="grid"
      aria-label={model.title}
      aria-rowcount={model.rowCount + 1}
      aria-colcount={columns.length}
      aria-readonly={model.readOnlyReason !== "" ? true : undefined}
      onScroll={measure}
    >
      <div className="table-header-row" role="row" aria-rowindex={1} style={rowStyle}>
        {columns.map((column, index) => (
          <ColumnHeader key={column.id} column={column} index={index} definition={definition} />
        ))}
      </div>
      {spacers.before > 0 && <div className="table-spacer" style={{ height: spacers.before }} aria-hidden="true" />}
      {indexesOf(window).map((index) => (
        <Row key={index} index={index} row={model.rows.get(index)} columns={columns} style={rowStyle} />
      ))}
      {spacers.after > 0 && <div className="table-spacer" style={{ height: spacers.after }} aria-hidden="true" />}
    </div>
  );
}

function ColumnHeader({ column, index, definition }: { column: TableColumn; index: number; definition: TableDefinition }) {
  const kind = kindOf(definition, column.kind);
  return (
    <div className="table-header-cell" role="columnheader" aria-colindex={index + 1} data-column-id={column.id}>
      <span className={`mdi ${kind.icon} table-kind-icon`} role="img" aria-label={kind.label} />
      <span className="table-header-name">{column.name}</span>
    </div>
  );
}

interface RowProps {
  index: number;
  /** The line at this index, or undefined while the model does not hold it yet. */
  row: TableRow | undefined;
  columns: readonly TableColumn[];
  style: { gridTemplateColumns: string; height: number };
}

function Row({ index, row, columns, style }: RowProps) {
  // The header is row 1 of the grid, so the view's line 0 is row 2.
  const rowIndex = index + 2;

  if (row === undefined) {
    return <div className="table-row table-row-loading" role="row" aria-rowindex={rowIndex} aria-busy="true" style={style} />;
  }

  if (row.isGroup) {
    return (
      <div className="table-row table-group-row" role="row" aria-rowindex={rowIndex} data-row-id={row.id} style={{ height: style.height }}>
        <div className="table-group-cell" role="gridcell" aria-colspan={Math.max(1, columns.length)}>
          <span className="table-group-label">{row.label}</span>
          <span className="table-group-count">{row.count}</span>
        </div>
      </div>
    );
  }

  const byColumn = new Map(row.cells.map((cell) => [cell.columnId, cell]));
  return (
    <div className="table-row" role="row" aria-rowindex={rowIndex} data-row-id={row.id} style={style}>
      {columns.map((column, columnIndex) => {
        const cell = byColumn.get(column.id);
        const classes = ["table-cell", column.wraps ? "table-cell-wraps" : "", cell?.pending ? "table-cell-pending" : ""].filter(Boolean).join(" ");
        return (
          <div
            key={column.id}
            className={classes}
            role="gridcell"
            aria-colindex={columnIndex + 1}
            data-column-id={column.id}
            style={column.isTitle && row.depth > 0 ? { paddingInlineStart: CELL_PADDING + row.depth * INDENT } : undefined}
          >
            {cellText(cell)}
          </div>
        );
      })}
    </div>
  );
}
