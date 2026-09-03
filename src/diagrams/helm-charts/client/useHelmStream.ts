import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { DiagramService } from "@client/generated/diagrams_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { applyDelta, emptyModel, type HelmModel } from "./helmModel";

export interface HelmStream {
  model: HelmModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;
  /**
   * Stores a box's authored position. A drag is a layout edit and nothing more: the backend
   * dispatches the core SetRegistrationLayoutCommand, the position lands in the `.adp`'s
   * `layout:` block - never in a chart file - and the change is one undo away (Requirement 6).
   * This is the one member the read-only sibling hook deliberately lacks, because this is the
   * one edit this type has.
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
}

/**
 * Opens the chart diagram at `path` over `DiagramService.Open` and folds its delta stream into
 * a `HelmModel`, re-baselining on reconnect. No view reporting: the whole chart is delivered
 * at open, because a chart is bounded.
 */
export function useHelmStream(projectId: Uint8Array, path: readonly string[]): HelmStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<HelmModel>(emptyModel);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const clientRef = useRef(createClient(DiagramService, transport));

  const pathKey = path.join("/");

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

  return { model, loading, failed, moveElementTo };
}
