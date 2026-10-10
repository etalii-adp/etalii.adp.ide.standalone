import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import type { TableCell, TableColumn } from "../api/tableModel";
import type { TableEditorKind } from "../definition/tableDefinition";
import { CellEditor } from "./CellEditor";

function column(kind: string, options: { id: string; name: string }[] = []): TableColumn {
  return { id: "p1", name: "Value", kind, options: options.map((option) => ({ ...option, color: "" })), width: 0, visible: true, isTitle: false, wraps: false, settings: {} };
}

function cell(values: string[], labels: string[] = []): TableCell {
  return { columnId: "p1", values, labels, pending: false };
}

function open(editor: Exclude<TableEditorKind, "checkbox">, options: { held?: TableCell; replace?: string; refusal?: string; column?: TableColumn } = {}) {
  const commit = vi.fn(async (_values: readonly string[], _settings?: Readonly<Record<string, string>>) => options.refusal ?? "");
  const onClose = vi.fn();
  render(<CellEditor editor={editor} column={options.column ?? column(editor)} cell={options.held} replace={options.replace} commit={commit} onClose={onClose} />);
  return { commit, onClose };
}

const input = () => screen.getByLabelText("Value") as HTMLInputElement;
const type = async (value: string, key: string, init: Partial<KeyboardEventInit> = {}) => {
  fireEvent.change(input(), { target: { value } });
  await act(async () => {
    fireEvent.keyDown(input(), { key, ...init });
  });
};

afterEach(cleanup);

describe("the editors on the browser's own inputs", () => {
  it.each([
    ["text", "text", "Amsterdam"],
    ["number", "text", "931298"],
    ["date", "date", "2026-10-09"],
    ["datetime", "datetime-local", "2026-10-09T08:30"],
    ["time", "time", "08:30"],
  ] as const)("edits a %s in an input of type %s and sends what was entered", async (editor, inputType, value) => {
    // Arrange.
    const { commit, onClose } = open(editor);
    expect(input().type).toBe(inputType);

    // Act.
    await type(value, "Enter");

    // Assert: the value as written, and the focus back on the cell.
    expect(commit).toHaveBeenCalledTimes(1);
    expect(commit.mock.calls[0]![0]).toEqual([value]);
    expect(onClose).toHaveBeenCalledWith("stay");
  });

  it("opens on the value the cell holds", () => {
    open("text", { held: cell(["Amsterdam"]) });
    expect(input().value).toBe("Amsterdam");
  });

  it("opens on the character that was typed over the cell", () => {
    open("text", { held: cell(["Amsterdam"]), replace: "R" });
    expect(input().value).toBe("R");
  });

  it("sends no value for a cell that was emptied", async () => {
    // Arrange.
    const { commit } = open("text", { held: cell(["Amsterdam"]) });

    // Act.
    await type("", "Enter");

    // Assert: an empty cell holds nothing, not an empty text.
    expect(commit.mock.calls[0]![0]).toEqual([]);
  });

  it("sends nothing when the value was not changed", async () => {
    // Arrange.
    const { commit, onClose } = open("text", { held: cell(["Amsterdam"]) });

    // Act.
    await act(async () => {
      fireEvent.keyDown(input(), { key: "Enter" });
    });

    // Assert.
    expect(commit).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledWith("stay");
  });

  it("commits and moves on with Tab, and back with Shift and Tab", async () => {
    // Arrange.
    const forwards = open("text");

    // Act and assert.
    await type("Amsterdam", "Tab");
    expect(forwards.commit.mock.calls[0]![0]).toEqual(["Amsterdam"]);
    expect(forwards.onClose).toHaveBeenCalledWith("next");
    cleanup();

    const backwards = open("text");
    await type("Antwerp", "Tab", { shiftKey: true });
    expect(backwards.onClose).toHaveBeenCalledWith("previous");
  });

  it("commits when the focus leaves it, without taking the focus back", async () => {
    // Arrange.
    const { commit, onClose } = open("text");

    // Act.
    fireEvent.change(input(), { target: { value: "Amsterdam" } });
    await act(async () => {
      fireEvent.blur(input());
    });

    // Assert.
    expect(commit.mock.calls[0]![0]).toEqual(["Amsterdam"]);
    expect(onClose).toHaveBeenCalledWith("leave");
  });

  it("cancels on Escape, and the blur that follows writes nothing", async () => {
    // Arrange.
    const { commit, onClose } = open("text", { held: cell(["Amsterdam"]) });

    // Act: Escape closes the editor, and closing it takes the focus away from the input.
    await type("Rotterdam", "Escape");
    await act(async () => {
      fireEvent.blur(input());
    });

    // Assert: declined means declined.
    expect(commit).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledWith("stay");
  });

  it("stays open with the backend's reason when the value is refused", async () => {
    // Arrange.
    const { commit, onClose } = open("number", { refusal: "A number is expected." });

    // Act.
    await type("about half a million", "Enter");

    // Assert: the reason is shown, the value typed is still there, and nothing closed.
    expect(commit).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("alert").textContent).toBe("A number is expected.");
    expect(input().value).toBe("about half a million");
    expect(input().getAttribute("aria-invalid")).toBe("true");
    expect(onClose).not.toHaveBeenCalled();

    // And it can be tried again: the editor was not left in a state that swallows the next commit.
    await type("500000", "Enter");
    expect(commit).toHaveBeenCalledTimes(2);
  });

  it("keeps its keys from the table underneath", () => {
    // Arrange.
    const onKeyDown = vi.fn();
    const commit = vi.fn(async () => "");
    render(
      <div onKeyDown={onKeyDown}>
        <CellEditor editor="text" column={column("text")} cell={undefined} commit={commit} onClose={vi.fn()} />
      </div>,
    );

    // Act.
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.keyDown(input(), { key: "a" });

    // Assert.
    expect(onKeyDown).not.toHaveBeenCalled();
  });
});

