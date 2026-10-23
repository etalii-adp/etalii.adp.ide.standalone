import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "../HierarchySurface";

// Made-up names throughout: the lookup, the boxes and the reveal know only what the model and the definition say.
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

function node(id: string, parentId: string, role: "level" | "entry", title: string, extra: Partial<HierarchySurfaceNode> = {}): HierarchySurfaceNode {
  return { id, number: `N-${id}`, role, type: role === "level" ? "Section" : "Point", parentId, title, description: "", labels: [], references: [], collapsed: false, ...extra };
}

/**
 * Orders (1) holds Take an order (2) and the collapsed Payment (3), which holds the collapsed Refunds (4) with Refund in
 * full (5). Shipping (6) holds Ship it (7), whose description refers to 5, and a point with no number of its own.
 */
function model(collapsed = true): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Shop",
    nodes: [
      node("1", "", "level", "Orders"),
      node("2", "1", "entry", "Take an order", { description: "Was [N-99] once.", references: [{ start: 4, length: 6, targetId: "" }] }),
      node("3", "1", "level", "Payment", { collapsed }),
      node("4", "3", "level", "Refunds", { collapsed }),
      node("5", "4", "entry", "Refund in full"),
      node("6", "", "level", "Shipping"),
      node("7", "6", "entry", "Ship it", { description: "After [N-5] at the latest.", references: [{ start: 6, length: 5, targetId: "5" }] }),
      node("line/40", "6", "entry", "Unnumbered"),
    ],
  };
}

/** The box whose title is `title`; a reference box shows the same words. */
function boxOf(title: string): HTMLElement {
  const own = screen.getAllByText(title).find((element) => element.classList.contains("hs-title"));
  return own?.closest(".hs-box") as HTMLElement;
}

/** Opens the description of `title` for typing, by a double-click on it. */
function openDescription(title: string, text: string): HTMLTextAreaElement {
  fireEvent.doubleClick(boxOf(title).querySelector(".hs-description") ?? within(boxOf(title)).getByText(text));
  return screen.getByRole("textbox", { name: /^Description of / }) as HTMLTextAreaElement;
}

/** Types one character at the end of a field, as a browser reports it: the new value and the caret after it. */
function typeAtEnd(field: HTMLTextAreaElement | HTMLInputElement, character: string) {
  const value = field.value + character;
  field.setSelectionRange(field.value.length, field.value.length);
  fireEvent.change(field, { target: { value, selectionStart: value.length, selectionEnd: value.length } });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("a reference typed in a description", () => {
  it("opens the lookup on #, of every numbered level and entry of the document, narrowed by number or title", () => {
    // Arrange.
    render(<HierarchySurface model={model(false)} definition={definition} onGesture={vi.fn()} />);
    const field = openDescription("Ship it", "After");

    // Act.
    typeAtEnd(field, "#");

    // Assert: the lookup has the focus, and lists every node with a number, by number and title.
    const search = screen.getByRole("combobox", { name: "Refer to" });
    expect(document.activeElement).toBe(search);
    const options = () => screen.getAllByRole("option").map((option) => option.textContent);
    expect(options()).toEqual(["N-1 Orders", "N-2 Take an order", "N-3 Payment", "N-4 Refunds", "N-5 Refund in full", "N-6 Shipping", "N-7 Ship it"]);

    // Act.
    fireEvent.change(search, { target: { value: "refund" } });

    // Assert.
    expect(options()).toEqual(["N-4 Refunds", "N-5 Refund in full"]);

    // Act.
    fireEvent.change(search, { target: { value: "n-2" } });

    // Assert.
    expect(options()).toEqual(["N-2 Take an order"]);
  });

  it("writes the pick as its bracketed number in place of the #, and keeps the field open until it is committed", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={model(false)} definition={definition} onGesture={onGesture} />);
    const field = openDescription("Ship it", "After");
    typeAtEnd(field, " ");
    typeAtEnd(field, "#");

    // Act.
    const search = screen.getByRole("combobox", { name: "Refer to" });
    fireEvent.change(search, { target: { value: "refunds" } });
    fireEvent.keyDown(search, { key: "Enter" });

    // Assert: the lookup is gone, the text holds the reference, and nothing was sent yet.
    expect(screen.queryByRole("combobox", { name: "Refer to" })).toBeNull();
    const open = screen.getByRole("textbox", { name: /^Description of / }) as HTMLTextAreaElement;
    expect(open.value).toBe("After [N-5] at the latest. [N-4]");
    expect(document.activeElement).toBe(open);
    expect(open.selectionStart).toBe(open.value.length);
    expect(onGesture).not.toHaveBeenCalled();

    // Act.
    fireEvent.keyDown(open, { key: "Enter", ctrlKey: true });

    // Assert.
    expect(onGesture).toHaveBeenCalledWith("redescribe", "7", { description: "After [N-5] at the latest. [N-4]" });
  });

  it("goes back to the text on Escape in the lookup, with the # kept and nothing committed", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={model(false)} definition={definition} onGesture={onGesture} />);
    const field = openDescription("Ship it", "After");
    typeAtEnd(field, "#");

    // Act.
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Refer to" }), { key: "Escape" });

    // Assert.
    expect(screen.queryByRole("combobox", { name: "Refer to" })).toBeNull();
    const open = screen.getByRole("textbox", { name: /^Description of / }) as HTMLTextAreaElement;
    expect(open.value).toBe("After [N-5] at the latest.#");
    expect(document.activeElement).toBe(open);
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("commits the text when the focus leaves the lookup for the page, and opens no lookup in a title", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={model(false)} definition={definition} onGesture={onGesture} />);
    const field = openDescription("Ship it", "After");
    typeAtEnd(field, "#");
    const search = screen.getByRole("combobox", { name: "Refer to" });

    // Act: the focus goes from the field to the lookup, which is no leaving; then from the lookup to the page.
    fireEvent.blur(field, { relatedTarget: search });
    expect(onGesture).not.toHaveBeenCalled();
    fireEvent.blur(search, { relatedTarget: document.body });

    // Assert.
    expect(onGesture).toHaveBeenCalledWith("redescribe", "7", { description: "After [N-5] at the latest.#" });

    // Act.
    fireEvent.doubleClick(boxOf("Shipping").querySelector(".hs-title")!);
    typeAtEnd(screen.getByRole("textbox", { name: /^Title of / }) as HTMLInputElement, "#");

    // Assert.
    expect(screen.queryByRole("combobox", { name: "Refer to" })).toBeNull();
  });
});

