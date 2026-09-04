import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { InlineLabelEditor } from "./InlineLabelEditor";
import type { LabelPlacement } from "../../shell/panels/InlineLabelPlacementContext";

const placement: LabelPlacement = { x: 10, y: 20, width: 120, height: 18, text: "before" };

/** A verdict source that always accepts, echoing the revision it was asked about. */
function acceptEverything() {
  return vi.fn(async (revision: number, _value: string) => ({ revision, valid: true, reason: "" }));
}

function accepts() {
  return vi.fn(async () => ({ completed: true, error: "" }));
}

function refuses(reason: string) {
  return vi.fn(async () => ({ completed: false, error: reason }));
}

function renderEditor(overrides: Partial<Parameters<typeof InlineLabelEditor>[0]> = {}) {
  const props = {
    placement,
    onPropose: acceptEverything(),
    onSubmit: accepts(),
    onCancel: vi.fn(),
    ...overrides,
  };
  // An svg wrapper because a foreignObject is only meaningful inside one, and the component's
  // whole placement argument is in that coordinate system.
  const result = render(
    <svg>
      <InlineLabelEditor {...props} />
    </svg>,
  );
  return { ...result, props };
}

function field(): HTMLInputElement {
  return screen.getByLabelText("Label") as HTMLInputElement;
}

function type(value: string) {
  fireEvent.change(field(), { target: { value } });
}

describe("InlineLabelEditor", () => {
  it("opens focused with the whole label selected, so typing replaces it", () => {
    // Arrange, Act.
    renderEditor();

    // Assert.
    // Focused but unselected is the defect: the first keystroke would append to the old label
    // instead of replacing it, which is not what in-place editing does anywhere else.
    expect(document.activeElement).toBe(field());
    expect(field().value).toBe("before");
    expect(field().selectionStart).toBe(0);
    expect(field().selectionEnd).toBe("before".length);
  });

  it("commits the typed value on Enter", async () => {
    // Arrange.
    const { props } = renderEditor();

    // Act.
    type("after");
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });

    // Assert.
    expect(props.onSubmit).toHaveBeenCalledWith("after");
  });

  it("dispatches nothing on Escape", async () => {
    // Arrange.
    const { props } = renderEditor();

    // Act.
    type("after");
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Escape" });
    });

    // Assert.
    // An abandon that still submits is the defect, and it is invisible until someone notices
    // their cancelled rename happened anyway.
    expect(props.onSubmit).not.toHaveBeenCalled();
    expect(props.onCancel).toHaveBeenCalled();
  });

  it("commits on blur rather than discarding", async () => {
    // Arrange.
    const { props } = renderEditor();

    // Act.
    type("after");
    await act(async () => {
      fireEvent.blur(field());
    });

    // Assert.
    // This is the rule most likely to be reversed later by someone who believes discarding is
    // the safer default. It is not: losing typed text to a stray click is the failure users
    // resent most, and the requirement says so.
    expect(props.onSubmit).toHaveBeenCalledWith("after");
  });

  it("stays open with the text intact when the value is refused", async () => {
    // Arrange.
    const onSubmit = refuses("A node called 'after' is already there.");
    const { props } = renderEditor({ onSubmit });

    // Act.
    type("after");
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });

    // Assert.
    // Closing on a refusal eats the user's typing and leaves them to retype it from the label
    // they can no longer see.
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("already there"));
    expect(field().value).toBe("after");
    expect(props.onCancel).not.toHaveBeenCalled();
  });

  it("dispatches nothing when the value is unchanged", async () => {
    // Arrange.
    const { props } = renderEditor();

    // Act.
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });

    // Assert.
    // Opening an editor and pressing Enter is not an edit: a command here would put an
    // inverse on the undo stack that undoes nothing.
    expect(props.onSubmit).not.toHaveBeenCalled();
    expect(props.onCancel).toHaveBeenCalled();
  });

  it("hands focus back to the canvas when it closes", () => {
    // Arrange.
    const onReturnFocus = vi.fn();
    const { unmount } = renderEditor({ onReturnFocus });

    // Act.
    unmount();

    // Assert.
    // Without this the canvas is left without keyboard focus after every rename, so the next
    // F2 or arrow key goes nowhere - a keyboard trap that a mouse user never notices.
    expect(onReturnFocus).toHaveBeenCalled();
  });

  it("does not commit twice when Enter is followed by the closing blur", async () => {
    // Arrange.
    const { props } = renderEditor();

    // Act.
    type("after");
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });
    await act(async () => {
      fireEvent.blur(field());
    });

    // Assert.
    // Blur committing and Enter committing are both right, and together they are one rename
    // dispatched twice - two commands, two undo entries, and a second refusal on the way back.
    expect(props.onSubmit).toHaveBeenCalledTimes(1);
  });
});
