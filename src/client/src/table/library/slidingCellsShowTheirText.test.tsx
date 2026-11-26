import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableModel } from "./api/tableModel";
import type { TableDefinition } from "./definition/tableDefinition";
import { TableSurface } from "./TableSurface";

/**
 * A table scrolled sideways draws no column blank (etalii.adp spec 092, T035, third run).
 *
 * ## The defect
 *
 * The first column stays in sight while the table scrolls sideways, and the next one slides under it. Scrolled as far as a
 * picker in the last column took it, the second column showed a sliver beside the first with nothing in it: its header and
 * its values were under the first column, and only their empty right ends were left. The "+ New" line had scrolled away too.
 *
 * ## The fix, and what this asserts
 *
 * The surface says where the columns that stay end, as `--table-sticky-edge`, and the content of a cell that slides stops
 * there, sticky, until its own cell pushes it on: a column half under the first still shows the start of what it holds. jsdom
 * lays nothing out, so this asserts the edge the surface gives and the rules that use it.
 */
const definition: TableDefinition = { kinds: { text: { icon: "mdi-format-text", label: "Text", editor: "text" } } };

function column(id: string, name: string, extra: Partial<TableColumn> = {}): TableColumn {
  return { id, name, kind: "text", options: [], width: 0, visible: true, isTitle: false, wraps: false, settings: {}, ...extra };
}

function tableOf(freeze: string): TableModel {
  const structure = {
    title: "People",
    columns: [column("p1", "Name", { isTitle: true, width: 200 }), column("p2", "Role", { width: 150 }), column("p3", "Team")],
    views: [{ id: "v1", name: "All" }],
    settings: { ...EMPTY_TABLE.settings, settings: { ...EMPTY_TABLE.settings.settings, freeze } },
    rowCount: 0,
    readOnlyReason: "",
  };
  return applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] });
}

afterEach(cleanup);

describe("a table scrolled sideways", () => {
  const css = readFileSync(join(__dirname, "table.css"), "utf8");

  it("says where the columns that stay end: the first, and the ones a view freezes", () => {
    render(<TableSurface model={tableOf("")} definition={definition} />);
    expect(screen.getByRole("grid").style.getPropertyValue("--table-sticky-edge")).toBe("200px");
    cleanup();

    render(<TableSurface model={tableOf("p2")} definition={definition} />);
    expect(screen.getByRole("grid").style.getPropertyValue("--table-sticky-edge")).toBe("350px");
  });

  it("keeps what a sliding cell holds, its header's name and the + New line in sight", () => {
    // The content of a cell that slides sticks at the edge; its cell clips rather than scrolls, or the content would stick inside it.
    expect(css).toMatch(/\.table-row > \.table-cell:not\(\.table-cell-first\):not\(\.table-cell-frozen\):not\(\.table-cell-editing\) > \*\s*\{[^}]*position:\s*sticky;[^}]*left:\s*calc\(var\(--table-sticky-edge\)/);
    expect(css).toMatch(/\.table-header-name\s*\{[^}]*position:\s*sticky;[^}]*left:\s*calc\(var\(--table-sticky-edge\)/);
    expect(css).toMatch(/overflow:\s*clip/);
    expect(css).toMatch(/\.table-new-row-cell\s*\{[^}]*position:\s*sticky;[^}]*left:\s*0/);
  });
});
