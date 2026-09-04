import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { DiagramService } from "@client/generated/diagrams_pb";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { applySkosDelta, emptySkosModel, type SkosModel } from "./skosModel";

export interface SkosStream {
  model: SkosModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;

  /** Tells the backend what this canvas can see, so a large document arrives as it is looked at. */
  reportView: (viewport: Viewport) => void;
  /**
   * Stores an element's authored position. A drag is a layout edit: the backend dispatches the
   * core SetRegistrationLayoutCommand, the position lands in the `.adp`'s layout: block - never
   * in the SKOS file - and the change is one undo away (skos-diagram Requirement 4.3).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
}

/**
 * Opens the scheme diagram at `path` and folds its delta stream into a `SkosModel`,
 * re-baselining on reconnect - the family's own stream shape, over this reading's payloads.
 */
export function useSkosStream(projectId: Uint8Array, path: readonly string[]): SkosStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<SkosModel>(emptySkosModel);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const clientRef = useRef(createClient(DiagramService, transport));

  const pathKey = path.join("/");

  useEffect(() => {
    const client = clientRef.current;
    const controller = new AbortController();
    let active = true;
    setModel(emptySkosModel);
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
            setModel((current) => applySkosDelta(current, delta));
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
          setModel(emptySkosModel);
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
        position: { x, y },
      });
      return response.error;
    } catch (error) {
      return error instanceof Error ? error.message : "The move could not be sent.";
    }
  };

  // Built on the client this hook already created: adopting the shared report does not mean
  // adopting useDiagramStream, which this reading deliberately does not use (Requirement 3.6).
  const reportView = viewReportOf(clientRef.current, projectId, watchId, path);

  return { model, loading, failed, reportView, moveElementTo };
}
