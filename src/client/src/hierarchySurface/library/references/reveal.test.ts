import { describe, expect, it } from "vitest";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import { indexOf } from "../layout";
import { revealOf } from "./reveal";

function node(id: string, parentId: string, role: "level" | "entry", collapsed = false): HierarchySurfaceNode {
  return { id, number: `N-${id}`, role, type: role, parentId, title: id, description: "", labels: [], references: [], collapsed };
}

/** 1 holds 2, which holds 3, which holds 4, which holds the entry 5; 6 stands beside 1 and holds the entry 7. */
const index = indexOf({
  ...EMPTY_HIERARCHY_SURFACE,
  nodes: [node("1", "", "level"), node("2", "1", "level", true), node("3", "2", "level"), node("4", "3", "level", true), node("5", "4", "entry"), node("6", "", "level"), node("7", "6", "entry")],
});
const collapsed = (each: HierarchySurfaceNode) => each.collapsed;

describe("revealing a node", () => {
  it("keeps the focus when the node is inside it and fits, and expands every collapsed level above it", () => {
    // Act.
    const plan = revealOf(index, "5", "2", Number.POSITIVE_INFINITY, collapsed);

    // Assert: 2 is the focus and collapsed, so it is expanded too.
    expect(plan?.focus).toBe("2");
    expect(plan?.expand.map((level) => level.id)).toEqual(["2", "4"]);
  });

  it("moves the focus to the deepest level the node shares with it, and to the whole page when they share none", () => {
    // Assert.
    expect(revealOf(index, "5", "6", Number.POSITIVE_INFINITY, collapsed)?.focus).toBe("");
    expect(revealOf(index, "7", "4", Number.POSITIVE_INFINITY, collapsed)?.focus).toBe("");
    expect(revealOf(index, "3", "4", Number.POSITIVE_INFINITY, collapsed)?.focus).toBe("3");
  });

  it("moves the focus down only as far as the node's level needs to fit the page", () => {
    // Assert: 5 stands in 4, the fourth level; two columns show 3 and 4, one column 4 alone.
    expect(revealOf(index, "5", "", 2, collapsed)?.focus).toBe("3");
    expect(revealOf(index, "5", "1", 1, collapsed)?.focus).toBe("4");
    expect(revealOf(index, "5", "2", 3, collapsed)?.focus).toBe("2");
    expect(revealOf(index, "1", "", 1, collapsed)?.focus).toBe("");
  });

  it("asks for nothing to be expanded when nothing hides the node, and shows no node the document lacks", () => {
    // Assert.
    expect(revealOf(index, "7", "", Number.POSITIVE_INFINITY, collapsed)).toEqual({ focus: "", expand: [] });
    expect(revealOf(index, "99", "", Number.POSITIVE_INFINITY, collapsed)).toBeNull();
  });
});
