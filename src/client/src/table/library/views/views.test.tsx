import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { pointer } from "../../../canvas/library/testing/canvasHarness";
import type { TableGesture } from "../api/tableEvents";
import { applyTableEvent, EMPTY_TABLE, type TableColumn, type TableFilterGroup, type TableModel, type TableRow, type TableViewSettings } from "../api/tableModel";
import type { TableDefinition } from "../definition/tableDefinition";
import { TableSurface } from "../TableSurface";
import { childPath, comparisonsOf, describeCondition, FilterEditor, MAXIMUM_FILTER_DEPTH } from "./FilterEditor";
import { groupableColumns } from "./GroupEditor";
import { describeSort, sortableColumns } from "./SortEditor";

/**
 * What stands above and inside the table besides its cells: the views as tabs, the bar with its
 * controls and its filter and sort items, the editors those open, group headings, nested rows
 * and the line a new row is added at. Each control raises one gesture, and none keeps a state
 * the model does not have.
 */

const definition: TableDefinition = {
  kinds: {
    text: {
      icon: "mdi-format-text",
      label: "Text",
      editor: "text",
      comparisons: [
        { id: "contains", label: "contains" },
        { id: "is", label: "is" },
      ],
    },
    number: { icon: "mdi-pound", label: "Number", editor: "number", comparisons: [{ id: "greaterThan", label: "is greater than" }] },
    select: { icon: "mdi-tag-outline", label: "Selection", editor: "option", groupable: true },
    formula: { icon: "mdi-function", label: "Formula", sortable: false },
  },
};

function column(id: string, name: string, kind: string, extra: Partial<TableColumn> = {}): TableColumn {
  return { id, name, kind, options: [], width: 100, visible: true, isTitle: false, wraps: false, settings: {}, ...extra };
}

const COLUMNS = [column("p1", "Name", "text", { isTitle: true }), column("p2", "Population", "number"), column("p3", "Kind", "select"), column("p4", "Density", "formula", { visible: false })];

function line(id: string, extra: Partial<TableRow> = {}): TableRow {
  return { id, depth: 0, cells: [], isGroup: false, label: "", count: 0, collapsed: false, hasChildren: false, isNewRow: false, ...extra };
}

function table(settings: Partial<TableViewSettings> = {}, rows: readonly TableRow[] = [], readOnlyReason = ""): TableModel {
  const structure = {
    title: "Cities",
    columns: COLUMNS,
    views: [
      { id: "v1", name: "All cities" },
      { id: "v2", name: "Capitals" },
      { id: "v3", name: "By kind" },
    ],
    settings: { ...EMPTY_TABLE.settings, viewId: "v1", ...settings },
    rowCount: rows.length,
    readOnlyReason,
  };
  return applyTableEvent(applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] }), { kind: "rows", first: 0, rows, rowCount: rows.length });
}

function renderTable(model = table()) {
  const onGesture = vi.fn(async (_gesture: TableGesture) => "");
  const onView = vi.fn();
  render(<TableSurface model={model} definition={definition} onGesture={onGesture} onView={onView} />);
  return { onGesture, onView };
}

const tab = (name: string) => screen.getByRole("tab", { name });
const press = (element: Element) => {
  fireEvent(element, pointer("pointerdown", { pointerId: 1, button: 0, clientX: 10, clientY: 10 }));
  fireEvent(element, pointer("pointerup", { pointerId: 1, button: 0, clientX: 10, clientY: 10 }));
};
const drag = (element: Element, by: { dx?: number; dy?: number }) => {
  fireEvent(element, pointer("pointerdown", { pointerId: 1, button: 0, clientX: 100, clientY: 100 }));
  fireEvent(element, pointer("pointermove", { pointerId: 1, clientX: 100 + (by.dx ?? 0), clientY: 100 + (by.dy ?? 0) }));
  fireEvent(element, pointer("pointerup", { pointerId: 1, button: 0, clientX: 100 + (by.dx ?? 0), clientY: 100 + (by.dy ?? 0) }));
};
const choose = async (label: string) => {
  await act(async () => {
    fireEvent.click(screen.getByRole("menuitem", { name: label }));
  });
};
const open = (control: string) => fireEvent.click(screen.getByRole("button", { name: control }));
const rect = (left: number, top: number, width: number, height: number) => ({ left, top, right: left + width, bottom: top + height, width, height, x: left, y: top, toJSON: () => ({}) });

