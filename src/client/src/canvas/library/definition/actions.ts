import { holds, resolveOne, type Binding, type BindingSource, type Condition } from "./binding";

/**
 * `actions` — which actions a diagram type offers, whether each is enabled, what it applies to,
 * and what invokes it.
 *
 * <b>The measured need is drift, not absence.</b> Eleven canvases hand-write a structural
 * shortcut key list and there are <b>four different spellings of the same intent</b> —
 * `["F2"]` in seven, `["F2", "Insert"]` in c4, `["F2", "Insert", "Tab", "Enter"]` in
 * dependency-graph and timeline, and `["Insert", "Enter", "F2", " ", "Tab"]` in mindmap. Nine
 * canvases <b>synthesise a keystroke to name an action</b>, building
 * `{ key: "Delete", ctrl: false, shift: false, alt: false, meta: false }` by hand and sending it
 * to the backend. And six wire nothing at all, so <em>"this type has no rename"</em> and
 * <em>"nobody wired one"</em> are indistinguishable.
 *
 * <b>Two things must disappear, and the task is not done until they do</b>: the four key lists,
 * and the synthesised `Delete`. A module must have no way to name an action by manufacturing a
 * key event — which is why {@link shortcutKeysOf} derives the key set from the declarations
 * rather than taking one, and why the library dispatches an action id instead of a keystroke.
 *
 * <b>Only the handler stays imperative</b>, which is precisely the carve-out the user permitted.
 * The declaration is what makes that carve-out checkable: an action's existence, enablement,
 * target and trigger are description; the code that runs when it fires is not.
 */

/** A value stated outright or read from the model. */
export type DeclaredFlag = boolean | Binding;

/** What invokes an action. */
export type ActionInvocation =
  | {
      kind: "shortcut";
      /** The `KeyboardEvent.key` value - `"F2"`, `"Delete"`, `"Enter"`, `" "`. */
      key: string;
      ctrl?: boolean;
      shift?: boolean;
      alt?: boolean;
      meta?: boolean;
    }
  | { kind: "menu" }
  /**
   * A canvas gesture the library already recognises. `delete` is the one that matters: it is
   * what nine canvases express today by building a fake `Delete` keystroke, and declaring it
   * here is what lets the library dispatch the action directly.
   */
  /**
   * `activate` and `context-menu` were added by the single-label migrations (register entry
   * G22). Three canvases - ansible-structure, dotnet-dependency-graph and helm-charts - hang
   * `onDoubleClick` and `onContextMenu` on the element they render, which is the OTHER reason
   * those three need a custom shape at all: not the drawing, the two handlers attached to it.
   * Declared, the library dispatches the module's own action id and the shape goes.
   */
  | { kind: "gesture"; gesture: "delete" | "connect" | "drop" | "activate" | "context-menu" };

/** What an action applies to. */
export type ActionTarget =
  | { kind: "element"; elementTypes?: readonly string[] }
  | { kind: "connection"; relationTypes?: readonly string[] }
  | { kind: "canvas" };

export interface ActionDeclaration {
  /** The id the module's handler switches on - `"mindmap.add-child"`, `"delete"`. */
  id: string;
  invokedBy: readonly ActionInvocation[];
  appliesTo: readonly ActionTarget[];
  /**
   * Whether the action is offered at all.
   *
   * <b>Absence and refusal become distinguishable here</b>, which is Requirement 2.8's whole
   * point: a type that declares no rename has none, and a type that declares one disabled has
   * one that is currently unavailable. Today the tree cannot tell those apart, because both look
   * like a handler nobody wrote.
   */
  enabled?: DeclaredFlag;
  /** What a menu calls it. */
  label?: string;
  when?: Condition;
}

/** One action the library has decided to dispatch. */
export interface DispatchedAction {
  actionId: string;
  targetKind: "element" | "connection" | "canvas";
  targetId?: string;
}

function flagHolds(flag: DeclaredFlag | undefined, source: BindingSource): boolean {
  if (flag === undefined) {
    return true;
  }

  if (typeof flag === "boolean") {
    return flag;
  }

  // A binding resolving to nothing reads as disabled rather than enabled: an action whose
  // enablement cannot be determined must not fire, because firing is the irreversible half.
  const value = resolveOne(flag, source);
  return value === "true";
}

/**
 * The keys a definition's actions listen for.
 *
 * <b>This is what replaces the four hand-written lists.</b> A module states its actions and the
 * library derives the key set; there is no list to spell differently, because there is no list.
 */
export function shortcutKeysOf(actions: readonly ActionDeclaration[] | undefined): readonly string[] {
  const keys = new Set<string>();
  for (const action of actions ?? []) {
    for (const invocation of action.invokedBy) {
      if (invocation.kind === "shortcut") {
        keys.add(invocation.key);
      }
    }
  }

  return [...keys];
}

