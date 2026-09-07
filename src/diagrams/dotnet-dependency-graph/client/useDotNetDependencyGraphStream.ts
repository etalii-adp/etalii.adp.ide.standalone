import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type DotNetDependencyGraphModel } from "./dotnetDependencyGraphModel";

export interface DotNetDependencyGraphStream {
  model: DotNetDependencyGraphModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;
  /**
   * Stores a box's authored position. A drag is a layout edit and nothing more: the backend
   * dispatches the core SetRegistrationLayoutCommand, the position lands in the `.adp`'s
   * `layout:` block, never in a `.csproj`, `.sln` or `.slnx` - and the change is one undo away
   * (Requirement 6.6). It is the ONLY edit this type has: elements drag although the subject is
   * read-only, because arrangement is a view concern rather than an edit to the solution
   * (Requirement 6.5).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /**
   * Reports what the reader is looking at, so the backend answers with the elements that
   * newly fall inside it and removes the ones that left (view-delta-adoption Requirement 1.2).
   */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the dependency graph at `path` and folds its delta stream into a
 * `DotNetDependencyGraphModel`. Transport, lifecycle, retry and state are the shared
 * `useDiagramStream`'s; what is this module's own is the model, its mapping, and the two calls
 * built on the client that hook returns.
 *
 * <b>This hand-rolled its own open loop until now, and the loop had drifted.</b> Hand-rolling is
 * sanctioned - `useDiagramStream`'s own doc-comment says three modules do it and report
 * perfectly well - so the defect was never the duplication itself. It was what the duplicate
 * had lost: <b>on a clean stream end this re-opened immediately, with no delay</b>, where the
 * shared hook waits `RECONNECT_DELAY_MS`. That delay is hot-loop protection
 * (technical-debt-cleanup R3.4): a server that keeps closing the stream could otherwise have
 * this client re-opening it as fast as the event loop allows.
 *
 * Adopting rather than adding the delay, because a second copy that has already drifted once
 * will drift again, and nothing here needed to be different.
 */
export function useDotNetDependencyGraphStream(
  projectId: Uint8Array,
  path: readonly string[],
): DotNetDependencyGraphStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

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
    } catch (error) {
      return error instanceof Error ? error.message : "The move could not be sent.";
    }
  };

  return { model, loading, failed, moveElementTo, reportView };
}
