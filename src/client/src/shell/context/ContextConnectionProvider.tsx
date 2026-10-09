import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createClient } from "@connectrpc/connect";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { base64Encode } from "@bufbuild/protobuf/wire";
import { useAuth } from "../../auth/AuthContext";
import { reportedCall, useCanvasRefusalReporter } from "../../canvas/library/surface/canvasRefusals";
import { ContextSourceSchema } from "../../generated/context-contract_pb";
import { ContextService, ContextSelectionSchema } from "../../generated/context_pb";
import type { Delta } from "../../generated/deltas_pb";
import type { OpenDiagramRequest } from "../../generated/diagrams_pb";
import type { HierarchyMessage } from "../../generated/hierarchy_pb";
import { WorkspaceService } from "../../generated/workspace_pb";
import type { ContextActionGroup, ContextLevelDetail, ContextShortcut, ContextSource } from "../../generated/context-contract_pb";
import type { ContextPrompt, ContextSelection, ContextSelectionChanged, ContextProperty } from "../../generated/context_pb";
import type { ProjectProblems } from "../../generated/problems_pb";
import type { ContextPromptSubmission, ContextPromptVerdict } from "./ContextPromptHost";
import { useCoalescedSelect } from "./useCoalescedSelect";
import { requestTextTab } from "../panels/textTabRequests";
import { onLocalNotice } from "./localNotices";
import { WorkspaceStreams } from "./workspaceStreams";

/** The `none` alternative: a plain selection, nothing more. */
export const NONE_DETAIL: ContextSelection["detail"] = { case: "none", value: create(EmptySchema) };

/**
 * Names the project itself as an action's target. Undo and redo belong to the project rather
 * than to what is selected, so the History group and the global shortcuts pass this as the
 * source (diagram-undo-redo Requirement 5.1). Empty, not a project id: the call already carries
 * the project it is scoped to.
 */
export const PROJECT_SOURCE: ContextSource = create(ContextSourceSchema, {
  source: { case: "project", value: create(EmptySchema) },
});

/**
 * Names the errors-and-warnings panel itself as a selection or an action's target. What the
 * panel selects on focus - which is what routes Validate all into the ribbon - and what its
 * shortcut passes as the source (errors-and-warnings-panel Requirement 7.4). Empty for the
 * same reason as {@link PROJECT_SOURCE}: the call already carries the project it is scoped to.
 */
export const PROBLEMS_SOURCE: ContextSource = create(ContextSourceSchema, {
  source: { case: "problems", value: create(EmptySchema) },
});

/** How long a burst of plain selections may keep coalescing before the last one is sent. */
export const SELECT_COALESCE_MS = 80;

const RECONNECT_INITIAL_MS = 500;
const RECONNECT_MAX_MS = 5000;

/**
 * How this channel reports a transport fault. It is one rule, not a choice made per method:
 *
 * **A method that returns a value its caller acts on resolves with the failure expressed in
 * that value. A method that returns `void` is advisory: it swallows its fault and says so in
 * a comment.**
 *
 * `select` and `onCancel` are the advisory pair and were always written this way; the rule is
 * not new, it was simply never written down, so every method added since had to guess. What a
 * value-returning method must not do is reject, because its callers are React event handlers
 * and effects: a rejection there is an unhandled rejection, which shows the user nothing while
 * leaving the component's own state saying the write succeeded.
 *
 * `channelResolvesRatherThanRejects.test.tsx` iterates the value-returning methods and holds
 * this true for whichever one is added next.
 */

/**
 * What to show for a transport fault. A `ConnectError` carries a message worth reading;
 * anything else would render as "[object Object]", which reads as a broken dialog rather than
 * as a connection that dropped.
 */
function faultMessage(caught: unknown): string {
  return caught instanceof Error && caught.message.length > 0 ? caught.message : "The connection to the project was lost.";
}

export interface ActionOutcome {
  accepted: boolean;
  error: string;
}

/**
 * The properties of what is selected, and whether they could be read at all.
 *
 * The array alone cannot say which happened: empty means both "this selection has no
 * properties" and "the describe never arrived", and those are exactly the two states the grid
 * has to show differently. An empty `properties` with an empty `error` stays the ordinary
 * no-properties case.
 */