/** Whether a target declaration admits this element type or relation type. */
function targetAdmits(target: ActionTarget, kind: "element" | "connection" | "canvas", typeId?: string): boolean {
  if (target.kind !== kind) {
    return false;
  }

  if (target.kind === "element") {
    return target.elementTypes === undefined || typeId === undefined || target.elementTypes.includes(typeId);
  }

  if (target.kind === "connection") {
    return target.relationTypes === undefined || typeId === undefined || target.relationTypes.includes(typeId);
  }

  return true;
}

export interface ActionLookup {
  actions: readonly ActionDeclaration[] | undefined;
  targetKind: "element" | "connection" | "canvas";
  targetId?: string;
  /** The target's element type or relation type id, for a declaration that narrows by type. */
  typeId?: string;
  source: BindingSource;
}

/**
 * The action a keystroke invokes, or null.
 *
 * Modifiers are compared explicitly rather than loosely: a declaration saying `ctrl` must not
 * fire on a bare press, and one saying nothing must not fire on a modified one. An unstated
 * modifier means "not held", because that is what every existing call site means by it.
 */
export function actionForKey(
  lookup: ActionLookup,
  event: { key: string; ctrlKey: boolean; shiftKey: boolean; altKey: boolean; metaKey: boolean },
): DispatchedAction | null {
  for (const action of lookup.actions ?? []) {
    if (!holds(action.when, lookup.source) || !flagHolds(action.enabled, lookup.source)) {
      continue;
    }

    if (!lookup.actions || !action.appliesTo.some((target) => targetAdmits(target, lookup.targetKind, lookup.typeId))) {
      continue;
    }

    for (const invocation of action.invokedBy) {
      if (
        invocation.kind === "shortcut" &&
        invocation.key === event.key &&
        (invocation.ctrl ?? false) === event.ctrlKey &&
        (invocation.shift ?? false) === event.shiftKey &&
        (invocation.alt ?? false) === event.altKey &&
        (invocation.meta ?? false) === event.metaKey
      ) {
        return { actionId: action.id, targetKind: lookup.targetKind, targetId: lookup.targetId };
      }
    }
  }

  return null;
}

/**
 * The action a canvas gesture invokes, or null.
 *
 * <b>This is what replaces the synthesised `Delete`.</b> A delete gesture reaches the module as
 * its own declared action id, so no module builds a key event to say "delete this" — and a type
 * that declares no delete action simply has none, rather than having one nobody wired.
 */
export function actionForGesture(
  lookup: ActionLookup,
  gesture: "delete" | "connect" | "drop" | "activate" | "context-menu",
): DispatchedAction | null {
  for (const action of lookup.actions ?? []) {
    if (!holds(action.when, lookup.source) || !flagHolds(action.enabled, lookup.source)) {
      continue;
    }

    if (!action.appliesTo.some((target) => targetAdmits(target, lookup.targetKind, lookup.typeId))) {
      continue;
    }

    if (action.invokedBy.some((invocation) => invocation.kind === "gesture" && invocation.gesture === gesture)) {
      return { actionId: action.id, targetKind: lookup.targetKind, targetId: lookup.targetId };
    }
  }

  return null;
}

/**
 * The declared action a shared-menu entry names, or null - which means the backend runs it.
 *
 * <b>The menu's entries are always the backend's list</b>: what is offered, under which label, in
 * which group, stays single-sourced. What a definition may declare is <b>who runs</b> an entry. One
 * whose id it declares with `invokedBy: [{ kind: "menu" }]` goes to the module's handler as
 * `action-invoked`, exactly as a declared shortcut or gesture does, and nothing is sent to the
 * backend - databricks' simulated runs, which must never reach a command or a file
 * (centralized-selection, design A).
 *
 * Matched by id, and the declaration's `when` and `enabled` must hold. `appliesTo` is NOT checked
 * again: the backend's list already decided where the entry is offered, and a second opinion
 * here could only disagree with it.
 */
export function actionForMenuEntry(lookup: ActionLookup, actionId: string): DispatchedAction | null {
  for (const action of lookup.actions ?? []) {
    if (action.id !== actionId || !action.invokedBy.some((invocation) => invocation.kind === "menu")) {
      continue;
    }

    if (holds(action.when, lookup.source) && flagHolds(action.enabled, lookup.source)) {
      return { actionId: action.id, targetKind: lookup.targetKind, targetId: lookup.targetId };
    }
  }

  return null;
}

/** Whether a declared flag - an anchor's `visible` or `enabled` - currently holds. */
export function flagOf(flag: DeclaredFlag | undefined, source: BindingSource, fallback = true): boolean {
  if (flag === undefined) {
    return fallback;
  }

  return flagHolds(flag, source);
}
