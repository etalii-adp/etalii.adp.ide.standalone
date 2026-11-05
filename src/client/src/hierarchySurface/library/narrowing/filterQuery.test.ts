import { describe, expect, it } from "vitest";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceLabelDefinition } from "../definition/hierarchySurfaceDefinition";
import { indexOf } from "../layout";
import { completionsOf, narrowingOf, parseQuery, queryText, sortOf, unhiding, withSort, wordAt } from "./filterQuery";

// Made-up names throughout: the query knows only the prefixes the definition gives its labels.
const labels: HierarchySurfaceLabelDefinition[] = [
  { label: "marks", prefix: "~", many: true, on: ["Section", "Point"] },
  { label: "k", prefix: "k", many: false, on: ["Point"] },
  { label: "kind", prefix: "kind:", many: false, on: ["Point"] },
  { label: "phase", prefix: "phase:", many: false, on: ["Point"] },
];

function node(id: string, parentId: string, role: "level" | "entry", title: string, values: { marks?: string[]; kind?: string; phase?: string } = {}): HierarchySurfaceNode {
  return {
    id,
    number: role === "level" ? `S-${id}` : `P-${id}`,
    role,
    type: role === "level" ? "Section" : "Point",
    parentId,
    title,
    description: "",
    labels: [
      { label: "marks", values: values.marks ?? [] },
      { label: "kind", values: values.kind === undefined ? [] : [values.kind] },
      { label: "phase", values: values.phase === undefined ? [] : [values.phase] },
    ],
    references: [],
    collapsed: false,
  };
}

/**
 * Two top sections, Backend (1) and Frontend (5); Backend holds the points 2 and 3 and the section Billing (4) with the
 * point 9; Frontend, which carries safe, holds the points 6 and 10. The numbers sort as 2, 3, 9, 10, not as text.
 */
function model(): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    nodes: [
      node("1", "", "level", "Backend"),
      node("3", "1", "entry", "Store an order", { marks: ["Fast"], kind: "rule", phase: "done" }),
      node("2", "1", "entry", "audit an order", { marks: ["safe"], kind: "goal" }),
      node("4", "1", "level", "Billing"),
      node("9", "4", "entry", "Charge a card", { marks: ["safe", "fast"], phase: "draft" }),
      node("5", "", "level", "Frontend", { marks: ["safe"] }),
      node("10", "5", "entry", "Show an order", { marks: ["fast"], kind: "goal", phase: "draft" }),
      node("6", "5", "entry", "Book a slot", { kind: "rule", phase: "done" }),
    ],
    optionLists: [
      { label: "phase", options: [{ id: "phase/draft", name: "draft", colour: "gray", implicit: false }, { id: "phase/done", name: "done", colour: "green", implicit: false }] },
    ],
  };
}

/** The titles the query leaves on the page, in the file's order. */
function shown(query: string): string[] {
  const page = model();
  const narrowing = narrowingOf(page, indexOf(page), parseQuery(query, labels));
  return page.nodes.filter(narrowing.shows).map((shownNode) => shownNode.title);
}

/** The titles of the entries of `levelId` in the query's order. */
function ordered(query: string, levelId: string): string[] {
  const page = model();
  const narrowing = narrowingOf(page, indexOf(page), parseQuery(query, labels));
  return narrowing.order(page.nodes.filter((entry) => entry.parentId === levelId && entry.role === "entry")).map((entry) => entry.title);
}

describe("a query", () => {
  it("reads each word by the longest prefix that fits it, and knows the item and sort words in any case", () => {
    // Act.
    const words = parseQuery("  ~fast kind:rule kx ITEM:back* Sort:Phase plain ", labels);

    // Assert: kind: is not the k label followed by ind:rule; a prefix alone is no word of its label.
    expect(words.map((word) => `${word.kind} ${word.label} ${word.value}`)).toEqual(["label marks fast", "label kind rule", "label k x", "item  back*", "sort  phase", "unknown  plain"]);
    expect(parseQuery("~ item:", labels).map((word) => word.kind)).toEqual(["unknown", "unknown"]);
    expect(queryText(words)).toBe("~fast kind:rule kx ITEM:back* Sort:Phase plain");
  });

  it("counts the first sort word, and changes the sort in its place, or adds it last, or takes every one out", () => {
    // Arrange.
    const words = parseQuery("~fast sort:title kind:goal sort:number", labels);

    // Act and assert.
    expect(sortOf(words)).toBe("title");
    expect(queryText(withSort(words, "phase"))).toBe("~fast sort:phase kind:goal");
    expect(queryText(withSort(parseQuery("~fast", labels), "number"))).toBe("~fast sort:number");
    expect(queryText(withSort(words, ""))).toBe("~fast kind:goal");
  });
});

