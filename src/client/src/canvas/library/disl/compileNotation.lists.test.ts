import { describe, expect, it } from "vitest";
import { layoutCompartments, compartmentsHeight } from "../definition/compartments";
import { compileNotation, type NotationBindings } from "./compileNotation";
import { parseDisl, type DislDocument } from "./disTypes";

/**
 * What the compiler reads of DISL 0.4: the lists inside a node, its badges, a force layout's rings,
 * a kept switch and the bound on a drawable derived relation - on a specification small enough to
 * read in one go, with what each mapping refuses.
 */

function specOf(overrides: Partial<DislDocument> = {}): DislDocument {
  return {
    disl: "0.4",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      enums: { State: { values: { open: { label: "Open" }, done: { label: "Done" } } } },
      types: {
        Board: { attributes: { name: { type: "string" } } },
        Card: { attributes: { name: { type: "string" }, board: { type: "Board" }, note: { type: "string" } }, children: { allowed: ["Item"] } },
        Item: { attributes: { title: { type: "string" }, state: { type: "State" } } },
      },
      relations: {
        BoardCard: { source: "Board", target: "Card", allowSelfLoops: false, derived: { source: "item.board", target: "item", edits: { connect: "setBoard" } } },
        Shown: { source: "Card", target: "Board", allowSelfLoops: false, derived: { source: "item", target: "item.board" } },
      },
    },
    notation: {
      nodes: {
        Board: { shape: "rect", labels: [{ id: "name", text: { attribute: "name" }, position: "center" }] },
        Card: {
          shape: "rect",
          labels: [
            { id: "name", text: { attribute: "name" }, position: "top" },
            { id: "kind", text: { attribute: "name" }, position: "bottom" },
            { id: "state", text: { attribute: "name" }, position: { anchor: [0.5, 0], offset: [0, 36] } },
          ],
          compartments: [
            { id: "note", items: { cel: "[self.note]" }, itemText: { cel: "item" }, itemLink: { cel: "self.noteLink" } },
            {
              id: "items",
              items: { children: ["Item"], slot: "items" },
              itemText: { cel: "item.title" },
              itemLink: { cel: "item.link" },
              itemOrder: { by: "item.updated", direction: "descending" },
              groupBy: { attribute: "state", collapsed: { done: true } },
            },
            { id: "more", title: "More", items: { children: ["Item"], slot: "more" }, itemText: { cel: "item.title" }, collapsible: true, collapsed: true },
          ],
          badges: [
            { id: "link", position: "top-right", visible: { cel: "self.link != ''" }, onClick: "openLink" },
            { id: "pinned", position: "top-left", visible: { cel: "self.view.pinned" }, tooltip: "Locked. Unlock it from its menu." },
          ],
        },
      },
      edges: { BoardCard: { sourceMarker: "none", targetMarker: "none" }, Shown: { sourceMarker: "none", targetMarker: "none" } },
      canvas: { filters: { archived: { label: "Show archived", control: "switch", persist: true } } },
    },
    toolbox: {
      contextMenus: [
        { for: ["Board", "Card"], tools: [{ kind: "editLabel", shortcut: "F2" }, { kind: "delete", shortcut: "Delete" }] },
        { for: ["Item"], tools: [{ kind: "editLabel", shortcut: "F2" }, { kind: "delete", shortcut: "Delete" }] },
      ],
    },
    behavior: { operations: { openLink: { actions: [{ open: "self.link" }] }, setBoard: { actions: [] }, shout: { actions: [{ set: {} }] } } },
    layout: { algorithms: { rings: { algorithm: "force", force: { tiers: ["Board", "Card"] } } }, default: "rings", respect: "pinned" },
    persistence: { view: { bind: { filters: { values: { archived: "showArchived" } } } } },
    ...overrides,
  };
}

const BINDINGS: NotationBindings = {
  celPaths: { "[self.note]": "payload.noteRows" },
  rowPaths: { item: "title", "self.noteLink": "link" },
  celConditions: { "self.view.pinned": { path: "payload.pinned", is: "true" } },
  classNames: () => [],
  badgeClassName: () => "t-lock",
};

