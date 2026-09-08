import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import { holds, resolveMany, resolveOne, type Binding, type BindingSource } from "./binding";

const element: DiagramModelElement = { id: "e1", type: "card", x: 10, y: 20, label: "Marie Curie" };

const source = (payload?: unknown): BindingSource => ({ element, payload });

describe("binding — the field form", () => {
  it("reads a payload field", () => {
    expect(resolveOne({ path: "payload.name" }, source({ name: "Serilog" }))).toBe("Serilog");
  });

  it("reads an element field, so the library's own contract is addressable too", () => {
    expect(resolveOne({ path: "element.label" }, source())).toBe("Marie Curie");
  });

  it("renders a number and a boolean as text, because a declaration cannot cast", () => {
    expect(resolveOne({ path: "payload.count" }, source({ count: 3 }))).toBe("3");
    expect(resolveOne({ path: "payload.conflict" }, source({ conflict: true }))).toBe("true");
  });

  it("draws nothing for a path that does not resolve, rather than throwing", () => {
    // THE RULE THAT MAKES A TYPO SURVIVABLE. A binding is authored data that ships; a
    // declaration with a misspelt path must leave a gap in one diagram, not take the canvas
    // down for every user of it. Every kind of failure answers the same way on purpose - a
    // resolver that distinguished "missing root" from "missing field" would be inviting a
    // caller to branch on why, which is module logic arriving through the back door.
    expect(resolveOne({ path: "payload.nope" }, source({ name: "x" }))).toBeNull();
    expect(resolveOne({ path: "payload.name" }, source(undefined))).toBeNull();
    expect(resolveOne({ path: "payload.name.deeper" }, source({ name: "x" }))).toBeNull();
    expect(resolveOne({ path: "" }, source({}))).toBeNull();
  });

  it("yields nothing for a value that is not a primitive", () => {
    expect(resolveOne({ path: "payload.rows" }, source({ rows: [1, 2] }))).toBeNull();
    expect(resolveOne({ path: "payload.nested" }, source({ nested: { a: 1 } }))).toBeNull();
  });
});

describe("binding — the template form", () => {
  it("composes several fields, which is the rdf row this was drawn from", () => {
    const binding: Binding = { template: "{payload.predicate}: {payload.value}" };
    expect(resolveOne(binding, source({ predicate: "born", value: "1867" }))).toBe("born: 1867");
  });

  it("drops an unresolved placeholder and collapses the gap it leaves", () => {
    // The measured shape is `predicate: value annotation` with the annotation often absent.
    const binding: Binding = { template: "{payload.p}: {payload.v} {payload.a}" };
    expect(resolveOne(binding, source({ p: "born", v: "1867" }))).toBe("born: 1867");
  });

  it("yields nothing when nothing in it resolved", () => {
    expect(resolveOne({ template: "{payload.a}{payload.b}" }, source({}))).toBeNull();
  });

  it("keeps literal text between two optional values - a stated limit, not an accident", () => {
    // Documented in fillTemplate: the remedy for a dangling separator is a `when`, or two
    // labels rather than one template. Pinned here so the limit is a decision on the record
    // rather than a surprise, and so a later "improvement" to punctuation-aware trimming has
    // to argue with a test.
    expect(resolveOne({ template: "{payload.a} - {payload.b}" }, source({ a: "left" }))).toBe("left -");
  });
});

describe("binding — the collection form", () => {
  const rows = [
    { predicate: "born", value: "1867" },
    { predicate: "died", value: "1934" },
  ];

  it("yields one value per entry, with paths rooted at the ITEM", () => {
    // THE LOAD-BEARING CASE. A fixed set of named slots would serve c4 and leave the rdf
    // family exactly where it is - the renderers that hand-roll a LOOP are why this binds to
    // a collection. Rooting `each` at the item is what lets a row say `{predicate}` rather
    // than `{payload.rows.0.predicate}`.
    const binding: Binding = { path: "payload.rows", each: { template: "{predicate}: {value}" } };
    expect(resolveMany(binding, source({ rows }))).toEqual(["born: 1867", "died: 1934"]);
  });

  it("takes a field binding per entry as well as a template", () => {
    const binding: Binding = { path: "payload.rows", each: { path: "predicate" } };
    expect(resolveMany(binding, source({ rows }))).toEqual(["born", "died"]);
  });

  it("yields nothing over an empty list", () => {
    expect(resolveMany({ path: "payload.rows", each: { path: "predicate" } }, source({ rows: [] }))).toEqual([]);
  });

  it("yields nothing when the path is not a list at all", () => {
    expect(resolveMany({ path: "payload.rows", each: { path: "p" } }, source({ rows: "not a list" }))).toEqual([]);
    expect(resolveMany({ path: "payload.missing", each: { path: "p" } }, source({}))).toEqual([]);
  });

  it("skips an entry that resolves to nothing rather than emitting a blank line", () => {
    const mixed = [{ p: "kept" }, { other: "x" }, { p: "also kept" }];
    expect(resolveMany({ path: "payload.rows", each: { path: "p" } }, source({ rows: mixed }))).toEqual([
      "kept",
      "also kept",
    ]);
  });

  it("gives its first entry to a single-value caller rather than throwing", () => {
    const binding: Binding = { path: "payload.rows", each: { path: "predicate" } };
    expect(resolveOne(binding, source({ rows }))).toBe("born");
  });
});

