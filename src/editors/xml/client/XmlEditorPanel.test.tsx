import { describe, expect, it, vi } from "vitest";
import { act, render, waitFor } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { EditorView } from "@codemirror/view";
import { foldEffect, foldable, forceParsing, syntaxTree } from "@codemirror/language";
import { forEachDiagnostic } from "@codemirror/lint";
import { undo } from "@codemirror/commands";
import { tags, type Tag } from "@lezer/highlight";
import { editorHighlight } from "@client/editors/editorHighlight";
import { AnalyzeTextResponseSchema, CompleteTextResponseSchema, type AnalyzeTextResponse } from "@client/generated/editors_pb";
import { ProblemSeverity } from "@client/generated/problems_pb";
import { XmlEditorPanel } from "./XmlEditorPanel";
import { checkingUnavailable, type XmlAnalysisCalls } from "./xmlAnalysis";

/**
 * The XML editor mounted over a fixed stream (xml-editor task 9): its colour through the shared
 * scheme, its tags, its folds, and its analysis asked of fake calls. jsdom applies no CSS, so
 * classes are asserted, never colours.
 */

// Every file the panel reads - its own and the schemas an answer names - comes from this store,
// so a test can change a schema "on disk" and see the panel ask again.
const files = vi.hoisted(() => {
  const texts = new Map<string, { text: string; revision: number }>();
  const listeners = new Set<() => void>();
  return {
    texts,
    listeners,
    set(key: string, text: string) {
      texts.set(key, { text, revision: (texts.get(key)?.revision ?? 0) + 1 });
      listeners.forEach((listener) => listener());
    },
  };
});

vi.mock("@client/editors/useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/editors/useEditorText")>();
  const { useSyncExternalStore } = await import("react");
  return {
    ...actual,
    useEditorText: (_projectId: Uint8Array, path: readonly string[]) => {
      const file = useSyncExternalStore(
        (listener) => {
          files.listeners.add(listener);
          return () => files.listeners.delete(listener);
        },
        () => files.texts.get(path.join("/")),
      );
      return {
        model: { text: file?.text ?? "", revision: file?.revision ?? 0, loaded: file !== undefined, contentMime: "editor/xml" },
        loading: false,
        failed: false,
        save: () => Promise.resolve(""),
      };
    },
  };
});

vi.mock("@client/auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

const quiet: XmlAnalysisCalls = {
  analyze: () => Promise.resolve(create(AnalyzeTextResponseSchema, { status: "No schema" })),
  complete: () => Promise.resolve(create(CompleteTextResponseSchema)),
};

function mount(text: string, calls: XmlAnalysisCalls = quiet) {
  files.set("doc.xml", text);
  const rendered = render(<XmlEditorPanel projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["doc.xml"]} calls={calls} analysisDelay={0} />);
  const view = EditorView.findFromDOM(rendered.getByTestId("base-text-editor"))!;
  return { rendered, view };
}

/**
 * Waits until the editor holds `text`, then completes its parse. CodeMirror parses for a fixed
 * time slice when the view is made and finishes later, when the browser is idle. On a cold, slow
 * runner a test can look before the tree exists, as the YAML editor's did (spec 025, T276, T281),
 * so the parse is completed before anything is asserted.
 */
async function parsed(view: EditorView, text: string) {
  await waitFor(() => expect(view.state.doc.toString()).toBe(text));
  forceParsing(view, view.state.doc.length, 10_000);
}

function classed(container: HTMLElement, tag: Tag): string[] {
  const name = editorHighlight.style([tag]);
  expect(name).toBeTruthy();
  return [...container.querySelectorAll(`.${name!.split(" ")[0]}`)].map((element) => element.textContent ?? "");
}

function diagnosticCount(view: EditorView): number {
  let count = 0;
  forEachDiagnostic(view.state, () => count++);
  return count;
}

/** Types as a keyboard does, through the editor's input handlers - which is where tags are closed. */
function type(view: EditorView, text: string) {
  const { from, to } = view.state.selection.main;
  const handled = view.state
    .facet(EditorView.inputHandler)
    .some((handler) => handler(view, from, to, text, () => view.state.update({ changes: { from, to, insert: text }, selection: { anchor: from + text.length }, userEvent: "input.type" })));
  if (!handled) {
    view.dispatch({ changes: { from, to, insert: text }, selection: { anchor: from + text.length }, userEvent: "input.type" });
  }
}

