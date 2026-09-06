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
