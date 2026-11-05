import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode, type HierarchySurfaceNote } from "../api/hierarchySurfaceModel";
import { ADD_NOTE_ICON, type HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "../HierarchySurface";

// Made-up names throughout: the icon and the list know only what the definition tells them.
const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [],
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

function node(id: string, parentId: string, role: "level" | "entry", title: string, extra: Partial<HierarchySurfaceNode> = {}): HierarchySurfaceNode {
  return { id, number: `[${id}]`, role, type: role === "level" ? "Section" : "Point", parentId, title, description: "", labels: [], references: [], collapsed: false, ...extra };
}

function note(id: string, aboutId: string, kind: string, extra: Partial<HierarchySurfaceNote> = {}): HierarchySurfaceNote {
  return { id, kind, about: `[${aboutId}]`, aboutId, text: `note ${id}`, detail: "", resolved: false, references: [], ...extra };
}

/** The section 1 holds the points 2 and 3 and the section 4, which holds the point 5. */
function model(notes: readonly HierarchySurfaceNote[], extra: Partial<HierarchySurfaceModel> = {}): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Notes",
    nodes: [
      node("1", "", "level", "Orders"),
      node("2", "1", "entry", "Take an order"),
      node("3", "1", "entry", "Cancel an order"),
      node("4", "1", "level", "Payment"),
      node("5", "4", "entry", "Pay by card"),
    ],
    notes,
    ...extra,
  };
}

function boxOf(title: string): HTMLElement {
  return screen.getByText(title).closest(".hs-box") as HTMLElement;
}

/** The icon at the top right of a box: its notes' mark, or the way to add the first. */
function iconOf(title: string): HTMLElement {
  return boxOf(title).querySelector(":scope > .hs-box-side > .hs-box-icons > .hs-note-mark, :scope > .hs-box-side > .hs-box-icons > .hs-note-add") as HTMLElement;
}

/** Picks a note's kind in the list: the choice opened, and the kind clicked among its options. */
function chooseKind(list: HTMLElement, label: string, kind: string) {
  fireEvent.click(within(list).getByRole("combobox", { name: label }));
  fireEvent.click(within(within(list).getByRole("listbox", { name: label })).getByRole("option", { name: kind }));
}