afterEach(cleanup);

describe("the views as tabs", () => {
  it("shows every view and marks the one this tab shows", () => {
    // Act.
    renderTable();

    // Assert.
    expect(screen.getAllByRole("tab").map((candidate) => `${candidate.textContent}:${candidate.getAttribute("aria-selected")}`)).toEqual(["All cities:true", "Capitals:false", "By kind:false"]);
  });

  it("switches view without editing the document", () => {
    // Arrange.
    const { onGesture, onView } = renderTable();

    // Act.
    press(tab("Capitals"));

    // Assert.
    expect(onView).toHaveBeenCalledWith("v2");
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("adds a view", () => {
    // Arrange.
    const { onGesture } = renderTable();

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Add a view" }));

    // Assert.
    expect(onGesture).toHaveBeenCalledWith({ kind: "addView" });
  });

  it.each([
    ["Duplicate", { kind: "duplicateView", viewId: "v1" }],
    ["Delete", { kind: "deleteView", viewId: "v1" }],
  ] as const)("offers %s in the menu of the view that is shown", async (label, gesture) => {
    // Arrange.
    const { onGesture, onView } = renderTable();

    // Act: pressing the tab of the view already shown opens its menu.
    press(tab("All cities"));
    await choose(label);

    // Assert.
    expect(onGesture).toHaveBeenCalledWith(gesture);
    expect(onView).not.toHaveBeenCalled();
  });

  it("renames a view in its tab", async () => {
    // Arrange.
    const { onGesture } = renderTable();
    press(tab("All cities"));
    await choose("Rename");
    const field = screen.getByRole("textbox", { name: "View name" });

    // Act.
    fireEvent.change(field, { target: { value: "Everything" } });
    await act(async () => {
      fireEvent.keyDown(field, { key: "Enter" });
    });

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "renameView", viewId: "v1", values: ["Everything"] });
  });

  it("does not let the last view be deleted, and says why", () => {
    // Arrange.
    const model = { ...table(), views: [{ id: "v1", name: "All cities" }] };
    renderTable(model);

    // Act.
    press(tab("All cities"));

    // Assert.
    const remove = screen.getByRole("menuitem", { name: /Delete/ });
    expect(remove.getAttribute("aria-disabled") ?? String((remove as HTMLButtonElement).disabled)).toBe("true");
  });

  it("moves a view to where its tab is dragged", () => {
    // Arrange.
    const { onGesture } = renderTable();
    screen.getAllByRole("tab").forEach((candidate, index) => {
      candidate.getBoundingClientRect = () => rect(index * 100, 0, 100, 30);
    });

    // Act: the first tab is dragged past the middle of the third.
    drag(tab("All cities"), { dx: 220 });

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "moveView", viewId: "v1", index: 2 });
  });

  it("only switches on a table that is read-only", () => {
    // Arrange.
    const { onGesture, onView } = renderTable(table({}, [], "This file is of a newer version."));

    // Act.
    press(tab("All cities"));
    press(tab("By kind"));

    // Assert.
    expect(screen.queryByRole("menu")).toBeNull();
    expect(screen.queryByRole("button", { name: "Add a view" })).toBeNull();
    expect(onView).toHaveBeenCalledWith("v3");
    expect(onGesture).not.toHaveBeenCalled();
    expect((screen.getByRole("button", { name: "Filter" }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("the bar's items", () => {
  const filter: TableFilterGroup = {
    kind: "group",
    any: false,
    items: [
      { kind: "condition", columnId: "p1", comparison: "contains", values: ["dam"] },
      { kind: "group", any: true, items: [{ kind: "condition", columnId: "p2", comparison: "isEmpty", values: [] }] },
    ],
  };
  const settings = { sorts: [{ columnId: "p2", descending: true }], filter };

  it("shows each sort and each filter as an item that says what it is", () => {
    // Act.
    renderTable(table(settings));

    // Assert.
    const items = within(screen.getByRole("list", { name: "Filters and sorts" })).getAllByRole("listitem");
    expect(items.map((item) => item.textContent)).toEqual(["Population descending", "Name contains dam", "Any of 1"]);
  });

  it("removes a sort and a filter from its item", () => {
    // Arrange.
    const { onGesture } = renderTable(table(settings));

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Remove sort Population descending" }));
    fireEvent.click(screen.getByRole("button", { name: "Remove filter Name contains dam" }));
    fireEvent.click(screen.getByRole("button", { name: "Remove filter Any of 1" }));

    // Assert: a filter is named by its place.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "removeSort", columnId: "p2" },
      { kind: "removeFilter", targetId: "0" },
      { kind: "removeFilter", targetId: "1" },
    ]);
  });

  it("opens the editor an item belongs to", () => {
    // Arrange.
    renderTable(table(settings));

    // Act and assert.
    fireEvent.click(screen.getByRole("button", { name: "Name contains dam" }));
    expect(screen.getByRole("group", { name: "Filter" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Population descending" }));
    expect(screen.getByRole("group", { name: "Sort" })).toBeTruthy();
    expect(screen.queryByRole("group", { name: "Filter" })).toBeNull();
  });

  it("has no items row for a view that neither filters nor sorts", () => {
    renderTable();
    expect(screen.queryByRole("list", { name: "Filters and sorts" })).toBeNull();
  });
});

describe("the filter editor", () => {
  const nested: TableFilterGroup = {
    kind: "group",
    any: false,
    items: [
      { kind: "condition", columnId: "p1", comparison: "contains", values: ["dam"] },
      { kind: "group", any: true, items: [{ kind: "group", any: false, items: [] }] },
    ],
  };

  it("adds a condition on a property with that property's first comparison", () => {
    // Arrange.
    const { onGesture } = renderTable();
    open("Filter");

    // Act.
    fireEvent.change(screen.getByRole("combobox", { name: "Add a condition to the filter" }), { target: { value: "p2" } });

    // Assert.
    expect(onGesture).toHaveBeenCalledWith({ kind: "addFilter", columnId: "p2", targetId: "", settings: { comparison: "greaterThan" } });
  });

  it("changes a condition's comparison and its value, each as one gesture naming its place", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ filter: nested }));
    open("Filter");

    // Act.
    fireEvent.change(screen.getByRole("combobox", { name: "Comparison of condition 0" }), { target: { value: "is" } });
    const value = screen.getByRole("textbox", { name: "Value of condition 0" });
    fireEvent.change(value, { target: { value: "Amsterdam" } });
    fireEvent.keyDown(value, { key: "Enter" });

    // Assert: typing raises nothing until the value is committed.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "setFilter", targetId: "0", columnId: "p1", settings: { comparison: "is" }, values: ["dam"] },
      { kind: "setFilter", targetId: "0", columnId: "p1", settings: { comparison: "contains" }, values: ["Amsterdam"] },
    ]);
  });

  it("offers every kind is empty and is not empty, beside its own comparisons", () => {
    expect(comparisonsOf(definition, "text").map((comparison) => comparison.id)).toEqual(["contains", "is", "isEmpty", "isNotEmpty"]);
    expect(comparisonsOf(definition, "select").map((comparison) => comparison.id)).toEqual(["isEmpty", "isNotEmpty"]);
    expect(comparisonsOf(definition, "hologram").map((comparison) => comparison.id)).toEqual(["isEmpty", "isNotEmpty"]);
  });

  it("asks for no value where the comparison needs none", () => {
    // Arrange.
    const filter: TableFilterGroup = { kind: "group", any: false, items: [{ kind: "condition", columnId: "p1", comparison: "isEmpty", values: [] }] };

    // Act.
    renderTable(table({ filter }));
    open("Filter");

    // Assert.
    expect(screen.queryByRole("textbox", { name: "Value of condition 0" })).toBeNull();
  });

  it("starts a condition on another property's first comparison when it does not have the one chosen", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ filter: nested }));
    open("Filter");

    // Act: a number is not compared by "contains".
    fireEvent.change(screen.getByRole("combobox", { name: "Property of condition 0" }), { target: { value: "p2" } });

    // Assert.
    expect(onGesture).toHaveBeenCalledWith({ kind: "setFilter", targetId: "0", columnId: "p2", settings: { comparison: "greaterThan" }, values: ["dam"] });
  });

  it("switches a group between all and any", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ filter: nested }));
    open("Filter");

    // Act.
    fireEvent.change(screen.getByRole("combobox", { name: "Rows match" }), { target: { value: "any" } });
    fireEvent.change(screen.getByRole("combobox", { name: "Group 1 matches" }), { target: { value: "all" } });

    // Assert.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "setFilterMatch", targetId: "", settings: { match: "any" } },
      { kind: "setFilterMatch", targetId: "1", settings: { match: "all" } },
    ]);
  });

  it("nests groups to three levels, and does not offer a fourth", () => {
    // Arrange: a group in a group in the filter is the third level.
    const { onGesture } = renderTable(table({ filter: nested }));
    open("Filter");

    // Assert: the first two levels can take a group, the third cannot.
    const depths = [...document.querySelectorAll<HTMLElement>("[data-filter-depth]")].map((group) => ({
      depth: Number(group.dataset.filterDepth),
      offersGroup: [...group.children].some((child) => child.classList.contains("table-filter-add") && child.querySelector(".table-filter-add-group") !== null),
    }));
    expect(depths).toEqual([
      { depth: 1, offersGroup: true },
      { depth: 2, offersGroup: true },
      { depth: 3, offersGroup: false },
    ]);
    expect(MAXIMUM_FILTER_DEPTH).toBe(3);

    // And the third still takes conditions.
    fireEvent.change(screen.getByRole("combobox", { name: "Add a condition to group 1/0" }), { target: { value: "p1" } });
    expect(onGesture).toHaveBeenCalledWith({ kind: "addFilter", columnId: "p1", targetId: "1/0", settings: { comparison: "contains" } });
  });

  it("adds a group where it is asked for, and removes one by its place", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ filter: nested }));
    open("Filter");

    // Act.
    // A group draws what is inside it before its own controls, so the inner group's button comes first.
    fireEvent.click(screen.getAllByRole("button", { name: "Add a group" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: "Remove group 1/0" }));
    fireEvent.click(screen.getByRole("button", { name: "Remove condition 0" }));

    // Assert.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "addFilterGroup", targetId: "1" },
      { kind: "removeFilter", targetId: "1/0" },
      { kind: "removeFilter", targetId: "0" },
    ]);
  });

  it("names an item's place by the indexes that lead to it", () => {
    expect(childPath("", 2)).toBe("2");
    expect(childPath("1", 0)).toBe("1/0");
    expect(childPath("1/0", 3)).toBe("1/0/3");
  });

  it("puts a condition in words, and still does for a property that is gone", () => {
    expect(describeCondition({ kind: "condition", columnId: "p2", comparison: "greaterThan", values: ["1000"] }, COLUMNS, definition)).toBe("Population is greater than 1000");
    expect(describeCondition({ kind: "condition", columnId: "p1", comparison: "isEmpty", values: [] }, COLUMNS, definition)).toBe("Name is empty");
    expect(describeCondition({ kind: "condition", columnId: "p9", comparison: "is", values: ["x"] }, COLUMNS, definition)).toBe("p9 is x");
  });

  it("names an option by its name, and keeps the id of one that is gone", () => {
    // Arrange: an option is stored by id.
    const kinds = [column("p3", "Kind", "select", { options: [{ id: "o1", name: "Port", color: "" }] })];

    // Assert.
    expect(describeCondition({ kind: "condition", columnId: "p3", comparison: "is", values: ["o1"] }, kinds, definition)).toBe("Kind is Port");
    expect(describeCondition({ kind: "condition", columnId: "p3", comparison: "is", values: ["o9"] }, kinds, definition)).toBe("Kind is o9");
  });

  it("offers a condition on a column with options its options, and raises the one chosen by id", () => {
    // Arrange.
    const kinds = [column("p3", "Kind", "select", { options: [{ id: "o1", name: "Port", color: "" }, { id: "o2", name: "Capital", color: "" }] })];
    const raise = vi.fn();
    render(<FilterEditor filter={{ kind: "group", any: false, items: [{ kind: "condition", columnId: "p3", comparison: "is", values: [] }] }} columns={kinds} definition={definition} raise={raise} />);

    // Act.
    const value = screen.getByRole("combobox", { name: "Value of condition 0" });
    fireEvent.change(value, { target: { value: "o2" } });

    // Assert: a list, not a box to type an id in.
    expect(screen.queryByRole("textbox", { name: "Value of condition 0" })).toBeNull();
    expect(within(value).getAllByRole("option").map((option) => option.textContent)).toEqual(["Choose…", "Port", "Capital"]);
    expect(raise).toHaveBeenCalledWith(expect.objectContaining({ kind: "setFilter", targetId: "0", columnId: "p3", values: ["o2"] }));
  });
});

