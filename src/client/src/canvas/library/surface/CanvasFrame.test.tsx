import { describe, expect, it } from "vitest";
import { act, fireEvent, render } from "@testing-library/react";
import { useEffect, useRef } from "react";
import { CanvasFrame } from "./CanvasFrame";
import { useCanvasRefusalReporter } from "./canvasRefusals";
import { combinedState, useCanvasStatusReporter, type CanvasStreamState } from "./canvasStatus";
import { useCanvasRefusal } from "./useCanvasRefusal";

/**
 * The library's frame around a module canvas: the ONE refusal line and the ONE status appearance
 * (client-centralization Requirement 2). What feeds them is held elsewhere - the calls in
 * contextConnectionReportsToCanvas.test.tsx and useDiagramStream.move.test.ts, the stream's state
 * in useDiagramStream.status.test.ts. This holds what the frame draws from what it is told.
 */

/** A stand-in stream: reports one state for as long as it is mounted. */
function Stream({ state }: { state: CanvasStreamState }) {
  const status = useCanvasStatusReporter();
  const id = useRef(Symbol("stream"));
  useEffect(() => {
    status?.report(id.current, state);
  }, [status, state]);
  useEffect(() => {
    const stream = id.current;
    return () => status?.forget(stream);
  }, [status]);
  return null;
}

let refusals: ReturnType<typeof useCanvasRefusalReporter> = null;

function Probe() {
  refusals = useCanvasRefusalReporter();
  return <div className="probe-canvas">the module's canvas</div>;
}

const statusOf = (container: HTMLElement) => container.querySelector('[data-canvas-surface="status"]');
const refusalOf = (container: HTMLElement) => container.querySelector('[data-canvas-surface="refusal"]');

describe("the library's frame around a canvas", () => {
  it("draws the module's canvas inside it, and nothing else while all is well", () => {
    // Act.
    const { container } = render(
      <CanvasFrame>
        <Probe />
        <Stream state={{ kind: "open" }} />
      </CanvasFrame>,
    );

    // Assert.
    expect(container.querySelector(".canvas-frame > .probe-canvas")).not.toBeNull();
    expect(statusOf(container)).toBeNull();
    expect(refusalOf(container)).toBeNull();
  });

  it.each([
    [{ kind: "opening" } as CanvasStreamState, "Opening…", "status"],
    [{ kind: "reconnecting" } as CanvasStreamState, "Reconnecting…", "status"],
    [{ kind: "unavailable", reason: "'x' diagrams cannot be opened yet." } as CanvasStreamState, "'x' diagrams cannot be opened yet.", "alert"],
    [{ kind: "unavailable", reason: "" } as CanvasStreamState, "This diagram could not be opened.", "alert"],
  ])("says %o as %s, in the one shared appearance", (state, sentence, role) => {
    // Act.
    const { container } = render(
      <CanvasFrame>
        <Stream state={state} />
      </CanvasFrame>,
    );

    // Assert.
    const status = statusOf(container)!;
    expect(status.textContent).toBe(sentence);
    expect(status.classList.contains("canvas-status")).toBe(true);
    expect(status.getAttribute("role")).toBe(role);
  });

  it("says the worst of several streams, and forgets a stream that has gone", () => {
    // Arrange: a canvas may hold more than one stream; it is only as usable as the least usable.
    const { container, rerender } = render(
      <CanvasFrame>
        <Stream state={{ kind: "open" }} />
        <Stream state={{ kind: "reconnecting" }} />
      </CanvasFrame>,
    );
    expect(statusOf(container)!.textContent).toBe("Reconnecting…");

    // Act: the reconnecting stream unmounts.
    rerender(
      <CanvasFrame>
        <Stream state={{ kind: "open" }} />
      </CanvasFrame>,
    );

    // Assert.
    expect(statusOf(container)).toBeNull();
  });

  it("shows a refusal on its one line, and a click dismisses it", () => {
    // Arrange.
    const { container } = render(
      <CanvasFrame>
        <Probe />
      </CanvasFrame>,
    );

    // Act.
    act(() => refusals!.refused("That connection would make a cycle."));

    // Assert.
    const line = refusalOf(container)!;
    expect(line.textContent).toBe("That connection would make a cycle.");
    expect(line.classList.contains("canvas-rejection")).toBe(true);
    expect(container.querySelectorAll(".canvas-rejection")).toHaveLength(1);

    // Act: dismissed by a click (the user's ruling, 2026-09-25).
    fireEvent.click(line);

    // Assert.
    expect(refusalOf(container)).toBeNull();
  });

  it("clears the line when the next gesture is sent", () => {
    // Arrange.
    const { container } = render(
      <CanvasFrame>
        <Probe />
      </CanvasFrame>,
    );
    act(() => refusals!.refused("Refused."));

    // Act.
    act(() => refusals!.attempted());

    // Assert.
    expect(refusalOf(container)).toBeNull();
  });

  it("gives each canvas its own line: a refusal on one frame does not show on another", () => {
    // Arrange: two tabs, two frames.
    let first: ReturnType<typeof useCanvasRefusalReporter> = null;
    function First() {
      first = useCanvasRefusalReporter();
      return null;
    }
    const { container } = render(
      <>
        <div className="one">
          <CanvasFrame>
            <First />
          </CanvasFrame>
        </div>
        <div className="two">
          <CanvasFrame>
            <Probe />
          </CanvasFrame>
        </div>
      </>,
    );

    // Act.
    act(() => first!.refused("Only on the first."));

    // Assert.
    expect(container.querySelector(".one .canvas-rejection")?.textContent).toBe("Only on the first.");
    expect(container.querySelector(".two .canvas-rejection")).toBeNull();
  });

  it("lets a module refuse a gesture it decided on itself, on the same line", () => {
    // Arrange: causal-loop's drop on nothing, and mindmap's own re-parenting move.
    let refusal: ReturnType<typeof useCanvasRefusal> | null = null;
    function Module() {
      refusal = useCanvasRefusal();
      return null;
    }
    const { container } = render(
      <CanvasFrame>
        <Module />
      </CanvasFrame>,
    );

    // Act.
    act(() => refusal!.refuse("Drop a link or a loop onto a variable."));

    // Assert.
    expect(refusalOf(container)!.textContent).toBe("Drop a link or a loop onto a variable.");

    // Act: the module's own next gesture clears it, as every other does.
    act(() => refusal!.attempted());

    // Assert.
    expect(refusalOf(container)).toBeNull();
  });
});

describe("combining stream states", () => {
  it("ranks unavailable over reconnecting over opening over open, and an empty canvas is open", () => {
    // Assert.
    expect(combinedState([])).toEqual({ kind: "open" });
    expect(combinedState([{ kind: "opening" }, { kind: "open" }])).toEqual({ kind: "opening" });
    expect(combinedState([{ kind: "opening" }, { kind: "reconnecting" }])).toEqual({ kind: "reconnecting" });
    expect(combinedState([{ kind: "reconnecting" }, { kind: "unavailable", reason: "Gone." }, { kind: "opening" }])).toEqual({
      kind: "unavailable",
      reason: "Gone.",
    });
  });
});
