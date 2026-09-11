import { useEffect, useRef } from "react";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction } from "@client/generated/context_pb";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { dispatchDiagramEvent, type DiagramEventHandlers, type DiagramSelection } from "./api/diagramEvents";
import type { DiagramContextIntegration } from "./DiagramCanvas";

/**
 * Which diagram a canvas draws, as the context channel names it: the `.adp` entry and its path.
 * The shell already hands every canvas these two values; given to `DiagramCanvas` as `source`,
 * they are all the library lacks to select on the backend itself.
 */
export interface CanvasSource {
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * What a pushed selection id means on THIS canvas, decided from the model alone.
 *
 * Every module wrote this rule by hand, and the four that worked agreed on it: the id is a
 * connection if it names one of the model's connections, an element if it names one of its
 * elements, and nothing otherwise. The library holds both lists, so it needs no module to say
 * which (centralized-selection, design A). An item whose type is declared unselectable resolves
 * to nothing, exactly as a press on it selects nothing (Requirement 2.3).
 *
 * Returns the library's set-shaped selection with at most one member: the context wire carries
 * one id today, and a set can later flow through here unchanged (Requirement 7.2).
 */
export function resolveSelection(id: string | null, model: DiagramModel, definition: DiagramDefinition): DiagramSelection {
  if (id === null) {
    return [];
  }

  const connection = model.connections.find((candidate) => candidate.id === id);
  if (connection !== undefined) {
    const relation = definition.relationTypes.find((type) => type.id === connection.type);
    return relation?.selectable === false ? [] : [{ kind: "connection", id }];
  }

  const element = model.elements.find((candidate) => candidate.id === id);
  if (element !== undefined) {
    const type = definition.elementTypes.find((candidate) => candidate.id === element.type);
    return type?.selectable === false ? [] : [{ kind: "element", id }];
  }

  return [];
}

/** What the library hands its own canvas in place of the three props a module used to wire. */
export interface LibrarySelection {
  selection: DiagramSelection;
  context: DiagramContextIntegration;
  events: DiagramEventHandlers;
}

/**
 * The selection glue sixteen canvases wrote by hand, written once (centralized-selection
 * Requirement 1): the pushed selection read and resolved, a gesture's selection pushed, and the
 * shared context menu wired to both.
 *
 * <b>Inbound</b> - the backend's selection, keyed by `innermostKey` as the channel keys it, is
 * resolved against the model by {@link resolveSelection}.
 *
 * <b>Outbound</b> - the canvas's `selection-changed`, which only a press raises (a drag never
 * selects), becomes one push: the single member as `elementSelectionOf`, or `null` for an empty
 * selection. A connection travels under its own id, never labelled as an element.
 *
 * <b>The menu</b> - the key, the pushed actions, the context-menu push and the action run are
 * built here. A refused action comes back as `action-refused`, for the module's rejection line.
 *
 * <b>A selection that vanishes clears.</b> When the item this canvas last resolved leaves the
 * model while the pushed selection still names it - an edit, a reload - `null` is pushed once
 * (Requirement 3.3). Only an item that WAS here: a pushed id this canvas never had is another
 * canvas's selection, or one whose element has not arrived yet, and clearing it would take the
 * selection away from whoever made it.
 */
export function useLibrarySelection(
  source: CanvasSource,
  model: DiagramModel,
  definition: DiagramDefinition,
  events: DiagramEventHandlers,
): LibrarySelection {
  const { select, executeAction } = useContextConnection();
  const { selection: pushed, actions } = useContextSelection();

  const selectionKey = innermostKey(pushed) ?? undefined;
  const selectedId = elementIdOfKey(selectionKey);
  const selection = resolveSelection(selectedId, model, definition);

  // Read through refs by the effect below, which must run on what is selected, not on the
  // identity of callbacks and handler maps a module recreates every render.
  const selectRef = useRef(select);
  selectRef.current = select;

  const presentRef = useRef<string | null>(null);
  const resolvedId = selection.length > 0 ? selection[0].id : null;
  useEffect(() => {
    if (resolvedId !== null) {
      presentRef.current = resolvedId;
      return;
    }

    const wasHere = presentRef.current;
    presentRef.current = null;
    if (wasHere !== null && wasHere === selectedId) {
      selectRef.current(null);
    }
  }, [resolvedId, selectedId]);

  const push = (id: string | null, gesture?: ContextSelectionAction) =>
    select(id === null ? null : elementSelectionOf(source.entryId, source.path, id, gesture));

  return {
    selection,
    context: {
      selectionKey,
      actions: actions ?? [],
      selectForMenu: (id) => push(id, ContextSelectionAction.CONTEXT_MENU),
      executeAction: async (actionId) => {
        const outcome = await executeAction(actionId, selectedId !== null ? elementSourceOf(selectedId) : undefined);
        if (!outcome.accepted && outcome.error) {
          dispatchDiagramEvent(events, { kind: "action-refused", actionId, message: outcome.error });
        }
      },
    },
    events: {
      ...events,
      onSelectionChanged: ({ selection: next }) => push(next.length > 0 ? next[0].id : null),
    },
  };
}