describe("a reference in a description", () => {
  it("stands as a box with its target's number and current title, and one that names nothing as written, marked", () => {
    // Act.
    render(<HierarchySurface model={model(false)} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    const box = within(boxOf("Ship it")).getByRole("button", { name: "N-5 Refund in full" });
    expect(box.className).toContain("hs-reference");
    expect(boxOf("Ship it").querySelector(".hs-description")?.textContent).toBe("After N-5 Refund in full at the latest.");
    const broken = within(boxOf("Take an order")).getByRole("img", { name: "[N-99] names nothing in this document" });
    expect(broken.className).toContain("hs-reference-broken");
    expect(broken.textContent).toBe("[N-99]");
  });

  it("shows its target when selected: the focus that keeps it in sight, its collapsed levels expanded, the file not written", () => {
    // Arrange: the page focused on Shipping, so Orders and all in it are out of sight.
    const onGesture = vi.fn();
    const onSelectNode = vi.fn();
    const { rerender } = render(<HierarchySurface model={model()} definition={definition} onGesture={onGesture} onSelectNode={onSelectNode} />);
    fireEvent.click(within(boxOf("Shipping")).getByRole("button", { name: "Focus on N-6 Shipping" }));
    expect(screen.queryByText("Orders")).toBeNull();

    // Act.
    fireEvent.click(within(boxOf("Ship it")).getByRole("button", { name: "N-5 Refund in full" }));

    // Assert: the whole page again, and only the collapse of the two levels that hide the target is sent.
    expect(screen.queryByRole("navigation", { name: "Trail" })).toBeNull();
    expect(onGesture.mock.calls).toEqual([
      ["fold", "3", {}],
      ["fold", "4", {}],
    ]);
    expect(onSelectNode).toHaveBeenLastCalledWith("5");

    // Act: the model in which the registration keeps them expanded.
    act(() => rerender(<HierarchySurface model={model(false)} definition={definition} onGesture={onGesture} onSelectNode={onSelectNode} />));

    // Assert.
    expect(boxOf("Refund in full").getAttribute("aria-selected")).toBe("true");
  });

  it("does not open the description for typing when its box is selected", () => {
    // Arrange.
    render(<HierarchySurface model={model(false)} definition={definition} onGesture={vi.fn()} />);

    // Act.
    fireEvent.doubleClick(within(boxOf("Ship it")).getByRole("button", { name: "N-5 Refund in full" }));

    // Assert.
    expect(screen.queryByRole("textbox")).toBeNull();
  });
});