function collapsed(id: string, of: HierarchySurfaceModel): HierarchySurfaceModel {
  return { ...of, nodes: of.nodes.map((each) => (each.id === id ? { ...each, collapsed: true } : each)) };
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("the icon of a box's notes", () => {
  it("offers the way to add the first note when there is none, at the box's top right", () => {
    // Act.
    render(<HierarchySurface model={model([])} definition={definition} onGesture={vi.fn()} />);

    // Assert: every box has it, first of the controls at its top right, which are laid out from right to left, so it
    // stands at the corner whatever else a box offers.
    for (const title of ["Orders", "Take an order", "Payment", "Pay by card"]) {
      const icon = iconOf(title);
      expect(icon.className).toContain(ADD_NOTE_ICON);
      expect(icon.className).toContain("hs-note-add");
      expect(icon.parentElement?.firstElementChild).toBe(icon);
    }
    expect(iconOf("Take an order").getAttribute("aria-label")).toBe("New note on [2]");
  });

  it("offers no way to add to a document that cannot be changed", () => {
    // Act.
    render(<HierarchySurface model={model([], { readOnlyReason: "Read only." })} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    expect(iconOf("Take an order")).toBeNull();
  });

  it("shows one open note by its kind's icon, and two of different kinds by the icon for several with their count", () => {
    // Act.
    render(
      <HierarchySurface
        model={model([note("n1", "2", "Aside"), note("n2", "3", "Doubt"), note("n3", "3", "Aside"), note("n4", "3", "Doubt", { resolved: true })])}
        definition={definition}
        onGesture={vi.fn()}
      />,
    );

    // Assert: the resolved note is not counted.
    const one = iconOf("Take an order");
    expect(one.className).toContain("hs-note-mark");
    expect(one.className).toContain("mdi-comment-text-outline");
    expect(one.textContent).toBe("");
    const two = iconOf("Cancel an order");
    expect(two.className).toContain("mdi-comment-multiple-outline");
    expect(two.textContent).toBe("2");
  });

  it("calls several open notes by the name the definition gives its list", () => {
    // Act.
    const remarks = { ...definition, notes: { ...definition.notes, words: { ...definition.notes.words, list: "Remarks" } } };
    render(<HierarchySurface model={model([note("n1", "3", "Doubt"), note("n2", "3", "Aside")])} definition={remarks} onGesture={vi.fn()} />);

    // Assert.
    expect(iconOf("Cancel an order").getAttribute("aria-label")).toBe("2 open remarks");
  });

  it("counts every open note inside a collapsed section on its icon, and only its own while it is open", () => {
    // Arrange.
    const notes = [note("n1", "2", "Aside"), note("n2", "5", "Doubt"), note("n3", "4", "Aside")];

    // Act.
    const { rerender } = render(<HierarchySurface model={collapsed("1", model(notes))} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    expect(iconOf("Orders").className).toContain("mdi-comment-multiple-outline");
    expect(iconOf("Orders").textContent).toBe("3");

    // Act.
    rerender(<HierarchySurface model={model(notes)} definition={definition} onGesture={vi.fn()} />);

    // Assert: open, it has none of its own, so it offers the way to add.
    expect(iconOf("Orders").className).toContain("hs-note-add");
  });
});

describe("the list of a box's notes", () => {
  it("opens from the icon and names each note's entry, its kind, text and detail, resolved ones too", () => {
    // Arrange.
    const notes = [note("n1", "2", "Aside", { detail: "Ask the shop." }), note("n2", "5", "Doubt", { resolved: true }), note("n3", "3", "Doubt")];
    render(<HierarchySurface model={collapsed("1", model(notes))} definition={definition} onGesture={vi.fn()} />);

    // Act.
    fireEvent.click(iconOf("Orders"));

    // Assert: a collapsed section lists everything inside it, in the file's order.
    const list = screen.getByRole("dialog", { name: "Notes of [1]" });
    const items = within(list).getAllByRole("listitem");
    expect(items.map((item) => item.querySelector(".hs-number")?.textContent)).toEqual(["[2]", "[5]", "[3]"]);
    expect(within(items[0]!).getByRole("img", { name: "Aside" }).className).toContain("mdi-comment-text-outline");
    expect(within(items[0]!).getByText("note n1")).toBeDefined();
    expect(within(items[0]!).getByText("Ask the shop.")).toBeDefined();
    expect(items[1]!.className).toContain("hs-note-resolved");
    expect(within(items[1]!).getByRole("button", { name: "Unsettle" })).toBeDefined();
    expect(within(items[2]!).getByRole("button", { name: "Settle" })).toBeDefined();
    expect(iconOf("Orders").getAttribute("aria-expanded")).toBe("true");
  });

  it("adds a note about the box's node, with the kind chosen and the text typed", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={model([])} definition={definition} onGesture={onGesture} />);
    fireEvent.click(iconOf("Take an order"));
    const list = screen.getByRole("dialog", { name: "Notes of [2]" });

    // Act.
    chooseKind(list, "Kind of the new one about [2]", "Aside");
    fireEvent.change(within(list).getByRole("textbox", { name: "New note about [2]" }), { target: { value: "Which shop?" } });
    fireEvent.keyDown(within(list).getByRole("textbox", { name: "New note about [2]" }), { key: "Enter" });

    // Assert: one gesture, and the box is not selected or its title opened by it.
    expect(onGesture).toHaveBeenCalledTimes(1);
    expect(onGesture).toHaveBeenCalledWith("remark", "2", { sort: "Aside", line: "Which shop?" });
  });

  it("adds nothing without a text", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={model([])} definition={definition} onGesture={onGesture} />);
    fireEvent.click(iconOf("Take an order"));
    const list = screen.getByRole("dialog");

    // Act.
    fireEvent.change(within(list).getByRole("textbox", { name: "New note about [2]" }), { target: { value: "  " } });
    fireEvent.click(within(list).getByRole("button", { name: "Add" }));

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("changes a note's kind, text and detail, resolves it, and removes it, each as the definition's operation", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={model([note("assessment/0", "2", "Doubt")])} definition={definition} onGesture={onGesture} />);
    fireEvent.click(iconOf("Take an order"));
    const list = screen.getByRole("dialog", { name: "Notes of [2]" });

    // Act.
    fireEvent.click(within(list).getByRole("button", { name: "Settle" }));
    fireEvent.click(within(list).getByRole("button", { name: "Strike" }));
    fireEvent.click(within(list).getByRole("button", { name: "Reword" }));
    chooseKind(list, "Kind of [2]", "Aside");
    fireEvent.change(within(list).getByRole("textbox", { name: "Text about [2]" }), { target: { value: "Settled by the shop" } });
    fireEvent.change(within(list).getByRole("textbox", { name: "Detail about [2]" }), { target: { value: "See the call." } });
    fireEvent.click(within(list).getByRole("button", { name: "Reword" }));

    // Assert.
    expect(onGesture.mock.calls).toEqual([
      ["settle", "assessment/0", {}],
      ["strike", "assessment/0", {}],
      ["reword", "assessment/0", { sort: "Aside", line: "Settled by the shop", more: "See the call." }],
    ]);
  });

  it("keeps its keys from the page, and closes on Escape and on a click outside", () => {
    // Arrange.
    const onGesture = vi.fn();
    const onShortcut = vi.fn(() => Promise.resolve(""));
    render(<HierarchySurface model={model([note("n1", "2", "Doubt")])} definition={definition} onGesture={onGesture} onShortcut={onShortcut} />);
    fireEvent.click(boxOf("Take an order"));
    fireEvent.click(iconOf("Take an order"));
    const list = screen.getByRole("dialog");

    // Act: Delete pressed in the list, on one of its buttons, is not the page's delete of the selected node.
    fireEvent.keyDown(within(list).getByRole("button", { name: "Strike" }), { key: "Delete" });
    fireEvent.keyDown(within(list).getByRole("button", { name: "Settle" }), { key: "Enter", ctrlKey: true });

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(onShortcut).not.toHaveBeenCalled();

    // Act.
    fireEvent.keyDown(within(list).getByRole("textbox", { name: "New note about [2]" }), { key: "Escape" });

    // Assert.
    expect(screen.queryByRole("dialog")).toBeNull();

    // Act.
    fireEvent.click(iconOf("Take an order"));
    fireEvent.pointerDown(boxOf("Pay by card"));

    // Assert.
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("closes on Escape when the focus is no longer in it, as after a removed note took its button away", () => {
    // Arrange.
    render(<HierarchySurface model={model([note("n1", "2", "Doubt")])} definition={definition} onGesture={vi.fn()} />);
    fireEvent.click(iconOf("Take an order"));
    expect(screen.getByRole("dialog")).toBeDefined();

    // Act.
    fireEvent.keyDown(document.body, { key: "Escape" });

    // Assert.
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("drops a change on Escape and keeps the list, with the focus in the change or not", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={model([note("n1", "2", "Doubt")])} definition={definition} onGesture={onGesture} />);
    fireEvent.click(iconOf("Take an order"));
    const list = screen.getByRole("dialog");
    fireEvent.click(within(list).getByRole("button", { name: "Reword" }));

    // Assert: the text is ready for typing.
    expect(document.activeElement).toBe(within(list).getByRole("textbox", { name: "Text about [2]" }));

    // Act.
    fireEvent.keyDown(document.body, { key: "Escape" });

    // Assert.
    expect(within(list).queryByRole("textbox", { name: "Text about [2]" })).toBeNull();
    expect(screen.getByRole("dialog")).toBeDefined();
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("stands above every pane, outside the page that would cut it off", () => {
    // Arrange.
    render(<HierarchySurface model={model([note("n1", "2", "Doubt")])} definition={definition} onGesture={vi.fn()} />);

    // Act.
    fireEvent.click(iconOf("Take an order"));

    // Assert: drawn at the document's top, not inside the page, whose edges cut off what stands outside them.
    const list = screen.getByRole("dialog", { name: "Notes of [2]" });
    expect(list.closest(".hs-page")).toBeNull();
    expect(list.parentElement).toBe(document.body);
  });

  it("offers a new note's kind with each kind's icon, its text over three lines, and Add below them", () => {
    // Arrange.
    render(<HierarchySurface model={model([])} definition={definition} onGesture={vi.fn()} />);
    fireEvent.click(iconOf("Take an order"));
    const list = screen.getByRole("dialog", { name: "Notes of [2]" });

    // Act.
    fireEvent.click(within(list).getByRole("combobox", { name: "Kind of the new one about [2]" }));

    // Assert: each kind with its icon to its left; the kind, then the text, then the add, one below the other.
    const options = within(within(list).getByRole("listbox")).getAllByRole("option");
    expect(options.map((option) => `${option.firstElementChild?.className} ${option.textContent}`)).toEqual([
      "mdi mdi-help-circle-outline Doubt",
      "mdi mdi-comment-text-outline Aside",
    ]);
    const text = within(list).getByRole("textbox", { name: "New note about [2]" });
    expect(text.tagName).toBe("TEXTAREA");
    expect(text.getAttribute("rows")).toBe("3");
    const add = within(list).getByRole("button", { name: "Add" });
    const order = Array.from(list.querySelector(".hs-note-new")?.children ?? [], (child) => child.className);
    expect(order).toEqual(["hs-kind", "hs-field hs-note-text-field", "hs-note-buttons"]);
    expect(add.parentElement?.className).toBe("hs-note-buttons");
  });

  it("offers a note's resolve, change and remove as icons, each saying what it does", () => {
    // Arrange.
    render(<HierarchySurface model={model([note("n1", "2", "Doubt")])} definition={definition} onGesture={vi.fn()} />);

    // Act.
    fireEvent.click(iconOf("Take an order"));

    // Assert.
    const item = within(screen.getByRole("dialog")).getAllByRole("listitem")[0]!;
    const buttons = within(item).getAllByRole("button");
    expect(buttons.map((button) => `${button.title}: ${button.textContent === "" && button.className.includes("mdi-")}`)).toEqual(["Settle: true", "Reword: true", "Strike: true"]);
  });

  it("only shows the notes of a document that cannot be changed", () => {
    // Arrange.
    render(<HierarchySurface model={model([note("n1", "2", "Doubt")], { readOnlyReason: "Read only." })} definition={definition} onGesture={vi.fn()} />);

    // Act.
    fireEvent.click(iconOf("Take an order"));

    // Assert.
    const list = screen.getByRole("dialog");
    expect(within(list).getByText("note n1")).toBeDefined();
    expect(within(list).queryAllByRole("button")).toHaveLength(0);
    expect(within(list).queryByRole("textbox")).toBeNull();
  });
});
