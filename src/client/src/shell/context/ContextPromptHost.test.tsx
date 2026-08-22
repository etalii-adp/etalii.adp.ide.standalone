import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextPromptSchema } from "../../generated/context_pb";
import type { ContextPrompt } from "../../generated/context_pb";
import { ContextPromptHost, type ContextPromptVerdict } from "./ContextPromptHost";

const DEBOUNCE_MS = 200;

function inputPrompt(): ContextPrompt {
  return create(ContextPromptSchema, {
    interactionId: { value: new Uint8Array(16).fill(1) },
    prompt: {
      case: "inputDialog",
      value: {
        title: "Rename file",
        icon: "mdi-pencil-outline",
        fieldLabel: "New name",
        initialValue: "original.txt",
        confirmLabel: "Rename",
      },
    },
  });
}

function confirmPrompt(danger = true): ContextPrompt {
  return create(ContextPromptSchema, {
    interactionId: { value: new Uint8Array(16).fill(2) },
    prompt: {
      case: "confirmDialog",
      value: {
        title: "Delete folder?",
        icon: "mdi-trash-can-outline",
        message: "Delete the folder 'doomed' and everything inside it from disk? This cannot be undone.",
        confirmLabel: "Delete",
        danger,
      },
    },
  });
}

/** A verdict source that always accepts, echoing the revision it was asked about. */
function acceptEverything() {
  return vi.fn(
    async (revision: number, _value: string): Promise<ContextPromptVerdict> => ({ revision, valid: true, reason: "" }),
  );
}

function nameField(): HTMLInputElement {
  return screen.getByLabelText("New name") as HTMLInputElement;
}

function confirmButton(label: string): HTMLButtonElement {
  return screen.getByRole("button", { name: label }) as HTMLButtonElement;
}

function type(value: string) {
  fireEvent.change(nameField(), { target: { value } });
}

async function settleDebounce() {
  await act(async () => {
    vi.advanceTimersByTime(DEBOUNCE_MS);
  });
}

