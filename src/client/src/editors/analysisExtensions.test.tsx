import { describe, expect, it, vi } from "vitest";
import { act, render, waitFor } from "@testing-library/react";
import { EditorView, runScopeHandlers } from "@codemirror/view";
import { forEachDiagnostic, type Diagnostic } from "@codemirror/lint";
import { completionStatus, currentCompletions } from "@codemirror/autocomplete";
import { BaseTextEditor } from "./BaseTextEditor";
import { TextEditorPanel } from "./TextEditorPanel";
import { analysisExtensions, reanalyse } from "./analysisExtensions";
import type { EditorAnalysis, EditorCompletion, EditorFinding, EditorFindingCounts } from "./editorAnalysis";

/**
 * The shared presentation of an analysis, over a fake one: what any editor module's findings,
 * completions and hover text look like and how they are reached (json-editor tasks 4 and 5).
 * The real CodeMirror view is mounted; jsdom applies no CSS, so what is asserted is structure -
 * classes, elements, text - never a colour.
 */

// The panel tests below read their file through a fixed stream, so the real view mounts over it.
const stream = { save: vi.fn((_content: string) => Promise.resolve("")) };
vi.mock("./useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./useEditorText")>();
  return {
    ...actual,
    useEditorText: () => ({
      model: { text: "alpha beta", revision: 1, loaded: true, contentMime: "editor/plain" },
      loading: false,
      failed: false,
      save: stream.save,
    }),
  };
});

// jsdom has no layout, and CodeMirror measures text ranges to place a list or a tooltip. An empty
// measurement places it at the origin, which is all a structural assertion needs.
const emptyRects = () => Object.assign([], { item: () => null }) as unknown as DOMRectList;
Range.prototype.getClientRects ??= emptyRects;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();

function mount(text: string, analysis: EditorAnalysis) {
  const onCounts = vi.fn<(counts: EditorFindingCounts) => void>();
  const extensions = analysisExtensions({ analysis: { current: analysis }, onCounts: { current: onCounts } }, { delay: 0 });
  const rendered = render(<BaseTextEditor content={text} onChange={() => {}} extensions={extensions} />);
  const view = EditorView.findFromDOM(rendered.getByTestId("base-text-editor"))!;
  return { rendered, view, onCounts };
}

function diagnostics(view: EditorView): Diagnostic[] {
  const found: Diagnostic[] = [];
  forEachDiagnostic(view.state, (diagnostic) => found.push(diagnostic));
  return found;
}

function press(view: EditorView, key: string, modifiers: { ctrlKey?: boolean; shiftKey?: boolean; keyCode?: number } = {}) {
  return runScopeHandlers(view, new KeyboardEvent("keydown", { key, ...modifiers }), "editor");
}

/** The completion list ignores Enter for its first 75 ms, so a slip of the finger accepts nothing. */
const pastInteractionDelay = () => act(() => new Promise((resolve) => setTimeout(resolve, 100)));

const finding = (from: number, to: number, severity: EditorFinding["severity"], message: string): EditorFinding => ({
  from,
  to,
  severity,
  message,
  source: "fake",
});

