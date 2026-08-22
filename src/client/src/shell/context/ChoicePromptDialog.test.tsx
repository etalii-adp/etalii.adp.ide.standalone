import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ChoiceDialogPromptSchema, type ChoiceDialogPrompt, type ContextOption } from "../../generated/context_pb";
import { ChoicePromptDialog, visibleRows } from "./ChoicePromptDialog";

function option(id: string, label: string, selectable: boolean, children: Partial<ContextOption>[] = []): Partial<ContextOption> {
  return { id, label, selectable, children: children as ContextOption[] };
}

/** Two vendors: c4 with two types, uml with one - and one type carrying a subtype. */
function twoVendorPrompt(): ChoiceDialogPrompt {
  return create(ChoiceDialogPromptSchema, {
    title: "Add diagram",
    icon: "mdi-plus",
    confirmLabel: "Add",
    emptyMessage: "No diagram types are available.",
    options: [
      option("c4", "c4", false, [
        option("c4/component", "Component", true, [option("c4/component/code", "Code", true)]),
        option("c4/context", "System Context", true),
      ]),
      option("uml", "uml", false, [option("uml/class", "Class diagram", true)]),
    ] as ContextOption[],
  });
}

function oneVendorPrompt(): ChoiceDialogPrompt {
  return create(ChoiceDialogPromptSchema, {
    title: "Add diagram",
    icon: "mdi-plus",
    confirmLabel: "Add",
    emptyMessage: "No diagram types are available.",
    options: [option("c4", "c4", false, [option("c4/context", "System Context", true)])] as ContextOption[],
  });
}

function emptyPrompt(): ChoiceDialogPrompt {
  return create(ChoiceDialogPromptSchema, {
    title: "Add diagram",
    icon: "mdi-plus",
    confirmLabel: "Add",
    emptyMessage: "No diagram types are available.",
    options: [],
  });
}

function renderDialog(
  prompt: ChoiceDialogPrompt,
  submitResult = { completed: true, error: "" },
  verdict: { valid: boolean; reason: string } = { valid: true, reason: "" },
) {
  const onSubmit = vi.fn(async (_value: string, _text?: string) => submitResult);
  const onPropose = vi.fn(async (revision: number, _value: string) => ({ revision, ...verdict }));
  const onCancel = vi.fn();
  render(<ChoicePromptDialog prompt={prompt} onPropose={onPropose} onSubmit={onSubmit} onCancel={onCancel} />);
  return { onSubmit, onPropose, onCancel };
}

const confirmButton = () => screen.getByRole("button", { name: "Add" });
const row = (label: string) => screen.getByText(label).closest("button") as HTMLButtonElement;
const tree = () => screen.getByRole("tree");
const treeItems = () => screen.getAllByRole("treeitem");

describe("visibleRows", () => {
  it("lists only what is on screen, in reading order", () => {
    const options = twoVendorPrompt().options;

    const closed = visibleRows(options, new Set());
    const c4Open = visibleRows(options, new Set(["c4"]));

    expect(closed.map((r) => r.option.id)).toEqual(["c4", "uml"]);
    expect(c4Open.map((r) => r.option.id)).toEqual(["c4", "c4/component", "c4/context", "uml"]);
  });

  it("records depth and parent so the keyboard can move up and in", () => {
    const rows = visibleRows(twoVendorPrompt().options, new Set(["c4", "c4/component"]));

    const code = rows.find((r) => r.option.id === "c4/component/code")!;
    expect(code.depth).toBe(2);
    expect(code.parentId).toBe("c4/component");
    expect(rows.find((r) => r.option.id === "c4")!.expanded).toBe(true);
  });
});

describe("ChoicePromptDialog rendering", () => {
  it("shows the vendors closed when there is more than one", () => {
    renderDialog(twoVendorPrompt());

    expect(treeItems().map((item) => item.textContent)).toEqual(["c4", "uml"]);
  });

  it("opens the only vendor when there is just one, so a choice is one click away", () => {
    renderDialog(oneVendorPrompt());

    expect(screen.getByText("System Context")).toBeTruthy();
  });

  it("marks groups and leaves apart", () => {
    renderDialog(oneVendorPrompt());

    const [group, leaf] = treeItems();
    expect(group.className).toContain("choice-tree-row-group");
    expect(group.getAttribute("aria-selected")).toBeNull();
    expect(leaf.className).not.toContain("choice-tree-row-group");
    expect(leaf.getAttribute("aria-selected")).toBe("false");
  });

  it("shows the empty message and keeps confirm disabled when there are no options", () => {
    renderDialog(emptyPrompt());

    expect(screen.getByText("No diagram types are available.")).toBeTruthy();
    expect(screen.queryByRole("tree")).toBeNull();
    expect(confirmButton()).toHaveProperty("disabled", true);
  });

  it("is a single Tab stop: exactly one row is tabbable", () => {
    renderDialog(twoVendorPrompt());

    const tabbable = screen
      .getAllByRole("button")
      .filter((button) => button.className.includes("choice-tree-label") && button.tabIndex === 0);
    expect(tabbable).toHaveLength(1);
  });

  it("keeps the chevron out of the tab order", () => {
    renderDialog(twoVendorPrompt());

    expect(screen.getByRole("button", { name: "Expand c4" }).tabIndex).toBe(-1);
  });
});

