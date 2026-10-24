import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode, type HierarchySurfaceNote } from "./api/hierarchySurfaceModel";
import { FOCUS_ICON, HIDDEN_LEVELS_ICON, type HierarchySurfaceDefinition } from "./definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "./HierarchySurface";

const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [
    { label: "marks", prefix: "#", many: true, on: ["Section", "Point"] },
    { label: "phase", prefix: "phase:", many: false, on: ["Point"] },
  ],
  noteIcons: { Doubt: "mdi-help-circle-outline", Aside: "mdi-comment-text-outline" },
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
  shortcutKeys: ["Enter"],
};

function level(id: string, parentId: string, title: string, extra: Partial<HierarchySurfaceNode> = {}): HierarchySurfaceNode {
  return { id, number: `[${id}]`, role: "level", type: "Section", parentId, title, description: "", labels: [], references: [], collapsed: false, ...extra };
}

function entry(id: string, parentId: string, title: string, extra: Partial<HierarchySurfaceNode> = {}): HierarchySurfaceNode {
  return { id, number: `[${id}]`, role: "entry", type: "Point", parentId, title, description: "", labels: [], references: [], collapsed: false, ...extra };
}

function note(id: string, aboutId: string, kind: string, extra: Partial<HierarchySurfaceNote> = {}): HierarchySurfaceNote {
  return { id, kind, about: `[${aboutId}]`, aboutId, text: `note ${id}`, detail: "", resolved: false, references: [], ...extra };
}

/**
 * Three levels deep: 1 holds the point 2 and the section 3, which holds the point 4 and the section 5, which holds the
 * point 6. A second top section, 7, stands beside 1.
 */
function threeLevels(extra: Partial<HierarchySurfaceModel> = {}): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Requirements",
    nodes: [
      level("1", "", "Orders"),
      entry("2", "1", "Take an order", {
        description: "Any open order can be taken.",
        labels: [
          { label: "marks", values: ["core"] },
          { label: "phase", values: ["draft"] },
        ],
      }),
      level("3", "1", "Payment"),
      entry("4", "3", "Pay by card"),
      level("5", "3", "Refunds"),
      entry("6", "5", "Refund in full"),
      level("7", "", "Shipping"),
    ],
    optionLists: [{ label: "marks", options: [{ id: "marks/core", name: "core", colour: "green", implicit: false }] }],
    ...extra,
  };
}

/** The box of the node whose title is `title`. */
function boxOf(title: string): HTMLElement {
  return screen.getByText(title).closest(".hs-box") as HTMLElement;
}

