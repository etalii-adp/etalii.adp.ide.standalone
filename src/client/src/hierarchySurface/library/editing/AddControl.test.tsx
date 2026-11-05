import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { pointer } from "@client/canvas/library/testing/canvasHarness";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode, type HierarchySurfaceNote } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";
import { HierarchySurface, type HierarchySurfaceProps } from "../HierarchySurface";
import { indexOf } from "../layout";
import { addedNode } from "./AddControl";
import { dropOf, slotIndex } from "./useDragEntry";

const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [],
  noteIcons: { Doubt: "mdi-help-circle-outline" },
  severalNotesIcon: "mdi-comment-multiple-outline",
  operations: {
    toggleCollapsed: "fold",
    setTitle: "retitle",
    setDescription: "redescribe",
    addTopLevel: { operation: "open", label: "New section", icon: "mdi-plus" },
    addEntry: { operation: "jot", label: "New point", icon: "mdi-note-plus-outline" },
    addLevel: { operation: "nest", label: "New section", icon: "mdi-folder-plus-outline" },
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
  deletion: { threshold: 1, message: (number, count) => `Drop ${number}? ${count} things concern it.` },
  shortcutKeys: ["Enter"],
};

function level(id: string, parentId: string, title: string, extra: Partial<HierarchySurfaceNode> = {}): HierarchySurfaceNode {
  return { id, number: `[${id}]`, role: "level", type: "Section", parentId, title, description: "", labels: [], references: [], collapsed: false, ...extra };
}

function entry(id: string, parentId: string, title: string, extra: Partial<HierarchySurfaceNode> = {}): HierarchySurfaceNode {
  return { id, number: `[${id}]`, role: "entry", type: "Point", parentId, title, description: "", labels: [], references: [], collapsed: false, ...extra };
}

function note(id: string, aboutId: string, extra: Partial<HierarchySurfaceNote> = {}): HierarchySurfaceNote {
  return { id, kind: "Doubt", about: `[${aboutId}]`, aboutId, text: "", detail: "", resolved: false, references: [], ...extra };
}

/** 1 holds the points 2 and 3 and the section 4, which holds the point 5; 6 stands at the top beside 1. */
function page(nodes?: HierarchySurfaceNode[], extra: Partial<HierarchySurfaceModel> = {}): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Plan",
    nodes: nodes ?? [level("1", "", "Orders"), entry("2", "1", "Take"), entry("3", "1", "Keep"), level("4", "1", "Payment"), entry("5", "4", "Pay"), level("6", "", "Shipping")],
    ...extra,
  };
}

function show(model: HierarchySurfaceModel, props: Partial<HierarchySurfaceProps> = {}) {
  const onGesture = vi.fn((): unknown => Promise.resolve(""));
  const view = render(<HierarchySurface model={model} definition={definition} onGesture={onGesture} {...props} />);
  const update = (next: HierarchySurfaceModel) => view.rerender(<HierarchySurface model={next} definition={definition} onGesture={onGesture} {...props} />);
  return { onGesture, update };
}

function boxOf(title: string): HTMLElement {
  return screen.getByText(title).closest(".hs-box") as HTMLElement;
}

