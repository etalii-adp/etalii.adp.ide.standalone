import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import { compartmentsHeight, layoutCompartments, type CompartmentDeclaration } from "./compartments";

const element: DiagramModelElement = { id: "s1", type: "specification", x: 0, y: 0 };
const bounds = { x: 100, y: 200, width: 220, height: 40 };
const source = (payload?: unknown): BindingSource => ({ element, payload });

/**
 * THE SHAPE THIS WAS DRAWN FROM: a specification's tasks, grouped by status under four headings
 * in a fixed order, two of them folded (agent-activity-diagram Requirements 4.1 to 4.4).
 */
const tasks: CompartmentDeclaration = {
  id: "tasks",
  rows: "payload.tasks",
  rowId: "id",
  text: { path: "title" },
  link: "link",
  orderBy: { path: "updated", direction: "descending" },
  groupBy: {
    path: "status",
    groups: [
      { value: "progressing", title: "Progressing" },
      { value: "pending", title: "Pending" },
      { value: "input-required", title: "Input Required" },
      { value: "finished", title: "Finished" },
    ],
    otherTitle: "Other",
  },
  collapsed: "payload.collapsed",
  top: 44,
  headingHeight: 20,
  rowHeight: 18,
  bottom: 8,
  insetX: 10,
  rowIndent: 12,
};

const payload = {
  collapsed: ["pending", "finished"],
  tasks: [
    { id: "t1", title: "Older progressing", status: "progressing", updated: "2026-10-08T10:00:00+02:00" },
    { id: "t2", title: "Pending one", status: "pending" },
    { id: "t3", title: "Newer progressing", status: "progressing", updated: "2026-10-09T10:00:00+02:00", link: "https://example.org/3" },
    { id: "t4", title: "Done", status: "finished" },
    { id: "t5", title: "Pending two", status: "pending" },
    { id: "t6", title: "No moment", status: "progressing" },
  ],
};

describe("compartments — rows grouped under headings", () => {
  it("draws one heading per group that has rows, in the declared order, each with its count", () => {
    const out = layoutCompartments([tasks], source(payload), bounds);

    // Input Required has no task, so it is not drawn at all.
    expect(out.headings.map((heading) => [heading.key, heading.title, heading.count, heading.collapsed])).toEqual([
      ["progressing", "Progressing", 3, false],
      ["pending", "Pending", 2, true],
      ["finished", "Finished", 1, true],
    ]);
  });

  it("draws no row of a folded group, and stacks what is left without a gap", () => {
    const out = layoutCompartments([tasks], source(payload), bounds);

    expect(out.rows.map((row) => row.groupKey)).toEqual(["progressing", "progressing", "progressing"]);
    // First heading at the declared top; its three rows beneath it; the next heading straight after.
    expect(out.headings[0]!.box.y).toBe(244);
    expect(out.rows.map((row) => row.box.y)).toEqual([264, 282, 300]);
    expect(out.headings[1]!.box.y).toBe(318);
    expect(out.headings[2]!.box.y).toBe(338);
    expect(out.bottom).toBe(366);
  });

  it("orders rows within a group by the declared field, with rows lacking it last", () => {
    const out = layoutCompartments([tasks], source(payload), bounds);

    expect(out.rows.map((row) => row.id)).toEqual(["t3", "t1", "t6"]);
  });

  it("carries a row's id and link, and leaves the link out where the row has none", () => {
    const out = layoutCompartments([tasks], source(payload), bounds);

    expect(out.rows[0]).toMatchObject({ id: "t3", link: "https://example.org/3", compartmentId: "tasks" });
    expect(out.rows[1]!.link).toBeUndefined();
  });

  it("keeps a row whose group value is none of the declared ones, under the other heading", () => {
    const out = layoutCompartments([tasks], source({ collapsed: [], tasks: [{ id: "x", title: "Odd", status: "blocked" }] }), bounds);

    expect(out.headings.map((heading) => [heading.key, heading.title])).toEqual([["", "Other"]]);
    expect(out.rows.map((row) => row.id)).toEqual(["x"]);
  });

  it("shortens a row that does not fit with an ellipsis and keeps the whole text", () => {
    const long = "A task whose title is far too long to fit on one line of this element";
    const out = layoutCompartments([tasks], source({ collapsed: [], tasks: [{ id: "x", title: long, status: "pending" }] }), bounds);

    expect(out.rows[0]!.fullText).toBe(long);
    expect(out.rows[0]!.text).not.toBe(long);
    expect(out.rows[0]!.text.endsWith("…")).toBe(true);
  });
});

describe("compartments — the element's height", () => {
  it("is the declared top, plus one line per heading and per visible row, plus the bottom inset", () => {
    // 44 + 3 headings × 20 + 3 visible rows × 18 + 8. The four rows of the two folded groups
    // must not count: that is the planted defect this was seen to fail against.
    expect(compartmentsHeight([tasks], source(payload))).toBe(44 + 3 * 20 + 3 * 18 + 8);
  });

  it("grows by a folded group's rows when it is unfolded", () => {
    const unfolded = { ...payload, collapsed: ["finished"] };

    expect(compartmentsHeight([tasks], source(unfolded))).toBe(44 + 3 * 20 + 5 * 18 + 8);
  });

  it("is null for an element with no rows, so the element keeps its own height", () => {
    expect(compartmentsHeight([tasks], source({ collapsed: [], tasks: [] }))).toBeNull();
    expect(compartmentsHeight([tasks], source({}))).toBeNull();
    expect(compartmentsHeight(undefined, source(payload))).toBeNull();
  });
});

describe("compartments — one group, and several compartments", () => {
  const pullRequests: CompartmentDeclaration = {
    id: "pullRequests",
    rows: "payload.pullRequests",
    rowId: "id",
    text: { path: "title" },
    title: "Pull requests",
    collapsed: "payload.collapsed",
    top: 60,
    headingHeight: 20,
    rowHeight: 18,
    bottom: 8,
    insetX: 10,
    rowIndent: 12,
  };

  it("draws an ungrouped compartment under its one heading, keyed by the compartment's id", () => {
    const out = layoutCompartments([pullRequests], source({ collapsed: ["pullRequests"], pullRequests: [{ id: "pr1", title: "One" }] }), bounds);

    expect(out.headings).toHaveLength(1);
    expect(out.headings[0]).toMatchObject({ key: "pullRequests", title: "Pull requests", count: 1, collapsed: true });
    expect(out.rows).toHaveLength(0);
  });

  it("keeps the model's order where none is declared", () => {
    const out = layoutCompartments([pullRequests], source({ collapsed: [], pullRequests: [{ id: "b", title: "B" }, { id: "a", title: "A" }] }), bounds);

    expect(out.rows.map((row) => row.id)).toEqual(["b", "a"]);
  });

  it("stacks a second compartment beneath the first, and gives an empty one no room", () => {
    const both = layoutCompartments([tasks, pullRequests], source({ ...payload, pullRequests: [{ id: "pr1", title: "One" }] }), bounds);
    const onlyTasks = layoutCompartments([tasks, pullRequests], source(payload), bounds);

    // The second begins where the first's last line ended, not at its own declared top.
    expect(both.headings.at(-1)!.box.y).toBe(358);
    expect(onlyTasks.bottom).toBe(366);
  });

  it("draws nothing, and throws nothing, for a path that is not a list", () => {
    const out = layoutCompartments([tasks], source({ tasks: "not a list", collapsed: 7 }), bounds);

    expect(out.headings).toHaveLength(0);
    expect(out.rows).toHaveLength(0);
  });
});
