import { expect } from "vitest";
import { fireEvent } from "@testing-library/react";

/**
 * How a module's own canvas test lets the shared assertion drive it.
 *
 * Every module fakes the context channel its own way - a key string here, a real selection chain
 * there - so the assertion does not assume one. The module says how to mount its REAL canvas with
 * a given selection pushed, and how to read back what the canvas pushed; the assertion does the
 * rest. That keeps each check inside the harness that already knows the module's stream protocol,
 * rather than one test faking sixteen of them (centralized-selection, design: *the two guards*).
 */
export interface LibrarySelectionHarness {
  /**
   * Mounts the module's real canvas and real model with the backend's selection naming this id -
   * or nothing, for `null` - and returns what it rendered into.
   */
  mountWith: (selectedId: string | null) => { container: HTMLElement; unmount: () => void };
  /**
   * The id every selection pushed so far names, oldest first, with `null` for a clear. The
   * assertion reads it after its own press, so a harness may keep one running list.
   */
  pushedIds: () => readonly (string | null)[];
  /** An element in the model, of a selectable type. */
  element: string;
  /**
   * A connection in the model, of a selectable relation type. Omitted only where the notation
   * selects no connections at all, which its definition then declares (Requirement 2.4).
   */
  connection?: string;
}

/**
 * The first element whose attribute equals the value exactly. Matched by value rather than by a
 * selector, as the library does: jsdom offers no `CSS.escape`, and an id is module data - `a->b`,
 * a URI, a path - that no selector grammar should have to survive.
 */
function byAttribute(container: HTMLElement, attribute: string, value: string): Element | null {
  return [...container.querySelectorAll(`[${attribute}]`)].find((candidate) => candidate.getAttribute(attribute) === value) ?? null;
}

/**
 * The selection every canvas must have, asserted against one module's real canvas
 * (centralized-selection Requirement 9.2):
 *
 * - a pushed element selection highlights that element;
 * - a pushed connection selection highlights that connection, wherever the relation selects;
 * - a press on the background clears the selection - it pushes `null`.
 *
 * Each module's canvas test calls this once, with its own harness. The text guard fails a
 * registered module whose canvas test does not, which is what makes "every canvas" true.
 */
export function expectLibrarySelection(harness: LibrarySelectionHarness): void {
  {
    const { container, unmount } = harness.mountWith(harness.element);
    const element = byAttribute(container, "data-element-id", harness.element);
    expect(element, `element "${harness.element}" is not on the canvas, so nothing about its selection can be said`).not.toBeNull();
    expect(element!.classList.contains("canvas-selected"), `a pushed selection naming element "${harness.element}" does not highlight it`).toBe(true);
    unmount();
  }

  if (harness.connection !== undefined) {
    const { container, unmount } = harness.mountWith(harness.connection);
    const connection = byAttribute(container, "data-connection-id", harness.connection);
    expect(connection, `connection "${harness.connection}" is not on the canvas, so nothing about its selection can be said`).not.toBeNull();
    expect(connection!.classList.contains("canvas-selected"), `a pushed selection naming connection "${harness.connection}" does not highlight it`).toBe(true);
    unmount();
  }

  {
    const { container, unmount } = harness.mountWith(harness.element);
    const surface = container.querySelector("svg.library-canvas-surface");
    expect(surface, "the canvas has no library surface to press, so it does not render through the library").not.toBeNull();
    const before = harness.pushedIds().length;
    fireEvent(surface!, new MouseEvent("pointerdown", { bubbles: true, cancelable: true, button: 0 }));
    fireEvent(surface!, new MouseEvent("pointerup", { bubbles: true, cancelable: true }));
    expect(harness.pushedIds().slice(before), "a press on the background does not clear the selection").toContain(null);
    unmount();
  }
}
