import type { ContextShortcut } from "../generated/context-contract_pb";

/**
 * The keyboard half of a canvas's element interaction, shared by every diagram type.
 *
 * The backend holds the key-to-action table; a canvas only forwards a keystroke as data. What
 * differs per diagram type is *which* keys carry structural meaning there - passed in, so the
 * table stays with the module and the mechanics stay here.
 */

/** Whether an event's target is a text input the browser should handle instead of the canvas. */
export function isTextTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  const tag = target.tagName.toLowerCase();
  return tag === "input" || tag === "textarea" || target.isContentEditable;
}

/**
 * A backend context shortcut for one key, with no modifiers.
 *
 * <b>This is what a module needs once the LIBRARY owns the keyboard.</b> A declared action
 * arrives by its own id, and the module's job is to say which shortcut the backend knows it by -
 * the backend holds the key-to-action table and this specification does not change that
 * contract. What it replaces is a canvas building a keystroke to describe a gesture the library
 * already handed it, which is a different thing entirely: there the key was fiction.
 *
 * One helper rather than a literal in each module, so `{ key: "Delete", ctrl: false, shift:
 * false, alt: false, meta: false }` stops appearing in canvases - the shape the conformance
 * guard reads as a synthesised key event, because that is how it has always looked.
 */
export function contextShortcutOf(key: string): ContextShortcut {
  return { key, ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut;
}

/**
 * A keyboard event as a backend shortcut, or null for a key that carries no structural meaning
 * on this canvas. `aliases` resolves key-to-key conventions (e.g. Tab meaning Insert on a
 * mindmap) - a key-to-key mapping, never a key-to-action one.
 */
export function structuralShortcutFor(
  event: React.KeyboardEvent,
  structuralKeys: readonly string[],
  aliases?: Readonly<Record<string, string>>,
): ContextShortcut | null {
  if (!structuralKeys.includes(event.key)) {
    return null;
  }

  const key = aliases?.[event.key] ?? event.key;
  return { key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey, meta: event.metaKey } as ContextShortcut;
}
