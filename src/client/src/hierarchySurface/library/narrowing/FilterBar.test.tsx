import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "../HierarchySurface";

// Made-up names throughout: the bar knows only what the definition tells it.
const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [
    { label: "marks", prefix: "~", many: true, on: ["Section", "Point"] },
    { label: "phase", prefix: "phase:", many: false, on: ["Point"] },
  ],
  noteIcons: {},
  severalNotesIcon: "mdi-comment-multiple-outline",
  operations: {
    toggleCollapsed: "fold",
    setTitle: "retitle",
    setDescription: "redescribe",
    addTopLevel: { operation: "open", label: "New section", icon: "mdi-plus" },
    addEntry: { operation: "jot", label: "New point", icon: "mdi-plus" },
    addLevel: { operation: "nest", label: "New section", icon: "mdi-plus" },
    moveLevel: "shift",
    moveEntry: "slide",
    delete: "drop",
  },
  notes: {
    operations: { add: "remark", change: "reword", resolve: "settle", remove: "strike" },
    params: { kind: "sort", text: "line", detail: "more" },
    words: { list: "Notes", add: "New note", change: "Reword", resolve: "Settle", reopen: "Unsettle", remove: "Strike" },
  },
  moveParams: { into: "section", after: "behind" },
  deletion: { threshold: 1, message: (number: string, count: number) => `Drop ${number}? ${count} things concern it.` },
  filters: {
    operations: { store: "keep", rename: "relabel", remove: "forget" },
    params: { name: "called", query: "words" },
    sortKeys: [{ key: "number", label: "By number" }],
    entries: "points",
  },
  shortcutKeys: ["Enter"],
};

function node(id: string, parentId: string, role: "level" | "entry", title: string, marks: readonly string[] = [], phase = ""): HierarchySurfaceNode {
  return {
    id,
    number: `[${id}]`,
    role,
    type: role === "level" ? "Section" : "Point",
    parentId,
    title,
    description: "",
    labels: [
      { label: "marks", values: marks },
      { label: "phase", values: phase === "" ? [] : [phase] },
    ],
    references: [],
    collapsed: false,
  };
}

/** Orders (1) holds 3, 2 and Refunds (4) with 5; Stock (6) holds 7. Two of four points carry fast; one filter is stored. */
function model(extra: Partial<HierarchySurfaceModel> = {}): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Points",
    nodes: [
      node("1", "", "level", "Orders"),
      node("3", "1", "entry", "Take an order", ["fast"], "done"),
      node("2", "1", "entry", "Cancel an order", [], "draft"),
      node("4", "1", "level", "Refunds"),
      node("5", "4", "entry", "Refund in full", ["fast"]),
      node("6", "", "level", "Stock"),
      node("7", "6", "entry", "Count the stock", [], "draft"),
    ],
    optionLists: [
      { label: "phase", options: [{ id: "phase/draft", name: "draft", colour: "gray", implicit: false }, { id: "phase/done", name: "done", colour: "green", implicit: false }] },
    ],
    filters: [{ id: "filter/Quick ones", name: "Quick ones", query: "~fast" }],
    ...extra,
  };
}

function titles(): string[] {
  return Array.from(document.querySelectorAll(".hs-box .hs-title"), (title) => title.textContent ?? "");
}

function bar(): HTMLElement {
  return screen.getByRole("search", { name: "Narrow and sort" });
}