describe("the sort editor", () => {
  const sorts = [
    { columnId: "p2", descending: true },
    { columnId: "p1", descending: false },
  ];

  it("adds a sort by a property that is not sorted by yet, and that can be", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ sorts: [sorts[0]!] }));
    open("Sort");
    const add = screen.getByRole("combobox", { name: "Add a sort" });

    // Assert: not the one already sorted by, and not the kind that cannot be sorted.
    expect(within(add).getAllByRole("option").map((option) => option.textContent)).toEqual(["Add a sort…", "Name", "Kind"]);

    // Act.
    fireEvent.change(add, { target: { value: "p3" } });
    expect(onGesture).toHaveBeenCalledWith({ kind: "addSort", columnId: "p3", settings: { direction: "ascending" } });
  });

  it("changes a sort's direction and removes it", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ sorts }));
    open("Sort");
    const editor = screen.getByRole("group", { name: "Sort" });

    // Act.
    fireEvent.change(within(editor).getByRole("combobox", { name: "Direction of sort Name" }), { target: { value: "descending" } });
    fireEvent.click(within(editor).getByRole("button", { name: "Remove sort Population" }));

    // Assert.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "setSort", columnId: "p1", settings: { direction: "descending" } },
      { kind: "removeSort", columnId: "p2" },
    ]);
  });

  it("orders the sorts by dragging", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ sorts }));
    open("Sort");
    const editor = screen.getByRole("group", { name: "Sort" });
    within(editor)
      .getAllByRole("listitem")
      .forEach((row, index) => {
        row.getBoundingClientRect = () => rect(0, index * 30, 300, 30);
      });

    // Act: the first sort is dragged below the second.
    drag(within(editor).getByRole("img", { name: "Move sort Population" }), { dy: 40 });

    // Assert.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith({ kind: "moveSort", columnId: "p2", index: 1 });
  });

  it("knows which properties can be sorted by, and puts a sort in words", () => {
    expect(sortableColumns(COLUMNS, definition).map((candidate) => candidate.id)).toEqual(["p1", "p2", "p3"]);
    expect(describeSort({ columnId: "p1", descending: false }, COLUMNS)).toBe("Name ascending");
    expect(describeSort({ columnId: "p9", descending: true }, COLUMNS)).toBe("p9 descending");
  });
});

