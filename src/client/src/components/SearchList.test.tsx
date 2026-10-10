import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { matching, SearchList } from "./SearchList";

const items = [
  { id: "o1", label: "Capital" },
  { id: "o2", label: "Port" },
  { id: "o3", label: "University town" },
];

function renderList(props: Partial<Parameters<typeof SearchList>[0]> = {}) {
  const onPick = vi.fn();
  const onCreate = vi.fn();
  const onClose = vi.fn();
  render(<SearchList label="Kind" items={items} selected={[]} onPick={onPick} onCreate={onCreate} onClose={onClose} {...props} />);
  return { onPick, onCreate, onClose };
}

const field = () => screen.getByRole("combobox", { name: "Kind" });
const options = () => screen.getAllByRole("option").map((option) => option.textContent);

afterEach(cleanup);

describe("matching", () => {
  it("keeps the items whose label holds the text, whatever its case", () => {
    expect(matching(items, "TOWN").map((item) => item.id)).toEqual(["o3"]);
    expect(matching(items, " p ").map((item) => item.id)).toEqual(["o1", "o2"]);
    expect(matching(items, "").map((item) => item.id)).toEqual(["o1", "o2", "o3"]);
  });
});

describe("SearchList", () => {
  it("lists every item until something is typed, then those that match", () => {
    // Arrange.
    renderList({ onCreate: undefined });
    expect(options()).toEqual(["Capital", "Port", "University town"]);

    // Act.
    fireEvent.change(field(), { target: { value: "or" } });

    // Assert.
    expect(options()).toEqual(["Port"]);
  });

  it("picks the row the arrow keys are on with Enter", () => {
    // Arrange.
    const { onPick } = renderList();

    // Act.
    fireEvent.keyDown(field(), { key: "ArrowDown" });
    fireEvent.keyDown(field(), { key: "ArrowDown" });
    fireEvent.keyDown(field(), { key: "ArrowUp" });
    fireEvent.keyDown(field(), { key: "Enter" });

    // Assert: down twice and up once is the second row.
    expect(onPick).toHaveBeenCalledTimes(1);
    expect(onPick).toHaveBeenCalledWith("o2");
  });

  it("picks a row that is clicked", () => {
    // Arrange.
    const { onPick } = renderList();

    // Act.
    fireEvent.click(screen.getByText("University town"));

    // Assert.
    expect(onPick).toHaveBeenCalledWith("o3");
  });

  it("says which items are chosen", () => {
    // Act.
    renderList({ selected: ["o2"] });

    // Assert.
    expect(screen.getAllByRole("option").map((option) => option.getAttribute("aria-selected"))).toEqual(["false", "true", "false"]);
  });

  it("offers to create text that names no item, and creates it", () => {
    // Arrange.
    const { onCreate, onPick } = renderList();

    // Act.
    fireEvent.change(field(), { target: { value: "Harbour" } });

    // Assert: nothing matches, so the offer is the only row and Enter takes it.
    expect(options()).toEqual(["Create “Harbour”"]);
    fireEvent.keyDown(field(), { key: "Enter" });
    expect(onCreate).toHaveBeenCalledWith("Harbour");
    expect(onPick).not.toHaveBeenCalled();
  });

  it("does not offer to create an item that exists, whatever its case", () => {
    // Arrange.
    renderList();

    // Act.
    fireEvent.change(field(), { target: { value: "capital" } });

    // Assert.
    expect(options()).toEqual(["Capital"]);
  });

  it("only picks when its owner gives no way to create", () => {
    // Arrange.
    renderList({ onCreate: undefined });

    // Act.
    fireEvent.change(field(), { target: { value: "Harbour" } });

    // Assert.
    expect(screen.queryAllByRole("option")).toEqual([]);
    expect(screen.getByText("Nothing matches.")).toBeTruthy();
  });

  it("closes on Escape", () => {
    // Arrange.
    const { onClose, onPick } = renderList();

    // Act.
    fireEvent.keyDown(field(), { key: "Escape" });

    // Assert.
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onPick).not.toHaveBeenCalled();
  });

  it("names the active row to assistive technology", () => {
    // Arrange.
    renderList();

    // Act.
    fireEvent.keyDown(field(), { key: "ArrowDown" });

    // Assert.
    expect(field().getAttribute("aria-activedescendant")).toBe(screen.getAllByRole("option")[1]!.id);
  });
});
