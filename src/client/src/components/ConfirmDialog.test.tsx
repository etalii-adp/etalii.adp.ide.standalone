import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { ConfirmDialog } from "./ConfirmDialog";

describe("ConfirmDialog", () => {
  it("renders the default icon and Confirm/Cancel buttons", () => {
    // Arrange and act.
    render(
      <ConfirmDialog
        open
        title="Remove project?"
        message="This can't be undone."
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );

    // Assert.
    expect(screen.getByText("Remove project?")).not.toBeNull();
    expect(screen.getByText("This can't be undone.")).not.toBeNull();
    expect(document.querySelector(".dialog-header-icon.mdi-help-circle-outline")).not.toBeNull();
    expect(screen.getByRole("button", { name: "Cancel" })).not.toBeNull();
    expect(screen.getByRole("button", { name: "Confirm" })).not.toBeNull();
  });

  it("invokes onConfirm and onCancel from their respective buttons", () => {
    // Arrange.
    const onConfirm = vi.fn();
    const onCancel = vi.fn();
    render(
      <ConfirmDialog
        open
        title="Remove project?"
        message="This can't be undone."
        onConfirm={onConfirm}
        onCancel={onCancel}
      />,
    );

    // Act and assert, step by step.
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("calls onCancel when Escape is pressed or the backdrop is clicked", () => {
    // Arrange.
    const onCancel = vi.fn();
    const { container } = render(
      <ConfirmDialog
        open
        title="Remove project?"
        message="This can't be undone."
        onConfirm={() => {}}
        onCancel={onCancel}
      />,
    );

    // Act and assert, step by step.
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onCancel).toHaveBeenCalledTimes(1);

    fireEvent.mouseDown(container.querySelector(".dialog-overlay") as HTMLElement);
    expect(onCancel).toHaveBeenCalledTimes(2);
  });

  it("supports custom labels and a danger confirm color for destructive actions", () => {
    // Arrange and act.
    render(
      <ConfirmDialog
        open
        icon="mdi-trash-can-outline"
        title="Delete project?"
        message="This can't be undone."
        confirmLabel="Delete"
        cancelLabel="Keep it"
        confirmColor="danger"
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );

    // Assert.
    expect(document.querySelector(".dialog-header-icon.mdi-trash-can-outline")).not.toBeNull();
    const deleteButton = screen.getByRole("button", { name: "Delete" });
    expect(deleteButton.className).toContain("dialog-button-danger");
    expect(screen.getByRole("button", { name: "Keep it" })).not.toBeNull();
  });
});