/** Gives every element the page measures a width, as a browser would; jsdom measures none. */
function pageWidth(width: number) {
  vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(width);
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("the hierarchy surface", () => {
  it("draws the whole document as one page, with no tree beside it", () => {
    // Act.
    const { container } = render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={vi.fn()} />);

    // Assert: one page holds every node, once, and nothing else lists them.
    expect(container.querySelectorAll(".hs-page")).toHaveLength(1);
    const page = container.querySelector(".hs-page") as HTMLElement;
    for (const title of ["Orders", "Take an order", "Payment", "Pay by card", "Refunds", "Refund in full", "Shipping"]) {
      expect(within(page).getAllByText(title)).toHaveLength(1);
    }
    expect(container.querySelectorAll(".hs-box")).toHaveLength(7);
    expect(container.querySelector(".hs-trail")?.textContent).toBe("Requirements");
  });

  it("puts a level's requirements to its right and above its sub-items, beside its own box", () => {
    // Act.
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={vi.fn()} />);

    // Assert: the level's row is its box and then what stands to its right, entries first and the add control last.
    const row = boxOf("Orders").closest(".hs-level") as HTMLElement;
    const [own, content] = Array.from(row.children) as HTMLElement[];
    expect(own!.className).toBe("hs-level-box");
    expect(content!.className).toBe("hs-level-content");
    const right = Array.from(content!.children).map((child) => child.querySelector(".hs-title")?.textContent ?? child.className);
    expect(right).toEqual(["Take an order", "Payment", "hs-add"]);
  });

  it("shows the number beside the title, the tags below it and the description under them", () => {
    // Act.
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    const box = boxOf("Take an order");
    const parts = Array.from(box.children).map((child) => child.className);
    // The note icon stands first of the controls at the box's top right, which the rest flows beside (FR-015).
    expect(parts).toEqual(["hs-box-icons", "hs-box-header", "hs-tags", "hs-description"]);
    expect(box.querySelector(".hs-box-icons")?.firstElementChild?.className).toBe("mdi mdi-comment-plus-outline hs-note-add");
    const header = box.querySelector(".hs-box-header") as HTMLElement;
    expect(within(header).getByText("[2]").nextElementSibling?.textContent).toBe("Take an order");
    const tags = Array.from(box.querySelectorAll(".hs-tags .table-option-tag")).map((tag) => `${tag.textContent} ${tag.className} in ${tag.closest(".hs-look")?.className}`);
    // Each label in a look of its own: a label of many values a tag, one of one value a badge.
    expect(tags).toEqual(["core table-option-tag table-option-green in hs-look hs-look-tag", "draft table-option-tag table-option-default in hs-look hs-look-badge"]);
    expect(box.querySelector(".hs-description")?.textContent).toBe("Any open order can be taken.");
  });

  it("shows as many levels as fit and marks an item whose deeper levels do not", () => {
    // Arrange: room for two levels beside the entries' least width.
    pageWidth(760);

    // Act.
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={vi.fn()} />);

    // Assert: the third level is not drawn; the second carries the mark, the first does not.
    expect(screen.queryByText("Refunds")).toBeNull();
    expect(screen.getByText("Pay by card")).toBeDefined();
    expect(boxOf("Payment").querySelector(`.hs-hidden-levels .${HIDDEN_LEVELS_ICON}`)).not.toBeNull();
    expect(boxOf("Payment").querySelector(".hs-hidden-levels")?.getAttribute("title")).toBe("1 more level");
    expect(boxOf("Orders").querySelector(".hs-hidden-levels")).toBeNull();
  });

  it("focuses an item by its mark, with a trail leading back, and sends nothing", () => {
    // Arrange.
    pageWidth(760);
    const onGesture = vi.fn();
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.click(boxOf("Payment").querySelector(".hs-hidden-levels") as HTMLElement);

    // Assert: the page starts at the item, its hidden level is drawn, and the trail names what is above it.
    expect(screen.queryByText("Orders", { selector: ".hs-title" })).toBeNull();
    expect(screen.queryByText("Shipping")).toBeNull();
    expect(screen.getByText("Refund in full")).toBeDefined();
    const trail = screen.getByRole("navigation", { name: "Trail" });
    expect(Array.from(trail.querySelectorAll("button, [aria-current]")).map((step) => step.textContent)).toEqual(["Requirements", "[1] Orders", "[3] Payment"]);
    expect(onGesture).not.toHaveBeenCalled();

    // Act: back through the trail, one level and then the whole page.
    fireEvent.click(within(trail).getByRole("button", { name: "[1] Orders" }));
    expect(screen.getByText("Orders", { selector: ".hs-title" })).toBeDefined();
    expect(screen.queryByText("Shipping")).toBeNull();
    fireEvent.click(within(screen.getByRole("navigation", { name: "Trail" })).getByRole("button", { name: "Requirements" }));

    // Assert.
    expect(screen.getByText("Shipping")).toBeDefined();
    expect(screen.getByRole("navigation", { name: "Trail" }).textContent).toBe("Requirements");
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("shows the levels an item's mark hides at every level, also where only one level fits", () => {
    // Arrange: room for one level beside the entries' least width.
    pageWidth(600);
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={vi.fn()} />);
    expect(screen.queryByText("Payment")).toBeNull();

    // Act: the mark of the top level; then the mark of the level it brought.
    fireEvent.click(boxOf("Orders").querySelector(".hs-hidden-levels") as HTMLElement);

    // Assert: its sub-levels stand in its place, since beside it they cannot fit.
    expect(boxOf("Payment")).toBeDefined();
    expect(screen.getByText("Pay by card")).toBeDefined();

    // Act.
    fireEvent.click(boxOf("Payment").querySelector(".hs-hidden-levels") as HTMLElement);

    // Assert: and so on, a level deeper; the trail leads back to the item.
    expect(screen.getByText("Refund in full")).toBeDefined();
    expect(screen.queryByText("Pay by card")).toBeNull();
    fireEvent.click(within(screen.getByRole("navigation", { name: "Trail" })).getByRole("button", { name: "[3] Payment" }));
    expect(screen.getByText("Pay by card")).toBeDefined();
  });

  it("always shows the trail, down to the level the selected node stands in, naming levels only", () => {
    // Arrange.
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={vi.fn()} />);
    const steps = () => Array.from(screen.getByRole("navigation", { name: "Trail" }).querySelectorAll("button, [aria-current]"), (step) => step.textContent);

    // Act and assert.
    fireEvent.click(boxOf("Pay by card"));
    expect(steps()).toEqual(["Requirements", "[1] Orders", "[3] Payment"]);
    fireEvent.click(boxOf("Refunds"));
    expect(steps()).toEqual(["Requirements", "[1] Orders", "[3] Payment", "[5] Refunds"]);
    fireEvent.click(boxOf("Shipping"));
    expect(steps()).toEqual(["Requirements", "[7] Shipping"]);
  });

  it("focuses an item by the control in its header", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={onGesture} />);

    // Act.
    const control = within(boxOf("Refunds")).getByRole("button", { name: "Focus on [5] Refunds" });
    expect(control.querySelector(`.${FOCUS_ICON}`)).not.toBeNull();
    fireEvent.click(control);

    // Assert.
    expect(screen.queryByText("Pay by card")).toBeNull();
    expect(screen.getByText("Refund in full")).toBeDefined();
    expect(screen.getByRole("navigation", { name: "Trail" }).textContent).toContain("[5] Refunds");
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("offers no focus on an entry", () => {
    // Act.
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    expect(boxOf("Take an order").querySelector(".hs-focus")).toBeNull();
  });

  it("shows a collapsed entry as its number, title, tags and note mark only", () => {
    // Arrange.
    const model = threeLevels({ notes: [note("n1", "2", "Doubt")] });
    const nodes = model.nodes.map((node) => (node.id === "2" ? { ...node, collapsed: true } : node));

    // Act.
    render(<HierarchySurface model={{ ...model, nodes }} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    const box = boxOf("Take an order");
    expect(Array.from(box.children).map((child) => child.className)).toEqual(["hs-box-icons", "hs-box-header", "hs-tags"]);
    expect(within(box).getByText("[2]")).toBeDefined();
    expect(box.querySelector(".hs-note-mark.mdi-help-circle-outline")).not.toBeNull();
    expect(within(box).getByRole("button", { name: "Expand [2] Take an order" }).getAttribute("aria-expanded")).toBe("false");
  });

  it("hides what a collapsed level holds, and counts the notes inside it on its mark", () => {
    // Arrange.
    const model = threeLevels({ notes: [note("n1", "4", "Doubt"), note("n2", "6", "Aside"), note("n3", "6", "Aside", { resolved: true })] });
    const nodes = model.nodes.map((node) => (node.id === "3" ? { ...node, collapsed: true } : node));

    // Act.
    render(<HierarchySurface model={{ ...model, nodes }} definition={definition} onGesture={vi.fn()} />);

    // Assert: two open notes inside, so the icon for several, with their count; the resolved one is not counted.
    expect(screen.queryByText("Pay by card")).toBeNull();
    expect(screen.queryByText("Refunds")).toBeNull();
    const mark = boxOf("Payment").querySelector(".hs-note-mark") as HTMLElement;
    expect(mark.className).toContain("mdi-comment-multiple-outline");
    expect(mark.textContent).toBe("2");
  });

  it("sends the definition's operation to collapse a numbered node", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={threeLevels()} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.click(within(boxOf("Payment")).getByRole("button", { name: "Collapse [3] Payment" }));

    // Assert: the document keeps it, so the page waits for the model that says so.
    expect(onGesture).toHaveBeenCalledWith("fold", "3", {});
    expect(screen.getByText("Pay by card")).toBeDefined();
  });

  it("collapses a node with no number of its own on the page alone", () => {
    // Arrange.
    const onGesture = vi.fn();
    const model = threeLevels();
    const nodes = model.nodes.map((node) => (node.id === "2" ? { ...node, id: "line/4", number: "[?]", description: "Unnumbered." } : node));
    render(<HierarchySurface model={{ ...model, nodes }} definition={definition} onGesture={onGesture} />);
    expect(screen.getByText("Unnumbered.")).toBeDefined();

    // Act.
    fireEvent.click(within(boxOf("Take an order")).getByRole("button", { name: "Collapse [?] Take an order" }));

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(screen.queryByText("Unnumbered.")).toBeNull();
  });

  it("shows one open note by its kind's icon", () => {
    // Act.
    render(<HierarchySurface model={threeLevels({ notes: [note("n1", "4", "Aside")] })} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    const mark = boxOf("Pay by card").querySelector(".hs-note-mark") as HTMLElement;
    expect(mark.className).toContain("mdi-comment-text-outline");
    expect(mark.getAttribute("aria-label")).toBe("Aside: note n1");
    expect(boxOf("Payment").querySelector(".hs-note-mark")).toBeNull();
  });

  it("lists the findings and says why a read-only document cannot be edited", () => {
    // Arrange.
    const onSelectNode = vi.fn();
    const model = threeLevels({
      readOnlyReason: "This document was written by a later version.",
      findings: [{ code: "std.missingId", severity: "warning", message: "Refund in full has no number.", nodeId: "6", line: 12 }],
    });

    // Act.
    render(<HierarchySurface model={model} definition={definition} onGesture={vi.fn()} onSelectNode={onSelectNode} />);
    fireEvent.click(screen.getByRole("button", { name: "Refund in full has no number." }));

    // Assert.
    expect(screen.getByRole("status").textContent).toContain("This document was written by a later version.");
    expect(within(screen.getByRole("list", { name: "Findings" })).getByText("line 12")).toBeDefined();
    expect(onSelectNode).toHaveBeenCalledWith("6");
    expect(boxOf("Refund in full").getAttribute("aria-selected")).toBe("true");
  });

  it("collapses on the page alone while the document is read-only", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={threeLevels({ readOnlyReason: "Read-only." })} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.click(within(boxOf("Payment")).getByRole("button", { name: "Collapse [3] Payment" }));

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(screen.queryByText("Pay by card")).toBeNull();
  });
});
