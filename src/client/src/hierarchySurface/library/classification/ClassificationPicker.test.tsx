import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "../HierarchySurface";

// Made-up names throughout: the picker knows only what the definition tells it.
const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [
    { label: "marks", prefix: "~", many: true, on: ["Section", "Point"] },
    {
      label: "phase",
      prefix: "phase:",
      many: false,
      on: ["Point"],
      pick: {
        operations: { set: "stage", clear: "unstage", add: "coin", rename: "rebrand", recolour: "tint", move: "shuffle", delete: "erase" },
        params: { value: "phase", name: "word", colour: "hue", after: "behind" },
        words: { list: "Phases", value: "Phase", clear: "No phase" },
        deletion: { threshold: 1, message: (name: string, count: number) => `${count} points are ${name}. Erase it?` },
      },
    },
  ],
  noteIcons: {},
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

function node(id: string, parentId: string, role: "level" | "entry", title: string, phase = ""): HierarchySurfaceNode {
  return {
    id,
    number: `[${id}]`,
    role,
    type: role === "level" ? "Section" : "Point",
    parentId,
    title,
    description: "",
    labels: [
      { label: "marks", values: role === "entry" ? ["fast"] : [] },
      { label: "phase", values: phase === "" ? [] : [phase] },
    ],
    references: [],
    collapsed: false,
  };
}

/** The section 1 holds 2, which is DONE, spelt as the list does not, 3 without a phase, and 4, which is done too. */
function model(extra: Partial<HierarchySurfaceModel> = {}): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Points",
    nodes: [node("1", "", "level", "Orders"), node("2", "1", "entry", "Take an order", "DONE"), node("3", "1", "entry", "Cancel an order"), node("4", "1", "entry", "Refund an order", "done")],
    optionLists: [
      {
        label: "phase",
        options: [
          { id: "phase/draft", name: "draft", colour: "gray", implicit: false },
          { id: "phase/done", name: "done", colour: "green", implicit: false },
          { id: "phase/gone", name: "gone", colour: "red", implicit: false },
        ],
      },
    ],
    ...extra,
  };
}

function boxOf(title: string): HTMLElement {
  return screen.getByText(title).closest(".hs-box") as HTMLElement;
}

/** Opens the picker of the box titled `title` from its value, selecting the box first. */
function openPicker(title: string, number: string, value: string): HTMLElement {
  fireEvent.click(screen.getByText(title));
  fireEvent.click(within(boxOf(title)).getByRole("button", { name: `Phase of ${number}: ${value}` }));
  return screen.getByRole("dialog", { name: `Phase of ${number}` });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("a label of one value", () => {
  it("shows the value in its member's colour after the other labels' tags, and the way to give one only on the selected box", () => {
    // Arrange.
    render(<HierarchySurface model={model()} definition={definition} onGesture={vi.fn()} />);
    const unselected = within(boxOf("Cancel an order")).queryByRole("button", { name: /^Phase of/ });

    // Act.
    fireEvent.click(screen.getByText("Cancel an order"));

    // Assert: the carried spelling in the colour of the member it names in any case.
    const tags = Array.from(boxOf("Take an order").querySelectorAll(".table-option-tag"), (tag) => `${tag.textContent} ${tag.className.replace("table-option-tag table-option-", "")}`);
    expect(tags).toEqual(["fast default", "DONE green"]);
    expect(unselected).toBeNull();
    expect(within(boxOf("Cancel an order")).getByRole("button", { name: "Phase of [3]: none" }).textContent).toBe("Phase");
  });

  it("offers nothing to change on a document that cannot be changed, and still shows the value", () => {
    // Act.
    render(<HierarchySurface model={model({ readOnlyReason: "Read only." })} definition={definition} onGesture={vi.fn()} />);
    fireEvent.click(screen.getByText("Cancel an order"));

    // Assert.
    expect(screen.queryByRole("button", { name: /^Phase of/ })).toBeNull();
    expect(boxOf("Take an order").textContent).toContain("DONE");
  });

  it("picks a member from the searchable list, which has the focus, as one gesture naming the node, and nothing for the value it has", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const list = openPicker("Take an order", "[2]", "DONE");
    const focused = document.activeElement === within(list).getByRole("combobox");
    const chosen = within(list).getAllByRole("option").filter((option) => option.getAttribute("aria-selected") === "true").map((option) => option.textContent);
    fireEvent.click(within(list).getByRole("option", { name: "done" }));
    const closed = screen.queryByRole("dialog", { name: "Phase of [2]" });
    fireEvent.change(within(openPicker("Take an order", "[2]", "DONE")).getByRole("combobox"), { target: { value: "dr" } });
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Phase of [2]" }), { key: "Enter" });

    // Assert: no offer to create a member here, which the list's editor does.
    expect(focused).toBe(true);
    expect(chosen).toEqual(["done"]);
    expect(closed).toBeNull();
    expect(onGesture.mock.calls).toEqual([["stage", "2", { phase: "draft" }]]);
  });

  it("clears the value, and offers no clear without one", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.click(within(openPicker("Take an order", "[2]", "DONE")).getByRole("button", { name: "No phase" }));
    const empty = openPicker("Cancel an order", "[3]", "none");

    // Assert.
    expect(onGesture.mock.calls).toEqual([["unstage", "2", {}]]);
    expect(within(empty).queryByRole("button", { name: "No phase" })).toBeNull();
  });

  it("keeps its keys from the page, and closes on a press outside", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act: Delete in the list is not the page's delete of the selected box.
    const list = openPicker("Take an order", "[2]", "DONE");
    fireEvent.keyDown(within(list).getByRole("button", { name: "No phase" }), { key: "Delete" });
    fireEvent.pointerDown(document.body);

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog", { name: "Phase of [2]" })).toBeNull();
  });
});