describe("grouping and which properties are shown", () => {
  it("groups by a property whose kind can be grouped by, and by nothing", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ groupBy: "p3" }));
    open("Group");
    const by = screen.getByRole("combobox", { name: "Group by" });
    expect(within(by).getAllByRole("option").map((option) => option.textContent)).toEqual(["Nothing", "Kind"]);
    expect(groupableColumns(COLUMNS, definition).map((candidate) => candidate.id)).toEqual(["p3"]);

    // Act.
    fireEvent.change(by, { target: { value: "" } });
    fireEvent.click(screen.getByRole("checkbox", { name: "Hide empty groups" }));

    // Assert.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "groupBy", columnId: "" },
      { kind: "setHideEmptyGroups", settings: { hide: "true" } },
    ]);
  });

  it("shows and hides a property, and never the one that names a row", () => {
    // Arrange.
    const { onGesture } = renderTable();
    open("Properties");
    const list = screen.getByRole("group", { name: "Properties" });

    // Assert: every property is listed, the hidden one too.
    const boxes = within(list).getAllByRole("checkbox") as HTMLInputElement[];
    expect(boxes.map((box) => `${box.checked}:${box.disabled}`)).toEqual(["true:true", "true:false", "true:false", "false:false"]);

    // Act.
    fireEvent.click(within(list).getByRole("checkbox", { name: "Density" }));
    fireEvent.click(within(list).getByRole("checkbox", { name: "Population" }));

    // Assert.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "showColumn", columnId: "p4" },
      { kind: "hideColumn", columnId: "p2" },
    ]);
  });
});