describe("binding — conditions", () => {
  it("suppresses a binding whose condition fails", () => {
    // Replaces `badges.length > 0 ? … : null`, which ten renderers spell as a ternary.
    const binding: Binding = { path: "payload.badge", when: { path: "payload.badges", is: "non-empty" } };
    expect(resolveOne(binding, source({ badge: "B", badges: [] }))).toBeNull();
    expect(resolveOne(binding, source({ badge: "B", badges: ["x"] }))).toBe("B");
  });

  it("answers every condition kind, with an unresolvable path reading as absent", () => {
    const s = source({ text: "", list: [], flag: false, name: "n" });
    expect(holds({ path: "payload.name", is: "present" }, s)).toBe(true);
    expect(holds({ path: "payload.nope", is: "present" }, s)).toBe(false);
    expect(holds({ path: "payload.nope", is: "absent" }, s)).toBe(true);
    expect(holds({ path: "payload.text", is: "empty" }, s)).toBe(true);
    expect(holds({ path: "payload.list", is: "empty" }, s)).toBe(true);
    expect(holds({ path: "payload.name", is: "non-empty" }, s)).toBe(true);
    expect(holds({ path: "payload.flag", is: "false" }, s)).toBe(true);
    expect(holds({ path: "payload.flag", is: "true" }, s)).toBe(false);
    expect(holds({ path: "payload.name", equals: "n" }, s)).toBe(true);
    expect(holds({ path: "payload.name", equals: "other" }, s)).toBe(false);
  });

  it("holds when there is no condition, so `when` is optional rather than defaulted", () => {
    expect(holds(undefined, source({}))).toBe(true);
  });

  it("applies a condition per entry inside a collection", () => {
    const rows = [{ p: "kept", ok: true }, { p: "dropped", ok: false }];
    const binding: Binding = {
      path: "payload.rows",
      each: { path: "p", when: { path: "ok", is: "true" } },
    };
    expect(resolveMany(binding, source({ rows }))).toEqual(["kept"]);
  });
});

describe("binding — the prohibition", () => {
  it("cannot express a function, and the type is what enforces it", () => {
    // THE CLAIM THIS FILE EXISTS FOR, and the honest limit of asserting it from inside
    // TypeScript. `{ path: () => "x" }` does not compile - `path` is a string - so the guard
    // is the type system and a runtime test cannot demonstrate a compile error.
    //
    // What this test CAN show is the behaviour when someone defeats the type with a cast,
    // which is the only way a function reaches here: it resolves to nothing. A function-valued
    // binding is therefore inert rather than invoked, so the escape hatch does not reopen
    // through a cast either. The conformance guard (task 15) is the third mechanism and reads
    // the source text, because a cast leaves no trace at runtime.
    const smuggled = { path: "payload.compute" } as Binding;
    const payload = { compute: () => "invoked" };

    expect(resolveOne(smuggled, source(payload))).toBeNull();
  });

  it("never invokes a function it walks past on the way to a field", () => {
    let called = false;
    const payload = {
      trap: () => {
        called = true;
        return { name: "x" };
      },
      name: "safe",
    };

    expect(resolveOne({ path: "payload.trap.name" }, source(payload))).toBeNull();
    expect(resolveOne({ path: "payload.name" }, source(payload))).toBe("safe");
    expect(called, "the resolver called a function while walking a path").toBe(false);
  });
});

/**
 * THE FIVE THINGS THE SUFFICIENCY TABLE FOUND MISSING, and each names the row that justifies
 * it. Nothing below was added because it seemed generally useful: a vocabulary that grows on
 * taste rather than on a measured row is how the escape hatch comes back wearing a new coat.
 */
