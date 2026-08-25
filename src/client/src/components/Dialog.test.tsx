import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Dialog } from "./Dialog";

describe("Dialog", () => {
  it("renders nothing when closed", () => {
    // Arrange and act.
    const { container } = render(
      <Dialog open={false} title="Title" onClose={() => {}} buttons={[]}>
        Body
      </Dialog>,
    );

    // Assert.
    expect(container.firstChild).toBeNull();
  });

  it("renders the icon, title, and body content when open", () => {
    // Arrange and act.
    render(
      <Dialog open icon="mdi-alert-circle-outline" title="Delete file?" onClose={() => {}} buttons={[]}>
        <p>This cannot be undone.</p>
      </Dialog>,
    );

    // Assert.
    expect(screen.getByRole("dialog")).not.toBeNull();
    expect(screen.getByText("Delete file?")).not.toBeNull();
    expect(screen.getByText("This cannot be undone.")).not.toBeNull();
    expect(document.querySelector(".dialog-header-icon.mdi-alert-circle-outline")).not.toBeNull();
  });

  it("renders configurable buttons with their label and color, and invokes onClick when clicked", () => {
    // Arrange.
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

    // Act and assert, step by step.
    const button = screen.getByRole("button", { name: "Delete" });
    expect(button.className).toContain("dialog-button-danger");

    fireEvent.click(button);
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it("does not invoke onClick for a disabled button", () => {
    // Arrange.
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

    // Act and assert, step by step.
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("calls onClose when Escape is pressed", () => {
    // Arrange.
    const onClose = vi.fn();
    render(
      <Dialog open title="Title" onClose={onClose} buttons={[]}>
        Body
      </Dialog>,
    );

    // Act and assert, step by step.
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("calls onClose when the backdrop is clicked, but not when the dialog itself is clicked", () => {
    // Arrange.
    const onClose = vi.fn();
    const { container } = render(
      <Dialog open title="Title" onClose={onClose} buttons={[]}>
        Body
      </Dialog>,
    );

    // Act and assert, step by step.
    fireEvent.mouseDown(container.querySelector(".dialog") as HTMLElement);
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.mouseDown(container.querySelector(".dialog-overlay") as HTMLElement);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("focuses the button flagged autoFocus on open, and returns focus to the trigger on close", () => {
    // Arrange.
    const trigger = document.createElement("button");
    trigger.textContent = "Open";
    document.body.appendChild(trigger);
    trigger.focus();

    // Arrange, continued.
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

    // Arrange, continued.
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Cancel" }));

    // Act.
    rerender(
      <Dialog open={false} title="Title" onClose={() => {}} buttons={[]}>
        Body
      </Dialog>,
    );

    // Assert.
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it("wraps Tab focus between the first and last focusable elements", () => {
    // Arrange.
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

    // Act and assert, step by step.
    confirmButton.focus();
    fireEvent.keyDown(document, { key: "Tab" });
    expect(document.activeElement).toBe(cancelButton);

    cancelButton.focus();
    fireEvent.keyDown(document, { key: "Tab", shiftKey: true });
    expect(document.activeElement).toBe(confirmButton);
  });

  it("supports a custom dialog whose embedded content enables/disables a footer button via its own callback", () => {
    // Arrange.
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

    // Act and assert, step by step.
    const saveButton = screen.getByRole("button", { name: "Save" }) as HTMLButtonElement;
    expect(saveButton.disabled).toBe(true);

    fireEvent.change(screen.getByLabelText("New name"), { target: { value: "renamed.txt" } });
    expect(saveButton.disabled).toBe(false);

    fireEvent.change(screen.getByLabelText("New name"), { target: { value: "" } });
    expect(saveButton.disabled).toBe(true);
  });
});
