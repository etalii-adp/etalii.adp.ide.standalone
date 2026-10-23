import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { hierarchicalRequirementSurface } from "./hierarchicalRequirementDefinition";
import { registrations } from "./register";

describe("the Hierarchical Requirement Designer registration", () => {
  it("claims etalii/hierarchical-requirement and nothing else", () => {
    // Assert.
    const registration = registrations[0]!;
    expect(registrations).toHaveLength(1);
    expect(registration.matches("etalii/hierarchical-requirement")).toBe(true);
    expect(registration.matches("etalii/knowledge")).toBe(false);
    expect(registration.matches("etalii/markdown")).toBe(false);
    expect(registration.Panel).toBeDefined();
  });
});

describe("the Hierarchical Requirement Designer's surface", () => {
  interface Definition {
    metamodel: { enums: { AssessmentKind: { values: Record<string, { value: string; icon: string }> } } };
    surface: {
      kind: string;
      levels: { type: string };
      entries: { type: string };
      labels: Record<string, { on: string[]; prefix: string }>;
      filters?: { type: string; name: string; query: string };
    };
    operations: Record<string, { label?: unknown; params?: Record<string, unknown>; confirm?: { message: { cel: string }; threshold: number } }>;
  }

  /** The designer's definition, as it is checked in. */
  function definition(): Definition {
    const source = readFileSync(join(__dirname, "..", "definition", "hierarchical-requirement.des"), "utf8");
    return JSON.parse(source.replace(/^\uFEFF/, "")) as Definition;
  }

  it("draws the levels and entries its definition names", () => {
    // Act.
    const { surface } = definition();

    // Assert.
    expect(surface.kind).toBe("hierarchy");
    expect(hierarchicalRequirementSurface.levelType).toBe(surface.levels.type);
    expect(hierarchicalRequirementSurface.entryType).toBe(surface.entries.type);
  });

  it("declares the labels its definition has, with their prefixes and the types that carry them", () => {
    // Act.
    const declared = definition().surface.labels;

    // Assert: the definition was read at all - three labels - and the surface names the same ones.
    expect(Object.keys(declared)).toHaveLength(3);
    expect(hierarchicalRequirementSurface.labels.map((label) => label.label).sort()).toEqual(Object.keys(declared).sort());
    for (const label of hierarchicalRequirementSurface.labels) {
      expect(`${label.label}: ${label.prefix} ${label.on.join(",")}`).toBe(`${label.label}: ${declared[label.label]?.prefix} ${declared[label.label]?.on.join(",")}`);
    }
  });

  it("shows each assessment kind by the icon its definition gives it, keyed by the stored form", () => {
    // Act.
    const kinds = Object.values(definition().metamodel.enums.AssessmentKind.values);

    // Assert.
    expect(kinds).toHaveLength(4);
    expect(Object.keys(hierarchicalRequirementSurface.noteIcons).sort()).toEqual(kinds.map((kind) => kind.value).sort());
    for (const kind of kinds) {
      expect(`${kind.value}: ${hierarchicalRequirementSurface.noteIcons[kind.value]}`).toBe(`${kind.value}: ${kind.icon}`);
    }
    expect(hierarchicalRequirementSurface.severalNotesIcon).toBe("mdi-comment-multiple-outline");
  });

  it("sends only operations its definition has", () => {
    // Act.
    const { operations } = definition();

    // Assert.
    for (const operation of Object.values(hierarchicalRequirementSurface.operations)) {
      expect(Object.keys(operations)).toContain(typeof operation === "string" ? operation : operation.operation);
    }
  });

  it("labels each add control as its operation is labelled, and names the move's params as its definition does", () => {
    // Act.
    const { operations } = definition();
    const { addTopLevel, addEntry, addLevel, moveLevel, moveEntry } = hierarchicalRequirementSurface.operations;

    // Assert.
    for (const add of [addTopLevel, addEntry, addLevel]) {
      expect(`${add.operation}: ${add.label}`).toBe(`${add.operation}: ${String(operations[add.operation]?.label)}`);
    }
    for (const move of [moveLevel, moveEntry]) {
      expect(Object.keys(operations[move]?.params ?? {}).sort()).toEqual(Object.values(hierarchicalRequirementSurface.moveParams).sort());
    }
  });

  it("sends the assessment operations its definition has, with their params, and words them as they are labelled", () => {
    // Act.
    const { operations } = definition();
    const { notes } = hierarchicalRequirementSurface;

    // Assert: an add names the kind and the text, a change the detail too; each control says what its operation's label says.
    expect(Object.keys(operations[notes.operations.add]?.params ?? {}).sort()).toEqual([notes.params.kind, notes.params.text].sort());
    expect(Object.keys(operations[notes.operations.change]?.params ?? {}).sort()).toEqual(Object.values(notes.params).sort());
    expect(Object.keys(operations)).toContain(notes.operations.resolve);
    expect(Object.keys(operations)).toContain(notes.operations.remove);
    expect(String(operations[notes.operations.add]?.label)).toBe(notes.words.add);
    expect(String(operations[notes.operations.change]?.label)).toBe(notes.words.change);
    expect(String(operations[notes.operations.remove]?.label)).toBe(notes.words.remove);
    expect((operations[notes.operations.resolve]?.label as { cel: string }).cel).toBe(`self.resolved ? '${notes.words.reopen}' : '${notes.words.resolve}'`);
  });

  it("edits the tags with the tag operations its definition has, with their params and labels, and asks before a delete as its confirm says", () => {
    // Act.
    const { operations } = definition();
    const edit = hierarchicalRequirementSurface.labels.find((label) => label.edit !== undefined)?.edit;

    // Assert: only the tags are edited here; each operation exists with the param the field sends, and words its control as labelled.
    expect(hierarchicalRequirementSurface.labels.filter((label) => label.edit !== undefined).map((label) => label.label)).toEqual(["tags"]);
    expect(Object.keys(operations[edit!.operations.add]?.params ?? {})).toEqual([edit!.params.name]);
    expect(Object.keys(operations[edit!.operations.remove]?.params ?? {})).toEqual([edit!.params.name]);
    expect(Object.keys(operations[edit!.operations.rename]?.params ?? {})).toEqual([edit!.params.name]);
    expect(Object.keys(operations[edit!.operations.recolour]?.params ?? {})).toEqual([edit!.params.colour]);
    expect(Object.keys(operations[edit!.operations.move]?.params ?? {})).toEqual([edit!.params.after]);
    expect(Object.keys(operations)).toContain(edit!.operations.delete);
    expect(String(operations[edit!.operations.add]?.label)).toBe(edit!.words.add);
    expect(String(operations[edit!.operations.remove]?.label)).toBe(edit!.words.remove);
    const confirm = operations[edit!.operations.delete]?.confirm;
    expect(confirm?.threshold).toBe(edit!.deletion.threshold);
    const written = (confirm?.message.cel ?? "").replace("string(count) + ' entries carry #' + self.name + '", "'3 entries carry #ux").replace(/^'|'$/g, "");
    expect(written).toBe(edit!.deletion.message("ux", 3));
  });

  it("picks the category and the state with the operations its definition has, with their params and labels, and asks before a delete as its confirm says", () => {
    // Act.
    const { operations } = definition();
    const picked = hierarchicalRequirementSurface.labels.filter((label) => label.pick !== undefined);

    // Assert: both labels of one value are picked; each operation exists with the params the picker sends, and words its control as labelled.
    expect(picked.map((label) => label.label)).toEqual(["category", "state"]);
    for (const { label, pick } of picked) {
      const { operations: names, params, words, deletion } = pick!;
      expect(Object.keys(operations[names.set]?.params ?? {})).toEqual([params.value]);
      expect(params.value).toBe(label);
      expect(Object.keys(operations[names.clear]?.params ?? {})).toEqual([]);
      expect(Object.keys(operations[names.add]?.params ?? {}).sort()).toEqual([params.name, params.colour].sort());
      expect(Object.keys(operations[names.rename]?.params ?? {})).toEqual([params.name]);
      expect(Object.keys(operations[names.recolour]?.params ?? {})).toEqual([params.colour]);
      expect(Object.keys(operations[names.move]?.params ?? {})).toEqual([params.after]);
      expect(Object.keys(operations)).toContain(names.delete);
      expect(String(operations[names.set]?.label)).toBe(words.value);
      expect(String(operations[names.clear]?.label)).toBe(words.clear);
      const confirm = operations[names.delete]?.confirm;
      expect(confirm?.threshold).toBe(deletion.threshold);
      const written = (confirm?.message.cel ?? "").replace("string(count) + ' requirements are ' + self.name + '", "'3 requirements are draft").replace(/^'|'$/g, "");
      expect(written).toBe(deletion.message("draft", 3));
    }
  });

  it("stores, renames and removes filters with the operations its definition has, and sorts by the keys of its notation", () => {
    // Act.
    const { operations, surface } = definition();
    const filters = hierarchicalRequirementSurface.filters!;

    // Assert: a store names the filter and its query, a rename the new name; a remove takes nothing.
    expect(surface.filters?.type).toBe("Filter");
    expect(Object.keys(operations[filters.operations.store]?.params ?? {}).sort()).toEqual([filters.params.name, filters.params.query].sort());
    expect(Object.keys(operations[filters.operations.rename]?.params ?? {})).toEqual([filters.params.name]);
    expect(Object.keys(operations[filters.operations.remove]?.params ?? {})).toEqual([]);
    expect(filters.sortKeys.map((key) => key.key)).toEqual(["number", "title"]);
  });

  it("asks before a delete in the words and at the threshold of its definition's confirm", () => {
    // Act.
    const confirm = definition().operations[hierarchicalRequirementSurface.operations.delete]?.confirm;

    // Assert: the CEL with self.id and the count in place gives the sentence the page asks.
    expect(confirm?.threshold).toBe(hierarchicalRequirementSurface.deletion.threshold);
    const cel = confirm?.message.cel ?? "";
    const written = cel
      .replace("'Delete ' + self.id + ' with everything in it? ' + string(count) + '", "'Delete ITEM-0002 with everything in it? 3")
      .replace(/^'|'$/g, "");
    expect(written).toBe(hierarchicalRequirementSurface.deletion.message("ITEM-0002", 3));
  });
});