const card = (spec = specOf(), bindings = BINDINGS) => compileNotation(spec, bindings).elementTypes.find((type) => type.id === "card")!;

/** A specification whose Card is changed. */
function withCard(change: (node: DislDocument["notation"]["nodes"][string]) => DislDocument["notation"]["nodes"][string]): DislDocument {
  const spec = specOf();
  return { ...spec, notation: { ...spec.notation, nodes: { ...spec.notation.nodes, Card: change(spec.notation.nodes.Card!) } } };
}

describe("compileNotation, DISL 0.4", () => {
  it("reads a specification of DISL 0.4, and still refuses one it was not written against", () => {
    expect(parseDisl(JSON.stringify(specOf())).disl).toBe("0.4");
    expect(() => parseDisl(JSON.stringify({ ...specOf(), disl: "0.5" }))).toThrow(/declares "0.5"/);
  });

  it("names types and actions as the specification does where a module states no wire ids", () => {
    const definition = compileNotation(specOf(), BINDINGS);

    expect(definition.elementTypes.map((type) => type.id)).toEqual(["board", "card"]);
    // The menu for Item, which the notation draws as rows and not as a node, declares nothing.
    expect(definition.actions).toEqual([
      { id: "editLabel", invokedBy: [{ kind: "shortcut", key: "F2" }, { kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
      { id: "delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
    ]);
  });

  it("puts a label on the first or the last line inside its node, and attaches lines anywhere on a node that states no anchors", () => {
    const type = card();

    expect(type.labels!.map((label) => [label.anchorTo, label.offset])).toEqual([
      ["top", { x: 0, y: 20 }],
      ["bottom", { x: 0, y: -10 }],
      ["top", { x: 0, y: 36 }],
    ]);
    expect(type.anchors).toEqual({ kind: "edge", visible: false });
  });

  it("compiles a node's lists: rows from a slot or a bound CEL list, grouped by an enum in its order, beneath the lowest label", () => {
    const [note, items, more] = card().compartments!;

    // A list with no title, no groups and nothing to fold it by is its rows alone, flush left.
    expect(note).toEqual({
      id: "note", rows: "payload.noteRows", rowId: "id", text: { path: "title" }, link: "link", heading: "none",
      collapsed: "payload.collapsed", top: 48, headingHeight: 20, rowHeight: 18, bottom: 8, insetX: 10, rowIndent: 0,
    });
    expect(items).toEqual({
      id: "items", rows: "payload.items", rowId: "id", text: { path: "title" }, link: "link",
      orderBy: { path: "updated", direction: "descending" },
      groupBy: { path: "state", groups: [{ value: "open", title: "Open" }, { value: "done", title: "Done" }], otherTitle: "Other" },
      collapsed: "payload.collapsed", top: 48, headingHeight: 20, rowHeight: 18, bottom: 8, insetX: 10, rowIndent: 12,
    });
    expect(more).toMatchObject({ id: "more", title: "More", rows: "payload.more", rowIndent: 12 });
    expect(more).not.toHaveProperty("heading");
  });

  it("refuses a CEL list or a row term it has no path for, and a group that is no enum of the items", () => {
    expect(() => card(specOf(), { ...BINDINGS, celPaths: {} })).toThrow(/CEL list "\[self\.note\]" has no payload path/);
    expect(() => card(specOf(), { ...BINDINGS, rowPaths: {} })).toThrow(/the CEL "item" has no path within a row/);
    expect(() => card(withCard((node) => ({ ...node, compartments: [{ id: "x", items: { children: ["Item"], slot: "items" }, itemText: { cel: "item.title" }, groupBy: { attribute: "title" } }] }))))
      .toThrow(/groups by "title", which is no enum attribute of its items/);
  });

  it("draws a badge that opens an attribute as its link symbol, and one that runs nothing as a mark while its condition holds", () => {
    const type = card();

    expect(type.links).toEqual([{ id: "link", link: "payload.link", at: { right: 20, top: 5 } }]);
    expect(type.decorations).toEqual([{
      glyph: "circle",
      anchor: "canvas",
      from: { x: { path: "bounds.left", number: { plus: 9 } }, y: { path: "bounds.top", number: { plus: 9 } } },
      radius: 3.5,
      className: "t-lock",
      tooltip: { template: "Locked. Unlock it from its menu." },
      accessibility: { role: "img", label: { template: "Locked" } },
      when: { path: "payload.pinned", is: "true" },
    }]);
  });

  it("refuses a badge it cannot draw: one that runs something else, one placed elsewhere, a mark with an unbound condition", () => {
    const badged = (badge: object) => withCard((node) => ({ ...node, badges: [badge as never] }));

    expect(() => card(badged({ id: "b", position: "top-right", onClick: "shout" }))).toThrow(/runs "shout"/);
    expect(() => card(badged({ id: "b", position: "bottom-left" }))).toThrow(/sits "bottom-left"/);
    expect(() => card(badged({ id: "b", position: "top-left", visible: { cel: "self.loud" } }))).toThrow(/shows when "self\.loud"/);
  });

  it("places the types on the rings a force layout names, and leaves what the reader pinned where it is", () => {
    expect(compileNotation(specOf(), BINDINGS).layout).toEqual({ modes: ["tiered-force"], tiers: [["board"], ["card"]], pinned: "payload.pinned" });

    const unpinned = specOf();
    expect(compileNotation({ ...unpinned, layout: { ...unpinned.layout, respect: undefined } }, BINDINGS).layout).not.toHaveProperty("pinned");
    expect(() => compileNotation({ ...specOf(), layout: { algorithms: { rings: { algorithm: "force" } }, default: "rings" } }, BINDINGS)).toThrow(/names no tiers/);
  });

  it("draws a kept switch from the name the persistence binds it to, and keeps it out of the filter box", () => {
    const definition = compileNotation(specOf(), BINDINGS);

    expect(definition.chrome).toEqual({ switches: [{ id: "archived", caption: "Show archived", on: "payload.showArchived" }] });
    expect(definition.filter).toBeUndefined();
    expect(() => compileNotation({ ...specOf(), persistence: undefined }, BINDINGS)).toThrow(/kept filter "archived" is bound to no name/);
  });

  it("bounds a derived relation a gesture can draw to one per element at the end that holds its key, and no other", () => {
    const [drawable, shown] = compileNotation(specOf(), BINDINGS).relationTypes;

    // A card names its one board: one line into each card.
    expect(drawable!.endpoints.cardinality).toEqual({ maxIntoTarget: 1 });
    // Derived, and not drawable: its count follows from the model, and nothing is refused.
    expect(shown!.endpoints.cardinality).toBeUndefined();

    const flipped = specOf();
    const relations = { ...flipped.metamodel.relations!, BoardCard: { ...flipped.metamodel.relations!.BoardCard!, derived: { source: "item", target: "item.card", edits: { connect: "setBoard" } } } };
    expect(compileNotation({ ...flipped, metamodel: { ...flipped.metamodel, relations } }, BINDINGS).relationTypes[0]!.endpoints.cardinality).toEqual({ maxFromSource: 1 });
  });
});

describe("a list without a heading", () => {
  const declaration = {
    id: "note", rows: "payload.rows", rowId: "id", text: { path: "title" }, heading: "none" as const,
    collapsed: "payload.collapsed", top: 30, headingHeight: 20, rowHeight: 18, bottom: 8, insetX: 10, rowIndent: 0,
  };
  const source = (collapsed: string[]) => ({
    element: { id: "e", type: "card", x: 0, y: 0, width: 200, height: 40 },
    payload: { rows: [{ id: "", title: "Default" }], collapsed },
  });

  it("draws its rows and no heading, takes the room of its rows alone, and is never folded", () => {
    // Folded by its own key in the model: a list without a heading has nothing to fold it by.
    const laidOut = layoutCompartments([declaration], source(["note"]), { x: 0, y: 0, width: 200, height: 40 });

    expect(laidOut.headings).toEqual([]);
    expect(laidOut.rows.map((row) => [row.fullText, row.box.y])).toEqual([["Default", 30]]);
    expect(compartmentsHeight([declaration], source([]))).toBe(30 + 18 + 8);
  });
});