describe("narrowing the page", () => {
  it("leaves everything when the query is empty or only sorts", () => {
    // Assert.
    expect(shown("")).toHaveLength(8);
    expect(shown("sort:title")).toHaveLength(8);
    expect(narrowingOf(model(), indexOf(model()), parseQuery("sort:title", labels)).narrows).toBe(false);
  });

  it("leaves the entries that carry a tag in any case, with the levels that contain them", () => {
    // Assert.
    expect(shown("~FAST")).toEqual(["Backend", "Store an order", "Billing", "Charge a card", "Frontend", "Show an order"]);
  });

  it("takes the words of one label as alternatives and combines the labels", () => {
    // Assert: safe or fast, and goal; then draft, and the category of 9 is none, so it goes.
    expect(shown("~safe ~fast kind:goal")).toEqual(["Backend", "audit an order", "Frontend", "Show an order"]);
    expect(shown("kind:rule kind:goal phase:draft")).toEqual(["Frontend", "Show an order"]);
  });

  it("narrows by state and by category alone", () => {
    // Assert.
    expect(shown("phase:done")).toEqual(["Backend", "Store an order", "Frontend", "Book a slot"]);
    expect(shown("kind:Rule")).toEqual(["Backend", "Store an order", "Frontend", "Book a slot"]);
  });

  it("selects levels by a title pattern of * and ? in any case, with everything in them, and keeps the levels above", () => {
    // Assert: Billing stays with Backend above it; b?ck* is Backend alone.
    expect(shown("item:bill*")).toEqual(["Backend", "Billing", "Charge a card"]);
    expect(shown("item:B?CK*")).toEqual(["Backend", "Store an order", "audit an order", "Billing", "Charge a card"]);
    expect(shown("item:end")).toEqual([]);
    expect(shown("item:?lling")).toEqual([]);
  });

  it("combines a pattern with the labels: the entries that carry them in the levels it selects", () => {
    // Assert.
    expect(shown("item:back* ~fast")).toEqual(["Backend", "Store an order", "Billing", "Charge a card"]);
    expect(shown("item:billing item:frontend phase:draft")).toEqual(["Backend", "Billing", "Charge a card", "Frontend", "Show an order"]);
  });

  it("leaves nothing for a value no entry carries", () => {
    // Assert.
    expect(shown("~none")).toEqual([]);
  });
});

describe("sorting the entries", () => {
  it("keeps the file's order without a sort, and sorts by number as numbers, and by title in any case", () => {
    // Assert.
    expect(ordered("", "5")).toEqual(["Show an order", "Book a slot"]);
    expect(ordered("sort:number", "5")).toEqual(["Book a slot", "Show an order"]);
    expect(ordered("sort:title", "1")).toEqual(["audit an order", "Store an order"]);
  });

  it("sorts by a label of one value in its list's order, those without a value last, and keeps the file's order of equals", () => {
    // Arrange: a third Frontend point without a phase.
    const page = model();
    const nodes = [...page.nodes, node("11", "5", "entry", "Undo a slot"), node("12", "5", "entry", "Rebook a slot", { phase: "done" })];
    const narrowing = narrowingOf({ ...page, nodes }, indexOf({ ...page, nodes }), parseQuery("sort:phase", labels));

    // Act.
    const titles = narrowing.order(nodes.filter((entry) => entry.parentId === "5")).map((entry) => entry.title);

    // Assert.
    expect(titles).toEqual(["Show an order", "Book a slot", "Rebook a slot", "Undo a slot"]);
  });

  it("keeps the file's order for a key it does not know", () => {
    // Assert.
    expect(ordered("sort:colour", "5")).toEqual(["Show an order", "Book a slot"]);
  });
});

describe("unhiding an entry", () => {
  function unhidden(query: string, nodeId: string): string {
    const page = model();
    return queryText(unhiding(page, indexOf(page), parseQuery(query, labels), nodeId));
  }

  it("keeps a query that already shows the node", () => {
    // Assert.
    expect(unhidden("~fast sort:title", "3")).toBe("~fast sort:title");
    expect(unhidden("~fast", "1")).toBe("~fast");
  });

  it("drops only the words that hide it: the labels it does not carry, and the patterns it stands outside", () => {
    // Assert: 6 is a rule, done, without marks, in Frontend.
    expect(unhidden("~fast kind:rule sort:number", "6")).toBe("kind:rule sort:number");
    expect(unhidden("item:back* phase:done", "6")).toBe("phase:done");
    expect(unhidden("item:front* ~fast ~safe", "2")).toBe("~fast ~safe");
  });

  it("drops every label word for a level, which holds no entry that stays", () => {
    // Assert.
    expect(unhidden("item:front* ~none sort:title", "1")).toBe("sort:title");
    expect(unhidden("item:back* ~none", "4")).toBe("item:back*");
    expect(unhidden("~safe", "5")).toBe("");
  });
});

describe("completing a word", () => {
  const sortKeys = [{ key: "number", label: "By number" }];

  it("finds the word the caret stands in, and what of it is typed", () => {
    // Act.
    const word = wordAt("~fast pha sort:number", 8);

    // Assert.
    expect(word).toEqual({ start: 6, end: 9, typed: "ph" });
  });

  it("offers the members of a label's list with its prefix, in any case, then the prefixes and the sort keys that fit", () => {
    // Act.
    const offered = completionsOf("PHASE:D", labels, model().optionLists, sortKeys).map((completion) => completion.word);
    const sorts = completionsOf("so", labels, model().optionLists, sortKeys).map((completion) => completion.word);

    // Assert.
    expect(offered).toEqual(["phase:draft", "phase:done"]);
    expect(sorts).toEqual(["sort:", "sort:number"]);
  });

  it("offers the prefixes for nothing typed, and nothing again for a word written in full", () => {
    // Act.
    const empty = completionsOf("", labels, model().optionLists, sortKeys).map((completion) => completion.word);
    const full = completionsOf("phase:done", labels, model().optionLists, sortKeys);

    // Assert.
    expect(empty).toEqual(["~", "k", "kind:", "phase:", "item:", "sort:"]);
    expect(full).toEqual([]);
  });
});
