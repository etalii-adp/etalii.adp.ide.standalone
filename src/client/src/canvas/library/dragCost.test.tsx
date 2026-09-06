import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

// jsdom implements no pointer capture on SVG elements; the arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * The guard on drag-and-drop-centralization's Requirement 1: a gesture's per-frame cost is
 * a function of the gesture, never of how many elements the diagram draws. Keyed on the
 * DEFECT rather than on the fix: it counts what actually rendered and times what a frame
 * actually cost, so any later change that re-renders the canvas per pointer frame fails it,
 * whatever mechanism reintroduces the cost.
 *
 * Accepted only after failing: with a per-frame canvas state write restored, the render
 * counts went non-zero and the ratio blew its tolerance (sabotage run, 2026-09-06).
 */

const FRAMES = 30;

/**
 * The ratio's tolerance. Disciplined, the large model's per-frame cost is the small one's -
 * one dragged-element render plus subscription checks - so the honest ratio sits near 1.
 * With per-element cost restored it tracks the model sizes (an order of magnitude apart
 * below), so 4 leaves generous room for jsdom's timer noise while a regression still blows
 * straight through it. Never an absolute millisecond budget - that flakes on slower
 * machines (Requirement 3.2).
 */
const RATIO_TOLERANCE = 4;
const SMALL = 10;
/** Sized with the survey's slow readings: the rdf family routinely draws hundreds. */
const LARGE = 500;

/** A definition whose one shape counts its own renders, per element id - the guard's lever. */
function countingDefinition(counts: Map<string, number>): DiagramDefinition {
  return {
    elementTypes: [
      {
        id: "node",
        shape: {
          customShape: "counting-box",
          render: (raw) => {
            const element = raw as { id: string; x: number; y: number };
            counts.set(element.id, (counts.get(element.id) ?? 0) + 1);
            return <rect x={element.x - 20} y={element.y - 10} width={40} height={20} />;
          },
          edgePoint: (bounds, towards) => ({ x: bounds.x, y: towards.y }),
        },
        anchors: { kind: "edge" },
        sizing: "model",
      },
    ],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

/** A grid of distinct positions, so hit-testing and drawing exercise every element. */
function gridModel(count: number): DiagramModel {
  return {
    elements: Array.from({ length: count }, (_, i) => ({
      id: `el-${i}`,
      type: "node",
      x: (i % 25) * 60,
      y: Math.floor(i / 25) * 40,
      width: 40,
      height: 20,
    })),
    connections: [],
  };
}

function mountCanvas(counts: Map<string, number>, count: number) {
  const definition = countingDefinition(counts);
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={gridModel(count)} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, button: 0, ...init });
}

/** Thirty pointer frames of a gesture on `target`, down to before the release. */
function driveFrames(target: Element) {
  fireEvent(target, pointer("pointerdown", { clientX: 10, clientY: 10 }));
  for (let frame = 1; frame <= FRAMES; frame++) {
    fireEvent(target, pointer("pointermove", { clientX: 10 + frame * 3, clientY: 10 + frame * 2 }));
  }
}

describe("drag cost", () => {
  it("neither a reposition nor a pan re-renders a non-gesture element, on a hundred-element model", () => {
    const counts = new Map<string, number>();
    const { container, unmount } = mountCanvas(counts, 100);

    // The two canaries, before anything is asserted about zero: a zero-render verdict on a
    // canvas that drew nothing, or drew the wrong model, would be vacuously green.
    expect(container.querySelectorAll("[data-element-id]")).toHaveLength(100); // population floor
    expect(container.querySelector('[data-element-id="el-42"]')).not.toBeNull(); // named member

    // The reposition, thirty frames on el-0.
    counts.clear();
    driveFrames(container.querySelector('[data-element-id="el-0"]')!);
    const draggedRenders = counts.get("el-0") ?? 0;
    const bystanderRenders = [...counts.entries()].filter(([id]) => id !== "el-0");
    expect(draggedRenders).toBeGreaterThan(0); // the discipline is scoped, not dead
    expect(bystanderRenders).toEqual([]); // and nobody else rendered, not once
    fireEvent(container.querySelector('[data-element-id="el-0"]')!, pointer("pointerup", { clientX: 200, clientY: 200 }));

    // The pan, thirty frames on the surface - included because a guard that only drags
    // would be satisfied by a pan regression, which is a detector keyed on the fix.
    counts.clear();
    driveFrames(container.querySelector("svg.library-canvas-surface")!);
    expect([...counts.entries()]).toEqual([]); // a pan renders no element per frame at all
    fireEvent(container.querySelector("svg.library-canvas-surface")!, pointer("pointerup", { clientX: 200, clientY: 200 }));

    unmount();
  });

  it("per-frame cost is flat across model sizes: the small/large ratio stays within tolerance", () => {
    // Both models measured in ONE run and compared as a ratio (Requirements 3.1, 3.2).
    // Each frame is timed ALONE and the MEDIAN single frame taken, across three mounts.
    // The estimator matters, and two wrong ones were measured before this one:
    // - the whole-window minimum (the first shipped version) inflates under parallel-gate
    //   contention, because the scheduler preempts inside every thirty-frame window - it
    //   read 4.345 against the tolerance of 4 on 2026-09-06 with nothing actually wrong,
    //   green 3-of-3 in isolation;
    // - the per-frame minimum is worse in the opposite direction: pointermove is a
    //   continuous-priority event, so React may flush a frame's render after the handler
    //   returns, one of ninety frames escapes its timing window, and the minimum finds
    //   exactly that frame - under the restored-sabotage check it read 1.11, a PASSING
    //   guard over broken code, caught only because the sabotage was re-run.
    // The median dodges both: contention spikes are outliers above it, deferred flushes
    // outliers below it, and 29 of 30 sabotaged frames carry the O(elements) render so
    // the sabotage stays loud - re-verified at 17.91 against the tolerance of 4 after
    // this change, on 2026-09-06.
    const perFrame = (count: number, gesture: "reposition" | "pan"): number => {
      const samples: number[] = [];
      for (let repetition = 0; repetition < 3; repetition++) {
        const counts = new Map<string, number>();
        const { container, unmount } = mountCanvas(counts, count);
        const target =
          gesture === "reposition"
            ? container.querySelector('[data-element-id="el-0"]')!
            : container.querySelector("svg.library-canvas-surface")!;
        fireEvent(target, pointer("pointerdown", { clientX: 10, clientY: 10 }));
        for (let frame = 1; frame <= FRAMES; frame++) {
          const started = performance.now();
          fireEvent(target, pointer("pointermove", { clientX: 10 + frame * 3, clientY: 10 + frame * 2 }));
          samples.push(performance.now() - started);
        }
        fireEvent(target, pointer("pointerup", { clientX: 200, clientY: 200 }));
        unmount();
      }
      samples.sort((a, b) => a - b);
      return samples[Math.floor(samples.length / 2)];
    };

    const repositionRatio = perFrame(LARGE, "reposition") / Math.max(perFrame(SMALL, "reposition"), 0.001);
    const panRatio = perFrame(LARGE, "pan") / Math.max(perFrame(SMALL, "pan"), 0.001);

    expect(repositionRatio).toBeLessThan(RATIO_TOLERANCE);
    expect(panRatio).toBeLessThan(RATIO_TOLERANCE);
  });
});
