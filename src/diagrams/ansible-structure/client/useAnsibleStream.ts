import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { applyDelta, emptyModel, type AnsibleModel } from "./ansibleModel";

/** A viewport the client reports; the backend answers with what falls inside it. */
export interface Viewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

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
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = (viewport: Viewport) => {
    void client
      .updateView({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        view: {
          center: { x: (viewport.minX + viewport.maxX) / 2, y: (viewport.minY + viewport.maxY) / 2 },
          boundingBox: { min: { x: viewport.minX, y: viewport.minY }, max: { x: viewport.maxX, y: viewport.maxY } },
        },
      })
      .catch(() => {
        // A view report is advisory; if it fails the backend keeps the last one it had.
      });
  };

  const moveElementTo = async (elementId: string, x: number, y: number): Promise<string> => {
    try {
      const response = await client.moveElement({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        elementId,
        // The position is what makes this an arrangement rather than a re-parenting; the
        // backend routes on its presence.
        position: { x, y },
      });
      return response.error;
    } catch {
      return "The position could not be saved.";
    }
  };

  return { model, loading, failed, reportView, moveElementTo };
}
