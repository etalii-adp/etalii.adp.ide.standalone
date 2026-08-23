import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextActionGroupSchema, type ContextActionGroup } from "../../generated/context_pb";
import { PROJECT_SOURCE } from "../context/ContextConnectionProvider";
import { RibbonHistoryGroup } from "./RibbonHistoryGroup";

const executeAction = vi.fn(async () => ({ accepted: true, error: "" }));
const projectActionsState: { groups: ContextActionGroup[] } = { groups: [] };

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select: vi.fn(), executeAction, executeShortcut: vi.fn() }),
    useProjectActions: () => projectActionsState.groups,
  };
});

const undoAndRedo = (undoAvailable: boolean, redoAvailable: boolean): ContextActionGroup[] => [
  create(ContextActionGroupSchema, {
    actions: [
      {
        id: "history.undo",
        label: "Undo",
        icon: "mdi-undo",
        available: undoAvailable,
        shortcut: { key: "z", ctrl: true },
        unavailableReason: undoAvailable ? "" : "There is nothing to undo.",
      },
      {
        id: "history.redo",
        label: "Redo",
        icon: "mdi-redo",
        available: redoAvailable,
        shortcut: { key: "y", ctrl: true },
        unavailableReason: redoAvailable ? "" : "There is nothing to redo.",
      },
    ],
  }),
];

describe("RibbonHistoryGroup", () => {
  beforeEach(() => {
    executeAction.mockClear();
    projectActionsState.groups = [];
  });

  it("renders nothing before the backend has pushed any project actions", () => {
    const { container } = render(<RibbonHistoryGroup />);

    expect(container.querySelector(".ribbon-group-history")).toBeNull();
  });

  it("renders a button per pushed action, with its icon, label and shortcut tooltip", () => {
    projectActionsState.groups = undoAndRedo(true, false);

    render(<RibbonHistoryGroup />);

    const undo = screen.getByRole("button", { name: "Undo" });
    expect(undo.title).toBe("Undo (Ctrl+Z)");
    expect(undo.querySelector(".mdi-undo")).toBeTruthy();
    expect((undo as HTMLButtonElement).disabled).toBe(false);
  });

  it("greys an unavailable action out with its reason as tooltip, rather than hiding it", () => {
    projectActionsState.groups = undoAndRedo(true, false);

    render(<RibbonHistoryGroup />);

    const redo = screen.getByRole("button", { name: "Redo" }) as HTMLButtonElement;
    expect(redo.disabled).toBe(true);
    expect(redo.title).toBe("There is nothing to redo.");
  });

  it("runs the action against the project as its source", async () => {
    projectActionsState.groups = undoAndRedo(true, false);
    render(<RibbonHistoryGroup />);

    fireEvent.click(screen.getByRole("button", { name: "Undo" }));

    await waitFor(() => expect(executeAction).toHaveBeenCalledWith("history.undo", PROJECT_SOURCE));
  });
});