describe("findings", () => {
  it("underlines each severity in its own line style and marks its line in the gutter", async () => {
    // Arrange and act.
    const { rendered, view } = mount("alpha\nbeta\ngamma\n", {
      findings: () => Promise.resolve([finding(0, 5, "error", "Alpha is wrong."), finding(6, 10, "warning", "Beta is odd."), finding(11, 16, "info", "Gamma, noted.")]),
    });
    await waitFor(() => expect(diagnostics(view)).toHaveLength(3));

    // Assert. Three classes, one per severity: the theme draws them wavy, dashed and dotted.
    const content = rendered.container;
    expect(content.querySelector(".cm-lintRange-error")?.textContent).toBe("alpha");
    expect(content.querySelector(".cm-lintRange-warning")?.textContent).toBe("beta");
    expect(content.querySelector(".cm-lintRange-info")?.textContent).toBe("gamma");
    expect(content.querySelectorAll(".cm-gutter-lint .cm-lint-marker")).toHaveLength(3);
  });

  it("reaches the next finding's sentence from the keyboard, F8 and the findings panel", async () => {
    // Arrange.
    const { rendered, view } = mount("alpha\nbeta\n", { findings: () => Promise.resolve([finding(6, 10, "error", "Beta is wrong.")]) });
    await waitFor(() => expect(diagnostics(view)).toHaveLength(1));

    // Act.
    act(() => {
      press(view, "F8");
    });
    act(() => {
      press(view, "M", { ctrlKey: true, shiftKey: true, keyCode: 77 });
    });

    // Assert.
    expect(view.state.selection.main.from).toBe(6);
    await waitFor(() => expect(rendered.container.querySelector(".cm-panel-lint")?.textContent).toContain("Beta is wrong."));
  });

  it("shows both findings that fall on the same text", async () => {
    // Arrange and act.
    const { rendered, view } = mount("{ }", {
      findings: () => Promise.resolve([finding(0, 3, "error", "Missing name."), finding(0, 3, "warning", "Odd shape.")]),
    });
    await waitFor(() => expect(diagnostics(view)).toHaveLength(2));
    act(() => {
      press(view, "M", { ctrlKey: true, shiftKey: true, keyCode: 77 });
    });

    // Assert.
    const panel = await waitFor(() => rendered.container.querySelector(".cm-panel-lint")!);
    expect(panel.textContent).toContain("Missing name.");
    expect(panel.textContent).toContain("Odd shape.");
  });

  it("reports the counts of errors and warnings, and none when there are none", async () => {
    // Arrange.
    let answer = [finding(0, 1, "error", "a"), finding(1, 2, "error", "b"), finding(2, 3, "warning", "c"), finding(0, 1, "info", "d")];
    const { view, onCounts } = mount("abc", { findings: () => Promise.resolve(answer) });

    // Act and assert.
    await waitFor(() => expect(onCounts).toHaveBeenLastCalledWith({ errors: 2, warnings: 1 }));
    answer = [];
    act(() => view.dispatch({ effects: reanalyse.of(null) }));
    await waitFor(() => expect(onCounts).toHaveBeenLastCalledWith({ errors: 0, warnings: 0 }));
  });

  it("drops an answer for a text the view no longer shows", async () => {
    // Arrange: the first answer is held back until the text has changed.
    let release: (findings: EditorFinding[]) => void = () => {};
    const calls: string[] = [];
    const { view } = mount("old", {
      findings: (text) => {
        calls.push(text);
        return text === "old" ? new Promise((resolve) => (release = resolve)) : Promise.resolve([]);
      },
    });
    await waitFor(() => expect(calls).toEqual(["old"]));

    // Act.
    act(() => view.dispatch({ changes: { from: 0, to: 3, insert: "new" } }));
    await waitFor(() => expect(calls).toEqual(["old", "new"]));
    await act(async () => release([finding(0, 3, "error", "About the old text.")]));

    // Assert.
    expect(diagnostics(view)).toEqual([]);
  });

  it("analyses the unchanged text again when asked to, as when a schema arrives", async () => {
    // Arrange.
    const findings = vi.fn(() => Promise.resolve<EditorFinding[]>([]));
    const { view } = mount("text", { findings });
    await waitFor(() => expect(findings).toHaveBeenCalledTimes(1));

    // Act.
    act(() => view.dispatch({ effects: reanalyse.of(null) }));

    // Assert.
    await waitFor(() => expect(findings).toHaveBeenCalledTimes(2));
  });
});

