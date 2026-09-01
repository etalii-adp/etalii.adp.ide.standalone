import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  ContextActionGroupSchema,
  ContextSelectionAction,
  ContextSelectionSource,
  ProblemSetState,
  ProblemSeverity,
  ProjectProblemsSchema,
  type ContextActionGroup,
  type ContextSelection,
  type Problem,
  type ProjectProblems,
} from "../../generated/context_pb";
import { ErrorsWarningsPanel, visibleProblems } from "./ErrorsWarningsPanel";
import { requestTextTab } from "./textTabRequests";

const select = vi.fn<(selection: ContextSelection | null) => void>();
const executeAction = vi.fn(async () => ({ accepted: true, error: "" }));
const revealPath = vi.fn();

// The gesture, not the tab strip, is under test: the request call is the panel's whole
// contribution to go-to-line, so it is observed rather than wired through real tabs.
vi.mock("./textTabRequests", () => ({ requestTextTab: vi.fn() }));

/** What the (mocked) context connection currently holds; tests set it to simulate pushes. */
const contextState: { selection: ContextSelection | null; actions: ContextActionGroup[]; problems: ProjectProblems | null } = {
  selection: null,
  actions: [],
  problems: null,
};

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, executeAction, revealPath, clearReveal: vi.fn(), executeShortcut: vi.fn() }),
    useContextSelection: () => ({ selection: contextState.selection, actions: contextState.actions, levels: [], preview: null, pendingReveal: null, connected: true }),
    useContextProblems: () => contextState.problems,
  };
});

function problem(overrides: Partial<Problem> = {}): Problem {
  return {
    severity: ProblemSeverity.ERROR,
    message: "'vendor/unheard-of' is not a known diagram type.",
    path: { segments: ["strange.adp"] },
    ruleId: "core.unknown-type",
    stale: false,
    ...overrides,
  } as Problem;
}

function problemsOf(
  state: ProblemSetState,
  problems: Partial<Problem>[],
  overrides: { errorCount?: number; warningCount?: number; truncatedAt?: number } = {},
): ProjectProblems {
  const built = problems.map((partial) => problem(partial));
  return create(ProjectProblemsSchema, {
    state,
    problems: built,
    errorCount: built.filter((p) => p.severity === ProblemSeverity.ERROR).length,
    warningCount: built.filter((p) => p.severity === ProblemSeverity.WARNING).length,
    ...overrides,
  });
}

/** The panel's selection of itself, as the mocked backend would echo it back. */
function panelSelectionEcho(): ContextSelection {
  return {
    source: ContextSelectionSource.PROBLEMS,
    path: { segments: [] },
    id: { source: { case: "problems", value: {} } },
    detail: { case: "none", value: {} },
  } as unknown as ContextSelection;
}

function validateAllGroup(): ContextActionGroup {
  return create(ContextActionGroupSchema, {
    actions: [
      {
        id: "problems.validate.all",
        label: "Validate all",
        icon: "mdi-check-all",
        shortcut: { key: "B", ctrl: true, shift: true },
        available: true,
      },
    ],
  });
}

beforeEach(() => {
  select.mockClear();
  executeAction.mockClear();
  revealPath.mockClear();
  vi.mocked(requestTextTab).mockClear();
  contextState.selection = null;
  contextState.actions = [];
  contextState.problems = null;
});

describe("visibleProblems", () => {
  it("filters by severity", () => {
    // Arrange and act.
    const error = problem();
    const warning = problem({ severity: ProblemSeverity.WARNING, ruleId: "mindmap.unnamed-root" });

    // Assert.
    expect(visibleProblems([error, warning], true, true)).toEqual([error, warning]);
    expect(visibleProblems([error, warning], true, false)).toEqual([error]);
    expect(visibleProblems([error, warning], false, true)).toEqual([warning]);
  });
});