describe("binding — what the sufficiency table forced", () => {
  it("reads the canvas's own state, which 23 of the 28 rows style on (G2)", () => {
    const selected: BindingSource = { element, payload: {}, state: { selected: true } };

    expect(holds({ path: "state.selected", is: "true" }, selected)).toBe(true);
    expect(holds({ path: "state.dragging", is: "true" }, selected)).toBe(false);
    // And an element nobody is touching answers the same way as one whose state is absent -
    // the "every failure answers identically" rule, applied to a root that may not be there.
    expect(holds({ path: "state.selected", is: "true" }, source({}))).toBe(false);
  });

  it("keeps state out of reach of a payload field with the same name", () => {
    // The reason state is its own root rather than merged in: a module payload carrying
    // `selected` must not be able to decide how selection looks.
    const source_: BindingSource = { element, payload: { selected: true }, state: { selected: false } };

    expect(holds({ path: "state.selected", is: "true" }, source_)).toBe(false);
    expect(holds({ path: "payload.selected", is: "true" }, source_)).toBe(true);
  });

  it("joins a collection into one badge strip rather than a stack of lines (G4)", () => {
    const badges = { path: "payload.badges", each: { path: "name" }, join: " · " } as const;

    expect(resolveMany(badges, source({ badges: [{ name: "Dataset" }, { name: "Table" }] }))).toEqual(["Dataset · Table"]);
    // AND AN EMPTY COLLECTION STAYS EMPTY. Joining nothing yields "", which would draw as a
    // blank label rather than as no label - the difference between an absent badge strip and
    // an empty one, which is exactly the distinction this specification keeps insisting on.
    expect(resolveMany(badges, source({ badges: [] }))).toEqual([]);
  });

  it("drops an absent part AND its separator, which a template cannot (G5)", () => {
    // The measured case: `mode · default · 2 overrides`, where "default" is conditional. The
    // template form leaves `mode ·  · 2 overrides` behind, which is the stated limit of
    // fillTemplate - so this is a different mechanism rather than a nicer template.
    const badges = {
      parts: [
        { path: "payload.mode" },
        { path: "payload.default", when: { path: "payload.default", is: "present" } },
        { template: "{payload.overrides} overrides", when: { path: "payload.overrides", is: "present" } },
      ],
      join: " · ",
    } as const;

    expect(resolveOne(badges, source({ mode: "shared", default: "default", overrides: 2 }))).toBe("shared · default · 2 overrides");
    expect(resolveOne(badges, source({ mode: "shared", overrides: 2 }))).toBe("shared · 2 overrides");
    expect(resolveOne(badges, source({ mode: "shared" }))).toBe("shared");
    expect(resolveOne(badges, source({}))).toBeNull();
  });

  it("declines the noun with the count, which a template cannot (G9)", () => {
    const jobs = { path: "payload.jobCount", plural: { one: "job", other: "jobs" } } as const;

    expect(resolveOne(jobs, source({ jobCount: 1 }))).toBe("1 job");
    expect(resolveOne(jobs, source({ jobCount: 3 }))).toBe("3 jobs");
    // Zero takes the plural, as English does - and a missing count draws nothing rather than
    // "undefined jobs".
    expect(resolveOne(jobs, source({ jobCount: 0 }))).toBe("0 jobs");
    expect(resolveOne(jobs, source({}))).toBeNull();
  });

  it("scales and offsets a number, which is the timeline's drag hint (G16)", () => {
    // x times seconds-per-unit plus origin, formatted as a clock: the one row whose text is
    // arithmetic over the module's scale rather than a field of the model.
    const hint = {
      path: "element.x",
      number: { times: "payload.secondsPerUnit", plus: "payload.originSeconds", format: "HH:mm" },
    } as const;
    const jan2026 = Date.UTC(2026, 0, 1) / 1000;

    expect(resolveOne(hint, { element: { ...element, x: 3 }, payload: { secondsPerUnit: 3600, originSeconds: jan2026 } })).toBe("03:00");
    // TIMES BEFORE PLUS, and this is the assertion that says so: with the order reversed the
    // same declaration reads 4562:00-ish rather than 03:00, because the origin would be scaled
    // by 3600 as well. An author guessing at precedence gets a wrong drawing, not an error.
    expect(resolveOne({ path: "element.x", number: { times: 2, plus: 10 } }, { element: { ...element, x: 3 }, payload: {} })).toBe("16");
    // A field that is not a number draws nothing, like every other unresolvable path.
    expect(resolveOne({ path: "payload.name", number: { times: 2 } }, source({ name: "x" }))).toBeNull();
  });

  it("formats an instant identically to a ruler tick, because it is the same formatter", () => {
    const at = Date.UTC(2026, 2, 9, 22, 30) / 1000;

    expect(resolveOne({ path: "payload.at", number: { format: "d MMM" } }, source({ at }))).toBe("9 Mar");
    expect(resolveOne({ path: "payload.at", number: { format: "HH:mm" } }, source({ at }))).toBe("22:30");
  });
});

describe("binding — the palette slot (G3)", () => {
  it("wraps an index into a fixed number of colour slots", () => {
    // Sufficiency row 1: ansible colours a play by `playIndex % PALETTE_SLOTS`, so the
    // fourteenth play reuses the first play's colour rather than running out. Without the
    // third term this is arithmetic no declaration can do, and the module keeps a renderer
    // for a class name.
    const slot = { path: "payload.playIndex", number: { modulo: 8 } } as const;

    expect(resolveOne(slot, source({ playIndex: 0 }))).toBe("0");
    expect(resolveOne(slot, source({ playIndex: 7 }))).toBe("7");
    expect(resolveOne(slot, source({ playIndex: 8 }))).toBe("0");
    expect(resolveOne(slot, source({ playIndex: 13 }))).toBe("5");
  });

  it("wraps a NEGATIVE index into the same range rather than out of it", () => {
    // JavaScript's % keeps the sign, so -1 % 8 is -1 and the class would be
    // `ansible-play--1` - a selector that matches nothing, silently, for the elements that
    // belong to no play. The sign is corrected here rather than left to the author.
    expect(resolveOne({ path: "payload.playIndex", number: { modulo: 8 } }, source({ playIndex: -1 }))).toBe("7");
  });
});
