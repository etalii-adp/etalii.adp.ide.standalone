import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextActionGroupSchema, ContextSelectionSource, ContextShortcutSchema } from "../../generated/context-contract_pb";
import { type ContextActionGroup } from "../../generated/context-contract_pb";
import { ContextSelectionSchema, type ContextSelection } from "../../generated/context_pb";
import { NONE_DETAIL, selectionFor } from "../context/ContextConnectionProvider";
import { RibbonBar } from "./RibbonBar";
import { RibbonContextualGroups, formatShortcut, tooltipFor } from "./RibbonContextualGroups";

const executeAction = vi.fn(async () => ({ accepted: true, error: "" }));
const contextState: { selection: ContextSelection | null; actions: ContextActionGroup[] } = { selection: null, actions: [] };

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select: vi.fn(), executeAction, executeShortcut: vi.fn() }),
    useContextSelection: () => ({ ...contextState, levels: [], preview: null, pendingReveal: null, connected: true }),
    // RibbonBar now also mounts the History group and the project shortcuts; give them empty
    // project actions and no open prompt so those consumers are inert in this file's tests.
    useProjectActions: () => [],
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  };
});

const entryA = new Uint8Array(16).fill(1);
const entryB = new Uint8Array(16).fill(2);

function select(entryId: Uint8Array, actions: ContextActionGroup[]) {
  contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, entryId, ["a.txt"], NONE_DETAIL);
  contextState.actions = actions;
}

const renameAndDelete = () => [
  create(ContextActionGroupSchema, {
    actions: [
      { id: "hierarchy.rename", label: "Rename…", icon: "mdi-pencil-outline", available: true, shortcut: { key: "F2" } },
      { id: "hierarchy.delete", label: "Delete", icon: "mdi-trash-can-outline", available: false, unavailableReason: "Locked." },
    ],
  }),
];