describe("ErrorsWarningsPanel", () => {
  it("renders each problem with severity, message and path", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {},
      { severity: ProblemSeverity.WARNING, message: "The map 'flow' has an unnamed central topic.", path: { segments: ["docs", "flow.adp"] } as Problem["path"], ruleId: "mindmap.unnamed-root" },
    ]);

    // Act.
    render(<ErrorsWarningsPanel />);

    // Assert.
    expect(screen.getByText("'vendor/unheard-of' is not a known diagram type.")).toBeTruthy();
    expect(screen.getByText("strange.adp")).toBeTruthy();
    expect(screen.getByText("The map 'flow' has an unnamed central topic.")).toBeTruthy();
    expect(screen.getByText("docs/flow.adp")).toBeTruthy();
  });

  it("shows a line location beside the path", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      { location: { location: { case: "line", value: 12 } } as Problem["location"] },
    ]);

    // Act.
    render(<ErrorsWarningsPanel />);

    // Assert.
    expect(screen.getByText("strange.adp:12")).toBeTruthy();
  });

  it("keeps the whole-set counts while a filter narrows the list", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {},
      { severity: ProblemSeverity.WARNING, message: "A warning.", ruleId: "mindmap.unnamed-root" },
    ]);

    // Arrange, continued.
    render(<ErrorsWarningsPanel />);

    // Act.
    fireEvent.click(screen.getByTitle("Hide warnings"));

    // Assert.
    // The warning row is gone...
    expect(screen.queryByText("A warning.")).toBeNull();
    // ...but both counts still say what the whole set holds (Requirement 1.8).
    const buttons = screen.getAllByRole("button");
    expect(buttons.map((button) => button.textContent)).toEqual(["1", "1"]);
  });

  it("says 'not checked yet' and 'no problems found' differently", () => {
    // Act and assert, step by step.
    contextState.problems = problemsOf(ProblemSetState.NEVER_VALIDATED, []);
    const { rerender } = render(<ErrorsWarningsPanel />);
    expect(screen.getByText("Not checked yet.")).toBeTruthy();
    expect(screen.queryByText("No problems found.")).toBeNull();

    contextState.problems = problemsOf(ProblemSetState.VALIDATED, []);
    rerender(<ErrorsWarningsPanel />);
    expect(screen.getByText("No problems found.")).toBeTruthy();
    expect(screen.queryByText("Not checked yet.")).toBeNull();
  });

  it("shows progress while validating", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATING, [{}]);

    // Act.
    render(<ErrorsWarningsPanel />);

    // Assert.
    expect(screen.getByText(/Checking/)).toBeTruthy();
    // The old list stands while the new answer is computed.
    expect(screen.getByText("'vendor/unheard-of' is not a known diagram type.")).toBeTruthy();
  });

  it("says how many problems a truncated set is not showing", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{}, {}], { errorCount: 5, truncatedAt: 2 });

    // Act.
    render(<ErrorsWarningsPanel />);

    // Assert.
    expect(screen.getByText("3 more problems are not shown.")).toBeTruthy();
  });

  it("marks a stale row as stale", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{ stale: true }]);

    // Act.
    render(<ErrorsWarningsPanel />);

    // Assert.
    expect(screen.getByText("stale")).toBeTruthy();
    expect(screen.getByRole("option").className).toContain("problems-row-stale");
  });

  it("selects the panel itself when focus arrives from outside", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{}]);

    // Act.
    render(<ErrorsWarningsPanel />);
    fireEvent.focus(screen.getByRole("option"));

    // Assert.
    expect(select).toHaveBeenCalledTimes(1);
    const sent = select.mock.calls[0]![0]!;
    expect(sent.source).toBe(ContextSelectionSource.PROBLEMS);
    expect(sent.id?.source.case).toBe("problems");
  });

  it("opens the context menu from pushed actions on right-click", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{}]);
    contextState.selection = panelSelectionEcho();
    contextState.actions = [validateAllGroup()];

    // Act.
    render(<ErrorsWarningsPanel />);
    fireEvent.contextMenu(screen.getByRole("listbox"));

    // Assert.
    expect(screen.getByRole("menuitem", { name: /Validate all/ })).toBeTruthy();
    // Selecting with CONTEXT_MENU is what asked the backend for these actions.
    const sent = select.mock.calls.at(-1)![0]!;
    expect(sent.detail.case).toBe("action");
    expect(sent.detail.value).toBe(ContextSelectionAction.CONTEXT_MENU);
  });

  it("executes a menu action against the panel source", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{}]);
    contextState.selection = panelSelectionEcho();
    contextState.actions = [validateAllGroup()];

    // Act.
    render(<ErrorsWarningsPanel />);
    fireEvent.contextMenu(screen.getByRole("listbox"));
    fireEvent.click(screen.getByRole("menuitem", { name: /Validate all/ }));

    // Assert.
    expect(executeAction).toHaveBeenCalledTimes(1);
    const [actionId, source] = executeAction.mock.calls[0] as unknown as [string, { source: { case: string } }];
    expect(actionId).toBe("problems.validate.all");
    expect(source.source.case).toBe("problems");
  });

  it("runs a pushed shortcut against the panel - and only a pushed one", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{}]);
    contextState.selection = panelSelectionEcho();
    contextState.actions = [validateAllGroup()];

    render(<ErrorsWarningsPanel />);
    const list = screen.getByRole("listbox");

    // Act and assert, step by step.
    fireEvent.keyDown(list, { key: "B", ctrlKey: true, shiftKey: true });
    expect(executeAction).toHaveBeenCalledWith("problems.validate.all", expect.anything());

    executeAction.mockClear();
    fireEvent.keyDown(list, { key: "B", ctrlKey: true }); // not the pushed binding
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("keeps exactly one row in the tab order", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{}, { message: "Second.", path: { segments: ["b.adp"] } as Problem["path"] }]);

    render(<ErrorsWarningsPanel />);

    // Act and assert, step by step.
    const rows = screen.getAllByRole("option");
    expect(rows.map((row) => row.tabIndex)).toEqual([0, -1]);
  });

  it("moves with the arrows and reveals with Enter, driven by real key events", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {},
      { message: "Second.", path: { segments: ["docs", "b.adp"] } as Problem["path"] },
    ]);

    render(<ErrorsWarningsPanel />);
    const list = screen.getByRole("listbox");
    const rows = screen.getAllByRole("option");
    rows[0]!.focus();

    // Act and assert, step by step.
    fireEvent.keyDown(list, { key: "ArrowDown" });
    expect(document.activeElement).toBe(rows[1]);

    fireEvent.keyDown(list, { key: "Enter" });
    expect(revealPath).toHaveBeenCalledWith(["docs", "b.adp"]);

    fireEvent.keyDown(list, { key: "ArrowUp" });
    expect(document.activeElement).toBe(rows[0]);
  });

  it("acts on the row the same tick's focus just reached", () => {
    // Arrange.
    // The choice dialog's race: focus and keydown in one tick, with no re-render between.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {},
      { message: "Second.", path: { segments: ["b.adp"] } as Problem["path"] },
    ]);

    // Arrange, continued.
    render(<ErrorsWarningsPanel />);
    const list = screen.getByRole("listbox");
    const rows = screen.getAllByRole("option");

    // Act.
    // Focus the second row and press Enter without letting React flush focusedKey state.
    rows[1]!.focus();
    fireEvent.keyDown(list, { key: "Enter" });

    // Assert.
    expect(revealPath).toHaveBeenCalledWith(["b.adp"]);
  });

  it("double-clicking a row reveals its file", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [{}]);

    // Act.
    render(<ErrorsWarningsPanel />);
    fireEvent.doubleClick(screen.getByRole("option"));

    // Assert: no line on this problem, so no text tab either - activation adds a
    // destination for line problems, it removes none (modular-text-editors R8.2).
    expect(revealPath).toHaveBeenCalledWith(["strange.adp"]);
    expect(requestTextTab).not.toHaveBeenCalled();
  });

  it("a line-carrying problem also opens the file's text editor at that line (R8.1)", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {
        message: "The workspace block never closes.",
        ruleId: "c4.unclosed-block",
        path: { segments: ["docs", "flow.dsl"] },
        location: { location: { case: "line", value: 12 } },
      } as Partial<Problem>,
    ]);

    // Act.
    render(<ErrorsWarningsPanel />);
    fireEvent.doubleClick(screen.getByRole("option"));

    // Assert: the reveal still happens (R8.2), and the text tab is asked for on top of it,
    // resolver-routed - the panel names no editor.
    expect(revealPath).toHaveBeenCalledWith(["docs", "flow.dsl"]);
    expect(requestTextTab).toHaveBeenCalledWith({ path: ["docs", "flow.dsl"], editorId: "*", line: 12 });
  });

  it("an element-carrying problem keeps its existing activation, with no text tab (R8.2)", () => {
    // Arrange.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {
        ruleId: "mindmap.empty-node",
        path: { segments: ["map.adp"] },
        location: { location: { case: "elementId", value: { value: "node-7" } } },
      } as Partial<Problem>,
    ]);

    // Act.
    render(<ErrorsWarningsPanel />);
    fireEvent.doubleClick(screen.getByRole("option"));

    // Assert.
    expect(revealPath).toHaveBeenCalledWith(["map.adp"]);
    expect(requestTextTab).not.toHaveBeenCalled();
  });

  it("a problem located in another file shows and reveals that file, not the diagram's", () => {
    // Arrange.
    // A folder-subject diagram: the .adp marks the folder, but the mistake is in the playbook
    // that named a role with no folder. Revealing the one-line registration instead would send
    // the reader somewhere with nothing to fix.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {
        message: "The role 'absent-role' has no folder.",
        ruleId: "ansible.role-missing",
        path: { segments: ["infrastructure.adp"] },
        location: {
          location: {
            case: "file",
            value: { path: { segments: ["infrastructure", "playbooks", "deploy.yml"] }, line: 6 },
          },
        },
      } as Partial<Problem>,
    ]);

    // Act.
    render(<ErrorsWarningsPanel />);
    const row = screen.getByRole("option");

    // Assert.
    expect(row.textContent).toContain("infrastructure/playbooks/deploy.yml:6");
    expect(row.textContent).not.toContain("infrastructure.adp");

    fireEvent.doubleClick(row);
    expect(revealPath).toHaveBeenCalledWith(["infrastructure", "playbooks", "deploy.yml"]);
  });

  it("a file location with no line names the file alone", () => {
    // Arrange.
    // Line 0 means the rule could only name the file - an empty role folder has no line.
    contextState.problems = problemsOf(ProblemSetState.VALIDATED, [
      {
        ruleId: "ansible.empty-role",
        location: {
          location: {
            case: "file",
            value: { path: { segments: ["infrastructure", "roles", "hollow"] }, line: 0 },
          },
        },
      } as Partial<Problem>,
    ]);

    // Act.
    render(<ErrorsWarningsPanel />);

    // Assert.
    expect(screen.getByRole("option").textContent).toContain("infrastructure/roles/hollow");
    expect(screen.getByRole("option").textContent).not.toContain(":0");
  });
});