function type(text: string) {
  fireEvent.change(within(bar()).getByRole("searchbox", { name: "Filter" }), { target: { value: text } });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("the filter bar", () => {
  it("is offered only by a definition that declares filters", () => {
    // Act.
    render(<HierarchySurface model={model()} definition={{ ...definition, filters: undefined }} onGesture={vi.fn()} />);

    // Assert.
    expect(screen.queryByRole("search")).toBeNull();
  });

  it("narrows the page as the query is typed, keeping the levels that contain what stays, and counts the entries", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);
    const before = within(bar()).getByRole("status").textContent;

    // Act.
    type("~FAST");

    // Assert: narrowing writes nothing (FR-049).
    expect(before).toBe("4 points");
    expect(titles()).toEqual(["Orders", "Take an order", "Refunds", "Refund in full"]);
    expect(within(bar()).getByRole("status").textContent).toBe("2 of 4 points");
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("selects levels by a title pattern, with everything in them", () => {
    // Act.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);
    type("item:REF*");

    // Assert.
    expect(titles()).toEqual(["Orders", "Refunds", "Refund in full"]);
  });

  it("sorts the entries within each level from the menu, writing the sort into the query and nothing to the file", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);
    const offered = within(bar()).getAllByRole("option").map((option) => option.textContent);

    // Act.
    fireEvent.change(within(bar()).getByRole("combobox", { name: "Sort" }), { target: { value: "number" } });
    const byNumber = titles();
    const sortWord = (within(bar()).getByRole("searchbox", { name: "Filter" }) as HTMLInputElement).value;
    fireEvent.change(within(bar()).getByRole("combobox", { name: "Sort" }), { target: { value: "phase" } });

    // Assert: the levels keep their place, the entries within Orders sort; a point without a phase comes last.
    expect(offered.slice(0, 3)).toEqual(["File order", "By number", "phase"]);
    expect(sortWord).toBe("sort:number");
    expect(byNumber).toEqual(["Orders", "Cancel an order", "Take an order", "Refunds", "Refund in full", "Stock", "Count the stock"]);
    expect(within(bar()).getByRole("searchbox", { name: "Filter" })).toHaveProperty("value", "sort:phase");
    expect(titles().slice(0, 3)).toEqual(["Orders", "Cancel an order", "Take an order"]);
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("keeps the query's other words when the sort changes, and takes the sort out for the file's order", () => {
    // Arrange.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);
    const query = () => (within(bar()).getByRole("searchbox", { name: "Filter" }) as HTMLInputElement).value;

    // Act.
    type("~fast sort:phase item:ord*");
    fireEvent.change(within(bar()).getByRole("combobox", { name: "Sort" }), { target: { value: "number" } });
    const changed = query();
    fireEvent.change(within(bar()).getByRole("combobox", { name: "Sort" }), { target: { value: "" } });

    // Assert.
    expect(changed).toBe("~fast sort:number item:ord*");
    expect(query()).toBe("~fast item:ord*");
  });

  it("applies a stored filter by picking it, and clears the query", () => {
    // Arrange.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);

    // Act.
    fireEvent.change(within(bar()).getByRole("combobox", { name: "Stored filters" }), { target: { value: "filter/Quick ones" } });
    const applied = titles();
    fireEvent.click(within(bar()).getByRole("button", { name: "Clear the filter" }));

    // Assert.
    expect(applied).toEqual(["Orders", "Take an order", "Refunds", "Refund in full"]);
    expect(titles()).toHaveLength(7);
  });

  it("stores the applied query under a name, as one gesture on the document", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    type(" phase:draft sort:number ");
    fireEvent.click(within(bar()).getByRole("button", { name: /Store/ }));
    const name = within(bar()).getByRole("textbox", { name: "Name of the filter" });
    fireEvent.change(name, { target: { value: " Drafts " } });
    fireEvent.keyDown(name, { key: "Enter" });

    // Assert.
    expect(onGesture.mock.calls).toEqual([["keep", "document", { called: "Drafts", words: "phase:draft sort:number" }]]);
  });

  it("renames and removes the stored filter that is applied, naming it", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    type("~fast");
    const storeOffered = within(bar()).queryByRole("button", { name: /Store/ });
    fireEvent.click(within(bar()).getByRole("button", { name: "Rename Quick ones" }));
    const name = within(bar()).getByRole("textbox", { name: "New name of Quick ones" });
    fireEvent.change(name, { target: { value: "Fast ones" } });
    fireEvent.keyDown(name, { key: "Enter" });
    fireEvent.click(within(bar()).getByRole("button", { name: "Remove Quick ones" }));

    // Assert: a filter already stored is not offered for storing again.
    expect(storeOffered).toBeNull();
    expect(onGesture.mock.calls).toEqual([
      ["relabel", "filter/Quick ones", { called: "Fast ones" }],
      ["forget", "filter/Quick ones", {}],
    ]);
  });

  it("offers nothing to store, rename or remove on a document that cannot be changed, and still narrows", () => {
    // Act.
    render(<HierarchySurface model={model({ readOnlyReason: "Read only." })} definition={definition} onGesture={vi.fn()} />);
    type("~fast");

    // Assert.
    expect(within(bar()).queryByRole("button", { name: /Store|Rename|Remove/ })).toBeNull();
    expect(titles()).toHaveLength(4);
  });
});