const document = [
  '<?xml version="1.0"?>',
  "<!DOCTYPE notes>",
  "<!-- one",
  "     two -->",
  '<notes xmlns:p="urn:p">',
  '  <p:note when="today">Text &amp; more</p:note>',
  "  <![CDATA[ raw",
  "  data ]]>",
  "</notes>",
  "",
].join("\n");

describe("colour", () => {
  it("tells each kind of token apart through the shared scheme (Requirement 3.1)", async () => {
    // Arrange and act.
    const { rendered, view } = mount(document);
    await parsed(view, document);

    // Assert: a class of the shared scheme on each kind.
    const container = rendered.container;
    await waitFor(() => expect(classed(container, tags.tagName)).toContain("notes"));
    expect(classed(container, tags.attributeName)).toContain("when");
    expect(classed(container, tags.attributeValue)).toContain('"today"');
    expect(classed(container, tags.blockComment).join("")).toContain("<!-- one");
    expect(classed(container, tags.special(tags.string)).join("")).toContain("<![CDATA[ raw");
    expect(classed(container, tags.processingInstruction).join("")).toContain("<?xml");
    expect(classed(container, tags.character)).toContain("&amp;");
    expect(classed(container, tags.documentMeta).join("")).toContain("<!DOCTYPE notes>");
  });

  it("tells a prefix apart from the local name (Requirement 3.2)", async () => {
    // Arrange and act.
    const { rendered, view } = mount(document);
    await parsed(view, document);

    // Assert: the prefix of both tags and of the declaration carries the namespace class.
    await waitFor(() => expect(classed(rendered.container, tags.namespace)).toEqual(["xmlns", "p", "p"]));
  });

  it("keeps colour on what can still be read in a document that is not well-formed (Requirement 3.4)", async () => {
    // Arrange and act.
    const text = '<a x="1"><b>\n</a>\n<c y="2"/>\n';
    const { rendered, view } = mount(text);
    await parsed(view, text);

    // Assert.
    await waitFor(() => expect(classed(rendered.container, tags.attributeName)).toEqual(["x", "y"]));
  });
});

describe("tags", () => {
  it("marks the partner of the tag the caret is in (Requirement 3.5)", async () => {
    // Arrange.
    const { rendered, view } = mount("<notes>\n  <note/>\n</notes>\n");

    // Act.
    act(() => view.dispatch({ selection: { anchor: 3 } }));

    // Assert.
    await waitFor(() => expect([...rendered.container.querySelectorAll(".cm-matchingBracket")].map((element) => element.textContent)).toEqual(["notes", "notes"]));
  });

  it("closes a start tag as its > is typed, one undo away (Requirement 3.6)", () => {
    // Arrange.
    const { view } = mount("<notes>\n  \n</notes>\n");
    act(() => view.dispatch({ selection: { anchor: "<notes>\n  ".length } }));

    // Act.
    act(() => type(view, "<note"));
    act(() => type(view, ">"));
    const typed = view.state.doc.toString();
    act(() => {
      undo(view);
    });

    // Assert.
    expect(typed).toBe("<notes>\n  <note></note>\n</notes>\n");
    expect(view.state.doc.toString()).not.toContain("</note>");
  });

  it("completes the open element's name after </, one undo away (Requirement 3.6)", () => {
    // Arrange.
    const { view } = mount("<notes>\n  <note>text\n</notes>\n");
    act(() => view.dispatch({ selection: { anchor: "<notes>\n  <note>text".length } }));

    // Act.
    act(() => type(view, "<"));
    act(() => type(view, "/"));
    const typed = view.state.doc.toString();
    act(() => {
      undo(view);
    });

    // Assert.
    expect(typed).toBe("<notes>\n  <note>text</note>\n</notes>\n");
    expect(view.state.doc.toString()).not.toContain("</note>");
  });

  it("folds an element, a comment and a CDATA section, and changes no text (Requirement 3.7)", () => {
    // Arrange.
    const { view } = mount(document);
    syntaxTree(view.state);
    const text = view.state.doc.toString();
    const lines = [3, 5, 7].map((number) => view.state.doc.line(number));

    // Act.
    const ranges = lines.map((line) => foldable(view.state, line.from, line.to));
    act(() => view.dispatch({ effects: ranges.flatMap((range) => (range ? [foldEffect.of(range)] : [])) }));

    // Assert.
    expect(ranges.map((range) => (range ? text.slice(range.from, range.to) : null))).toEqual([
      " one\n     two ",
      '\n  <p:note when="today">Text &amp; more</p:note>\n  <![CDATA[ raw\n  data ]]>\n',
      " raw\n  data ",
    ]);
    expect(view.state.doc.toString()).toBe(text);
  });
});

