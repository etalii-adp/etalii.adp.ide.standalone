import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextLevelDetailSchema, ContextSelectionSource, ContextPropertyEditor } from "../../generated/context-contract_pb";
import { type ContextActionGroup, type ContextLevelDetail } from "../../generated/context-contract_pb";
import { ContextSelectionAction, type ContextSelection, type ContextProperty, ContextPropertySchema } from "../../generated/context_pb";
import { EntryKind } from "../../generated/shared_pb";
import { NONE_DETAIL, selectionFor } from "../context/ContextConnectionProvider";
import { PropertyGridPanel, groupsOf, levelsOf } from "./PropertyGridPanel";
import { ContextSelectionSchema } from "../../generated/context_pb";
import { clearPropertyPreview, endPropertyPreview, settlePropertyPreview, showPropertyPreview } from "./propertyPreview";

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
  describeError: string;
} = { properties: [], writes: [], reject: "", describeError: "" };

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
      describeProperties: async () => ({ properties: backend.properties, error: backend.describeError }),
      setProperty: async (propertyId: string, value: string) => {
        backend.writes.push({ propertyId, value });
        return backend.reject.length > 0
          ? { accepted: false, error: backend.reject }
          : { accepted: true, error: "" };
      },
    }),
  };
});

/**
 * A property message with the fields a test cares about.
 *
 * The metadata keys are excluded from the overrides: `Partial<ContextProperty>` makes `$typeName`
 * optional, and `create` will not accept a possibly-absent one. Naming the exclusion keeps this
 * helper working as fields are added to the message.
 */
type PropertyOverrides = Omit<Partial<ContextProperty>, "$typeName" | "$unknown"> & {
  id: string;
  label: string;
  value: string;
};