describe("the editors on a searchable list", () => {
  const kinds = column("select", [
    { id: "o1", name: "Capital" },
    { id: "o2", name: "Port" },
  ]);
  const field = () => screen.getByRole("combobox", { name: "Value" });

  it("sets the one option that is picked, and closes", async () => {
    // Arrange.
    const { commit, onClose } = open("option", { column: kinds });

    // Act.
    await act(async () => {
      fireEvent.click(screen.getByText("Port"));
    });

    // Assert.
    expect(commit.mock.calls[0]![0]).toEqual(["o2"]);
    expect(onClose).toHaveBeenCalledWith("stay");
  });

  it("takes the one option away when it is picked again", async () => {
    // Arrange.
    const { commit } = open("option", { column: kinds, held: cell(["o2"]) });

    // Act.
    await act(async () => {
      fireEvent.click(screen.getByText("Port"));
    });

    // Assert.
    expect(commit.mock.calls[0]![0]).toEqual([]);
  });

  it("adds to and takes from several options, and stays open", async () => {
    // Arrange.
    const { commit, onClose } = open("options", { column: kinds, held: cell(["o1"]) });

    // Act.
    await act(async () => {
      fireEvent.click(screen.getByText("Port"));
    });
    await act(async () => {
      fireEvent.click(screen.getByText("Capital"));
    });

    // Assert: each pick is an edit of its own, against the value the cell holds.
    expect(commit.mock.calls.map((call) => call[0])).toEqual([["o1", "o2"], []]);
    expect(onClose).not.toHaveBeenCalled();
  });

  it("creates an option from text that names none, with the value it is added to", async () => {
    // Arrange.
    const { commit } = open("options", { column: kinds, held: cell(["o1"]) });

    // Act.
    fireEvent.change(field(), { target: { value: "Harbour" } });
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });

    // Assert.
    expect(commit).toHaveBeenCalledWith(["o1"], { newOption: "Harbour" });
  });

  it("offers the related rows and never offers to create one", async () => {
    // Arrange: a related row the list does not hold keeps the label the cell gave it.
    const { commit } = open("rows", { column: column("relation", [{ id: "r1", name: "Netherlands" }]), held: cell(["r9"], ["Belgium"]) });
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Netherlands", "Belgium"]);

    // Act.
    fireEvent.change(field(), { target: { value: "Germany" } });

    // Assert.
    expect(screen.queryAllByRole("option")).toEqual([]);
    expect(commit).not.toHaveBeenCalled();
  });

  it("stays open with the backend's reason when a pick is refused", async () => {
    // Arrange.
    const { onClose } = open("option", { column: kinds, refusal: "This table is read-only." });

    // Act.
    await act(async () => {
      fireEvent.click(screen.getByText("Port"));
    });

    // Assert.
    expect(screen.getByRole("alert").textContent).toBe("This table is read-only.");
    expect(onClose).not.toHaveBeenCalled();
  });

  it("closes on Escape and moves on with Tab, writing nothing", () => {
    // Arrange.
    const { commit, onClose } = open("option", { column: kinds });

    // Act.
    fireEvent.keyDown(field(), { key: "Tab" });
    fireEvent.keyDown(field(), { key: "Escape" });

    // Assert.
    expect(onClose.mock.calls.map((call) => call[0])).toEqual(["next", "stay"]);
    expect(commit).not.toHaveBeenCalled();
  });
});