describe("asking the backend", () => {
  const withError = (text: string): AnalyzeTextResponse =>
    create(AnalyzeTextResponseSchema, {
      status: "No schema",
      findings: [{ $typeName: "etalii.adp.TextFinding", from: 1, to: 6, severity: ProblemSeverity.ERROR, message: `About ${text.length}.`, source: "XML" }],
    });

  it("takes the findings away the moment the text changes (Requirement 4.7)", async () => {
    // Arrange: the first answer arrives; the next never does.
    let calls = 0;
    const { rendered, view } = mount("<notes>\n", {
      ...quiet,
      analyze: (text) => (++calls === 1 ? Promise.resolve(withError(text)) : new Promise(() => {})),
    });
    await waitFor(() => expect(rendered.getByTestId("finding-counts").textContent).toBe("1 error"));

    // Act.
    act(() => view.dispatch({ changes: { from: 8, insert: "</notes>" } }));

    // Assert: gone at once, while the answer about the new text is still out.
    expect(diagnosticCount(view)).toBe(0);
  });

  it("never shows an answer for a text that has since changed (Requirement 9.3)", async () => {
    // Arrange: the answer about the first text is held back until the text has changed.
    let release: (response: AnalyzeTextResponse) => void = () => {};
    const asked: string[] = [];
    const { view } = mount("<notes>\n", {
      ...quiet,
      analyze: (text) => {
        asked.push(text);
        return asked.length === 1 ? new Promise((resolve) => (release = resolve)) : new Promise(() => {});
      },
    });
    await waitFor(() => expect(asked).toHaveLength(1));
    act(() => view.dispatch({ changes: { from: 8, insert: "x" } }));

    // Act.
    await act(async () => release(withError(asked[0])));

    // Assert.
    expect(diagnosticCount(view)).toBe(0);
  });

  it("asks again when a schema the answer read changes on disk (Requirement 5.8)", async () => {
    // Arrange: the answer says it read library.xsd, which the store serves.
    files.set("library.xsd", "<xs:schema/>");
    const analyze = vi.fn<XmlAnalysisCalls["analyze"]>(() =>
      Promise.resolve(create(AnalyzeTextResponseSchema, { status: "Schema: library.xsd", dependsOn: [{ $typeName: "etalii.adp.Path", segments: ["library.xsd"] }] })),
    );
    const { rendered } = mount("<library/>\n", { ...quiet, analyze });
    await waitFor(() => expect(rendered.getByTestId("analysis-status").textContent).toBe("Schema: library.xsd"));
    await waitFor(() => expect(analyze).toHaveBeenCalledTimes(1));

    // Act.
    act(() => files.set("library.xsd", "<xs:schema><!-- changed --></xs:schema>"));

    // Assert.
    await waitFor(() => expect(analyze).toHaveBeenCalledTimes(2));
  });

  it("keeps colour and says once that checking is unavailable when the backend cannot be reached (Requirement 9.5)", async () => {
    // Arrange and act.
    const { rendered, view } = mount(document, { ...quiet, analyze: () => Promise.reject(new Error("unreachable")) });
    await parsed(view, document);

    // Assert.
    await waitFor(() => expect(rendered.getByTestId("analysis-status").textContent).toBe(checkingUnavailable));
    expect(rendered.getAllByText(checkingUnavailable)).toHaveLength(1);
    expect(classed(rendered.container, tags.tagName)).toContain("notes");
  });
});
