import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { DiagramService } from "@client/generated/diagrams_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
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
 * Opens the dependency graph at `path` over `DiagramService.Open` and folds its delta stream
 * into a `DotNetDependencyGraphModel`, re-baselining on reconnect. The whole graph is delivered
 * at open; the view report tells the backend what the reader is looking at.
 */
export function useDotNetDependencyGraphStream(projectId: Uint8Array, path: readonly string[]): DotNetDependencyGraphStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<DotNetDependencyGraphModel>(emptyModel);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const clientRef = useRef(createClient(DiagramService, transport));

  const pathKey = path.join("/");

  const reportView = viewReportOf(clientRef.current, projectId, watchId, path);

  useEffect(() => {
    const client = clientRef.current;
    const controller = new AbortController();
    let active = true;
    setModel(emptyModel);
    setFailed(false);
    setLoading(true);

    void (async () => {
      while (active) {
        try {
          const stream = client.open(
            { projectId: { value: projectId }, watchId: { value: watchId }, path: { segments: [...path] } },
            { signal: controller.signal },
          );
          for await (const delta of stream) {
            if (!active) {
              return;
            }
            setLoading(false);
            setModel((current) => applyDelta(current, delta));
          }
        } catch (error) {
          if (!active) {
            return;
          }
          if (
            error instanceof ConnectError &&
            (error.code === Code.FailedPrecondition ||
              error.code === Code.NotFound ||
              error.code === Code.Unimplemented)
          ) {
            setFailed(true);
            setLoading(false);
            return;
          }
          await new Promise((resolve) => setTimeout(resolve, 500));
          setModel(emptyModel);
        }
      }
    })();

    return () => {
      active = false;
      controller.abort();
    };
    // path is compared by value through pathKey, not by array identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, watchId, pathKey]);

  const moveElementTo = async (elementId: string, x: number, y: number): Promise<string> => {
    try {
      const response = await clientRef.current.moveElement({
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
