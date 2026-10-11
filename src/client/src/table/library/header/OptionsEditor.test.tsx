import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import type { TableColumn } from "../api/tableModel";
import type { TableDefinition } from "../definition/tableDefinition";
import { columnMenuGroups } from "./columnActions";
import { OptionsEditor } from "./OptionsEditor";

/**
 * A column's options, edited: every change raises one gesture naming the column and the option,
 * and the entry that opens the panel is offered only where there are options to edit.
 */

const definition: TableDefinition = {
  kinds: {
    text: { icon: "mdi-format-text", label: "Text", editor: "text" },
    selection: { icon: "mdi-chevron-down-circle-outline", label: "Selection", editor: "option" },
    tags: { icon: "mdi-format-list-bulleted", label: "Tags", editor: "options" },
  },
  columnActions: ["rename", "options", "delete"],
};

function column(kind: string): TableColumn {
  return {
    id: "p2",
    name: "Country",
    kind,
    options: [
      { id: "o1", name: "Netherlands", color: "blue" },
      { id: "o2", name: "Belgium", color: "mauve" },
    ],
    width: 0,
    visible: true,
    isTitle: false,
    wraps: false,
    settings: {},
  };
}

function open() {
  const raise = vi.fn();
  const onClose = vi.fn();
  render(<OptionsEditor column={column("selection")} position={{ x: 10, y: 20 }} raise={raise} onClose={onClose} />);
  return { raise, onClose };
}

afterEach(cleanup);