function pageElement(): HTMLElement {
  return document.querySelector(".hs-page") as HTMLElement;
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("adding a node", () => {
  it("offers the controls at the end of every level and of the page, and adds without a dialog", () => {
    // Arrange.
    const { onGesture } = show(page());

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "New point in [4]" }));
    fireEvent.click(screen.getByRole("button", { name: "New section in [1]" }));
    fireEvent.click(screen.getByRole("button", { name: "New section" }));

    // Assert: every level has both, the page one, each sent at once, and nothing asked.
    expect(screen.getAllByRole("button", { name: /^New point in / })).toHaveLength(3);
    expect(onGesture.mock.calls).toEqual([
      ["jot", "4", {}],
      ["nest", "1", {}],
      ["open", "", {}],
    ]);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("offers each add as its icon alone, its label the tooltip", () => {
    // Act.
    show(page());

    // Assert.
    const control = screen.getByRole("button", { name: "New point in [4]" });
    expect(control.textContent).toBe("");
    expect(control.title).toBe("New point");
    expect(control.querySelector(".mdi-note-plus-outline")).not.toBeNull();
    expect(screen.getByRole("button", { name: "New section in [4]" }).querySelector(".mdi-folder-plus-outline")).not.toBeNull();
  });

  it("opens the added node's title for typing when it arrives, and selects it", async () => {
    // Arrange.
    const onSelectNode = vi.fn();
    const { onGesture, update } = show(page(), { onSelectNode });

    // Act: added at the end of 4, and the edit's model arrives with it.
    fireEvent.click(screen.getByRole("button", { name: "New point in [4]" }));
    await act(async () => undefined);
    update(page([...page().nodes.slice(0, 5), entry("7", "4", "New point"), level("6", "", "Shipping")], { cause: "edit" }));

    // Assert.
    expect(onGesture).toHaveBeenCalledWith("jot", "4", {});
    const field = screen.getByRole<HTMLInputElement>("textbox", { name: "Title of [7]" });
    expect(field.value).toBe("New point");
    expect(document.activeElement).toBe(field);
    expect(onSelectNode).toHaveBeenLastCalledWith("7");
  });

  it("opens nothing when the add was refused, though a node arrives later", async () => {
    // Arrange.
    const { update } = show(page(), { onGesture: vi.fn(() => Promise.resolve("No more points here.")) });

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "New point in [4]" }));
    await act(async () => undefined);
    update(page([...page().nodes, entry("7", "6", "Written by another program")], { cause: "outside" }));

    // Assert.
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("sends Ctrl+Enter and Ctrl+Shift+Enter on a selected node to the backend, then opens what it added", async () => {
    // Arrange.
    const onShortcut = vi.fn(() => Promise.resolve(""));
    const { update } = show(page(), { onShortcut });
    fireEvent.click(boxOf("Keep"));

    // Act.
    fireEvent.keyDown(pageElement(), { key: "Enter", ctrlKey: true });
    fireEvent.keyDown(pageElement(), { key: "Enter", ctrlKey: true, shiftKey: true });
    fireEvent.keyDown(pageElement(), { key: "Enter" });
    await act(async () => undefined);
    update(page([level("1", "", "Orders"), entry("2", "1", "Take"), entry("3", "1", "Keep"), entry("8", "1", "New point"), level("4", "1", "Payment"), entry("5", "4", "Pay"), level("6", "", "Shipping")], { cause: "edit" }));

    // Assert: the chord as it was pressed, the backend's to match; plain Enter is no chord.
    expect(onShortcut.mock.calls).toEqual([
      ["3", { key: "Enter", ctrl: true, shift: false, alt: false, meta: false }],
      ["3", { key: "Enter", ctrl: true, shift: true, alt: false, meta: false }],
    ]);
    expect(screen.getByRole<HTMLInputElement>("textbox", { name: "Title of [8]" }).value).toBe("New point");
  });

  it("finds the added node though an edit numbered a placeholder named by its line", () => {
    // Arrange: a placeholder, line/9, is given a number by the edit that adds 7.
    const before = [level("1", "", "Orders"), entry("line/9", "1", "Not numbered yet")];
    const after = [level("1", "", "Orders"), entry("12", "1", "Not numbered yet"), entry("13", "1", "New point")];

    // Act.
    const added = addedNode(before, after);

    // Assert.
    expect(added?.id).toBe("13");
    expect(addedNode(after, after)).toBeUndefined();
  });
});

