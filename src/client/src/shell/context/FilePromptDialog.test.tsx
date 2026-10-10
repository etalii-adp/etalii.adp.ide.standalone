import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextPromptSchema, FileDialogPromptSchema, type ContextOption, type FileDialogPrompt } from "../../generated/context_pb";
import { ContextPromptHost } from "./ContextPromptHost";
import { FilePromptDialog } from "./FilePromptDialog";

function option(id: string, label: string, selectable: boolean, children: Partial<ContextOption>[] = []): Partial<ContextOption> {
  return { id, label, selectable, children: children as ContextOption[] };
}

/** One pinned file, and a tree of one folder holding a file beside one file at the top. */
function filePrompt(): FileDialogPrompt {
  return create(FileDialogPromptSchema, {
    title: "Relate to",
    icon: "mdi-link-variant",
    confirmLabel: "Choose",
    emptyMessage: "No table in this project.",
    pinned: [option("notes.yaml", "This file", true)] as ContextOption[],
    files: [option("data", "data", false, [option("data/countries.yaml", "countries.yaml", true)]), option("cities.yaml", "cities.yaml", true)] as ContextOption[],
  });
}

function renderDialog(prompt: FileDialogPrompt, submitResult = { completed: true, error: "" }) {
  const onSubmit = vi.fn(async (_value: string, _text?: string) => submitResult);
  const onPropose = vi.fn(async (revision: number, _value: string) => ({ revision, valid: true, reason: "" }));
  const onCancel = vi.fn();
  render(<FilePromptDialog prompt={prompt} onPropose={onPropose} onSubmit={onSubmit} onCancel={onCancel} />);
  return { onSubmit, onPropose, onCancel };
}

const confirmButton = () => screen.getByRole("button", { name: "Choose" }) as HTMLButtonElement;
const row = (label: string) => screen.getByText(label).closest("button") as HTMLButtonElement;
const rowLabels = () => screen.getAllByRole("treeitem").map((item) => item.textContent);

describe("FilePromptDialog", () => {
  it("shows the pinned entries first and then the tree it was given", () => {
    // Act.
    renderDialog(filePrompt());

    // Assert: the folder is closed, so its file is not on screen yet.
    expect(screen.getByRole("tree", { name: "Relate to" })).toBeTruthy();
    expect(rowLabels()).toEqual(["This file", "data", "cities.yaml"]);
  });

  it("answers with the chosen file's path", async () => {
    // Arrange.
    const { onSubmit } = renderDialog(filePrompt());

    // Act: open the folder, pick the file in it, confirm.
    fireEvent.click(screen.getByRole("button", { name: "Expand data" }));
    fireEvent.click(row("countries.yaml"));
    await act(async () => {
      fireEvent.click(confirmButton());
    });

    // Assert: the value is the path, and no name travels with it.
    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit.mock.calls[0][0]).toBe("data/countries.yaml");
    expect(onSubmit.mock.calls[0][1] ?? "").toBe("");
  });

  it("answers with a pinned entry's path", async () => {
    // Arrange.
    const { onSubmit } = renderDialog(filePrompt());

    // Act.
    fireEvent.click(row("This file"));
    await act(async () => {
      fireEvent.click(confirmButton());
    });

    // Assert.
    expect(onSubmit.mock.calls[0][0]).toBe("notes.yaml");
  });

  it("does not let a folder be the answer", () => {
    // Arrange.
    const { onSubmit } = renderDialog(filePrompt());

    // Act.
    fireEvent.click(row("data"));

    // Assert.
    expect(confirmButton().disabled).toBe(true);
    fireEvent.click(confirmButton());
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("asks for no name", () => {
    // Act.
    renderDialog(filePrompt());

    // Assert.
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("says so when there is nothing to choose", () => {
    // Act.
    renderDialog(create(FileDialogPromptSchema, { title: "Relate to", confirmLabel: "Choose", emptyMessage: "No table in this project." }));

    // Assert.
    expect(screen.getByText("No table in this project.")).toBeTruthy();
    expect(confirmButton().disabled).toBe(true);
  });

  it("writes nothing when it is cancelled", () => {
    // Arrange.
    const { onSubmit, onCancel } = renderDialog(filePrompt());

    // Act.
    fireEvent.click(row("cities.yaml"));
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    // Assert.
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("keeps the dialog open with the backend's sentence when the choice is refused", async () => {
    // Arrange.
    renderDialog(filePrompt(), { completed: false, error: "That file cannot be chosen here." });

    // Act.
    fireEvent.click(row("cities.yaml"));
    await act(async () => {
      fireEvent.click(confirmButton());
    });

    // Assert.
    expect(screen.getByRole("alert").textContent).toBe("That file cannot be chosen here.");
    expect(screen.getByRole("tree")).toBeTruthy();
  });
});

describe("ContextPromptHost with a file dialog", () => {
  it("renders the file dialog, and does not cancel it as a prompt it cannot show", () => {
    // Arrange.
    const prompt = create(ContextPromptSchema, { interactionId: { value: new Uint8Array(16) }, prompt: { case: "fileDialog", value: filePrompt() } });
    const onCancel = vi.fn();

    // Act.
    render(<ContextPromptHost prompt={prompt} onPropose={vi.fn()} onSubmit={vi.fn()} onCancel={onCancel} />);

    // Assert.
    expect(screen.getByRole("tree", { name: "Relate to" })).toBeTruthy();
    expect(rowLabels()).toEqual(["This file", "data", "cities.yaml"]);
    expect(onCancel).not.toHaveBeenCalled();
  });
});