describe("a label of one value's list, edited", () => {
  function openEditor(): HTMLElement {
    fireEvent.click(within(openPicker("Take an order", "[2]", "DONE")).getByRole("button", { name: "Edit phases" }));
    return screen.getByRole("dialog", { name: "Options of Phases" });
  }

  it("adds a member on the document, and renames, recolours and moves one, each one gesture naming the member", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const editor = openEditor();
    fireEvent.change(within(editor).getByRole("textbox", { name: "New option" }), { target: { value: "review" } });
    fireEvent.keyDown(within(editor).getByRole("textbox", { name: "New option" }), { key: "Enter" });
    const name = within(editor).getByRole("textbox", { name: "Name of draft" });
    fireEvent.change(name, { target: { value: "sketch" } });
    fireEvent.keyDown(name, { key: "Enter" });
    fireEvent.change(within(editor).getByRole("combobox", { name: "Colour of done" }), { target: { value: "blue" } });
    fireEvent.click(within(editor).getByRole("button", { name: "Move gone up" }));

    // Assert.
    expect(onGesture.mock.calls).toEqual([
      ["coin", "document", { word: "review" }],
      ["rebrand", "phase/draft", { word: "sketch" }],
      ["tint", "phase/done", { hue: "blue" }],
      ["shuffle", "phase/gone", { behind: "phase/draft" }],
    ]);
  });

  it("deletes a member nothing carries at once, and asks first, in the definition's words, for one that is carried in any case", () => {
    // Arrange.
    const onGesture = vi.fn(() => "");
    render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} />);

    // Act.
    const editor = openEditor();
    fireEvent.click(within(editor).getByRole("button", { name: "Delete gone" }));
    fireEvent.click(within(editor).getByRole("button", { name: "Delete done" }));
    const question = within(editor).getByRole("alertdialog", { name: "Delete done?" });
    const askedBeforeSending = onGesture.mock.calls.length;
    fireEvent.click(within(question).getByRole("button", { name: "Delete" }));

    // Assert: DONE and done are two points.
    expect(question.textContent).toContain("2 points are done. Erase it?");
    expect(askedBeforeSending).toBe(1);
    expect(onGesture.mock.calls).toEqual([
      ["erase", "phase/gone", {}],
      ["erase", "phase/done", {}],
    ]);
  });
});
