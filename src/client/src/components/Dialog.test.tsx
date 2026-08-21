import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Dialog } from "./Dialog";

describe("Dialog", () => {
  it("renders nothing when closed", () => {
    const { container } = render(
      <Dialog open={false} title="Title" onClose={() => {}} buttons={[]}>
        Body
      </Dialog>,
    );

    expect(container.firstChild).toBeNull();
  });

  it("renders the icon, title, and body content when open", () => {
    render(
      <Dialog open icon="mdi-alert-circle-outline" title="Delete file?" onClose={() => {}} buttons={[]}>
        <p>This cannot be undone.</p>
      </Dialog>,
    );

    expect(screen.getByRole("dialog")).not.toBeNull();
    expect(screen.getByText("Delete file?")).not.toBeNull();
    expect(screen.getByText("This cannot be undone.")).not.toBeNull();
    expect(document.querySelector(".dialog-header-icon.mdi-alert-circle-outline")).not.toBeNull();
  });

  it("renders configurable buttons with their label and color, and invokes onClick when clicked", () => {
    const onConfirm = vi.fn();
    render(
      <Dialog
        open
        title="Title"
        onClose={() => {}}
        buttons={[{ key: "confirm", label: "Delete", color: "danger", onClick: onConfirm }]}
      >
        Body
      </Dialog>,
    );

    const button = screen.getByRole("button", { name: "Delete" });
    expect(button.className).toContain("dialog-button-danger");

    fireEvent.click(button);
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it("does not invoke onClick for a disabled button", () => {
    const onConfirm = vi.fn();
    render(
      <Dialog
        open
        title="Title"
        onClose={() => {}}
        buttons={[{ key: "confirm", label: "Save", disabled: true, onClick: onConfirm }]}
      >
        Body
      </Dialog>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("calls onClose when Escape is pressed", () => {
    const onClose = vi.fn();
    render(
      <Dialog open title="Title" onClose={onClose} buttons={[]}>
        Body
      </Dialog>,
    );

    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("calls onClose when the backdrop is clicked, but not when the dialog itself is clicked", () => {
    const onClose = vi.fn();
    const { container } = render(
      <Dialog open title="Title" onClose={onClose} buttons={[]}>
        Body
      </Dialog>,
    );

    fireEvent.mouseDown(container.querySelector(".dialog") as HTMLElement);
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.mouseDown(container.querySelector(".dialog-overlay") as HTMLElement);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("focuses the button flagged autoFocus on open, and returns focus to the trigger on close", () => {
    const trigger = document.createElement("button");
    trigger.textContent = "Open";
    document.body.appendChild(trigger);
    trigger.focus();

    const { rerender } = render(
      <Dialog
        open
        title="Title"
        onClose={() => {}}
        buttons={[
          { key: "cancel", label: "Cancel", autoFocus: true, onClick: () => {} },
          { key: "confirm", label: "Confirm", onClick: () => {} },
        ]}
      >
        Body
      </Dialog>,
    );

    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Cancel" }));

    rerender(
      <Dialog open={false} title="Title" onClose={() => {}} buttons={[]}>
        Body
      </Dialog>,
    );

    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it("wraps Tab focus between the first and last focusable elements", () => {
    render(
      <Dialog
        open
        title="Title"
        onClose={() => {}}
        buttons={[
          { key: "cancel", label: "Cancel", autoFocus: true, onClick: () => {} },
          { key: "confirm", label: "Confirm", onClick: () => {} },
        ]}
      >
        Body
      </Dialog>,
    );

    const cancelButton = screen.getByRole("button", { name: "Cancel" });
    const confirmButton = screen.getByRole("button", { name: "Confirm" });

    confirmButton.focus();
    fireEvent.keyDown(document, { key: "Tab" });
    expect(document.activeElement).toBe(cancelButton);

    cancelButton.focus();
    fireEvent.keyDown(document, { key: "Tab", shiftKey: true });
    expect(document.activeElement).toBe(confirmButton);
  });

  it("supports a custom dialog whose embedded content enables/disables a footer button via its own callback", () => {
    function CustomDialogHarness() {
      const [isValid, setIsValid] = useState(false);
      return (
        <Dialog
          open
          title="Rename"
          onClose={() => {}}
          buttons={[{ key: "save", label: "Save", disabled: !isValid, onClick: () => {} }]}
        >
          <input
            aria-label="New name"
            onChange={(event) => setIsValid(event.target.value.trim().length > 0)}
          />
        </Dialog>
      );
    }

    render(<CustomDialogHarness />);

    const saveButton = screen.getByRole("button", { name: "Save" }) as HTMLButtonElement;
    expect(saveButton.disabled).toBe(true);

    fireEvent.change(screen.getByLabelText("New name"), { target: { value: "renamed.txt" } });
    expect(saveButton.disabled).toBe(false);

    fireEvent.change(screen.getByLabelText("New name"), { target: { value: "" } });
    expect(saveButton.disabled).toBe(true);
  });
});
