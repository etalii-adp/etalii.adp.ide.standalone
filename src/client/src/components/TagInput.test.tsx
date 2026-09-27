import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { TagInput } from "./TagInput";

/** A tag field over the tags a diagram already uses. */
function field(tags: string[] = ["energy"], suggestions = ["energy", "industry", "transport", "information"]) {
  const onChange = vi.fn<(tags: string[]) => void>();
  const view = render(<TagInput label="Tags" tags={tags} suggestions={suggestions} onChange={onChange} />);
  const input = view.container.querySelector(".tag-input-field") as HTMLInputElement;
  const type = (text: string) => fireEvent.change(input, { target: { value: text } });
  const key = (name: string) => fireEvent.keyDown(input, { key: name });
  const suggested = () => [...view.container.querySelectorAll(".tag-input-suggestion")].map((option) => option.textContent);
  return { ...view, input, type, key, suggested, onChange };
}

describe("TagInput", () => {
  it("shows each tag as a chip, and its x removes that tag alone", () => {
    const { container, getByRole, onChange } = field(["energy", "industry"]);

    expect([...container.querySelectorAll(".tag-input-chip-text")].map((chip) => chip.textContent)).toEqual(["energy", "industry"]);
    fireEvent.click(getByRole("button", { name: "Remove energy" }));

    expect(onChange).toHaveBeenCalledWith(["industry"]);
  });

  it("looks what is typed up among the tags in use, leaving out those already chosen", () => {
    const { type, suggested } = field(["energy"]);

    type("in");

    // Starting with the text first, then containing it; energy is chosen already.
    expect(suggested()).toEqual(["industry", "information"]);
  });

  it("adds the matching tag on Enter", () => {
    const { type, key, onChange } = field([]);

    type("transp");
    key("Enter");

    expect(onChange, "Enter added the text as typed rather than the tag it matched").toHaveBeenCalledWith(["transport"]);
  });

  it("adds a new tag on Enter when none in use matches", () => {
    const { type, key, onChange } = field(["energy"]);

    type("space travel");
    key("Enter");

    expect(onChange).toHaveBeenCalledWith(["energy", "space travel"]);
  });

  it("keeps an existing tag's spelling when only the case differs, so one tag never becomes two", () => {
    const { type, key, onChange } = field([]);

    type("ENERGY");
    key("Enter");

    expect(onChange).toHaveBeenCalledWith(["energy"]);
  });

  it("adds a suggestion when it is clicked, and the arrows choose which Enter adds", () => {
    const first = field([]);
    first.type("in");
    fireEvent.mouseDown(first.getByRole("option", { name: "information" }));
    expect(first.onChange).toHaveBeenCalledWith(["information"]);
    first.unmount();

    const second = field([]);
    second.type("in");
    second.key("ArrowDown");
    second.key("ArrowDown");
    second.key("Enter");
    expect(second.onChange).toHaveBeenCalledWith(["information"]);
  });

  it("removes the last chip on Backspace in an empty field, and never makes a tag of a comma", () => {
    const first = field(["energy", "industry"]);
    first.key("Backspace");
    expect(first.onChange).toHaveBeenCalledWith(["energy"]);
    first.unmount();

    const second = field([], []);
    second.type("a,b");
    second.key("Enter");
    expect(second.onChange).toHaveBeenCalledWith(["a b"]);
  });
});
