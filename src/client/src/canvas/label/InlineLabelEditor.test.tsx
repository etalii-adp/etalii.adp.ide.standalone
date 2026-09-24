import { StrictMode } from "react";
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

/** A verdict source that refuses everything, echoing the revision it judged. */
function refuseEverything(reason: string) {
  return vi.fn(async (revision: number, _value: string) => ({ revision, valid: false, reason }));
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

  it("still commits under StrictMode, whose double-invoked effects run the teardown once while alive", async () => {
    // Arrange.
    // The app renders inside StrictMode, so in development React runs every effect as
    // mount-cleanup-mount. The editor's cleanup marks it as closing, to stop the blur that its
    // own teardown causes being read as a user commit - and that mark used to latch on the first
    // paint and never clear. The box then appeared, took focus and accepted typing, and Enter,
    // blur and every other route to committing did nothing at all, in complete silence. This
    // renders the way the app does rather than the way a unit test finds convenient, because
    // outside StrictMode the bug does not exist.
    const props = {
      placement,
      onPropose: acceptEverything(),
      onSubmit: accepts(),
      onCancel: vi.fn(),
    };
    render(
      <StrictMode>
        <svg>
          <InlineLabelEditor {...props} />
        </svg>
      </StrictMode>,
    );

    // Act.
    type("after");
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });

    // Assert.
    expect(props.onSubmit).toHaveBeenCalledWith("after");
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

  it("never sends a value the module has already refused", async () => {
    // Arrange.
    // The dialog cannot send one, because its confirm button is disabled while a verdict is
    // invalid. The editor has no button, and its first version sent anyway - on the reasoning
    // that the handler would validate its own preconditions. It does not: a module's
    // ValidateAsync runs on propose and SubmitInteraction does not run it again. Typing an empty
    // name into a C4 element therefore showed "Every C4 element needs a name." and then wrote
    // `container ""` into the document. Found by running the app, not by any test.
    const props = {
      placement,
      onPropose: refuseEverything("Every C4 element needs a name."),
      onSubmit: accepts(),
      onCancel: vi.fn(),
    };
    render(
      <svg>
        <InlineLabelEditor {...props} />
      </svg>,
    );

    // Act.
    type("");
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("needs a name"));
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });

    // Assert.
    expect(props.onSubmit).not.toHaveBeenCalled();
    expect(props.onCancel).not.toHaveBeenCalled();
    expect(field().value).toBe("");
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

/**
 * The multi-line editor, for a label the definition declares `wrap: true`.
 *
 * <b>The shapes that matter are the two negatives.</b> A textarea whose Enter still commits is the
 * single-line editor wearing a different tag: the newline the user typed never reaches the model,
 * and nothing on screen says why. And an input rendered for a wrapped label is the same defect
 * from the other side. So the assertions here are that Enter does NOT commit and is NOT prevented
 * - the default is what inserts the newline - and that a plain placement still gets an input.
 */
describe("InlineLabelEditor, for a wrapped label", () => {
  const wrapped: LabelPlacement = { x: 10, y: 20, width: 160, height: 60, text: "before", multiline: true };

  function area(): HTMLTextAreaElement {
    return screen.getByLabelText("Label") as HTMLTextAreaElement;
  }

  it("renders a textarea for a multiline placement, and an input for a single-line one", () => {
    // Arrange, act.
    const { unmount } = renderEditor({ placement: wrapped });

    // Assert: the tag itself, because an input cannot hold a newline however it is styled.
    expect(area().tagName).toBe("TEXTAREA");
    unmount();

    renderEditor();
    expect(field().tagName).toBe("INPUT");
  });

  it("opens focused with the whole text selected, exactly as the single-line editor does", () => {
    // The multi-line branch is a second field, and a second field is a second chance to lose a
    // behaviour that was paid for once already.
    renderEditor({ placement: wrapped });

    expect(document.activeElement).toBe(area());
    expect(area().selectionStart).toBe(0);
    expect(area().selectionEnd).toBe("before".length);
  });

  it("leaves Enter to the textarea, committing nothing and preventing nothing", async () => {
    // Arrange.
    const { props } = renderEditor({ placement: wrapped });

    // Act. fireEvent returns false when the handler called preventDefault, and jsdom performs no
    // default text insertion - so "was the default allowed" is the closest a unit test gets to
    // "the newline was typed", and it is the half the code decides.
    type("after");
    let allowed = true;
    await act(async () => {
      allowed = fireEvent.keyDown(area(), { key: "Enter" });
    });

    // Assert.
    expect(props.onSubmit).not.toHaveBeenCalled();
    expect(allowed).toBe(true);
  });

  it("commits on Ctrl+Enter, with the newline intact in the committed value", async () => {
    // Arrange.
    const { props } = renderEditor({ placement: wrapped });

    // Act.
    type("first\nsecond");
    await act(async () => {
      fireEvent.keyDown(area(), { key: "Enter", ctrlKey: true });
    });

    // Assert: the newline is the payload. A value arriving as "firstsecond", or as "first", is the
    // defect this whole task exists to prevent, and it is invisible in a screenshot.
    expect(props.onSubmit).toHaveBeenCalledWith("first\nsecond");
  });

  it("commits on Cmd+Enter too, because that is the same gesture on a Mac", async () => {
    // Arrange.
    const { props } = renderEditor({ placement: wrapped });

    // Act.
    type("first\nsecond");
    await act(async () => {
      fireEvent.keyDown(area(), { key: "Enter", metaKey: true });
    });

    // Assert.
    expect(props.onSubmit).toHaveBeenCalledWith("first\nsecond");
  });

  it("dispatches nothing on Escape, leaving the model as it was", async () => {
    // Arrange.
    const { props } = renderEditor({ placement: wrapped });

    // Act.
    type("first\nsecond");
    await act(async () => {
      fireEvent.keyDown(area(), { key: "Escape" });
    });

    // Assert.
    expect(props.onSubmit).not.toHaveBeenCalled();
    expect(props.onCancel).toHaveBeenCalled();
  });

  it("still commits on blur, so the rule does not differ between the two fields", async () => {
    // Arrange.
    const { props } = renderEditor({ placement: wrapped });

    // Act.
    type("first\nsecond");
    await act(async () => {
      fireEvent.blur(area());
    });

    // Assert. Blur-commits is the rule most likely to be "fixed" the other way by a later reader;
    // a second field is exactly where it would be dropped without anyone noticing.
    expect(props.onSubmit).toHaveBeenCalledWith("first\nsecond");
  });
});