describe("RibbonContextualGroups", () => {
  beforeEach(() => {
    executeAction.mockClear();
    contextState.selection = null;
    contextState.actions = [];
  });

  it("renders nothing before anything has ever been selected", () => {
    // Act.
    const { container } = render(<RibbonContextualGroups />);

    // Assert.
    expect(container.querySelector(".ribbon-group-contextual")).toBeNull();
  });

  it("keeps its buttons, disabled, once the selection goes away", () => {
    // Act and assert, step by step.
    select(entryA, renameAndDelete());
    const { rerender } = render(<RibbonContextualGroups />);
    expect((screen.getByRole("button", { name: "Rename…" }) as HTMLButtonElement).disabled).toBe(false);

    contextState.selection = null;
    contextState.actions = [];
    rerender(<RibbonContextualGroups />);

    // Still there - a ribbon that changes shape under the pointer is worse than one
    // holding greyed-out buttons saying what would apply.
    const rename = screen.getByRole("button", { name: "Rename…" }) as HTMLButtonElement;
    expect(rename.disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Delete" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("does not open a drop-down that is only being held over", async () => {
    // Arrange.
    const groups = [
      create(ContextActionGroupSchema, {
        actions: [{ id: "convert", label: "Convert to…", icon: "", available: true, items: [{ actions: [{ id: "x", label: "X", icon: "", available: true }] }] }],
      }),
    ];
    select(entryA, groups);
    const { rerender } = render(<RibbonContextualGroups />);

    // Act.
    contextState.selection = null;
    contextState.actions = [];
    rerender(<RibbonContextualGroups />);
    fireEvent.click(screen.getByRole("button", { name: /Convert to…/ }));

    // Assert.
    await waitFor(() => expect(screen.queryByRole("menu")).toBeNull());
  });

  it("renders one button per action with icon, label and tooltip, after the static groups", () => {
    // Arrange.
    select(entryA, renameAndDelete());

    render(<RibbonBar />);

    // Act and assert, step by step.
    const groups = screen.getAllByRole("button").map((button) => button.closest(".ribbon-group"));
    const contextual = document.querySelector(".ribbon-group-contextual") as HTMLElement;
    expect(groups.indexOf(contextual)).toBeGreaterThan(0);
    const rename = screen.getByRole("button", { name: "Rename…" });
    expect(rename.title).toBe("Rename… (F2)");
    expect(rename.querySelector(".mdi-pencil-outline")).toBeTruthy();
    expect(rename.closest(".ribbon-group-contextual")).toBe(contextual);
  });

  it("greys an unavailable action out with its reason as tooltip, rather than hiding it", () => {
    // Arrange.
    select(entryA, renameAndDelete());

    render(<RibbonContextualGroups />);

    // Act and assert, step by step.
    const del = screen.getByRole("button", { name: "Delete" }) as HTMLButtonElement;
    expect(del.disabled).toBe(true);
    expect(del.title).toBe("Locked.");
  });

  it("runs an action against the current selection, with no source of its own", async () => {
    // Arrange.
    select(entryA, renameAndDelete());
    render(<RibbonContextualGroups />);

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Rename…" }));

    // Assert.
    await waitFor(() => expect(executeAction).toHaveBeenCalledWith("hierarchy.rename"));
  });

  it("shows a diagram element's actions - a selected node is a selection like any other", () => {
    // Arrange.
    // Found by the diagram-workspace-tabs manual pass: innermostKey answered undefined for an
    // element-innermost chain, so the ribbon treated a selected canvas node as nothing
    // selected and held the file's actions greyed instead of showing the node's own
    // (mindmap-diagram Requirement 8.4).
    const node = create(ContextSelectionSchema, {
      source: ContextSelectionSource.DIAGRAM_CANVAS,
      id: { source: { case: "elementId", value: { value: "ID_1" } } },
      path: { segments: ["roadmap"] },
      detail: { case: "none", value: {} },
    });
    contextState.selection = create(ContextSelectionSchema, {
      source: ContextSelectionSource.EXPLORER,
      id: { source: { case: "entryId", value: { value: entryA } } },
      path: { segments: ["roadmap.adp"] },
      detail: { case: "child", value: node },
    });
    contextState.actions = [
      create(ContextActionGroupSchema, {
        actions: [{ id: "mindmap.add-child", label: "Add child", icon: "mdi-subdirectory-arrow-right", available: true }],
      }),
    ];

    render(<RibbonContextualGroups />);

    // Act and assert, step by step.
    const addChild = screen.getByRole("button", { name: "Add child" }) as HTMLButtonElement;
    expect(addChild.disabled).toBe(false);
  });

  it("keeps the previous buttons, disabled, until the new selection's actions arrive", () => {
    // Act and assert, step by step.
    select(entryA, renameAndDelete());
    const { rerender } = render(<RibbonContextualGroups />);
    expect((screen.getByRole("button", { name: "Rename…" }) as HTMLButtonElement).disabled).toBe(false);

    select(entryB, []);
    rerender(<RibbonContextualGroups />);
    const held = screen.getByRole("button", { name: "Rename…" }) as HTMLButtonElement;
    expect(held.disabled).toBe(true);

    select(entryB, renameAndDelete());
    rerender(<RibbonContextualGroups />);
    expect((screen.getByRole("button", { name: "Rename…" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("renders an action with children as a drop-down that opens the shared menu and runs a child", async () => {
    // Arrange.
    select(entryA, [
      create(ContextActionGroupSchema, {
        actions: [
          {
            id: "convert",
            label: "Convert to…",
            icon: "mdi-swap-horizontal",
            available: true,
            items: [{ actions: [{ id: "convert.mindmap", label: "Mindmap", icon: "", available: true }] }],
          },
        ],
      }),
    ]);
    render(<RibbonContextualGroups />);

    // Arrange, continued.
    const dropdown = screen.getByRole("button", { name: /Convert to…/ });
    expect(dropdown.querySelector(".ribbon-chevron")).toBeTruthy();
    expect(dropdown.getAttribute("aria-haspopup")).toBe("menu");

    // Act.
    fireEvent.keyDown(dropdown, { key: "ArrowDown" });
    fireEvent.click(await screen.findByRole("menuitem", { name: "Mindmap" }));

    // Assert.
    await waitFor(() => expect(executeAction).toHaveBeenCalledWith("convert.mindmap"));
    await waitFor(() => expect(screen.queryByRole("menu")).toBeNull());
  });

  it("closes an open drop-down when the selection changes", async () => {
    // Arrange.
    const groups = [
      create(ContextActionGroupSchema, {
        actions: [{ id: "convert", label: "Convert to…", icon: "", available: true, items: [{ actions: [{ id: "x", label: "X", icon: "", available: true }] }] }],
      }),
    ];
    select(entryA, groups);
    const { rerender } = render(<RibbonContextualGroups />);
    fireEvent.click(screen.getByRole("button", { name: /Convert to…/ }));
    await screen.findByRole("menu");

    // Act.
    select(entryB, groups);
    rerender(<RibbonContextualGroups />);

    // Assert.
    await waitFor(() => expect(screen.queryByRole("menu")).toBeNull());
  });
});

describe("formatShortcut / tooltipFor", () => {
  it("formats modifiers in a fixed order and upper-cases a single-character key", () => {
    // Arrange, act and assert.
    expect(formatShortcut(create(ContextShortcutSchema, { key: "s", ctrl: true, shift: true }))).toBe("Ctrl+Shift+S");
    expect(formatShortcut(create(ContextShortcutSchema, { key: "Delete" }))).toBe("Delete");
  });

  it("uses the label alone when there is no shortcut", () => {
    // Arrange and act.
    const action = renameAndDelete()[0]!.actions[1]!;
    const available = { ...action, available: true, unavailableReason: "" };
    // Assert.
    expect(tooltipFor(available)).toBe("Delete");
  });
});
