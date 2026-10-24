import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel, type HierarchySurfaceNode } from "./api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "./definition/hierarchySurfaceDefinition";
import { HierarchySurface } from "./HierarchySurface";

const definition: HierarchySurfaceDefinition = {
  levelType: "Section",
  entryType: "Point",
  labels: [],
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
  shortcutKeys: ["Enter"],
};

function node(id: string, role: "level" | "entry", parentId: string, title: string, description = ""): HierarchySurfaceNode {
  return { id, number: `[${id}]`, role, type: role === "level" ? "Section" : "Point", parentId, title, description, labels: [], references: [], collapsed: false };
}

/** A section holding two points, the first with a description, as the file was read for `cause`. */
function page(cause: string, firstTitle = "Take an order"): HierarchySurfaceModel {
  return {
    ...EMPTY_HIERARCHY_SURFACE,
    title: "Requirements",
    cause,
    nodes: [node("1", "level", "", "Orders"), node("2", "entry", "1", firstTitle, "Any open order can be taken."), node("3", "entry", "1", "Cancel an order")],
  };
}

/** Opens the title field of the node titled `title` and types `text` in it. */
function typeTitle(title: string, text: string): HTMLInputElement {
  fireEvent.doubleClick(screen.getByText(title, { selector: ".hs-title" }));
  const field = screen.getByRole("textbox") as HTMLInputElement;
  fireEvent.change(field, { target: { value: text } });
  return field;
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("typing a title or a description on the page", () => {
  it("sends the definition's operation with the typed title on Enter", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);

    // Act.
    const field = typeTitle("Take an order", "Take any order");
    expect(field.getAttribute("aria-label")).toBe("Title of [2]");
    fireEvent.keyDown(field, { key: "Enter" });

    // Assert.
    expect(onGesture).toHaveBeenCalledExactlyOnceWith("retitle", "2", { title: "Take any order" });
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("sends a description on Ctrl+Enter, keeping the line Enter adds", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.doubleClick(screen.getByText("Any open order can be taken."));
    const field = screen.getByRole("textbox", { name: "Description of [2]" });
    fireEvent.change(field, { target: { value: "Any open order.\nOnly once." } });
    fireEvent.keyDown(field, { key: "Enter" });
    expect(onGesture).not.toHaveBeenCalled();
    fireEvent.keyDown(field, { key: "Enter", ctrlKey: true });

    // Assert.
    expect(onGesture).toHaveBeenCalledExactlyOnceWith("redescribe", "2", { description: "Any open order.\nOnly once." });
  });

  it("offers a description to a selected entry that has none", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);
    expect(screen.queryByRole("button", { name: "Add a description" })).toBeNull();

    // Act.
    fireEvent.click(screen.getByText("Cancel an order", { selector: ".hs-title" }));
    fireEvent.click(screen.getByRole("button", { name: "Add a description" }));
    const field = screen.getByRole("textbox", { name: "Description of [3]" });
    fireEvent.change(field, { target: { value: "Until it ships." } });
    fireEvent.blur(field);

    // Assert.
    expect(onGesture).toHaveBeenCalledExactlyOnceWith("redescribe", "3", { description: "Until it ships." });
  });

  it("sends nothing for Escape, nor for a text left as it was", () => {
    // Arrange.
    const onGesture = vi.fn();
    render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.keyDown(typeTitle("Take an order", "Lost"), { key: "Escape" });
    fireEvent.keyDown(typeTitle("Take an order", "Take an order"), { key: "Enter" });

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(screen.getByText("Take an order", { selector: ".hs-title" })).toBeDefined();
  });

  it("opens no field on a read-only document", () => {
    // Act.
    render(<HierarchySurface model={{ ...page("open"), readOnlyReason: "Read-only." }} definition={definition} onGesture={vi.fn()} />);
    fireEvent.doubleClick(screen.getByText("Take an order", { selector: ".hs-title" }));

    // Assert.
    expect(screen.queryByRole("textbox")).toBeNull();
  });
});

