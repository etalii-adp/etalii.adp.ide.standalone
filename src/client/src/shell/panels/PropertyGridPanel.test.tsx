import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  ContextLevelDetailSchema,
  ContextSelectionAction,
  ContextSelectionSource,
  type ContextActionGroup,
  type ContextLevelDetail,
  type ContextSelection,
  type ContextProperty,
  ContextPropertySchema,
  ContextPropertyEditor,
} from "../../generated/context_pb";
import { EntryKind } from "../../generated/hierarchy_pb";
import { NONE_DETAIL, selectionFor } from "../context/ContextConnectionProvider";
import { PropertyGridPanel, groupsOf, levelsOf } from "./PropertyGridPanel";

const contextState: { selection: ContextSelection | null; levels: ContextLevelDetail[]; actions: ContextActionGroup[] } = {
  selection: null,
  levels: [],
  actions: [],
};

/** What the backend would describe, and what it does with a write - both under the test's control. */
const backend: {
  properties: ContextProperty[];
  writes: Array<{ propertyId: string; value: string }>;
  reject: string;
} = { properties: [], writes: [], reject: "" };

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextSelection: () => ({ ...contextState, preview: null, pendingReveal: null, connected: true }),
    useContextConnection: () => ({
      watchId: new Uint8Array(16),
      select: () => {},
      executeAction: async () => ({ accepted: true, error: "" }),
      executeShortcut: async () => ({ accepted: true, error: "" }),
      clearReveal: () => {},
      revealPath: () => {},
      describeProperties: async () => backend.properties,
      setProperty: async (propertyId: string, value: string) => {
        backend.writes.push({ propertyId, value });
        return backend.reject.length > 0
          ? { accepted: false, error: backend.reject }
          : { accepted: true, error: "" };
      },
    }),
  };
});

function property(overrides: Partial<ContextProperty> & { id: string; label: string; value: string }): ContextProperty {
  return create(ContextPropertySchema, { editor: ContextPropertyEditor.LINE, readOnlyReason: "", group: "", ...overrides });
}

function entryDetail(kind: EntryKind, available = true): ContextLevelDetail {
  return create(ContextLevelDetailSchema, { detail: { case: "entry", value: { kind, available } } });
}

function elementDetail(text: string, options: { hasChildren?: boolean; folded?: boolean; linked?: boolean } = {}): ContextLevelDetail {
  return create(ContextLevelDetailSchema, {
    detail: {
      case: "element",
      value: { text, hasChildren: options.hasChildren ?? false, folded: options.folded ?? false, linked: options.linked ?? false },
    },
  });
}