describe("completions and hover text", () => {
  const items = (overrides: Partial<EditorCompletion>[] = []): EditorCompletion[] => [
    { label: "name", filterText: '"name"', detail: "string", info: "What it is called.", from: 2, to: 2, insert: '"name": "${1}"', boost: 1 },
    { label: "port", filterText: '"port"', info: "<b>Where</b> it listens.", from: 2, to: 2, insert: '"port": 8080' },
    ...overrides.map((item) => ({ label: "x", from: 2, to: 2, insert: "x", ...item })),
  ];

  const completing = (list = items()): EditorAnalysis => ({
    findings: () => Promise.resolve([]),
    completions: () => Promise.resolve({ items: list }),
  });

  it("opens the list on Ctrl+Space, walks it with the arrows and accepts with Enter", async () => {
    // Arrange.
    const { view } = mount("{ }", completing());
    view.dispatch({ selection: { anchor: 2 } });

    // Act.
    act(() => {
      press(view, " ", { ctrlKey: true });
    });
    await waitFor(() => expect(completionStatus(view.state)).toBe("active"));
    await pastInteractionDelay();
    act(() => {
      press(view, "ArrowDown");
    });
    act(() => {
      press(view, "Enter");
    });

    // Assert. Required first: "name" led the list, so one step down is "port".
    expect(view.state.doc.toString()).toBe('{ "port": 8080}');
  });

  it("dismisses the list with Escape", async () => {
    // Arrange.
    const { view } = mount("{ }", completing());
    view.dispatch({ selection: { anchor: 2 } });
    act(() => {
      press(view, " ", { ctrlKey: true });
    });
    await waitFor(() => expect(completionStatus(view.state)).toBe("active"));

    // Act.
    act(() => {
      press(view, "Escape");
    });

    // Assert.
    expect(completionStatus(view.state)).toBeNull();
    expect(view.state.doc.toString()).toBe("{ }");
  });

  it("opens the list on typing", async () => {
    // Arrange.
    const { view } = mount("{ }", completing());

    // Act.
    act(() => view.dispatch({ changes: { from: 2, insert: '"' }, selection: { anchor: 3 }, userEvent: "input.type" }));

    // Assert.
    await waitFor(() => expect(currentCompletions(view.state).map((completion) => completion.displayLabel)).toEqual(["name", "port"]));
  });

  it("applies an insert with tab stops as a snippet, the caret on its first stop", async () => {
    // Arrange.
    const { view } = mount("{ }", completing());
    view.dispatch({ selection: { anchor: 2 } });
    act(() => {
      press(view, " ", { ctrlKey: true });
    });
    await waitFor(() => expect(completionStatus(view.state)).toBe("active"));
    await pastInteractionDelay();

    // Act.
    act(() => {
      press(view, "Enter");
    });

    // Assert.
    expect(view.state.doc.toString()).toBe('{ "name": ""}');
    expect(view.state.selection.main.head).toBe('{ "name": "'.length);
  });

  it("shows an item's description beside it, as text even when it holds markup", async () => {
    // Arrange.
    const { rendered, view } = mount("{ }", completing());
    view.dispatch({ selection: { anchor: 2 } });
    act(() => {
      press(view, " ", { ctrlKey: true });
    });
    await waitFor(() => expect(completionStatus(view.state)).toBe("active"));
    await pastInteractionDelay();

    // Act.
    act(() => {
      press(view, "ArrowDown");
    });

    // Assert.
    const info = await waitFor(() => {
      const element = rendered.container.querySelector(".cm-completionInfo");
      expect(element?.textContent).toBe("<b>Where</b> it listens.");
      return element!;
    });
    expect(info.querySelector("b")).toBeNull();
    expect(rendered.container.querySelector(".cm-completionDetail")).not.toBeNull();
  });

  it("shows hover text at the caret from the keyboard, as text even when it holds markup", async () => {
    // Arrange.
    const { rendered, view } = mount('{ "name": 1 }', {
      findings: () => Promise.resolve([]),
      hover: (_text, offset) => Promise.resolve(offset >= 2 && offset <= 8 ? { from: 2, to: 8, text: "The <i>name</i>." } : null),
    });
    view.dispatch({ selection: { anchor: 4 } });

    // Act.
    act(() => {
      press(view, "k", { ctrlKey: true });
      press(view, "i", { ctrlKey: true });
    });

    // Assert.
    const hover = await waitFor(() => {
      const element = rendered.container.querySelector(".cm-editor-hover");
      expect(element?.textContent).toBe("The <i>name</i>.");
      return element!;
    });
    expect(hover.querySelector("i")).toBeNull();

    // Escape closes it.
    act(() => {
      press(view, "Escape");
    });
    await waitFor(() => expect(rendered.container.querySelector(".cm-editor-hover")).toBeNull());
  });
});

describe("the shared panel with an analysis", () => {
  const props = { projectId: new Uint8Array(16), entryId: new Uint8Array(16), path: ["notes.txt"] };

  it("says how many errors and warnings there are, then the analysis's own status", async () => {
    // Arrange and act.
    const rendered = render(
      <TextEditorPanel
        {...props}
        analysisDelay={0}
        analysis={{
          findings: () => Promise.resolve([finding(0, 5, "error", "a"), finding(6, 10, "warning", "b"), finding(6, 10, "info", "c")]),
          status: () => "Schema: Settings",
        }}
      />,
    );

    // Assert.
    await waitFor(() => expect(rendered.getByTestId("finding-counts").textContent).toBe("1 error, 1 warning"));
    expect(rendered.getByTestId("analysis-status").textContent).toBe("Schema: Settings");
  });

  it("says nothing of findings when there are none", async () => {
    // Arrange.
    const findings = vi.fn(() => Promise.resolve<EditorFinding[]>([]));

    // Act.
    const rendered = render(<TextEditorPanel {...props} analysisDelay={0} analysis={{ findings }} />);

    // Assert.
    await waitFor(() => expect(findings).toHaveBeenCalled());
    expect(rendered.queryByTestId("finding-counts")).toBeNull();
  });

  it("saves a document that has findings like any other", async () => {
    // Arrange.
    stream.save.mockClear();
    const rendered = render(<TextEditorPanel {...props} analysisDelay={0} analysis={{ findings: () => Promise.resolve([finding(0, 5, "error", "a")]) }} />);
    await waitFor(() => expect(rendered.getByTestId("finding-counts").textContent).toBe("1 error"));
    const view = EditorView.findFromDOM(rendered.getByTestId("base-text-editor"))!;

    // Act.
    act(() => view.dispatch({ changes: { from: 10, insert: "!" } }));
    act(() => {
      press(view, "s", { ctrlKey: true });
    });

    // Assert.
    await waitFor(() => expect(stream.save).toHaveBeenCalledWith("alpha beta!"));
  });
});