describe("completing the word being typed", () => {
  const withMarks = { label: "marks", options: [{ id: "marks/fast", name: "fast", colour: "default", implicit: true }] };

  /** Types `text` into the focused field, with the caret at its end. */
  function typeFocused(text: string) {
    const field = within(bar()).getByRole("searchbox", { name: "Filter" }) as HTMLInputElement;
    field.focus();
    fireEvent.change(field, { target: { value: text, selectionStart: text.length } });
    return field;
  }

  it("lists the members of a label's list with its prefix under the field, and Tab takes the one marked and applies it", () => {
    // Arrange.
    const page = model();
    render(<HierarchySurface model={{ ...page, optionLists: [...page.optionLists, withMarks] }} definition={definition} onGesture={vi.fn()} />);
    const field = typeFocused("~f");
    const offered = within(screen.getByRole("listbox", { name: "Completions" })).getAllByRole("option").map((option) => option.textContent);

    // Act.
    fireEvent.keyDown(field, { key: "Tab" });

    // Assert.
    expect(offered).toEqual(["~fastmarks"]);
    expect(field.value).toBe("~fast ");
    expect(titles()).toEqual(["Orders", "Take an order", "Refunds", "Refund in full"]);
  });

  it("keeps a prefix open for its name, then offers the members, and a press takes one", () => {
    // Arrange.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);
    const field = typeFocused("~fast ph");
    fireEvent.keyDown(field, { key: "Enter" });
    const afterPrefix = field.value;
    fireEvent.change(field, { target: { value: afterPrefix, selectionStart: afterPrefix.length } });

    // Act.
    fireEvent.pointerDown(within(screen.getByRole("listbox", { name: "Completions" })).getByRole("option", { name: /phase:done/ }));

    // Assert.
    expect(afterPrefix).toBe("~fast phase:");
    expect(field.value).toBe("~fast phase:done ");
    expect(titles()).toEqual(["Orders", "Take an order"]);
  });

  it("offers nothing after Escape, and nothing while the field does not have the focus", () => {
    // Arrange.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);
    const field = typeFocused("ph");

    // Act.
    fireEvent.keyDown(field, { key: "Escape" });
    const afterEscape = screen.queryByRole("listbox", { name: "Completions" });
    fireEvent.change(field, { target: { value: "pha", selectionStart: 3 } });
    const afterTyping = screen.queryByRole("listbox", { name: "Completions" });
    fireEvent.blur(field);

    // Assert.
    expect(afterEscape).toBeNull();
    expect(afterTyping).not.toBeNull();
    expect(screen.queryByRole("listbox", { name: "Completions" })).toBeNull();
  });
});

describe("a finding the filter hides", () => {
  it("stays listed, and selecting it shows its entry by taking out the words that hide it", () => {
    // Arrange: Count the stock carries no fast.
    const findings = [{ code: "std.duplicate", severity: "warning" as const, message: "Count the stock twice.", nodeId: "7", line: 9 }];
    render(<HierarchySurface model={model({ findings })} definition={definition} onGesture={vi.fn()} />);
    type("~fast sort:number");
    const hidden = !titles().includes("Count the stock");

    // Act.
    fireEvent.click(within(screen.getByRole("list", { name: "Findings" })).getByRole("button", { name: "Count the stock twice." }));

    // Assert: the sort stays; the entry is shown and selected.
    expect(hidden).toBe(true);
    expect(within(bar()).getByRole("searchbox", { name: "Filter" })).toHaveProperty("value", "sort:number");
    expect(titles()).toContain("Count the stock");
    expect(screen.getByText("Count the stock").closest(".hs-box")?.classList.contains("hs-selected")).toBe(true);
  });
});