export interface PropertyDescription {
  properties: ContextProperty[];
  error: string;
}

/** What a producer of selections needs: stable across selection pushes, so `select()` callers never re-render for them. */
export interface ContextConnectionValue {
  /** Generated here once per mounted shell; the explorer uses it for ListEntries too. */
  watchId: Uint8Array;
  /** A plain selection (`none`) is coalesced; any action, or `null` to clear, goes at once. */
  select: (selection: ContextSelection | null) => void;
  executeAction: (actionId: string, source?: ContextSource) => Promise<ActionOutcome>;
  /** Forgets a pending reveal, once whoever shows the hierarchy has acted on it. */
  clearReveal: () => void;
  /**
   * Asks whoever shows the hierarchy to reveal a project-relative path - what activating a
   * problem does (errors-and-warnings-panel Requirement 7.7). The same mechanism a created
   * file uses, so the explorer expands, focuses and activates it the one way it knows.
   */
  revealPath: (segments: string[]) => void;
  executeShortcut: (shortcut: ContextShortcut, source?: ContextSource) => Promise<ActionOutcome>;
  /**
   * The properties of what is selected, described by whichever module owns it. Asked for
   * rather than pushed: the property grid is the only thing that wants them, and asking keeps
   * them off the selection stream every other panel reads.
   */
  describeProperties: (source?: ContextSource) => Promise<PropertyDescription>;
  /** Writes one property, through a command on the backend. Rejected values come back as an error to show. */
  setProperty: (propertyId: string, value: string, source?: ContextSource) => Promise<ActionOutcome>;
}

/** What a consumer of selections reads: updated on every push. */
export interface ContextSelectionValue {
  /** The current (non-transient) chain, or null when nothing is selected. */
  selection: ContextSelection | null;
  /** Outermost first; one per level of `selection`. */
  levels: ContextLevelDetail[];
  /** The innermost level's actions. */
  actions: ContextActionGroup[];
  /** The last PREVIEW, cleared by the next message of any kind. */
  preview: ContextSelectionChanged | null;
  /**
   * Project-relative segments of something just created on this connection, waiting to be
   * revealed in the hierarchy. Set by a submission that created something; cleared by
   * whoever reveals it.
   */
  pendingReveal: string[] | null;
  connected: boolean;
}

/**
 * The feeds that ride the tab's one stream (two-tab-connection-wedge Requirement 3.1), for the
 * two consumers that used to open streams of their own. Kept off {@link ContextConnectionValue}:
 * these hand out streams, which end and reject by design, not calls that resolve.
 */
export interface WorkspaceStreamsValue {
  /**
   * One diagram's deltas - the baseline, then every change - shaped like the `DiagramService.Open`
   * call it replaced: it ends when the connection drops, and rejects with the backend's own
   * refusal when the diagram cannot be opened here.
   */
  openDiagramStream: (
    request: Pick<OpenDiagramRequest, "path" | "editorId">,
    options: { signal: AbortSignal },
  ) => AsyncIterable<Delta>;
  /** The hierarchy's changes; fails when the connection drops, as the explorer's own stream did. */
  watchHierarchy: (options: { signal: AbortSignal }) => AsyncIterable<HierarchyMessage>;
}