describe("dragging a node", () => {
  /** Puts `element` under the pointer, its box 40 pixels high from `top`. */
  function under(element: HTMLElement, top = 0) {
    document.elementFromPoint = vi.fn(() => element);
    element.getBoundingClientRect = () => ({ left: 0, right: 200, top, bottom: top + 40, width: 200, height: 40, x: 0, y: top, toJSON: () => ({}) });
  }

  function dragBy(grip: Element, toY: number) {
    fireEvent(grip, pointer("pointerdown", { pointerId: 1, button: 0, clientX: 5, clientY: 5 }));
    fireEvent(grip, pointer("pointermove", { pointerId: 1, clientX: 5, clientY: toY }));
    fireEvent(grip, pointer("pointerup", { pointerId: 1, button: 0, clientX: 5, clientY: toY }));
  }

  function gripOf(title: string): Element {
    return boxOf(title).querySelector(".hs-grip") as Element;
  }

  it("moves a level with everything in it, after a level from its lower half and into it from its upper one", () => {
    // Arrange.
    const { onGesture } = show(page());

    // Act.
    under(boxOf("Shipping"));
    dragBy(gripOf("Payment"), 30);
    dragBy(gripOf("Payment"), 10);

    // Assert: one move each, naming the level; what it holds goes with it.
    expect(onGesture.mock.calls).toEqual([
      ["shift", "4", { section: "", behind: "6" }],
      ["shift", "4", { section: "6", behind: "" }],
    ]);
  });

  it("moves an entry alone, after or before another entry, or into a level", () => {
    // Arrange.
    const { onGesture } = show(page());

    // Act.
    under(boxOf("Take"));
    dragBy(gripOf("Pay"), 30);
    under(boxOf("Keep"));
    dragBy(gripOf("Pay"), 10);
    under(boxOf("Shipping"));
    dragBy(gripOf("Take"), 30);

    // Assert.
    expect(onGesture.mock.calls).toEqual([
      ["slide", "5", { section: "1", behind: "2" }],
      ["slide", "5", { section: "1", behind: "2" }],
      ["slide", "2", { section: "6", behind: "" }],
    ]);
  });

  it("shows the dragged node where it would stand while it is dragged, and drops it there from that place", () => {
    // Arrange.
    const { onGesture } = show(page());
    const grip = gripOf("Pay");

    // Act: dragged over the lower half of Take, not yet dropped.
    under(boxOf("Take"));
    fireEvent(grip, pointer("pointerdown", { pointerId: 1, button: 0, clientX: 5, clientY: 5 }));
    fireEvent(grip, pointer("pointermove", { pointerId: 1, clientX: 5, clientY: 30 }));

    // Assert: its place drawn after Take, with its number and title, and the node itself still in its own, faded.
    const slot = document.querySelector(".hs-drop-slot") as HTMLElement;
    expect(slot.textContent).toBe("[5] Pay");
    expect(slot.previousElementSibling?.querySelector(".hs-title")?.textContent).toBe("Take");
    expect(screen.getByText("Pay", { selector: ".hs-title" }).closest(".hs-entry")?.className).toContain("hs-dragging");
    expect(onGesture).not.toHaveBeenCalled();

    // Act: the place moved the boxes away from under the pointer; over it, the drop stays, and it is made there.
    under(slot);
    fireEvent(grip, pointer("pointermove", { pointerId: 1, clientX: 5, clientY: 34 }));
    expect(document.querySelector(".hs-drop-slot")).not.toBeNull();
    fireEvent(grip, pointer("pointerup", { pointerId: 1, button: 0, clientX: 5, clientY: 34 }));

    // Assert.
    expect(onGesture.mock.calls).toEqual([["slide", "5", { section: "1", behind: "2" }]]);
    expect(document.querySelector(".hs-drop-slot")).toBeNull();
  });

  it("shows a drop without a sibling where the move puts it: to the front in its own level, after the last of its kind in another", () => {
    // Arrange: 1 shows 2, 3 and 4.
    const index = indexOf(page());
    const shown = (index.children.get("1") ?? []).slice();
    const hint = (id: string, into: string) => ({ nodeId: into, kind: "into" as const, dragged: index.byId.get(id)!, drop: { into, after: "" } });

    // Act and assert: an entry of its own level to the front, one from another after the last entry; a level of its
    // own level before the first level, one from another at the end.
    expect(slotIndex(shown, hint("3", "1"))).toBe(0);
    expect(slotIndex(shown, hint("5", "1"))).toBe(2);
    expect(slotIndex(shown, hint("4", "1"))).toBe(2);
    expect(slotIndex(shown, hint("6", "1"))).toBe(3);
  });

  it("does not drop a level into itself or into what it holds", () => {
    // Arrange.
    const index = indexOf(page());
    const orders = index.byId.get("1")!;

    // Act and assert.
    expect(dropOf(index, orders, orders, false)).toBeNull();
    expect(dropOf(index, orders, index.byId.get("4")!, false)).toBeNull();
    expect(dropOf(index, orders, index.byId.get("5")!, true)).toBeNull();
    expect(dropOf(index, index.byId.get("2")!, index.byId.get("2")!, true)).toBeNull();
  });
});

