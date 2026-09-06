import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { ContextSourceSchema } from "../generated/context-contract_pb";
import { ContextSelectionSchema } from "../generated/context_pb";
import type { ContextSource } from "../generated/context-contract_pb";
import type { ContextSelection, ContextSelectionAction } from "../generated/context_pb";

/**
 * How a diagram canvas talks about its elements to the context channel.
 *
 * Every canvas reports the same nested selection - the diagram's `.adp` entry as the outer
 * level, the element inside it as a DIAGRAM_CANVAS child - and every canvas used to build it
 * with its own copy of the same twenty lines. This is the one copy. What an element *is*
 * stays each module's business; that a selection names one is not.
 */

/** The nested `file -> element` selection a canvas click reports. */
export function elementSelectionOf(
  entryId: Uint8Array,
  path: readonly string[],
  elementId: string,
  gesture?: ContextSelectionAction,
): ContextSelection {
  const child = create(ContextSelectionSchema, {
    source: 2, // DIAGRAM_CANVAS
    id: { source: { case: "elementId", value: { value: elementId } } },
    // Empty asks the backend to fill in the full path: the resolver derives the element's path
    // and echoes it back, and a canvas that guessed at it would be rejected for disagreeing.
    path: { segments: [] },
    detail: gesture === undefined
      ? { case: "none" as const, value: create(EmptySchema) }
      : { case: "action" as const, value: gesture },
  });

  return create(ContextSelectionSchema, {
    source: 1, // EXPLORER-origin file, selected on the canvas's behalf
    id: { source: { case: "entryId", value: { value: entryId } } },
    path: { segments: [...path] },
    detail: { case: "child", value: child },
  });
}

/** An element id as the `ContextSource` an action or shortcut executes against. */
export function elementSourceOf(elementId: string): ContextSource {
  return create(ContextSourceSchema, { source: { case: "elementId", value: { value: elementId } } });
}

/** The element id a pushed selection chain names, wherever in the chain it sits. */
export function selectedElementIdOf(selection: ContextSelection | null | undefined): string | undefined {
  let cursor: ContextSelection | undefined = selection ?? undefined;
  while (cursor) {
    if (cursor.id?.source.case === "elementId") {
      return cursor.id.source.value.value;
    }
    cursor = cursor.detail.case === "child" ? cursor.detail.value : undefined;
  }
  return undefined;
}

/** The element id inside an `element:{id}` selection key, or null for any other key. */
export function elementIdOfKey(key: string | null | undefined): string | null {
  return key?.startsWith("element:") ? key.slice("element:".length) : null;
}