describe("PropertyGridPanel", () => {
  beforeEach(() => {
    backend.properties = [];
    backend.writes = [];
    backend.reject = "";
    contextState.selection = null;
    contextState.levels = [];
  });

  it("shows an intentional empty state when nothing is selected", () => {
    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    expect(screen.getByText("Nothing selected")).toBeTruthy();
  });

  it("shows a selected file's name, path, kind, availability and source", () => {
    // Arrange.
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16), ["docs", "design.mm"], NONE_DETAIL);
    contextState.levels = [entryDetail(EntryKind.FILE)];

    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    expect(screen.getByRole("heading", { name: "design.mm" })).toBeTruthy();
    expect(screen.getByText("docs/design.mm")).toBeTruthy();
    expect(screen.getByText("File")).toBeTruthy();
    expect(screen.getByText("Yes")).toBeTruthy();
    expect(screen.getByText("Explorer")).toBeTruthy();
  });

  it("shows a selected diagram element, with the properties its own module describes", async () => {
    // Arrange.
    // The panel used to render Text, Collapsed and Linked itself, off the pushed ElementDetail -
    // a message shaped like a mindmap node, which is why C4 had nothing to put in it. What a
    // selected element has is now the owning module's answer, and the panel renders rows it
    // does not understand.
    const node = selectionFor(ContextSelectionSource.DIAGRAM_CANVAS, new Uint8Array(16).fill(2), ["Milestones"], NONE_DETAIL);
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16).fill(1), ["roadmap.adp"], {
      case: "child",
      value: node,
    });
    contextState.levels = [entryDetail(EntryKind.FILE), elementDetail("Milestones", { hasChildren: true, folded: true })];
    backend.properties = [
      property({ id: "mindmap.text", label: "Text", value: "Milestones" }),
      property({ id: "mindmap.notes", label: "Notes", value: "", editor: ContextPropertyEditor.TEXT }),
    ];

    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    expect(screen.getByRole("heading", { name: "Milestones" })).toBeTruthy();
    expect(screen.getByText("Node")).toBeTruthy();
    expect(((await screen.findByLabelText("Text")) as HTMLInputElement).value).toBe("Milestones");
    expect(screen.getByLabelText("Notes")).toBeTruthy();
  });

  it("shows every level of a chain, outermost first", () => {
    // Arrange.
    const inner = selectionFor(ContextSelectionSource.DIAGRAM_CANVAS, new Uint8Array(16).fill(2), ["root", "node"], {
      case: "action",
      value: ContextSelectionAction.ACTIVATE,
    });
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16).fill(1), ["docs"], { case: "child", value: inner });
    contextState.levels = [entryDetail(EntryKind.FOLDER, false)];

    render(<PropertyGridPanel />);

    // Act and assert, step by step.
    const headings = screen.getAllByRole("heading").map((h) => h.textContent);
    expect(headings).toEqual(["docs", "node"]);
    expect(screen.getByText("Folder")).toBeTruthy();
    expect(screen.getByText("No")).toBeTruthy();
    expect(screen.getByText("Diagram")).toBeTruthy();
    expect(levelsOf(contextState.selection, contextState.levels)).toHaveLength(2);
  });

  // ---- editing ------------------------------------------------------------------------

  /** Selects a diagram element, which is what has properties worth editing. */
  function selectElement() {
    contextState.selection = selectionFor(ContextSelectionSource.DIAGRAM_CANVAS, new Uint8Array(16).fill(1), ["model.dsl"], {
      case: "action",
      value: ContextSelectionAction.ACTIVATE,
    });
    contextState.levels = [elementDetail("Web Application")];
  }

  it("renders a described property as an editable field holding its value", async () => {
    // Arrange.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web Application" })];

    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;
    expect(field.value).toBe("Web Application");
  });

  it("writes nothing while the user is typing", async () => {
    // Arrange.
    // The rule that matters most: every write is a command on the project history, so writing
    // per keystroke would put the document through one undo entry per character.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    field.focus();
    fireEvent.change(field, { target: { value: "Web A" } });
    fireEvent.change(field, { target: { value: "Web Ap" } });
    fireEvent.change(field, { target: { value: "Web App" } });

    // Assert.
    expect(backend.writes).toEqual([]);
  });

  it("writes once when Enter is pressed", async () => {
    // Arrange.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    field.focus();
    fireEvent.change(field, { target: { value: "Web App" } });
    await act(async () => {
      fireEvent.keyDown(field, { key: "Enter" });
    });

    // Assert.
    expect(backend.writes).toEqual([{ propertyId: "c4.name", value: "Web App" }]);
  });

  it("writes once when the field loses focus", async () => {
    // Arrange.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    field.focus();
    fireEvent.change(field, { target: { value: "Web App" } });
    await act(async () => {
      fireEvent.blur(field);
    });

    // Assert.
    expect(backend.writes).toEqual([{ propertyId: "c4.name", value: "Web App" }]);
  });

  it("writes nothing when focus leaves a field nobody changed", async () => {
    // Arrange.
    // Clicking into a field and out again is not an edit, and putting an entry on the history
    // for it would fill the undo stack with nothing.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    await act(async () => {
      field.focus();
      fireEvent.blur(field);
    });

    // Assert.
    expect(backend.writes).toEqual([]);
  });

  it("abandons the edit on Escape, writing nothing and putting the value back", async () => {
    // Arrange.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    field.focus();
    fireEvent.change(field, { target: { value: "Something else" } });
    await act(async () => {
      fireEvent.keyDown(field, { key: "Escape" });
    });

    // Assert.
    expect(backend.writes).toEqual([]);
    expect(field.value).toBe("Web");
  });

  it("shows a read-only property as text, with the reason and no field", async () => {
    // Arrange.
    // Shown rather than omitted: a value a reader cannot change here is still worth seeing, and
    // the reason tells them what would have to change instead.
    selectElement();
    backend.properties = [
      property({
        id: "c4.kind",
        label: "Kind",
        value: "Container",
        readOnlyReason: "Changing this changes the shape of the model.",
      }),
    ];

    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    expect(await screen.findByText("Container")).toBeTruthy();
    expect(screen.getByText("Changing this changes the shape of the model.")).toBeTruthy();
    expect(screen.queryByLabelText("Kind")).toBeNull();
  });

  it("shows a property whose editor it does not know, and offers no field for it", async () => {
    // Arrange.
    // The editor enum is widened by whichever diagram type first needs a new editor, so a
    // client older than the backend meets one eventually. Showing the value is right;
    // editing it through a control this client guessed at is how a value gets mangled.
    selectElement();
    backend.properties = [
      property({ id: "pipeline.dependsOn", label: "Depends on", value: "build, test", editor: 99 as ContextPropertyEditor }),
    ];

    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    expect(await screen.findByText("build, test")).toBeTruthy();
    expect(screen.queryByLabelText("Depends on")).toBeNull();
    expect(document.querySelector(".property-grid input, .property-grid textarea")).toBeNull();
  });

  it("puts the old value back and says why when a write is refused", async () => {
    // Arrange.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    backend.reject = "A name cannot be empty.";
    render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    field.focus();
    fireEvent.change(field, { target: { value: "" } });
    await act(async () => {
      fireEvent.blur(field);
    });

    // Assert.
    // A field that keeps a rejected value looks like it was accepted, which is the one thing a
    // property grid must never do.
    expect(screen.getByText("A name cannot be empty.")).toBeTruthy();
    expect(field.value).toBe("Web");
  });

  it("groups what the provider grouped, ungrouped rows first", () => {
    // Act and assert.
    const grouped = groupsOf([
      property({ id: "b", label: "B", value: "", group: "Model" }),
      property({ id: "a", label: "A", value: "" }),
      property({ id: "c", label: "C", value: "", group: "Model" }),
    ]);

    expect(grouped.map((group) => group.name)).toEqual(["", "Model"]);
    expect(grouped[1].properties.map((entry) => entry.id)).toEqual(["b", "c"]);
  });

});