describe("deleting a node", () => {
  it("deletes at once a node nothing concerns", () => {
    // Arrange.
    const { onGesture } = show(page());
    fireEvent.click(boxOf("Keep"));

    // Act.
    fireEvent.keyDown(pageElement(), { key: "Delete" });

    // Assert.
    expect(onGesture).toHaveBeenCalledWith("drop", "3", {});
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("asks first, in the definition's words, when it holds anything or is named, and deletes only on Delete", () => {
    // Arrange: 4 holds 5; a note is about it, another names it, and 2's description names it.
    const named = page(
      [
        level("1", "", "Orders"),
        entry("2", "1", "Take", { references: [{ start: 0, length: 3, targetId: "4" }] }),
        entry("3", "1", "Keep"),
        level("4", "1", "Payment"),
        entry("5", "4", "Pay"),
        level("6", "", "Shipping"),
      ],
      { notes: [note("0", "4"), note("1", "6", { references: [{ start: 0, length: 3, targetId: "4" }] }), note("2", "6")] },
    );
    const { onGesture } = show(named);
    fireEvent.click(boxOf("Payment"));

    // Act: asked, cancelled, asked again and confirmed.
    fireEvent.keyDown(pageElement(), { key: "Delete" });
    const question = screen.getByRole("dialog");
    expect(question.textContent).toContain("Drop [4]? 4 things concern it.");
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onGesture).not.toHaveBeenCalled();
    fireEvent.keyDown(pageElement(), { key: "Delete" });
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    // Assert.
    expect(onGesture.mock.calls).toEqual([["drop", "4", {}]]);
  });

  it("offers a selected box's delete at its bottom right, and always asks first, saying what it takes", () => {
    // Arrange.
    const { onGesture } = show(page());
    expect(screen.queryByRole("button", { name: "Delete [3] Keep" })).toBeNull();
    fireEvent.click(boxOf("Keep"));

    // Act: nothing concerns it, and it still asks.
    fireEvent.click(screen.getByRole("button", { name: "Delete [3] Keep" }));

    // Assert.
    expect(screen.getByRole("dialog").textContent).toContain("Delete [3] Keep? Nothing else concerns it.");
    expect(onGesture).not.toHaveBeenCalled();

    // Act: confirmed; then a level that holds something asks in the definition's words.
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    fireEvent.click(boxOf("Payment"));
    fireEvent.click(screen.getByRole("button", { name: "Delete [4] Payment" }));

    // Assert.
    expect(screen.getByRole("dialog").textContent).toContain("Drop [4]? 1 things concern it.");
    expect(onGesture.mock.calls).toEqual([["drop", "3", {}]]);
  });

  it("offers no delete on a document that cannot be changed", () => {
    // Arrange.
    show(page(undefined, { readOnlyReason: "Read only." }));

    // Act.
    fireEvent.click(boxOf("Keep"));

    // Assert.
    expect(screen.queryByRole("button", { name: "Delete [3] Keep" })).toBeNull();
  });

  it("leaves Delete and Enter to a field that is open", () => {
    // Arrange.
    const onShortcut = vi.fn(() => Promise.resolve(""));
    const { onGesture } = show(page(), { onShortcut });
    fireEvent.click(boxOf("Keep"));
    fireEvent.doubleClick(screen.getByText("Keep"));
    const field = screen.getByRole("textbox", { name: "Title of [3]" });

    // Act.
    fireEvent.keyDown(field, { key: "Delete" });
    fireEvent.keyDown(field, { key: "Enter", ctrlKey: true });

    // Assert: the Enter committed the field, and nothing else was sent.
    expect(onShortcut).not.toHaveBeenCalled();
    expect(onGesture.mock.calls.filter((call: unknown[]) => call[0] !== "retitle")).toEqual([]);
  });
});