describe("ContextPromptHost input dialog", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders nothing when there is no prompt", () => {
    const { container } = render(
      <ContextPromptHost prompt={null} onPropose={acceptEverything()} onSubmit={vi.fn()} onCancel={vi.fn()} />,
    );

    expect(container.innerHTML).toBe("");
  });

  it("opens pre-filled with the current name and its confirm button disabled", () => {
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={acceptEverything()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    expect(nameField().value).toBe("original.txt");
    expect(confirmButton("Rename").disabled).toBe(true);
  });

  it("issues no validation call before the input has been quiet for the whole window", async () => {
    const onPropose = acceptEverything();
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={onPropose} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    type("renamed.txt");
    await act(async () => {
      vi.advanceTimersByTime(DEBOUNCE_MS - 1);
    });

    expect(onPropose).not.toHaveBeenCalled();
  });

  it("issues exactly one validation call for a burst of typing, carrying the final text", async () => {
    const onPropose = acceptEverything();
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={onPropose} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    for (const value of ["r", "re", "ren", "renamed.txt"]) {
      type(value);
      await act(async () => {
        vi.advanceTimersByTime(50);
      });
    }
    await settleDebounce();

    expect(onPropose).toHaveBeenCalledTimes(1);
    expect(onPropose.mock.calls[0]?.[1]).toBe("renamed.txt");
  });

  it("enables the confirm button only once a valid verdict for the current text arrives", async () => {
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={acceptEverything()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    type("renamed.txt");
    expect(confirmButton("Rename").disabled).toBe(true); // still inside the window

    await settleDebounce();

    expect(confirmButton("Rename").disabled).toBe(false);
  });

  it("disables the confirm button again the moment the text changes after a valid verdict", async () => {
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={acceptEverything()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    type("renamed.txt");
    await settleDebounce();
    expect(confirmButton("Rename").disabled).toBe(false);

    // The verdict now describes text the user has moved past; submitting it would rename
    // to something they no longer typed, so the button must go dead until it is re-judged.
    type("renamed-again.txt");

    expect(confirmButton("Rename").disabled).toBe(true);
  });

  it("shows the backend's reason and keeps the button disabled for a rejected name", async () => {
    const onPropose = vi.fn(async (revision: number): Promise<ContextPromptVerdict> => ({
      revision,
      valid: false,
      reason: "An item named 'taken.txt' already exists in this folder.",
    }));
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={onPropose} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    type("taken.txt");
    await settleDebounce();

    expect(screen.getByRole("alert").textContent).toContain("already exists");
    expect(confirmButton("Rename").disabled).toBe(true);
  });

  it("keeps the dialog open with the typed text intact when the submit fails", async () => {
    const onSubmit = vi.fn(async () => ({ completed: false, error: "Could not rename this item: the file is in use." }));
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={acceptEverything()} onSubmit={onSubmit} onCancel={vi.fn()} />);

    type("renamed.txt");
    await settleDebounce();
    await act(async () => {
      fireEvent.click(confirmButton("Rename"));
    });

    expect(screen.getByRole("alert").textContent).toContain("in use");
    expect(nameField().value).toBe("renamed.txt");
  });

  it("cancels without submitting anything", () => {
    const onCancel = vi.fn();
    const onSubmit = vi.fn();
    render(<ContextPromptHost prompt={inputPrompt()} onPropose={acceptEverything()} onSubmit={onSubmit} onCancel={onCancel} />);

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(onCancel).toHaveBeenCalled();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

describe("ContextPromptHost confirm dialog", () => {
  it("renders the backend's own message with a danger-coloured confirm button", () => {
    render(<ContextPromptHost prompt={confirmPrompt()} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    expect(screen.getByText(/everything inside it/)).toBeTruthy();
    expect(confirmButton("Delete").className).toContain("dialog-button-danger");
  });

  it("submits on confirm and does nothing to disk on cancel", () => {
    const onSubmit = vi.fn();
    const onCancel = vi.fn();
    const { rerender } = render(
      <ContextPromptHost prompt={confirmPrompt()} onPropose={vi.fn()} onSubmit={onSubmit} onCancel={onCancel} />,
    );

    fireEvent.click(confirmButton("Delete"));
    expect(onSubmit).toHaveBeenCalled();

    rerender(<ContextPromptHost prompt={confirmPrompt()} onPropose={vi.fn()} onSubmit={onSubmit} onCancel={onCancel} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onCancel).toHaveBeenCalled();
  });
});

function choicePrompt(interactionByte = 3): ContextPrompt {
  return create(ContextPromptSchema, {
    interactionId: { value: new Uint8Array(16).fill(interactionByte) },
    prompt: {
      case: "choiceDialog",
      value: {
        title: "Add diagram",
        icon: "mdi-plus",
        confirmLabel: "Add",
        emptyMessage: "No diagram types are available.",
        options: [{ id: "c4", label: "c4", selectable: false, children: [{ id: "c4/context", label: "System Context", selectable: true, children: [] }] }],
      },
    },
  });
}

describe("ContextPromptHost choice dialog", () => {
  it("renders the choice dialog for a choiceDialog prompt", () => {
    render(<ContextPromptHost prompt={choicePrompt()} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    expect(screen.getByText("Add diagram")).toBeTruthy();
    expect(screen.getByRole("tree")).toBeTruthy();
    expect(confirmButton("Add")).toHaveProperty("disabled", true);
  });

  it("remounts for a new interaction, so a choice made in one never leaks into the next", () => {
    const { rerender } = render(
      <ContextPromptHost prompt={choicePrompt(3)} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />,
    );
    fireEvent.click(screen.getByText("System Context").closest("button")!);
    expect(confirmButton("Add")).toHaveProperty("disabled", false);

    rerender(<ContextPromptHost prompt={choicePrompt(4)} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    expect(confirmButton("Add")).toHaveProperty("disabled", true);
  });
});

describe("ContextPromptHost with a prompt this build cannot render", () => {
  // A newer backend asking for a dialog kind added after this client was built - or, as
  // actually happened, a client whose generated stubs predate the contract, so a prompt it
  // was sent deserialises with no case at all.
  function unknownPrompt(): ContextPrompt {
    return create(ContextPromptSchema, { interactionId: { value: new Uint8Array(16).fill(9) } });
  }

  it("cancels the interaction rather than leaving it open with nothing on screen", () => {
    const onCancel = vi.fn();

    render(<ContextPromptHost prompt={unknownPrompt()} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={onCancel} />);

    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("tells the user the action ended, instead of a click that does nothing", () => {
    render(<ContextPromptHost prompt={unknownPrompt()} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    expect(screen.getByText("Action not supported")).toBeTruthy();
    expect(screen.getByText(/needs a newer version/)).toBeTruthy();
  });

  it("closes that notice when dismissed", () => {
    render(<ContextPromptHost prompt={unknownPrompt()} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.queryByText("Action not supported")).toBeNull();
  });

  it("shows nothing at all when there is simply no prompt", () => {
    const onCancel = vi.fn();

    render(<ContextPromptHost prompt={null} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={onCancel} />);

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(onCancel).not.toHaveBeenCalled();
  });

  it("lets a prompt it can render take over from the notice", () => {
    const { rerender } = render(
      <ContextPromptHost prompt={unknownPrompt()} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />,
    );
    expect(screen.getByText("Action not supported")).toBeTruthy();

    rerender(<ContextPromptHost prompt={confirmPrompt()} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={vi.fn()} />);

    expect(screen.queryByText("Action not supported")).toBeNull();
    expect(screen.getByText("Delete folder?")).toBeTruthy();
  });
});