describe("group headings, nested rows and the new row", () => {
  const lines = [
    line("capital", { isGroup: true, label: "Capital", count: 12 }),
    line("r1", { cells: [{ columnId: "p1", values: ["Amsterdam"], labels: [], pending: false }], hasChildren: true }),
    line("r2", { depth: 1, cells: [{ columnId: "p1", values: ["Centrum"], labels: [], pending: false }] }),
    line("capital", { isNewRow: true }),
    line("port", { isGroup: true, label: "Port", count: 0, collapsed: true }),
    line("port", { isNewRow: true }),
  ];
  const rowAt = (index: number) => screen.getAllByRole("row")[index + 1]!;

  it("shows a heading with the value, the count the model gives, and a toggle", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ groupBy: "p3" }, lines));

    // Assert.
    expect(rowAt(0).textContent).toBe("Capital12");
    expect(rowAt(4).textContent).toBe("Port0");

    // Act: one open group is folded, one folded group is opened.
    fireEvent.click(within(rowAt(0)).getByRole("button", { name: "Collapse Capital" }));
    fireEvent.click(within(rowAt(4)).getByRole("button", { name: "Expand Port" }));

    // Assert: the fold is raised, and the heading still shows what the model says.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "toggleGroup", targetId: "capital", settings: { collapsed: "true" } },
      { kind: "toggleGroup", targetId: "port", settings: { collapsed: "false" } },
    ]);
    expect(within(rowAt(0)).getByRole("button", { name: "Collapse Capital" }).getAttribute("aria-expanded")).toBe("true");
  });

  it("gives a row with rows under it a toggle, and a row without none", () => {
    // Arrange.
    const { onGesture } = renderTable(table({}, lines));

    // Act.
    fireEvent.click(within(rowAt(1)).getByRole("button", { name: "Collapse Amsterdam" }));

    // Assert.
    expect(onGesture).toHaveBeenCalledWith({ kind: "toggleRow", rowId: "r1", settings: { collapsed: "true" } });
    expect(within(rowAt(2)).queryByRole("button")).toBeNull();
  });

  it("adds a row at the bottom of a group, to that group", () => {
    // Arrange.
    const { onGesture } = renderTable(table({ groupBy: "p3" }, lines));

    // Act.
    fireEvent.click(within(rowAt(3)).getByRole("button", { name: "New" }));
    fireEvent.click(within(rowAt(5)).getByRole("button", { name: "New" }));

    // Assert: each names the group it was asked in.
    expect(onGesture.mock.calls.map((call) => call[0])).toEqual([
      { kind: "addRow", settings: { group: "capital" } },
      { kind: "addRow", settings: { group: "port" } },
    ]);
  });

  it("adds a row at the bottom of a table that is not grouped, to no group", () => {
    // Arrange.
    const { onGesture } = renderTable(table({}, [line("r1"), line("", { isNewRow: true })]));

    // Act.
    fireEvent.click(within(rowAt(1)).getByRole("button", { name: "New" }));

    // Assert: the key is there and empty, not left out.
    expect(onGesture).toHaveBeenCalledWith({ kind: "addRow", settings: { group: "" } });
  });

  it("keeps the new-row line and offers nothing on it when the table is read-only", () => {
    // Act.
    renderTable(table({}, [line("r1"), line("", { isNewRow: true })], "This file is of a newer version."));

    // Assert: the line is still a line, so the rows after it keep their places.
    expect(screen.getAllByRole("row")).toHaveLength(3);
    expect(screen.queryByRole("button", { name: "New" })).toBeNull();
  });
});
