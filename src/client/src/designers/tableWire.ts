import { base64Encode } from "@bufbuild/protobuf/wire";
import type * as Wire from "@client/generated/designers_pb";
import type { TableStreamEvent } from "@client/shell/context/workspaceStreams";
import type {
  TableCondition,
  TableEvent,
  TableFilterGroup,
  TableFinding,
  TableRow,
  TableStructure,
  TableViewSettings,
} from "@client/table/library/api/tableModel";

/**
 * A table stream's wire messages as the table library's own shapes. The one file that reads a
 * generated table message for the library's sake: the library draws the shapes below and a
 * change to the wire stops here.
 */

/** What became of an edit the backend accepted, by the edit's id as text. */
export interface TableEditOutcome {
  editId: string;
  written: boolean;
  error: string;
  /** The later edits taken back with it, by id. */
  takenBack: readonly string[];
}

/** A stream event as the library's event, or as an edit's outcome, which is the hook's to keep. */
export function fromWire(event: TableStreamEvent): TableEvent | { kind: "outcome"; outcome: TableEditOutcome } | undefined {
  if (event.kind === "baseline") {
    return { kind: "baseline", structure: structureOf(event.baseline), findings: event.baseline.findings.map(findingOf) };
  }

  const change = event.change.change;
  switch (change.case) {
    case "rows":
      return { kind: "rows", first: change.value.first, rows: change.value.rows.map(rowOf), rowCount: change.value.rowCount };
    case "structure":
      return { kind: "structure", structure: structureOf(change.value) };
    case "findings":
      return { kind: "findings", findings: change.value.findings.map(findingOf) };
    case "outcome":
      return {
        kind: "outcome",
        outcome: {
          editId: editKey(change.value.editId?.value),
          written: change.value.written,
          error: change.value.error,
          takenBack: change.value.takenBack.map((id) => editKey(id.value)),
        },
      };
    default:
      // A change this build does not know: nothing to draw from it.
      return undefined;
  }
}

/** An edit's id as text, for keeping track of edits that are not settled yet. */
export function editKey(id: Uint8Array | undefined): string {
  return id === undefined ? "" : base64Encode(id);
}

function structureOf(source: Wire.TableBaseline | Wire.TableStructure): TableStructure {
  return {
    title: source.title,
    columns: source.columns.map((column) => ({
      id: column.id,
      name: column.name,
      kind: column.kind,
      options: column.options.map((option) => ({ id: option.id, name: option.name, color: option.color })),
      width: column.width,
      visible: column.visible,
      isTitle: column.isTitle,
      wraps: column.wraps,
      settings: { ...column.settings },
    })),
    views: source.views.map((view) => ({ id: view.id, name: view.name })),
    settings: settingsOf(source.settings),
    rowCount: source.rowCount,
    readOnlyReason: source.readOnlyReason,
  };
}

function settingsOf(source: Wire.TableViewSettings | undefined): TableViewSettings {
  return {
    viewId: source?.viewId ?? "",
    sorts: (source?.sorts ?? []).map((sort) => ({ columnId: sort.columnId, descending: sort.descending })),
    filter: source?.filter === undefined ? undefined : groupOf(source.filter),
    groupBy: source?.groupBy ?? "",
    hidesEmptyGroups: source?.hidesEmptyGroups ?? false,
    settings: { ...(source?.settings ?? {}) },
  };
}

function groupOf(source: Wire.TableFilterGroup): TableFilterGroup {
  const items: (TableCondition | TableFilterGroup)[] = [];
  for (const { item } of source.items) {
    if (item.case === "condition") {
      items.push({ kind: "condition", columnId: item.value.columnId, comparison: item.value.comparison, values: [...item.value.values] });
    } else if (item.case === "group") {
      items.push(groupOf(item.value));
    }
  }
  return { kind: "group", any: source.any, items };
}

function rowOf(source: Wire.TableRow): TableRow {
  return {
    id: source.id,
    depth: source.depth,
    cells: source.cells.map((cell) => ({ columnId: cell.columnId, values: [...cell.values], labels: [...cell.labels], pending: cell.pending })),
    isGroup: source.isGroup,
    label: source.label,
    count: source.count,
    collapsed: source.collapsed,
    hasChildren: source.hasChildren,
    isNewRow: source.isNewRow,
  };
}

function findingOf(source: Wire.TableFinding): TableFinding {
  return {
    code: source.code,
    severity: source.severity === "error" || source.severity === "warning" ? source.severity : "info",
    message: source.message,
    rowId: source.rowId,
    columnId: source.columnId,
  };
}
