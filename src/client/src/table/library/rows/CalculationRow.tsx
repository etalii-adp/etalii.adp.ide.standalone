import { useRef, useState, type CSSProperties } from "react";
import { ContextMenu } from "../../../shell/context/ContextMenu";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn } from "../api/tableModel";

/** The setting a column lists the calculations it can be given under, the one it names its own under, and the one that says what it comes to. */
const CALCULATIONS = "calculations";
const CALCULATE = "calculate";
const CALCULATION = "calculation";

/** What each calculation is called, in the menu and beside its figure: Notion's names for them. */
const LABELS: Readonly<Record<string, string>> = {
  countAll: "Count all",
  countValues: "Count values",
  countUnique: "Count unique values",
  countEmpty: "Count empty",
  countNotEmpty: "Count not empty",
  percentEmpty: "Percent empty",
  percentNotEmpty: "Percent not empty",
  sum: "Sum",
  average: "Average",
  median: "Median",
  min: "Min",
  max: "Max",
  range: "Range",
  earliest: "Earliest date",
  latest: "Latest date",
  dateRange: "Date range",
};

/** What a calculation is called: its own name where the library knows none for it. */
export function calculationLabel(calculation: string): string {
  return LABELS[calculation] ?? calculation;
}

/** The calculations a column says it can be given; none for a column that says nothing. */
export function calculationsOf(column: TableColumn): string[] {
  return (column.settings[CALCULATIONS] ?? "").split(",").filter((calculation) => calculation !== "");
}

/** Whether the table has a line of calculations to show: some column can be given one and the table can be edited, or some column has one. */
export function showsCalculations(columns: readonly TableColumn[], editable: boolean): boolean {
  return columns.some((column) => (column.settings[CALCULATE] ?? "") !== "") || (editable && columns.some((column) => calculationsOf(column).length > 0));
}

export interface CalculationRowProps {
  columns: readonly TableColumn[];
  /** The grid's tracks and the row's height: the same as every row's. */
  style: CSSProperties;
  /** The row's place in the grid, counted from 1 with the header. */
  rowIndex: number;
  /** False for a table that cannot be edited: the line then shows what is calculated and changes nothing. */
  editable: boolean;
  raise: (gesture: TableGesture) => void;
  /** How far from the left a column stays while the table scrolls sideways, when the view freezes it. */
  frozenAt: (index: number) => number | undefined;
  /** The place of the column the view's columns are frozen up to, or -1. */
  frozenUpTo: number;
}

/**
 * The line under the columns, as in Notion: per column, what it calculates over the rows in sight
 * and what that comes to. Pressing a column's place opens the list of calculations its type has;
 * the figure itself is the backend's, which has every row where the table draws a window of them.
 */
export function CalculationRow({ columns, style, rowIndex, editable, raise, frozenAt, frozenUpTo }: CalculationRowProps) {
  const [menu, setMenu] = useState<{ columnId: string; x: number; y: number } | null>(null);
  const rowRef = useRef<HTMLDivElement>(null);
  const menuColumn = menu === null ? undefined : columns.find((column) => column.id === menu.columnId);
  const current = menuColumn?.settings[CALCULATE] ?? "";

  return (
    <div ref={rowRef} className="table-row table-calculation-row" role="row" aria-rowindex={rowIndex} aria-label="Calculations" style={style}>
      {columns.map((column, index) => {
        const calculate = column.settings[CALCULATE] ?? "";
        const offered = calculationsOf(column);
        const left = frozenAt(index);
        const classes = ["table-cell", "table-calculation-cell", index === 0 ? "table-cell-first" : "", left !== undefined ? "table-cell-frozen" : "", index === frozenUpTo ? "table-cell-frozen-edge" : ""].filter(Boolean).join(" ");
        const shown = (
          <>
            {calculate !== "" && <span className="table-calculation-label">{calculationLabel(calculate)}</span>}
            {calculate !== "" && <span className="table-calculation-value">{column.settings[CALCULATION] ?? ""}</span>}
          </>
        );
        return (
          <div key={column.id} className={classes} role="gridcell" aria-colindex={index + 1} data-column-id={column.id} style={left !== undefined ? { left } : undefined}>
            {editable && offered.length > 0 ? (
              <button
                type="button"
                className="table-calculation-button"
                aria-label={`Calculate ${column.name}`}
                aria-haspopup="menu"
                aria-expanded={menu?.columnId === column.id}
                onClick={(event) => {
                  const rect = event.currentTarget.getBoundingClientRect();
                  setMenu({ columnId: column.id, x: rect.left, y: rect.top });
                }}
              >
                {calculate === "" ? <span className="table-calculation-none">Calculate</span> : shown}
              </button>
            ) : (
              shown
            )}
          </div>
        );
      })}
      <ContextMenu
        open={menu !== null}
        groups={
          menuColumn === undefined
            ? []
            : [
                [{ id: "none", label: "None", icon: current === "" ? "mdi-check" : undefined, onSelect: () => raise({ kind: "setColumnCalculation", columnId: menuColumn.id, settings: { calculate: "" } }) }],
                calculationsOf(menuColumn).map((calculation) => ({
                  id: calculation,
                  label: calculationLabel(calculation),
                  icon: current === calculation ? "mdi-check" : undefined,
                  onSelect: () => raise({ kind: "setColumnCalculation", columnId: menuColumn.id, settings: { calculate: calculation } }),
                })),
              ]
        }
        position={menu ?? { x: 0, y: 0 }}
        onClose={() => setMenu(null)}
      />
    </div>
  );
}
