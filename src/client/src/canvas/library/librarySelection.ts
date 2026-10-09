import { useEffect, useRef } from "react";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { contextShortcutOf } from "@client/canvas/interaction";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction } from "@client/generated/context_pb";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import { actionForMenuEntry, backendKeyOf, type ActionLookup } from "./definition/actions";
import type { DiagramModel } from "./api/diagramModel";
import { hasRow } from "./definition/compartments";
import {
  dispatchDiagramEvent,
  type ActionInvoked,
  type DiagramEventHandlers,
  type DiagramSelection,
  type LibraryEventHandlers,
} from "./api/diagramEvents";
import type { ContextActionGroup } from "@client/generated/context-contract_pb";

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

  // A row of a compartment: not an element of the model, but an entry of one, with an id of its
  // own. Looked for last, so an element or a connection that shares the id is what resolves.
  const owner = model.elements.find((candidate) => {
    const type = definition.elementTypes.find((entry) => entry.id === candidate.type);
    return hasRow(type?.compartments, { element: candidate, payload: candidate.payload }, id);
  });
  return owner === undefined ? [] : [{ kind: "row", id }];
}

/**
 * What a menu entry's declaration is asked against: the selected item's actions - the definition's
 * and, for an element, its type's - and the item itself, for a `when` or `enabled` to read. The
 * same lookup a declared shortcut gets.
 */
function menuLookup(selection: DiagramSelection, model: DiagramModel, definition: DiagramDefinition): ActionLookup {
  const item = selection[0];
  const element = item?.kind === "element" ? model.elements.find((candidate) => candidate.id === item.id) : undefined;
  const connection = item?.kind === "connection" ? model.connections.find((candidate) => candidate.id === item.id) : undefined;
  const type = element !== undefined ? definition.elementTypes.find((candidate) => candidate.id === element.type) : undefined;
  return {
    // A row declares no actions of its own and is no declared target: every entry of its menu is
    // the backend's, and runs as one.
    actions: item?.kind === "row" ? [] : [...(definition.actions ?? []), ...(type?.actions ?? [])],
    targetKind: item === undefined || item.kind === "row" ? "canvas" : item.kind,
    targetId: item?.id,
    typeId: element?.type ?? connection?.type,
    source: { element: element ?? { id: item?.id ?? "", type: connection?.type ?? "", x: 0, y: 0 }, payload: element?.payload },
  };
}

/**
 * The shared menu's wiring, as the library hands it to its own canvas: which selection the
 * backend holds, its pushed actions, the context-menu push and the action run. <b>Library-internal.</b>
 * Until centralized-selection task 21 a module built this by hand, as `DiagramContextIntegration`.
 */
export interface LibraryContextIntegration {
  /** `innermostKey(selection)` - which selection the backend currently holds. */
  selectionKey?: string;
  /** The pushed action groups for that selection - the menu renders these, never a guess. */
  actions: ContextActionGroup[];
  /** Push a selection for the menu gesture (the CONTEXT_MENU action). */
  selectForMenu: (id: string) => void;
  executeAction: (actionId: string) => void | Promise<unknown>;
}

/** What the library hands its own canvas: the selection, the menu's wiring, and the handlers. */
export interface LibrarySelection {
  selection: DiagramSelection;
  context: LibraryContextIntegration;
  events: LibraryEventHandlers;
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
 * built here. An entry the definition declares `invokedBy: [{ kind: "menu" }]` is the module's to
 * run: it is raised as `action-invoked` (see {@link actionForMenuEntry}), and it is sent to the
 * backend only if its declaration names a `backendKey` - which is a module choosing to send it,
 * so databricks' simulated runs, which declare none, still never reach a command. This used to say
 * "and never sent", which held until task 6 let a declaration carry its key. Every
 * other entry runs against the backend's CURRENT selection, with no source of its own:
 * the menu opens only once the pushed selection is the item it was opened on, and a source
 * naming that same item would say nothing the backend does not already hold. A refused action
 * comes back as `action-refused`, for the module's rejection line.
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
  const { select, executeAction, executeShortcut } = useContextConnection();
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

  /**
   * A declared action, raised to the module - and, where it declares a `backendKey`, sent to the
   * backend as that keystroke by the library itself (client-centralization Requirement 5).
   *
   * <b>Why the library sends it rather than handing the module a shortcut to send.</b> Requirement
   * 5.1 says the library derives the keystroke and no module builds a `ContextShortcut`; it does not
   * say who sends it, and a library that derived the shortcut and passed it along would satisfy the
   * letter while the module still awaited the call and still owned the refusal. Sending it here
   * follows the call beside it - `executeAction`, which this hook already makes itself for an
   * undeclared menu entry - and it is what lets a refused shortcut reach the library at all, which
   * task 3's single refusal surface depends on.
   *
   * <b>The module's own handler is still called</b>, so a module that observes an action loses
   * nothing; it only no longer builds the keystroke, which its guard forbids. The refusal comes back
   * as `action-refused`, the event a refused menu action already raises.
   */
  const invokeDeclared = (invoked: ActionInvoked) => {
    const key = backendKeyOf(definition, invoked.actionId);
    if (key !== undefined && invoked.targetId !== undefined) {
      void executeShortcut(contextShortcutOf(key), elementSourceOf(invoked.targetId, source.entryId)).then((outcome) => {
        if (!outcome.accepted && outcome.error) {
          dispatchDiagramEvent(events, { kind: "action-refused", actionId: invoked.actionId, message: outcome.error });
        }
      });
    }

    dispatchDiagramEvent(events, invoked);
  };

  return {
    selection,
    context: {
      selectionKey,
      actions: actions ?? [],
      selectForMenu: (id) => push(id, ContextSelectionAction.CONTEXT_MENU),
      executeAction: async (actionId) => {
        const declared = actionForMenuEntry(menuLookup(selection, model, definition), actionId);
        if (declared !== null) {
          invokeDeclared({ kind: "action-invoked", ...declared });
          return;
        }

        const outcome = await executeAction(actionId);
        if (!outcome.accepted && outcome.error) {
          dispatchDiagramEvent(events, { kind: "action-refused", actionId, message: outcome.error });
        }
      },
    },
    events: {
      ...events,
      onSelectionChanged: ({ selection: next }) => push(next.length > 0 ? next[0].id : null),
      // The canvas raises every declared action through here, so this is the one place a declared
      // backend key is sent - a key, a gesture and a menu entry alike.
      onActionInvoked: invokeDeclared,
    },
  };
}