describe("OptionsEditor", () => {
  it("lists the column's options, each with its name, its colour and its place", () => {
    // Act.
    open();

    // Assert.
    const panel = screen.getByRole("dialog", { name: "Options of Country" });
    expect(within(panel).getAllByRole("listitem")).toHaveLength(2);
    expect((screen.getByRole("textbox", { name: "Name of Netherlands" }) as HTMLInputElement).value).toBe("Netherlands");
    expect((screen.getByRole("combobox", { name: "Colour of Netherlands" }) as HTMLSelectElement).value).toBe("blue");
    // A colour the theme does not have is shown as the default it is drawn in.
    expect((screen.getByRole("combobox", { name: "Colour of Belgium" }) as HTMLSelectElement).value).toBe("default");
    expect((screen.getByRole("button", { name: "Move Netherlands up" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Move Belgium down" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("stands under its header, and inside the window when the header is scrolled out of it", () => {
    // Arrange: found in the browser - a header scrolled away to the left took its panel with it.
    const { rerender } = render(<OptionsEditor column={column("selection")} position={{ x: 240, y: 64 }} raise={vi.fn()} onClose={vi.fn()} />);

    // Assert.
    expect(screen.getByRole("dialog").style.left).toBe("240px");
    expect(screen.getByRole("dialog").style.top).toBe("64px");

    // Act.
    rerender(<OptionsEditor column={column("selection")} position={{ x: -635, y: 64 }} raise={vi.fn()} onClose={vi.fn()} />);

    // Assert.
    expect(screen.getByRole("dialog").style.left).toBe("8px");
  });

  it("renames an option when its field is left, and not while it is typed in", () => {
    // Arrange.
    const { raise } = open();
    const name = screen.getByRole("textbox", { name: "Name of Netherlands" });

    // Act.
    fireEvent.change(name, { target: { value: "The Netherlands" } });

    // Assert: nothing yet.
    expect(raise).not.toHaveBeenCalled();

    // Act.
    fireEvent.blur(name);

    // Assert.
    expect(raise).toHaveBeenCalledTimes(1);
    expect(raise).toHaveBeenCalledWith({ kind: "renameOption", columnId: "p2", targetId: "o1", values: ["The Netherlands"] });
  });

  it("keeps the name an option has when it is renamed to nothing or to itself", () => {
    // Arrange.
    const { raise } = open();
    const name = screen.getByRole("textbox", { name: "Name of Netherlands" }) as HTMLInputElement;

    // Act.
    fireEvent.change(name, { target: { value: "   " } });
    fireEvent.keyDown(name, { key: "Enter" });

    // Assert.
    expect(raise).not.toHaveBeenCalled();
    expect(name.value).toBe("Netherlands");
  });

  it("recolours, moves and deletes an option, each as one gesture", () => {
    // Arrange.
    const { raise } = open();

    // Act.
    fireEvent.change(screen.getByRole("combobox", { name: "Colour of Netherlands" }), { target: { value: "green" } });
    fireEvent.click(screen.getByRole("button", { name: "Move Netherlands down" }));
    fireEvent.click(screen.getByRole("button", { name: "Delete Belgium" }));

    // Assert.
    expect(raise.mock.calls.map((call) => call[0])).toEqual([
      { kind: "recolourOption", columnId: "p2", targetId: "o1", settings: { colour: "green" } },
      { kind: "moveOption", columnId: "p2", targetId: "o1", index: 1 },
      { kind: "deleteOption", columnId: "p2", targetId: "o2" },
    ]);
  });

  it("tells how many rows have an option before deleting it, and deletes only when that is confirmed", () => {
    // Arrange: two rows have Netherlands; nobody has Belgium.
    const raise = vi.fn();
    render(<OptionsEditor column={{ ...column("selection"), settings: { "uses:o1": "2" } }} position={{ x: 10, y: 20 }} raise={raise} onClose={vi.fn()} />);

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Delete Netherlands" }));

    // Assert: asked, with the number, and nothing deleted yet.
    const ask = screen.getByRole("alertdialog", { name: "Delete Netherlands?" });
    expect(ask.textContent).toContain("2 rows have this option. Deleting it clears those values.");
    expect(raise).not.toHaveBeenCalled();

    // Act: kept.
    fireEvent.click(within(ask).getByRole("button", { name: "Keep" }));

    // Assert.
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(raise).not.toHaveBeenCalled();

    // Act: asked again, and confirmed.
    fireEvent.click(screen.getByRole("button", { name: "Delete Netherlands" }));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Delete" }));

    // Assert.
    expect(raise).toHaveBeenCalledWith({ kind: "deleteOption", columnId: "p2", targetId: "o1" });

    // An option nobody has is deleted without a question.
    fireEvent.click(screen.getByRole("button", { name: "Delete Belgium" }));
    expect(raise).toHaveBeenLastCalledWith({ kind: "deleteOption", columnId: "p2", targetId: "o2" });
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("adds an option by its name, on Enter or the button, and never an empty one", () => {
    // Arrange.
    const { raise } = open();
    const field = screen.getByRole("textbox", { name: "New option" }) as HTMLInputElement;
    const add = screen.getByRole("button", { name: "Add" }) as HTMLButtonElement;

    // Assert: nothing to add yet.
    expect(add.disabled).toBe(true);

    // Act.
    fireEvent.change(field, { target: { value: " Luxembourg " } });
    fireEvent.keyDown(field, { key: "Enter" });

    // Assert: raised once, trimmed, and the field ready for the next.
    expect(raise).toHaveBeenCalledWith({ kind: "addOption", columnId: "p2", values: ["Luxembourg"] });
    expect(field.value).toBe("");
  });

  it("closes on Escape and on a press outside it, and not on a press inside", () => {
    // Arrange.
    const { onClose } = open();

    // Act.
    fireEvent.pointerDown(screen.getByRole("textbox", { name: "New option" }));

    // Assert.
    expect(onClose).not.toHaveBeenCalled();

    // Act.
    fireEvent.pointerDown(document.body);
    fireEvent.keyDown(document, { key: "Escape" });

    // Assert.
    expect(onClose).toHaveBeenCalledTimes(2);
  });
});

describe("the column menu's entry for options", () => {
  const handlers = { raise: vi.fn(), startRename: vi.fn(), editOptions: vi.fn() };
  const ids = (kind: string, given = handlers) => columnMenuGroups(column(kind), definition, given).flat().map((item) => item.id);

  it("is offered for a column whose values are chosen from options, one or several", () => {
    // Assert.
    expect(ids("selection")).toEqual(["rename", "options", "delete"]);
    expect(ids("tags")).toEqual(["rename", "options", "delete"]);
  });

  it("is not offered for a column of another kind, nor where nothing can open the panel", () => {
    // Assert.
    expect(ids("text")).toEqual(["rename", "delete"]);
    expect(ids("selection", { raise: vi.fn(), startRename: vi.fn(), editOptions: undefined as never })).toEqual(["rename", "delete"]);
  });

  it("opens the panel when it is chosen", () => {
    // Arrange.
    const editOptions = vi.fn();
    const entry = columnMenuGroups(column("selection"), definition, { raise: vi.fn(), startRename: vi.fn(), editOptions }).flat().find((item) => item.id === "options");

    // Act.
    entry?.onSelect?.();

    // Assert.
    expect(editOptions).toHaveBeenCalledTimes(1);
  });
});
