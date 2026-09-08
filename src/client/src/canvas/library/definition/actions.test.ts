import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import { actionForGesture, actionForKey, flagOf, shortcutKeysOf, type ActionDeclaration } from "./actions";

const element: DiagramModelElement = { id: "e1", type: "node", x: 0, y: 0 };
const source = (payload?: unknown): BindingSource => ({ element, payload });

const press = (key: string, mods: Partial<Record<"ctrlKey" | "shiftKey" | "altKey" | "metaKey", boolean>> = {}) => ({
  key,
  ctrlKey: false,
  shiftKey: false,
  altKey: false,
  metaKey: false,
  ...mods,
});

const lookup = (actions: readonly ActionDeclaration[], payload?: unknown) => ({
  actions,
  targetKind: "element" as const,
  targetId: "e1",
  typeId: "node",
  source: source(payload),
});

/**
 * THE FOUR SPELLINGS THAT MUST DISAPPEAR. Eleven canvases hand-write a structural key list and
 * there are four different spellings of one intent: ["F2"] in seven, ["F2","Insert"] in c4,
 * ["F2","Insert","Tab","Enter"] in dependency-graph and timeline, and
 * ["Insert","Enter","F2"," ","Tab"] in mindmap. There is no list to spell differently once the
 * key set is DERIVED from the declarations.
 */
describe("actions — the key set is derived, never written", () => {
  const declared: ActionDeclaration[] = [
    { id: "rename", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    { id: "add-child", invokedBy: [{ kind: "shortcut", key: "Insert" }, { kind: "menu" }], appliesTo: [{ kind: "element" }] },
    { id: "add-sibling", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
  ];

  it("collects every declared shortcut key and nothing else", () => {
    expect([...shortcutKeysOf(declared)].sort()).toEqual(["Enter", "F2", "Insert"]);
  });

  it("yields no keys for a type that declares no shortcut actions", () => {
    // Six canvases wire nothing today. Declaring nothing must mean nothing - not a default set
    // that quietly gives a read-only type a rename.
    expect(shortcutKeysOf(undefined)).toEqual([]);
    expect(shortcutKeysOf([{ id: "x", invokedBy: [{ kind: "menu" }], appliesTo: [{ kind: "element" }] }])).toEqual([]);
  });

  it("dispatches the action a key invokes, by id rather than by keystroke", () => {
    const dispatched = actionForKey(lookup(declared), press("F2"));
    expect(dispatched).toEqual({ actionId: "rename", targetKind: "element", targetId: "e1" });
  });

  it("does not fire on a modified press when the declaration named no modifier", () => {
    // An unstated modifier means "not held", which is what every existing call site means.
    expect(actionForKey(lookup(declared), press("F2", { ctrlKey: true }))).toBeNull();
  });

  it("fires on the modified press a declaration does name, and not on the bare one", () => {
    const withCtrl: ActionDeclaration[] = [
      { id: "duplicate", invokedBy: [{ kind: "shortcut", key: "d", ctrl: true }], appliesTo: [{ kind: "element" }] },
    ];

    expect(actionForKey(lookup(withCtrl), press("d", { ctrlKey: true }))?.actionId).toBe("duplicate");
    expect(actionForKey(lookup(withCtrl), press("d"))).toBeNull();
  });
});

/**
 * THE SYNTHESISED DELETE THAT MUST DISAPPEAR. Nine canvases build
 * `{ key: "Delete", ctrl: false, shift: false, alt: false, meta: false }` by hand and send it to
 * the backend - a module manufacturing a fake key event to say "delete this" is the sharpest
 * evidence available that the action had nowhere to be declared.
 */
describe("actions — a gesture dispatches an action, not a keystroke", () => {
  const withDelete: ActionDeclaration[] = [
    { id: "dependencies.delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
  ];

  it("dispatches the declared delete action for a delete gesture", () => {
    expect(actionForGesture(lookup(withDelete), "delete")).toEqual({
      actionId: "dependencies.delete",
      targetKind: "element",
      targetId: "e1",
    });
  });

  it("dispatches nothing when the type declares no delete, which is now distinguishable", () => {
    // Requirement 2.8: a type offering no delete must be distinguishable from one whose delete
    // nobody wired. Declaring none means none, and the gesture finds nothing to dispatch.
    const noDelete: ActionDeclaration[] = [
      { id: "rename", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    ];

    expect(actionForGesture(lookup(noDelete), "delete")).toBeNull();
  });

  it("respects the target a declaration names, so a connection delete does not fire on an element", () => {
    const connectionsOnly: ActionDeclaration[] = [
      { id: "unlink", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
    ];

    expect(actionForGesture(lookup(connectionsOnly), "delete")).toBeNull();
    expect(
      actionForGesture({ ...lookup(connectionsOnly), targetKind: "connection", typeId: "calls" }, "delete")?.actionId,
    ).toBe("unlink");
  });

  it("narrows by element type where a declaration lists them", () => {
    const storesOnly: ActionDeclaration[] = [
      { id: "drop-store", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element", elementTypes: ["store"] }] },
    ];

    expect(actionForGesture(lookup(storesOnly), "delete")).toBeNull();
    expect(actionForGesture({ ...lookup(storesOnly), typeId: "store" }, "delete")?.actionId).toBe("drop-store");
  });
});

describe("actions — enablement, stated rather than inferred", () => {
  const conditional: ActionDeclaration[] = [
    {
      id: "rename",
      invokedBy: [{ kind: "shortcut", key: "F2" }],
      appliesTo: [{ kind: "element" }],
      enabled: { path: "payload.editable" },
    },
  ];

  it("fires when the bound flag is true and not when it is false", () => {
    expect(actionForKey(lookup(conditional, { editable: true }), press("F2"))?.actionId).toBe("rename");
    expect(actionForKey(lookup(conditional, { editable: false }), press("F2"))).toBeNull();
  });

  it("does not fire when enablement cannot be determined at all", () => {
    // Firing is the irreversible half. An action whose enablement resolves to nothing must not
    // run - the alternative is a delete that happens because a path was misspelt.
    expect(actionForKey(lookup(conditional, {}), press("F2"))).toBeNull();
  });

  it("takes a literal false as readily as a binding", () => {
    const off: ActionDeclaration[] = [
      { id: "rename", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }], enabled: false },
    ];

    expect(actionForKey(lookup(off), press("F2"))).toBeNull();
  });

  it("is enabled when nothing says otherwise, so declaring an action is enough", () => {
    const plain: ActionDeclaration[] = [
      { id: "rename", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    ];

    expect(actionForKey(lookup(plain), press("F2"))?.actionId).toBe("rename");
  });
});

describe("actions — anchor visibility and enablement on the same mechanism", () => {
  it("resolves a declared flag, defaulting to on", () => {
    // All twenty-nine anchor declarations in the tree are `{ kind: "edge" }`, so the positional
    // half of AnchorSet has never been needed - and the half that was missing is the one
    // modules actually want. Same DeclaredFlag, same resolver, no second mechanism.
    expect(flagOf(undefined, source({}))).toBe(true);
    expect(flagOf(false, source({}))).toBe(false);
    expect(flagOf({ path: "payload.connectable" }, source({ connectable: true }))).toBe(true);
    expect(flagOf({ path: "payload.connectable" }, source({ connectable: false }))).toBe(false);
  });

  it("reads an unresolvable flag as off, matching the action rule", () => {
    expect(flagOf({ path: "payload.nope" }, source({}))).toBe(false);
  });
});