describe("ChoicePromptDialog choosing", () => {
  it("keeps confirm disabled until a leaf is chosen, and a group never enables it", () => {
    renderDialog(twoVendorPrompt());
    expect(confirmButton()).toHaveProperty("disabled", true);

    fireEvent.click(row("c4")); // a group: opens it
    expect(confirmButton()).toHaveProperty("disabled", true);

    fireEvent.click(row("System Context"));
    expect(confirmButton()).toHaveProperty("disabled", false);
  });

  it("clicking a group toggles it open and closed", () => {
    renderDialog(twoVendorPrompt());

    fireEvent.click(row("c4"));
    expect(screen.getByText("System Context")).toBeTruthy();

    fireEvent.click(row("c4"));
    expect(screen.queryByText("System Context")).toBeNull();
  });

  it("the chevron toggles on one click and ignores the second click of a double click", () => {
    renderDialog(twoVendorPrompt());
    const chevron = () => screen.getByRole("button", { name: /^(Expand|Collapse) c4$/ });

    fireEvent.click(chevron(), { detail: 1 });
    expect(screen.getByText("System Context")).toBeTruthy();

    // A real double click is click(1), click(2), dblclick; handled naively the row would
    // close again on the second click.
    fireEvent.click(chevron(), { detail: 1 });
    fireEvent.click(chevron(), { detail: 2 });
    fireEvent.doubleClick(chevron(), { detail: 2 });
    expect(screen.queryByText("System Context")).toBeNull();
  });

  it("submits the chosen leaf's id on confirm", async () => {
    const { onSubmit } = renderDialog(twoVendorPrompt());
    fireEvent.click(row("c4"));
    fireEvent.click(row("System Context"));

    await act(async () => {
      fireEvent.click(confirmButton());
    });

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith("c4/context");
  });

  it("double-clicking a leaf chooses it and submits in one go", async () => {
    const { onSubmit } = renderDialog(oneVendorPrompt());

    await act(async () => {
      fireEvent.doubleClick(screen.getByText("System Context").closest("li")!);
    });

    expect(onSubmit).toHaveBeenCalledWith("c4/context");
  });

  it("shows a failed submission's error and stays open with the choice intact", async () => {
    const { onSubmit } = renderDialog(oneVendorPrompt(), { completed: false, error: "Creating a System Context diagram is not supported yet." });
    fireEvent.click(row("System Context"));

    await act(async () => {
      fireEvent.click(confirmButton());
    });

    expect(screen.getByRole("alert").textContent).toBe("Creating a System Context diagram is not supported yet.");
    expect(screen.getByRole("tree")).toBeTruthy();
    expect(row("System Context").closest("li")!.getAttribute("aria-selected")).toBe("true");
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it("cancel and Escape call onCancel without submitting", () => {
    const { onSubmit, onCancel } = renderDialog(oneVendorPrompt());
    fireEvent.click(row("System Context"));

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    fireEvent.keyDown(document, { key: "Escape" });

    expect(onCancel).toHaveBeenCalled();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

describe("ChoicePromptDialog keyboard", () => {
  function focusRow(label: string) {
    act(() => row(label).focus());
  }

  it("ArrowDown and ArrowUp move through the visible rows", () => {
    renderDialog(twoVendorPrompt());
    focusRow("c4");

    fireEvent.keyDown(tree(), { key: "ArrowDown" });
    expect(document.activeElement).toBe(row("uml"));

    fireEvent.keyDown(tree(), { key: "ArrowUp" });
    expect(document.activeElement).toBe(row("c4"));
  });

  it("ArrowRight opens a closed group, then moves into it; ArrowLeft closes or moves to the parent", () => {
    renderDialog(twoVendorPrompt());
    focusRow("c4");

    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    expect(screen.getByText("Component")).toBeTruthy();
    expect(document.activeElement).toBe(row("c4"));

    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    expect(document.activeElement).toBe(row("Component"));

    fireEvent.keyDown(tree(), { key: "ArrowLeft" });
    expect(document.activeElement).toBe(row("c4"));

    fireEvent.keyDown(tree(), { key: "ArrowLeft" });
    expect(screen.queryByText("Component")).toBeNull();
  });

  it("acts on the row that has focus even when the key arrives in the same tick as the focus", () => {
    // Found in the manual pass (task 17): focus() and an immediate keydown, with no render
    // in between, left the handler acting on the previously focused row - the onFocus
    // state update had not been committed yet. The key must follow the DOM's focus, not
    // the state's memory of it. No act() around focus() here, on purpose: that flush is
    // exactly what hid the bug from the other keyboard tests.
    renderDialog(twoVendorPrompt());
    row("c4").focus();
    fireEvent.keyDown(tree(), { key: "ArrowRight" });
    expect(screen.getByText("System Context")).toBeTruthy();

    row("uml").focus();
    fireEvent.keyDown(tree(), { key: "ArrowRight" });

    expect(screen.getByText("Class diagram")).toBeTruthy();
    // ...and c4 was not toggled again by the stale id.
    expect(screen.getByText("System Context")).toBeTruthy();
  });

  it("ArrowRight on a leaf does nothing", () => {
    renderDialog(oneVendorPrompt());
    focusRow("System Context");

    fireEvent.keyDown(tree(), { key: "ArrowRight" });

    expect(document.activeElement).toBe(row("System Context"));
  });

  it("Space chooses a leaf; Enter on the chosen leaf confirms", async () => {
    const { onSubmit } = renderDialog(oneVendorPrompt());
    focusRow("System Context");

    fireEvent.keyDown(tree(), { key: " " });
    expect(confirmButton()).toHaveProperty("disabled", false);

    await act(async () => {
      fireEvent.keyDown(tree(), { key: "Enter" });
    });
    expect(onSubmit).toHaveBeenCalledWith("c4/context");
  });

  it("Enter on an unchosen leaf chooses it rather than submitting", async () => {
    const { onSubmit } = renderDialog(oneVendorPrompt());
    focusRow("System Context");

    await act(async () => {
      fireEvent.keyDown(tree(), { key: "Enter" });
    });

    expect(confirmButton()).toHaveProperty("disabled", false);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("Enter on a group toggles it", () => {
    renderDialog(twoVendorPrompt());
    focusRow("c4");

    fireEvent.keyDown(tree(), { key: "Enter" });

    expect(screen.getByText("System Context")).toBeTruthy();
  });
});

/** A prompt that also asks for a name, with a suggestion on each selectable option. */
function namedPrompt(): ChoiceDialogPrompt {
  return create(ChoiceDialogPromptSchema, {
    title: "Add diagram",
    icon: "mdi-plus",
    confirmLabel: "Add",
    emptyMessage: "No diagram types are available.",
    nameField: { label: "Name", initialValue: "" },
    options: [
      option("c4", "c4", false, [
        { ...option("c4/context", "System Context", true), suggestedValue: "context" },
        { ...option("c4/container", "Container", true), suggestedValue: "container" },
      ]),
    ] as ContextOption[],
  });
}

const nameInput = () => screen.getByLabelText("Name") as HTMLInputElement;

describe("ChoicePromptDialog name field", () => {
  it("starts out holding the first selectable option's suggestion", () => {
    renderDialog(namedPrompt());

    expect(nameInput().value).toBe("context");
  });

  it("follows the selected option until the user types, and never after", () => {
    renderDialog(namedPrompt());

    fireEvent.click(row("Container"));
    expect(nameInput().value).toBe("container");

    fireEvent.change(nameInput(), { target: { value: "domain" } });
    fireEvent.click(row("System Context"));

    expect(nameInput().value).toBe("domain");
  });

  it("submits the chosen option together with the name", async () => {
    const { onSubmit } = renderDialog(namedPrompt());

    fireEvent.click(row("System Context"));
    fireEvent.change(nameInput(), { target: { value: "domain" } });
    // Wait for the debounced verdict, which is what enables the confirm button.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 250));
    });
    fireEvent.click(confirmButton());

    await act(async () => {});
    expect(onSubmit).toHaveBeenCalledWith("c4/context", "domain");
  });

  it("can be confirmed on the untouched suggestion without any round trip", async () => {
    const { onSubmit, onPropose } = renderDialog(namedPrompt());

    fireEvent.click(row("System Context"));
    expect(confirmButton()).toHaveProperty("disabled", false);
    fireEvent.click(confirmButton());

    await act(async () => {});
    expect(onSubmit).toHaveBeenCalledWith("c4/context", "context");
    expect(onPropose).not.toHaveBeenCalled();
  });

  it("keeps the confirm button disabled while a typed name has no verdict yet", () => {
    renderDialog(namedPrompt());

    fireEvent.click(row("System Context"));
    fireEvent.change(nameInput(), { target: { value: "domain" } });

    // The verdict for this revision has not arrived, so the name is not yet acceptable.
    expect(confirmButton()).toHaveProperty("disabled", true);
  });

  it("shows the reason a name was refused and keeps the confirm button disabled", async () => {
    renderDialog(namedPrompt(), { completed: true, error: "" }, { valid: false, reason: "An item named 'taken.adp' already exists in this folder." });

    fireEvent.click(row("System Context"));
    fireEvent.change(nameInput(), { target: { value: "taken" } });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 250));
    });

    expect(screen.getByRole("alert").textContent).toContain("already exists");
    expect(confirmButton()).toHaveProperty("disabled", true);
  });

  it("renders no name field for a prompt that does not ask for one", () => {
    renderDialog(oneVendorPrompt());

    expect(screen.queryByLabelText("Name")).toBeNull();
  });
});
