import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "../HierarchySurface";

// Made-up names throughout: the field and its list know only what the definition tells them.
const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [
    {
      label: "marks",
      prefix: "~",
      many: true,
      on: ["Section", "Point"],
      edit: {
        operations: { add: "mark", remove: "unmark", rename: "rebrand", recolour: "tint", move: "shuffle", delete: "erase" },
        params: { name: "word", colour: "hue", after: "behind" },
        words: { list: "Marks", add: "Add mark", remove: "Unmark" },
        deletion: { threshold: 1, message: (name: string, count: number) => `${count} things carry ~${name}. Erase it?` },
      },
    },
    { label: "phase", prefix: "phase:", many: false, on: ["Point"] },
  ],
  noteIcons: { Doubt: "mdi-help-circle-outline" },
  severalNotesIcon: "mdi-comment-multiple-outline",
  operations: {
    toggleCollapsed: "fold",
    setTitle: "retitle",
    setDescription: "redescribe",
    addTopLevel: { operation: "open", label: "New section" },
    addEntry: { operation: "jot", label: "New point" },
    addLevel: { operation: "nest", label: "New section" },
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

function node(id: string, parentId: string, role: "level" | "entry", title: string, marks: readonly string[] = [], extra: Partial<HierarchySurfaceNode> = {}): HierarchySurfaceNode {
  return {
    id,
    number: `[${id}]`,
    role,
    type: role === "level" ? "Section" : "Point",
    parentId,
    title,
    description: "",
    labels: [{ label: "marks", values: marks }, ...(role === "entry" ? [{ label: "phase", values: ["draft"] }] : [])],
    references: [],
    collapsed: false,
    ...extra,
  };
}

/** The section 1 holds the points 2 and 3; 1 carries fast, 2 carries fast and cheap, spelt as the list does not. */
function model(extra: Partial<HierarchySurfaceModel> = {}): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Marks",
    nodes: [node("1", "", "level", "Orders", ["fast"]), node("2", "1", "entry", "Take an order", ["fast", "cheap"]), node("3", "1", "entry", "Cancel an order")],
    optionLists: [
      {
        label: "marks",
        options: [
          { id: "mark/fast", name: "fast", colour: "green", implicit: false },
          { id: "mark/cheap", name: "Cheap", colour: "blue", implicit: false },
          { id: "mark/slow", name: "slow", colour: "default", implicit: false },
        ],
      },
      { label: "phase", options: [{ id: "phase/draft", name: "draft", colour: "gray", implicit: false }] },
    ],
    ...extra,
  };
}

function boxOf(title: string): HTMLElement {
  return screen.getByText(title).closest(".hs-box") as HTMLElement;
}

/** Opens the field's list of the box titled `title` from the control in its header. */
function openList(title: string, number: string): HTMLElement {
  fireEvent.click(within(boxOf(title)).getByRole("button", { name: `Add mark to ${number}` }));
  return screen.getByRole("dialog", { name: `Marks of ${number}` });
}

/** Opens the list's editor from the field of the box titled `title`. */
function openEditor(title: string, number: string): HTMLElement {
  fireEvent.click(within(openList(title, number)).getByRole("button", { name: "Edit marks" }));
  return screen.getByRole("dialog", { name: "Options of Marks" });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("a label's field", () => {
  it("shows the values a node carries in their list's colours, below the title and before the other labels", () => {
    // Act.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);

    // Assert: the carried spelling, the member's colour, and the other label after them in the same row.
    const row = boxOf("Take an order").querySelector(".hs-tags") as HTMLElement;
    const tags = Array.from(row.querySelectorAll(".table-option-tag"), (tag) => `${tag.textContent} ${tag.className.replace("table-option-tag table-option-", "")}`);
    expect(tags).toEqual(["fast green", "cheap blue", "draft gray"]);
    expect(row.previousElementSibling?.className).toBe("hs-box-header");
  });

  it("offers the way to add a value in the box's header, and nothing to change on a document that cannot be changed", () => {
    // Act.
    const { rerender } = render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);
    const parent = within(boxOf("Cancel an order")).getByRole("button", { name: "Add mark to [3]" }).parentElement?.className;
    rerender(<HierarchySurface model={model({ readOnlyReason: "Read only." })} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    expect(parent).toBe("hs-box-header");
    expect(within(boxOf("Cancel an order")).queryByRole("button", { name: "Add mark to [3]" })).toBeNull();
    expect(within(boxOf("Take an order")).queryByRole("button", { name: /^Marks of/ })).toBeNull();
  });

  it("searches the list, which has the focus, and picks a member with the list's spelling, and opens from the values too", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act: CHE narrows the list to Cheap, which Enter picks for the third box.
    const list = openList("Cancel an order", "[3]");
    const focused = document.activeElement === within(list).getByRole("combobox");
    fireEvent.change(within(list).getByRole("combobox", { name: "Add mark to [3]" }), { target: { value: "CHE" } });
    const offered = within(list).getAllByRole("option").map((option) => option.textContent);
    fireEvent.keyDown(within(list).getByRole("combobox"), { key: "Enter" });
    fireEvent.keyDown(within(boxOf("Take an order")).getByRole("button", { name: "Marks of [2]: fast, cheap" }), { key: "Enter" });

    // Assert.
    expect(focused).toBe(true);
    expect(offered).toEqual(["Cheap", "Create “CHE”"]);
    expect(onGesture).toHaveBeenCalledWith("mark", "3", { word: "Cheap" });
    expect(document.activeElement).toBe(within(screen.getByRole("dialog", { name: "Marks of [2]" })).getByRole("combobox"));
  });

  it("creates a value from text that names no member, as typed", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const list = openList("Cancel an order", "[3]");
    fireEvent.change(within(list).getByRole("combobox"), { target: { value: "brand new" } });
    fireEvent.keyDown(within(list).getByRole("combobox"), { key: "Enter" });

    // Assert: the offer to create, taken with Enter; the name is written as the specification says, by the backend.
    expect(onGesture).toHaveBeenCalledWith("mark", "3", { word: "brand new" });
  });

  it("takes a value off by its cross, and by picking it again, with the spelling the node carries", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const list = openList("Take an order", "[2]");
    const chosen = within(list).getAllByRole("option").filter((option) => option.getAttribute("aria-selected") === "true").map((option) => option.textContent);
    fireEvent.click(within(list).getByRole("button", { name: "Unmark fast" }));
    fireEvent.click(within(list).getByRole("option", { name: "Cheap" }));

    // Assert.
    expect(chosen).toEqual(["fast", "Cheap"]);
    expect(onGesture).toHaveBeenNthCalledWith(1, "unmark", "2", { word: "fast" });
    expect(onGesture).toHaveBeenNthCalledWith(2, "unmark", "2", { word: "cheap" });
  });

  it("keeps its keys from the page, and closes on Escape and on a press outside", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act: Delete in the list is not the page's delete of the selected box; Escape closes; so does a press outside.
    let list = openList("Take an order", "[2]");
    fireEvent.keyDown(within(list).getByRole("button", { name: "Unmark fast" }), { key: "Delete" });
    fireEvent.keyDown(within(list).getByRole("combobox"), { key: "Escape" });
    const afterEscape = screen.queryByRole("dialog", { name: "Marks of [2]" });
    list = openList("Take an order", "[2]");
    fireEvent.pointerDown(document.body);

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(afterEscape).toBeNull();
    expect(screen.queryByRole("dialog", { name: "Marks of [2]" })).toBeNull();
  });
});

