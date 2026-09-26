import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type AnsibleModel } from "./ansibleModel";

/** Re-exported so the module's own files keep one name for it; the shape is the shared one. */
export type { Viewport };

export interface AnsibleStream {
  model: AnsibleModel;
  /** True until the first delta arrives, so the canvas can show it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at
   * this path any more (deleted, moved, unroutable). The reconnect loop has stopped.
   */
  failed: boolean;
  /** Tells the backend what the canvas can see, so a large project does not stream in full. */
  reportView: (viewport: Viewport) => void;
  /**
   * Records where the user dragged an element to. The backend dispatches the core
   * SetRegistrationLayoutCommand, so the position lands in the `.adp`'s layout: block - never
   * in a file Ansible owns - and the change is one undo away (Requirements 1.1, 2.1).
   * Resolves to the empty string on success, or the backend's own reason for refusing.
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
}

/**
 * Opens the diagram at `path` and folds its delta stream into an `AnsibleModel`. The
 * transport, lifecycle, retry and state live in the shared `useDiagramStream`; what is this
 * module's own here is the model, its mapping, and the viewport report built on the returned
 * client.
 *
 * It exposes exactly one write, `moveElementTo`, and deliberately no other: this type still
 * edits no Ansible file, and the one thing a user may author is where a node sits. The comment
 * here used to say the hook exposed no move call at all, on the reasoning that a read-only
 * type's client should not be able to ask - ansible-refinements gave the backend a position to
 * store, so the gesture now has an answer other than a refusal.
 */
export function useAnsibleStream(projectId: Uint8Array, path: readonly string[]): AnsibleStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, reportView, moveElementTo };
}
