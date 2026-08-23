import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "../../../auth/AuthContext";
import { DiagramService } from "../../../generated/diagrams_pb";
import { useContextConnection } from "../../context/ContextConnectionProvider";
import { applyDelta, emptyModel, type MindmapModel } from "./mindmapModel";

/** A viewport the client reports; the backend answers with what falls inside it. */
export interface Viewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface MindmapStream {
  model: MindmapModel;
  /** True until the first delta arrives, so the canvas can show it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at
   * this path any more (deleted, moved, unroutable). The reconnect loop has stopped; the
   * canvas shows an unavailable state instead (diagram-workspace-tabs Requirement 5.1).
   */
  failed: boolean;
  /** Tells the backend what the canvas can see, so a large map does not stream in full. */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the diagram at `path` over `DiagramService.Open` and folds its delta stream into a
 * `MindmapModel`, re-baselining on reconnect. Viewport reports go up the paired unary
 * `UpdateView` leg (mindmap-diagram Requirement 11). The connection id is the shell's own
 * `watchId`, shared with the hierarchy and context calls.
 */
export function useMindmapStream(projectId: Uint8Array, path: readonly string[]): MindmapStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<MindmapModel>(emptyModel);
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
      // A dropped stream re-opens with the same request; the first message is always the
      // current document, so the client re-baselines without a protocol of its own.
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
          // A permanent answer ends the loop: the backend said this diagram cannot be opened
          // here - missing, unroutable, or a type without a session. Retrying would spin
          // forever behind a tab showing nothing (diagram-workspace-tabs Requirement 5.1).
          // Everything else stays a transient fault and keeps the reconnect behaviour.
          if (
            error instanceof ConnectError &&
            (error.code === Code.FailedPrecondition || error.code === Code.NotFound || error.code === Code.Unimplemented)
          ) {
            setFailed(true);
            setLoading(false);
            return;
          }
          // Brief back-off before re-opening, so a persistent failure does not spin.
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

  const reportView = (viewport: Viewport) => {
    void clientRef.current
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

  return { model, loading, failed, reportView };
}