describe("a label's list, edited", () => {
  it("opens the editor where the field's list was on the screen", () => {
    // Arrange: only the list has a place; the field around it draws no box, so it measures nothing.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
      return this.classList.contains("hs-tag-list") ? new DOMRect(240, 310, 300, 120) : new DOMRect(0, 0, 0, 0);
    });

    // Act.
    const editor = openEditor("Take an order", "[2]");

    // Assert.
    expect(`${editor.style.left} ${editor.style.top}`).toBe("240px 310px");
  });

  it("renames and recolours a member, each one gesture naming the member", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const editor = openEditor("Take an order", "[2]");
    const name = within(editor).getByRole("textbox", { name: "Name of fast" });
    fireEvent.change(name, { target: { value: "quick" } });
    fireEvent.keyDown(name, { key: "Enter" });
    fireEvent.change(within(editor).getByRole("combobox", { name: "Colour of Cheap" }), { target: { value: "red" } });

    // Assert: the field's list closed for the editor, which adds nothing, since a member is made by carrying it.
    expect(onGesture).toHaveBeenNthCalledWith(1, "rebrand", "mark/fast", { word: "quick" });
    expect(onGesture).toHaveBeenNthCalledWith(2, "tint", "mark/cheap", { hue: "red" });
    expect(screen.queryByRole("dialog", { name: "Marks of [2]" })).toBeNull();
    expect(within(editor).queryByRole("textbox", { name: "New option" })).toBeNull();
  });

  it("puts a member in its place as the member it goes after, or first with none", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const editor = openEditor("Take an order", "[2]");
    fireEvent.click(within(editor).getByRole("button", { name: "Move fast down" }));
    fireEvent.click(within(editor).getByRole("button", { name: "Move slow up" }));
    fireEvent.click(within(editor).getByRole("button", { name: "Move Cheap up" }));

    // Assert.
    expect(onGesture.mock.calls).toEqual([
      ["shuffle", "mark/fast", { behind: "mark/cheap" }],
      ["shuffle", "mark/slow", { behind: "mark/fast" }],
      ["shuffle", "mark/cheap", { behind: "" }],
    ]);
  });

  it("deletes a member nothing carries at once, and asks first, in the definition's words, for one that is carried", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const editor = openEditor("Take an order", "[2]");
    fireEvent.click(within(editor).getByRole("button", { name: "Delete slow" }));
    const sentAtOnce = onGesture.mock.calls.length;
    fireEvent.click(within(editor).getByRole("button", { name: "Delete fast" }));
    const question = within(editor).getByRole("alertdialog", { name: "Delete fast?" });
    const askedBeforeSending = onGesture.mock.calls.length;
    fireEvent.click(within(question).getByRole("button", { name: "Delete" }));

    // Assert: two boxes carry fast.
    expect(sentAtOnce).toBe(1);
    expect(question.textContent).toContain("2 things carry ~fast. Erase it?");
    expect(askedBeforeSending).toBe(1);
    expect(onGesture.mock.calls).toEqual([
      ["erase", "mark/slow", {}],
      ["erase", "mark/fast", {}],
    ]);
  });
});