describe("a change from outside while a field is open", () => {
  it("shows the file around the field and asks what to do with the text", () => {
    // Arrange.
    const onGesture = vi.fn();
    const { rerender } = render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);
    typeTitle("Cancel an order", "Cancel any order");

    // Act: another program renames the first point.
    rerender(<HierarchySurface model={page("outside", "Take an order by phone")} definition={definition} onGesture={onGesture} />);

    // Assert: the page is the file's, the text is still there, and the bar offers both sides.
    expect(screen.getByRole("alert").textContent).toContain("This file was changed outside the designer.");
    expect(screen.getByText("Take an order by phone", { selector: ".hs-title" })).toBeDefined();
    expect((screen.getByRole("textbox") as HTMLInputElement).value).toBe("Cancel any order");
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("drops the text for Show the file", () => {
    // Arrange.
    const onGesture = vi.fn();
    const { rerender } = render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);
    typeTitle("Cancel an order", "Cancel any order");
    rerender(<HierarchySurface model={page("outside", "Take an order by phone")} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Show the file" }));

    // Assert.
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.getByText("Cancel an order", { selector: ".hs-title" })).toBeDefined();
    expect(onGesture).not.toHaveBeenCalled();
  });

  it("keeps the field's focus when a bar button is pressed, and commits nothing when the focus leaves for the bar", () => {
    // Arrange.
    const onGesture = vi.fn();
    const { rerender } = render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);
    const field = typeTitle("Cancel an order", "Cancel any order");
    rerender(<HierarchySurface model={page("outside", "Take an order by phone")} definition={definition} onGesture={onGesture} />);
    const show = screen.getByRole("button", { name: "Show the file" });
    const keep = screen.getByRole("button", { name: "Keep my text" });

    // Act and assert: a press does not move the focus, so no blur commits the text a button is about to decide on.
    expect(fireEvent.mouseDown(show)).toBe(false);
    expect(fireEvent.mouseDown(keep)).toBe(false);

    // Act: the focus leaves for the bar all the same, as by Tab, and the file is chosen.
    fireEvent.blur(field, { relatedTarget: show });
    fireEvent.click(show);

    // Assert.
    expect(onGesture).not.toHaveBeenCalled();
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("keeps the text for Keep my text, and commits it as an edit of the file as it now is", () => {
    // Arrange.
    const onGesture = vi.fn();
    const { rerender } = render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);
    typeTitle("Cancel an order", "Cancel any order");
    rerender(<HierarchySurface model={page("outside", "Take an order by phone")} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Keep my text" }));
    expect(screen.queryByRole("alert")).toBeNull();
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Enter" });

    // Assert.
    expect(onGesture).toHaveBeenCalledExactlyOnceWith("retitle", "3", { title: "Cancel any order" });
  });

  it("still sends the text of a number the file no longer has, for the file to refuse", () => {
    // Arrange.
    const onGesture = vi.fn();
    const { rerender } = render(<HierarchySurface model={page("open")} definition={definition} onGesture={onGesture} />);
    typeTitle("Cancel an order", "Cancel any order");
    const gone = page("outside");
    rerender(<HierarchySurface model={{ ...gone, nodes: gone.nodes.filter((shown) => shown.id !== "3") }} definition={definition} onGesture={onGesture} />);

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Keep my text" }));
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Enter" });

    // Assert: the page cannot know the number is gone for good; the session refuses it with the sentence of R10.
    expect(onGesture).toHaveBeenCalledExactlyOnceWith("retitle", "3", { title: "Cancel any order" });
  });

  it("asks nothing for the page's own edit, nor when no field is open", () => {
    // Arrange.
    const { rerender } = render(<HierarchySurface model={page("open")} definition={definition} onGesture={vi.fn()} />);
    typeTitle("Cancel an order", "Cancel any order");

    // Act: the page's own edit of another point arrives while the field is open.
    rerender(<HierarchySurface model={page("edit", "Take any order")} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    expect(screen.queryByRole("alert")).toBeNull();
    expect((screen.getByRole("textbox") as HTMLInputElement).value).toBe("Cancel any order");

    // Act: the field closed, a change from outside.
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Escape" });
    rerender(<HierarchySurface model={page("outside", "Take an order by phone")} definition={definition} onGesture={vi.fn()} />);

    // Assert.
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