export interface ContextPromptValue {
  prompt: ContextPrompt | null;
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

const ConnectionContext = createContext<ContextConnectionValue | undefined>(undefined);
const SelectionContext = createContext<ContextSelectionValue | undefined>(undefined);
const PromptContext = createContext<ContextPromptValue | undefined>(undefined);
const WorkspaceStreamsContext = createContext<WorkspaceStreamsValue | undefined>(undefined);
// The project's own actions, kept apart from the selection so a project-actions push never
// re-renders a selection consumer (diagram-undo-redo Deviation 1). Empty until the first push.
const ProjectActionsContext = createContext<ContextActionGroup[]>([]);
// The project's problems, apart from the selection for the same reason: a validation
// finishing must re-render the panel, not every selection consumer (errors-and-warnings-panel
// Requirement 1.1). Null until the baseline arrives - the panel shows "not checked yet"
// through the message's own NEVER_VALIDATED state, not through this null.
const ProblemsContext = createContext<ProjectProblems | null>(null);

/** One thing worth telling the user, alongside an edit that succeeded. */
export interface ContextNotice {
  /** Distinguishes two notices carrying the same sentence. */
  readonly id: number;
  readonly text: string;
  /**
   * Text the notice offers to put on the clipboard - a location the page could not open, so the
   * reader can take it somewhere that can. Absent, the notice offers nothing but its dismissal.
   */
  readonly copy?: string;
}

let noticeSequence = 0;
const nextNoticeId = (): number => (noticeSequence += 1);

interface NoticeState {
  readonly notices: readonly ContextNotice[];
  readonly dismiss: (id: number) => void;
}

const NoticesContext = createContext<NoticeState>({ notices: [], dismiss: () => {} });

const EMPTY_SELECTION: ContextSelectionValue = {
  selection: null,
  levels: [],
  actions: [],
  preview: null,
  pendingReveal: null,
  connected: false,
};

/**
 * The id of a chain's innermost level, as a comparable key, or undefined for no selection.
 * An entry keys as its id's base64; a diagram element keys as `element:` plus its id, so a
 * node selection is a selection to every consumer - the ribbon above all, which would
 * otherwise treat a selected node as nothing selected and never show its actions.
 */
export function innermostKey(selection: ContextSelection | null | undefined): string | undefined {
  let cursor = selection ?? undefined;
  while (cursor?.detail.case === "child") {
    cursor = cursor.detail.value;
  }
  const id = cursor?.id?.source;
  if (id?.case === "entryId") {
    return base64Encode(id.value.value);
  }
  if (id?.case === "problems") {
    // The errors-and-warnings panel selecting itself is a selection like any other - above
    // all to the ribbon, which shows the selection's actions only for a keyed selection.
    return "problems";
  }
  return id?.case === "elementId" ? `element:${id.value.value}` : undefined;
}

/** Whether the chain's innermost level carries this gesture. */
export function innermostAction(selection: ContextSelection | null | undefined): number | undefined {
  let cursor = selection ?? undefined;
  while (cursor?.detail.case === "child") {
    cursor = cursor.detail.value;
  }
  return cursor?.detail.case === "action" ? cursor.detail.value : undefined;
}

/** The innermost level's entry path, or undefined when the selection is not a file entry. */
function innermostEntryPath(selection: ContextSelection | null): string[] | undefined {
  let cursor = selection ?? undefined;
  while (cursor?.detail.case === "child") {
    cursor = cursor.detail.value;
  }
  if (cursor?.id?.source.case !== "entryId") {
    return undefined;
  }
  const segments = cursor.path?.segments;
  return segments !== undefined && segments.length > 0 ? [...segments] : undefined;
}

export interface ContextConnectionProviderProps {
  projectId: Uint8Array;
  children: ReactNode;
}

/**
 * The one place the client talks to ContextService: owns the connection's watch_id and
 * its single Watch stream, coalesces plain selections, and fans the pushed context out
 * to every panel through hooks - one gRPC stream per connection, never one per consumer.
 *
 * That stream is `WorkspaceService.Watch`, the only server stream a tab opens: it carries the
 * context, the hierarchy's changes and every open diagram's deltas, which used to be three
 * streams and three of a browser's six HTTP/1.1 connections per origin (two-tab-connection-wedge
 * Requirement 3.1). The hierarchy and diagram feeds are handed out through
 * {@link useWorkspaceStreams}. Anything new the backend has to push rides this stream too.
 */
export function ContextConnectionProvider({ projectId, children }: ContextConnectionProviderProps) {
  const { transport } = useAuth();
  const client = useMemo(() => createClient(ContextService, transport), [transport]);
  const workspaceClient = useMemo(() => createClient(WorkspaceService, transport), [transport]);
  const streamsRef = useRef<WorkspaceStreams>(new WorkspaceStreams());
  const watchIdRef = useRef<Uint8Array>(crypto.getRandomValues(new Uint8Array(16)));
  const lastSentRef = useRef<ContextSelection | null>(null);

  const [selectionValue, setSelectionValue] = useState<ContextSelectionValue>(EMPTY_SELECTION);
  const [prompt, setPrompt] = useState<ContextPrompt | null>(null);
  // The current selection, readable from inside stable callbacks: what the editor gestures
  // below resolve their file path from, since the wire's responses carry none.
  const selectionRef = useRef<ContextSelection | null>(null);
  selectionRef.current = selectionValue.selection;
  // Set while an "Open with…" dialog is in flight: the file it was asked for, so the chosen
  // editor and the file meet again at submit time (modular-text-editors R4.4).
  const pendingOpenWithRef = useRef<string[] | null>(null);
  const [projectActions, setProjectActions] = useState<ContextActionGroup[]>([]);
  const [problems, setProblems] = useState<ProjectProblems | null>(null);
  // Notices are a queue rather than one value: two edits can each lose something, and the
  // second must not silently replace the first before anyone has read it.
  const [notices, setNotices] = useState<ContextNotice[]>([]);

  const setPendingReveal = useCallback(
    (segments: string[] | null) => setSelectionValue((previous) => ({ ...previous, pendingReveal: segments })),
    [],
  );

  const sendSelect = useCallback(
    (selection: ContextSelection | null) => {
      lastSentRef.current = selection;
      client
        .select({ projectId: { value: projectId }, watchId: { value: watchIdRef.current }, selection: selection ?? undefined })
        .catch(() => {
          // A rejected or failed select leaves the backend's current selection as it was;
          // the next focus move simply tries again. Nothing to show the user.
        });
    },
    [client, projectId],
  );
  const coalescedSelect = useCoalescedSelect(sendSelect, SELECT_COALESCE_MS);

  const select = useCallback(
    (selection: ContextSelection | null) => {
      // Only a plain selection may wait: a gesture or a clear flushes whatever was pending.
      coalescedSelect(selection, selection === null || selection.detail.case !== "none");
    },
    [coalescedSelect],
  );

  useEffect(() => {
    const abortController = new AbortController();
    let reconnectDelay = RECONNECT_INITIAL_MS;

    const run = async () => {
      while (!abortController.signal.aborted) {
        try {
          const stream = workspaceClient.watch(
            { projectId: { value: projectId }, watchId: { value: watchIdRef.current } },
            { signal: abortController.signal },
          );
          let first = true;
          for await (const workspaceMessage of stream) {
            if (workspaceMessage.message.case === "hierarchy") {
              streamsRef.current.hierarchy(workspaceMessage.message.value);
              continue;
            }
            if (workspaceMessage.message.case === "diagram") {
              streamsRef.current.diagram(workspaceMessage.message.value);
              continue;
            }
            if (workspaceMessage.message.case !== "context") {
              continue;
            }
            const message = workspaceMessage.message.value;
            if (first) {
              first = false;
              reconnectDelay = RECONNECT_INITIAL_MS;
              // The context's baseline is written once the backend has registered the connection,
              // so from here a diagram stream can be opened on it.
              streamsRef.current.connect();
              // The baseline is the backend's truth; if the backend forgot us (idle
              // eviction after a drop) but we still hold a selection, put it back.
              if (message.message.case === "selection" && !message.message.value.selection && lastSentRef.current) {
                sendSelect(lastSentRef.current);
              }
            }
            if (message.message.case === "prompt") {
              setPrompt(message.message.value);
              continue;
            }
            if (message.message.case === "projectActions") {
              // Its own state, so undo/redo availability updates the History group and the
              // shortcuts without disturbing the selection (diagram-undo-redo Deviation 1).
              setProjectActions(message.message.value.actions);
              continue;
            }
            if (message.message.case === "notice") {
              // Appended with an id of its own, because the same sentence can arrive twice -
              // two drags that both failed to record - and both are worth showing.
              const text = message.message.value.message;
              setNotices((previous) => [...previous, { id: nextNoticeId(), text }]);
              continue;
            }
            if (message.message.case === "problems") {
              // Exactly what the backend sent - no client-side filtering, sorting or
              // counting here; the panel derives its view from this one message.
              setProblems(message.message.value);
              continue;
            }
            if (message.message.case !== "selection") {
              continue;
            }
            const changed = message.message.value;
            setSelectionValue((previous) =>
              changed.transient
                ? { ...previous, preview: changed, connected: true }
                : {
                    selection: changed.selection ?? null,
                    levels: changed.levels,
                    actions: changed.actions,
                    preview: null,
                    // A reveal outlives the selection pushes that arrive while the new entry
                    // is still on its way through the watcher.
                    pendingReveal: previous.pendingReveal,
                    connected: true,
                  },
            );
          }
        } catch {
          // Fall through to the reconnect below unless we were told to stop.
        }
        // An aborted loop belongs to a mount that is gone, and its stream rejects after the next
        // mount's consumers have already subscribed - StrictMode's development remount does exactly
        // that. Disconnecting here would fail their live feeds on a connection that never dropped.
        if (abortController.signal.aborted) {
          return;
        }
        streamsRef.current.disconnect();
        setSelectionValue((previous) => ({ ...previous, connected: false }));
        await new Promise((resolve) => setTimeout(resolve, reconnectDelay));
        reconnectDelay = Math.min(reconnectDelay * 2, RECONNECT_MAX_MS);
      }
    };
    void run();

    return () => abortController.abort();
    // One stream per mounted provider (Requirement 4.5); the project cannot change underneath it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const describeProperties = useCallback(
    async (source?: ContextSource): Promise<PropertyDescription> => {
      try {
        const response = await client.describeProperties({
          projectId: { value: projectId },
          watchId: { value: watchIdRef.current },
          source,
        });
        return { properties: response.properties, error: "" };
      } catch (caught) {
        return { properties: [], error: faultMessage(caught) };
      }
    },
    [client, projectId],
  );

  const setProperty = useCallback(
    async (propertyId: string, value: string, source?: ContextSource): Promise<ActionOutcome> => {
      try {
        const response = await client.setProperty({
          projectId: { value: projectId },
          watchId: { value: watchIdRef.current },
          source,
          propertyId,
          value,
        });
        return { accepted: response.accepted, error: response.error };
      } catch (caught) {
        return { accepted: false, error: faultMessage(caught) };
      }
    },
    [client, projectId],
  );

  const execute = useCallback(
    async (
      trigger: { case: "actionId"; value: string } | { case: "shortcut"; value: ContextShortcut },
      source?: ContextSource,
    ): Promise<ActionOutcome> => {
      // Only the call is guarded. What follows opens a workspace tab, and a fault there is a
      // defect in this client rather than a connection that dropped - reporting it as one would
      // send the reader looking down the wire for a bug that is here.
      let response: Awaited<ReturnType<typeof client.executeAction>>;
      try {
        response = await client.executeAction({
          projectId: { value: projectId },
          watchId: { value: watchIdRef.current },
          source,
          interactionId: { value: crypto.getRandomValues(new Uint8Array(16)) },
          trigger,
        });
      } catch (caught) {
        return { accepted: false, error: faultMessage(caught) };
      }

      // The editor family's open gestures end client-side: workspace tabs are per-connection
      // client state, so the backend's Completed answer means "your request stands" and the
      // tab - whose stream then forces the editor resolution - is opened here (R5.2, R4.4).
      if (trigger.case === "actionId" && response.accepted) {
        const entryPath = innermostEntryPath(selectionRef.current);
        if (trigger.value === "editor.open-as-text" && entryPath !== undefined) {
          requestTextTab({ path: entryPath, editorId: "*" });
        }
        if (trigger.value === "editor.open-with") {
          pendingOpenWithRef.current = entryPath ?? null;
        }
      }

      return { accepted: response.accepted, error: response.error };
    },
    [client, projectId],
  );

  const connectionValue = useMemo<ContextConnectionValue>(
    () => ({
      watchId: watchIdRef.current,
      select,
      executeAction: (actionId, source) => execute({ case: "actionId", value: actionId }, source),
      clearReveal: () => setPendingReveal(null),
      revealPath: (segments) => setPendingReveal(segments),
      executeShortcut: (shortcut, source) => execute({ case: "shortcut", value: shortcut }, source),
      describeProperties,
      setProperty,
    }),
    [select, execute, setPendingReveal, describeProperties, setProperty],
  );

  const streamsValue = useMemo<WorkspaceStreamsValue>(
    () => ({
      openDiagramStream: (request, { signal }) =>
        streamsRef.current.openDiagram(
          {
            open: (streamId) =>
              workspaceClient.openDiagram({
                streamId: { value: streamId },
                request: {
                  projectId: { value: projectId },
                  watchId: { value: watchIdRef.current },
                  path: request.path,
                  editorId: request.editorId,
                },
              }),
            close: (streamId) =>
              workspaceClient.closeDiagram({ watchId: { value: watchIdRef.current }, streamId: { value: streamId } }),
          },
          signal,
        ),
      watchHierarchy: ({ signal }) => streamsRef.current.watchHierarchy(signal),
    }),
    [workspaceClient, projectId],
  );

  const promptInteractionId = prompt?.interactionId?.value;

  const onPropose = useCallback(
    async (revision: number, value: string) => {
      try {
        const response = await client.proposeInput({
          interactionId: promptInteractionId ? { value: promptInteractionId } : undefined,
          revision,
          value,
        });
        return { revision: response.revision, valid: response.valid, reason: response.reason };
      } catch (caught) {
        // Carrying the revision back matters as much as the reason: a dialog compares it
        // against what is in the box, so a failure for text the user has since edited is
        // recognisably stale rather than a complaint about what they are typing now.
        return { revision, valid: false, reason: faultMessage(caught) };
      }
    },
    [client, promptInteractionId],
  );

  const onSubmit = useCallback(
    async (value: string, text?: string) => {
      // As in `execute`: the call is guarded, and what it sets in motion afterwards is not.
      let response: Awaited<ReturnType<typeof client.submitInteraction>>;
      try {
        response = await client.submitInteraction({
          interactionId: promptInteractionId ? { value: promptInteractionId } : undefined,
          value,
          text,
        });
      } catch (caught) {
        // The dialog stays open with what the user typed intact, which is what its own
        // not-completed path already does for a value the backend refused.
        return { completed: false, error: faultMessage(caught) };
      }
      if (response.completed) {
        setPrompt(null);

        // An "Open with…" choice was just confirmed: the submitted value is the chosen
        // editor's id, and the file was remembered when the dialog was asked for (R4.4).
        const openWithPath = pendingOpenWithRef.current;
        if (openWithPath !== null) {
          pendingOpenWithRef.current = null;
          requestTextTab({ path: openWithPath, editorId: value });
        }
      }
      // Something was created: hand its path to whoever shows the hierarchy, so the entry
      // can be revealed once the watcher announces it. The backend never selects it for us -
      // it has no id for a file its watcher has not seen yet.
      const createdPath = response.createdPath?.segments;
      if (response.completed && createdPath !== undefined && createdPath.length > 0) {
        setPendingReveal(createdPath);
      }

      return { completed: response.completed, error: response.error, createdPath };
    },
    [client, promptInteractionId],
  );

  const onCancel = useCallback(() => {
    pendingOpenWithRef.current = null;
    setPrompt(null);
    void client.cancelInteraction({ interactionId: promptInteractionId ? { value: promptInteractionId } : undefined }).catch(() => {
      // The dialog is already gone client-side; the interaction also dies with the
      // connection, so a failed cancel leaves nothing stranded that matters.
    });
  }, [client, promptInteractionId]);

  // A notice the PAGE observed rather than one the backend sent - a request that has not come
  // back within its bound (two-tab-connection-wedge Requirement 5.1). Same surface and same
  // dismissal as a backend notice, because to a reader it is the same kind of thing: something
  // that happened which they need to know and can then put away.
  useEffect(
    () => onLocalNotice((text, copy) => setNotices((previous) => [...previous, { id: nextNoticeId(), text, copy }])),
    [],
  );

  const dismissNotice = useCallback((id: number) => {
    setNotices((previous) => previous.filter((notice) => notice.id !== id));
  }, []);
  const noticesValue = useMemo<NoticeState>(() => ({ notices, dismiss: dismissNotice }), [notices, dismissNotice]);

  const promptValue = useMemo<ContextPromptValue>(
    () => ({ prompt, onPropose, onSubmit, onCancel }),
    [prompt, onPropose, onSubmit, onCancel],
  );

  return (
    <ConnectionContext.Provider value={connectionValue}>
      <WorkspaceStreamsContext.Provider value={streamsValue}>
      <ProjectActionsContext.Provider value={projectActions}>
        <ProblemsContext.Provider value={problems}>
        <NoticesContext.Provider value={noticesValue}>
          <SelectionContext.Provider value={selectionValue}>
            <PromptContext.Provider value={promptValue}>{children}</PromptContext.Provider>
          </SelectionContext.Provider>
        </NoticesContext.Provider>
        </ProblemsContext.Provider>
      </ProjectActionsContext.Provider>
      </WorkspaceStreamsContext.Provider>
    </ConnectionContext.Provider>
  );
}

/** The feeds that ride the tab's one stream: a diagram's deltas and the hierarchy's changes. */
export function useWorkspaceStreams(): WorkspaceStreamsValue {
  const value = useContext(WorkspaceStreamsContext);
  if (!value) {
    throw new Error("useWorkspaceStreams must be used within a ContextConnectionProvider.");
  }
  return value;
}

/**
 * The workspace's context connection. <b>Inside a canvas</b> its three gesture calls -
 * `executeAction`, `executeShortcut` and `setProperty` - also report to that canvas's refusal line,
 * so a refusal is shown by the library whoever sent the call (client-centralization Requirement 2).
 * Outside one, and for every other member, the value is the provider's own, unchanged.
 */
export function useContextConnection(): ContextConnectionValue {
  const value = useContext(ConnectionContext);
  if (!value) {
    throw new Error("useContextConnection must be used within a ContextConnectionProvider.");
  }
  const reporter = useCanvasRefusalReporter();
  return useMemo(
    () =>
      reporter === null
        ? value
        : {
            ...value,
            executeAction: (actionId, source) => reportedCall(reporter, () => value.executeAction(actionId, source)),
            executeShortcut: (shortcut, source) => reportedCall(reporter, () => value.executeShortcut(shortcut, source)),
            setProperty: (propertyId, newValue, source) =>
              reportedCall(reporter, () => value.setProperty(propertyId, newValue, source)),
          },
    [value, reporter],
  );
}

export function useContextSelection(): ContextSelectionValue {
  const value = useContext(SelectionContext);
  if (!value) {
    throw new Error("useContextSelection must be used within a ContextConnectionProvider.");
  }
  return value;
}

export function useContextPrompt(): ContextPromptValue {
  const value = useContext(PromptContext);
  if (!value) {
    throw new Error("useContextPrompt must be used within a ContextConnectionProvider.");
  }
  return value;
}

/**
 * The project's own actions - undo and redo - as last pushed by the backend. Separate from
 * the selection, so reading it never couples a consumer to selection changes. Empty until the
 * first push arrives.
 */
export function useProjectActions(): ContextActionGroup[] {
  return useContext(ProjectActionsContext);
}

/**
 * The project's problems, as last pushed by the backend - one list for every viewer of the
 * project. Null until the baseline arrives; after that, exactly what the backend sent,
 * including the whole-set counts and the NEVER_VALIDATED/VALIDATING/VALIDATED state the
 * panel words its empty states from.
 */
export function useContextProblems(): ProjectProblems | null {
  return useContext(ProblemsContext);
}

/**
 * Things that happened alongside edits that SUCCEEDED, oldest first, with the means to
 * dismiss one. Empty most of the time.
 */
export function useContextNotices(): NoticeState {
  return useContext(NoticesContext);
}

/** Builds a one-level selection message; the explorer's helper for its own entries. */
export function selectionFor(
  source: number,
  entryId: Uint8Array,
  path: string[],
  detail: ContextSelection["detail"],
): ContextSelection {
  return create(ContextSelectionSchema, {
    source,
    path: { segments: path },
    id: { source: { case: "entryId", value: { value: entryId } } },
    detail,
  });
}
