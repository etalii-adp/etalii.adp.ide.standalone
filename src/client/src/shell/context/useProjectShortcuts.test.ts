import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { createElement } from "react";
import { create } from "@bufbuild/protobuf";
import { ContextActionGroupSchema } from "../../generated/context-contract_pb";
import { type ContextActionGroup } from "../../generated/context-contract_pb";
import { type ContextPrompt } from "../../generated/context_pb";
import { PROJECT_SOURCE } from "./ContextConnectionProvider";
import { useProjectShortcuts } from "./useProjectShortcuts";

const executeAction = vi.fn(async () => ({ accepted: true, error: "" }));
const state: { groups: ContextActionGroup[]; prompt: ContextPrompt | null } = { groups: [], prompt: null };

vi.mock("./ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select: vi.fn(), executeAction, executeShortcut: vi.fn() }),
    useProjectActions: () => state.groups,
    useContextPrompt: () => ({ prompt: state.prompt, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  };
});

function undoAndRedo(): ContextActionGroup[] {
  return [
    create(ContextActionGroupSchema, {
      actions: [
        { id: "history.undo", label: "Undo", icon: "mdi-undo", available: true, shortcut: { key: "z", ctrl: true } },
        { id: "history.redo", label: "Redo", icon: "mdi-redo", available: true, shortcut: { key: "y", ctrl: true } },
      ],
    }),
  ];
}

function Harness() {
  useProjectShortcuts();
  return null;
}

describe("useProjectShortcuts", () => {
  beforeEach(() => {
    executeAction.mockClear();
    state.groups = undoAndRedo();
    state.prompt = null;
  });

  afterEach(() => {
    document.body.innerHTML = "";
  });

  it("runs the matched action on Ctrl+Z", () => {
    // Arrange.
    render(createElement(Harness));

    // Act.
    fireEvent.keyDown(document.body, { key: "z", ctrlKey: true });

    // Assert.
    expect(executeAction).toHaveBeenCalledWith("history.undo", PROJECT_SOURCE);
  });

  it("reaches redo on Ctrl+Shift+Z, the alias normalised to Ctrl+Y", () => {
    // Arrange.
    render(createElement(Harness));

    // Act.
    fireEvent.keyDown(document.body, { key: "z", ctrlKey: true, shiftKey: true });

    // Assert.
    expect(executeAction).toHaveBeenCalledWith("history.redo", PROJECT_SOURCE);
  });

  it("leaves a key it does not match entirely alone", () => {
    // Arrange.
    render(createElement(Harness));

    // Act.
    fireEvent.keyDown(document.body, { key: "a", ctrlKey: true });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("ignores the shortcut while typing in an input", () => {
    // Arrange.
    render(createElement(Harness));
    const input = document.createElement("input");
    document.body.appendChild(input);

    // Act.
    fireEvent.keyDown(input, { key: "z", ctrlKey: true });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("ignores the shortcut while typing in a textarea", () => {
    // Arrange.
    render(createElement(Harness));
    const textarea = document.createElement("textarea");
    document.body.appendChild(textarea);

    // Act.
    fireEvent.keyDown(textarea, { key: "z", ctrlKey: true });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("ignores the shortcut inside a contenteditable surface", () => {
    // Arrange.
    render(createElement(Harness));
    const editable = document.createElement("div");
    editable.setAttribute("contenteditable", "true");
    // jsdom does not always derive isContentEditable from the attribute; state it outright.
    Object.defineProperty(editable, "isContentEditable", { value: true, configurable: true });
    document.body.appendChild(editable);

    // Act.
    fireEvent.keyDown(editable, { key: "z", ctrlKey: true });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("ignores the shortcut while a modal prompt is open", () => {
    // Arrange.
    state.prompt = { interactionId: undefined, prompt: { case: "inputDialog", value: {} } } as unknown as ContextPrompt;
    render(createElement(Harness));

    // Act.
    fireEvent.keyDown(document.body, { key: "z", ctrlKey: true });

    // Assert.
    expect(executeAction).not.toHaveBeenCalled();
  });
});
