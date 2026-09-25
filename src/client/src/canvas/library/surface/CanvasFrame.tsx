import type { ReactNode } from "react";
import { CanvasRefusalContext, useCanvasRefusalLine } from "./canvasRefusals";
import { CanvasStatusContext, statusSentence, useCanvasStatus } from "./canvasStatus";

/**
 * The library's frame around one module canvas: the ONE place a refusal is shown and the ONE
 * appearance of opening, reconnecting and unavailable (client-centralization Requirement 2; placed
 * around the canvas where the shell mounts it, as the user ruled on 2026-09-25).
 *
 * It is the shell that puts a canvas inside it, not the module, so a module neither declares a
 * surface nor wires one: its stream reports the status, and every gesture call it makes - and every
 * one the library makes for it - reports its refusal, through the two contexts provided here.
 *
 * Both lines are marked `data-canvas-surface`, which is how a guard tells the library's surface from
 * one a module declared.
 */
export function CanvasFrame({ children }: { children: ReactNode }) {
  const refusals = useCanvasRefusalLine();
  const { status, reporter } = useCanvasStatus();
  const sentence = statusSentence(status);

  return (
    <CanvasRefusalContext.Provider value={refusals.reporter}>
      <CanvasStatusContext.Provider value={reporter}>
        <div className="canvas-frame">
          {children}
          {sentence !== null ? (
            <p
              className="canvas-status"
              data-canvas-surface="status"
              role={status.kind === "unavailable" ? "alert" : "status"}
            >
              {sentence}
            </p>
          ) : null}
          {refusals.message !== null ? (
            <p
              className="canvas-rejection"
              data-canvas-surface="refusal"
              role="alert"
              title="Click to dismiss"
              onClick={refusals.dismiss}
            >
              {refusals.message}
            </p>
          ) : null}
        </div>
      </CanvasStatusContext.Provider>
    </CanvasRefusalContext.Provider>
  );
}