function property(overrides: PropertyOverrides): ContextProperty {
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
    backend.describeError = "";
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

  describe("while a canvas gesture previews a value", () => {
    /** A trend selected on the canvas, by element id, as the library pushes one. */
    function selectTrend(elementId: string) {
      const node = create(ContextSelectionSchema, {
        source: ContextSelectionSource.DIAGRAM_CANVAS,
        path: { segments: ["Industrial Revolution"] },
        id: { source: { case: "elementId", value: { value: elementId } } },
      });
      contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16).fill(1), ["trends.ghg"], { case: "child", value: node });
      contextState.levels = [entryDetail(EntryKind.FILE), elementDetail("Industrial Revolution")];
      backend.properties = [property({ id: "ghg.start", label: "Start", value: "1760-01" }), property({ id: "ghg.peak-end", label: "Peak ends", value: "1780-01" })];
    }

    afterEach(() => clearPropertyPreview());

    it("shows the previewed value for the selected element as the drag goes, and writes nothing", async () => {
      // Arrange.
      selectTrend("industrial-revolution");
      render(<PropertyGridPanel />);
      const peakEnds = (await screen.findByLabelText("Peak ends")) as HTMLInputElement;

      // Act: a boundary drag in flight.
      act(() => showPropertyPreview("industrial-revolution", { "ghg.peak-end": "1790-06" }));

      // Assert.
      expect(peakEnds.value, "the grid kept the backend's value while the chevron was being dragged, and caught up only after the release").toBe("1790-06");
      expect((screen.getByLabelText("Start") as HTMLInputElement).value).toBe("1760-01");
      expect(backend.writes).toEqual([]);
    });

    it("ignores a preview for an element that is not the selected one", async () => {
      // Arrange.
      selectTrend("industrial-revolution");
      render(<PropertyGridPanel />);
      const peakEnds = (await screen.findByLabelText("Peak ends")) as HTMLInputElement;

      // Act.
      act(() => showPropertyPreview("steam-engine", { "ghg.peak-end": "1790-06" }));

      // Assert.
      expect(peakEnds.value).toBe("1780-01");
    });

    it("keeps a released preview until the answer is read, and drops an abandoned one at once", async () => {
      // Arrange.
      selectTrend("industrial-revolution");
      render(<PropertyGridPanel />);
      const peakEnds = (await screen.findByLabelText("Peak ends")) as HTMLInputElement;

      // Act: released and written, the answer not read yet.
      act(() => {
        showPropertyPreview("industrial-revolution", { "ghg.peak-end": "1790-06" });
        settlePropertyPreview();
        endPropertyPreview();
      });

      // Assert: no flash back to the old value while the write travels.
      expect(peakEnds.value).toBe("1790-06");

      // Act: abandoned instead.
      act(() => {
        showPropertyPreview("industrial-revolution", { "ghg.peak-end": "1795-01" });
        endPropertyPreview();
      });

      // Assert.
      expect(peakEnds.value).toBe("1780-01");
    });
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

  it("shows what the backend describes after a write, never what was typed", async () => {
    // Arrange.
    // The committed value reaches the grid one way only: the document changes, the change is
    // pushed, and the properties are read again. An optimistic row would show a value the
    // backend may have normalised, refused later, or never stored at all.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    const { rerender } = render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    // The backend will normalise what it was given, but has not pushed anything yet.
    field.focus();
    fireEvent.change(field, { target: { value: "  Web App  " } });
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web App" })];
    await act(async () => {
      fireEvent.keyDown(field, { key: "Enter" });
    });

    // Assert.
    // The write went as typed...
    expect(backend.writes).toEqual([{ propertyId: "c4.name", value: "  Web App  " }]);
    // ...and until the push arrives the row still shows the last value the backend described.
    // A grid that showed the typed value here would be guessing at what the backend stored.
    expect(((await screen.findByLabelText("Name")) as HTMLInputElement).value).toBe("Web");

    // Act, continued: the pushed detail changes, which is what makes the panel read again.
    contextState.levels = [elementDetail("Web App")];
    await act(async () => {
      rerender(<PropertyGridPanel />);
    });

    // Assert: now it shows what the backend actually stored - normalised, not as typed.
    expect(((await screen.findByLabelText("Name")) as HTMLInputElement).value).toBe("Web App");
  });

  it("carries a toggle as the text true and false, both ways", async () => {
    // Arrange.
    // The value is always text on the wire, whatever the editor: a toggle carries "true" or
    // "false", and nothing in between is a value this contract knows.
    selectElement();
    backend.properties = [
      property({ id: "x.flag", label: "Enabled", value: "false", editor: ContextPropertyEditor.TOGGLE }),
    ];
    render(<PropertyGridPanel />);
    const box = (await screen.findByLabelText("Enabled")) as HTMLInputElement;

    // Assert (described "false" renders unchecked)...
    expect(box.checked).toBe(false);

    // Act.
    await act(async () => {
      fireEvent.click(box);
    });

    // Assert (...and checking it writes the text "true").
    expect(backend.writes).toEqual([{ propertyId: "x.flag", value: "true" }]);
  });

  it("offers a choice as a list of the candidates its provider supplied", async () => {
    // Arrange.
    // The panel renders the candidates without understanding any of them - that the valid values
    // of a pipeline stage's dependsOn are the other stages is the provider's knowledge, not the
    // shell's, exactly as it is for a toolbox entry or a context action.
    selectElement();
    backend.properties = [
      property({
        id: "azure-pipeline.depends-on",
        label: "Depends on",
        value: "Build",
        editor: ContextPropertyEditor.CHOICE,
        candidates: ["Build", "Test", "(nothing)"],
      }),
    ];
    render(<PropertyGridPanel />);

    // Act.
    const list = (await screen.findByLabelText("Depends on")) as HTMLSelectElement;

    // Assert.
    expect(list.tagName).toBe("SELECT");
    expect([...list.options].map((option) => option.value)).toEqual(["Build", "Test", "(nothing)"]);
    expect(list.value).toBe("Build");
  });

  it("writes a choice the moment one is picked", async () => {
    // Arrange.
    // Picking from a list has no "finished typing", so the selection is the commit - the same
    // reasoning that makes a toggle write on click rather than on blur.
    selectElement();
    backend.properties = [
      property({
        id: "azure-pipeline.depends-on",
        label: "Depends on",
        value: "Build",
        editor: ContextPropertyEditor.CHOICE,
        candidates: ["Build", "Test"],
      }),
    ];
    render(<PropertyGridPanel />);
    const list = (await screen.findByLabelText("Depends on")) as HTMLSelectElement;

    // Act.
    await act(async () => {
      fireEvent.change(list, { target: { value: "Test" } });
    });

    // Assert.
    expect(backend.writes).toEqual([{ propertyId: "azure-pipeline.depends-on", value: "Test" }]);
  });

  it("keeps a current value selectable even when the provider did not list it", async () => {
    // Arrange.
    // A value the file already holds must stay in the list, or merely opening it would offer to
    // change the document to something else.
    selectElement();
    backend.properties = [
      property({
        id: "azure-pipeline.depends-on",
        label: "Depends on",
        value: "Legacy",
        editor: ContextPropertyEditor.CHOICE,
        candidates: ["Build", "Test"],
      }),
    ];
    render(<PropertyGridPanel />);

    // Act.
    const list = (await screen.findByLabelText("Depends on")) as HTMLSelectElement;

    // Assert.
    expect([...list.options].map((option) => option.value)).toEqual(["Legacy", "Build", "Test"]);
    expect(list.value).toBe("Legacy");
  });

  it("shows a read-only choice as a value and a reason, with no list to open", async () => {
    // Arrange.
    // A read-only property gets no control of any kind, whatever its editor says and whatever
    // candidates came with it - the same rule every other editor already follows, and the reason
    // a provider can mark one row of a Choice-shaped property unwritable without a second type.
    selectElement();
    backend.properties = [
      property({
        id: "azure-pipeline.depends-on",
        label: "Depends on",
        value: "A, B",
        editor: ContextPropertyEditor.CHOICE,
        candidates: ["A", "B"],
        readOnlyReason: "This waits for several things, which is edited in the pipeline file.",
      }),
    ];

    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    expect(await screen.findByText("A, B")).toBeTruthy();
    expect(await screen.findByText(/several things/)).toBeTruthy();
    expect(screen.queryByLabelText("Depends on")).toBeNull();
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

  it("says the properties could not be read, rather than showing the empty grid of a thing with none", async () => {
    // Arrange.
    // The two states are indistinguishable from an array alone, which is why describeProperties
    // returns a result shape at all: a user shown an empty grid concludes this thing has no
    // properties, and goes looking for the wrong explanation.
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16), ["docs", "design.mm"], NONE_DETAIL);
    contextState.levels = [entryDetail(EntryKind.FILE)];
    backend.properties = [];
    backend.describeError = "The connection to the project was lost.";

    // Act.
    render(<PropertyGridPanel />);

    // Assert.
    expect(await screen.findByText("Unavailable")).toBeTruthy();
    expect(screen.getByText("The connection to the project was lost.")).toBeTruthy();
  });

  it("shows no unavailable row for a selection that simply has no properties", async () => {
    // Arrange.
    contextState.selection = selectionFor(ContextSelectionSource.EXPLORER, new Uint8Array(16), ["docs", "design.mm"], NONE_DETAIL);
    contextState.levels = [entryDetail(EntryKind.FILE)];
    backend.properties = [];
    backend.describeError = "";

    // Act.
    render(<PropertyGridPanel />);
    await act(async () => {
      await Promise.resolve();
    });

    // Assert.
    expect(screen.queryByText("Unavailable")).toBeNull();
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


  it("writes once even if the field is blurred twice before the write lands", async () => {
    // Arrange.
    // Until the write comes back, the pushed value is still the old one - so a second blur
    // would compare the draft against it, find them different, and write again. One edit, two
    // entries on the project history.
    selectElement();
    backend.properties = [property({ id: "c4.name", label: "Name", value: "Web" })];
    render(<PropertyGridPanel />);
    const field = (await screen.findByLabelText("Name")) as HTMLInputElement;

    // Act.
    field.focus();
    fireEvent.change(field, { target: { value: "Web App" } });
    await act(async () => {
      fireEvent.blur(field);
      fireEvent.blur(field);
    });

    // Assert.
    expect(backend.writes).toEqual([{ propertyId: "c4.name", value: "Web App" }]);
  });

});
