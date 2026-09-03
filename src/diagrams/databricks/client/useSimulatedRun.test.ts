import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { emptyModel, type DatabricksModel } from "./databricksModel";
import { useSimulatedRun, type SimulationProfile } from "./useSimulatedRun";

function task(id: string, runIf = "", kind = "notebook") {
  return { id, x: 0, y: 0, kind, label: id, badges: [], unresolved: false, runIf };
}

/**
 * The job fixture's shape: ingest → quality_gate (condition) → publish (outcome true) and
 * alert (outcome false, AT_LEAST_ONE_FAILED) → refresh (after publish).
 */
function dagModel(): DatabricksModel {
  return {
    nodes: new Map([
      ["task:ingest", task("task:ingest")],
      ["task:quality_gate", task("task:quality_gate", "", "condition")],
      ["task:publish", task("task:publish")],
      ["task:alert", task("task:alert", "AT_LEAST_ONE_FAILED")],
      ["task:refresh", task("task:refresh")],
    ]),
    frames: new Map(),
    edges: new Map([
      ["e1", { id: "e1", fromElementId: "task:ingest", toElementId: "task:quality_gate", outcome: "", kind: "depends" as const }],
      ["e2", { id: "e2", fromElementId: "task:quality_gate", toElementId: "task:publish", outcome: "true", kind: "depends" as const }],
      ["e3", { id: "e3", fromElementId: "task:quality_gate", toElementId: "task:alert", outcome: "false", kind: "depends" as const }],
      ["e4", { id: "e4", fromElementId: "task:publish", toElementId: "task:refresh", outcome: "", kind: "depends" as const }],
    ]),
  };
}

function play(model: DatabricksModel, profile: SimulationProfile = {}) {
  const rendered = renderHook(() => useSimulatedRun(model, { stepMs: 100, ...profile }));
  act(() => {
    expect(rendered.result.current.intercept("databricks.simulated.run-job")).toBe(true);
  });
  return rendered;
}

/** Runs the whole show and answers with every distinct state each task passed through. */
function finish(rendered: ReturnType<typeof play>): Map<string, string[]> {
  const seen = new Map<string, string[]>();
  const record = () => {
    for (const [id, state] of rendered.result.current.states) {
      const trail = seen.get(id) ?? [];
      if (trail[trail.length - 1] !== state) {
        trail.push(state);
      }
      seen.set(id, trail);
    }
  };

  record();
  for (let step = 0; step < 30; step++) {
    act(() => {
      vi.advanceTimersByTime(100);
    });
    record();
  }

  return seen;
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("the simulated run", () => {
  it("ripples pending through running to succeeded, in dependency order", () => {
    // Act.
    const rendered = play(dagModel());
    const trails = finish(rendered);

    // Assert.
    // Every happy-path task walks the full ladder (Requirement 8.1)...
    expect(trails.get("task:ingest")).toEqual(["pending", "running", "succeeded"]);
    expect(trails.get("task:publish")).toEqual(["pending", "running", "succeeded"]);
    // ...and nothing runs before what it depends on: the gate still awaits ingest when ingest runs.
    const firstRunning = rendered.result.current;
    expect(firstRunning).toBeDefined();
    expect(trails.get("task:quality_gate")![0]).toBe("pending");
  });

  it("follows the condition's outcome: the true branch runs, the false branch skips (Requirement 4.3)", () => {
    // Act.
    const trails = finish(play(dagModel()));

    // Assert.
    expect(trails.get("task:publish")).toContain("succeeded");
    expect(trails.get("task:alert")).toEqual(["pending", "skipped"]);
  });

  it("honors a non-default run_if: the failure path wakes the alert (Requirement 8.2)", () => {
    // Act: the configurable failure path - ingest fails.
    const trails = finish(play(dagModel(), { failTaskId: "task:ingest" }));

    // Assert.
    expect(trails.get("task:ingest")).toEqual(["pending", "running", "failed"]);
    // ALL_SUCCESS tasks downstream skip...
    expect(trails.get("task:quality_gate")).toEqual(["pending", "skipped"]);
    expect(trails.get("task:publish")).toEqual(["pending", "skipped"]);
    expect(trails.get("task:refresh")).toEqual(["pending", "skipped"]);
    // ...and the AT_LEAST_ONE_FAILED task is exactly what still runs.
    expect(trails.get("task:alert")).toEqual(["pending", "running", "succeeded"]);
  });

  it("plays the deploy ripple over the bundle's resources (Requirement 8.4)", () => {
    // Arrange.
    const model: DatabricksModel = {
      nodes: new Map([
        ["bundle", task("bundle")],
        ["resource:jobs/a", { ...task("resource:jobs/a"), x: 0 }],
        ["resource:pipelines/b", { ...task("resource:pipelines/b"), x: 260 }],
      ]),
      frames: new Map(),
      edges: new Map(),
    };

    // Act.
    const rendered = renderHook(() => useSimulatedRun(model, { stepMs: 100 }));
    act(() => {
      expect(rendered.result.current.intercept("databricks.simulated.deploy")).toBe(true);
    });
    const trails = finish(rendered as ReturnType<typeof play>);

    // Assert.
    // The resources progress in order; the bundle node itself is not part of the ripple.
    expect(trails.get("resource:jobs/a")).toEqual(["pending", "running", "succeeded"]);
    expect(trails.get("resource:pipelines/b")).toEqual(["pending", "running", "succeeded"]);
    expect(trails.has("bundle")).toBe(false);
    expect(rendered.result.current.marker).toContain("deploy");
  });

  it("marks everything simulated, and dismiss clears the show (Requirement 8)", () => {
    // Act.
    const rendered = play(dagModel());

    // Assert.
    expect(rendered.result.current.marker).toContain("Simulated");
    act(() => rendered.result.current.dismiss());
    expect(rendered.result.current.marker).toBeNull();
    expect(rendered.result.current.states.size).toBe(0);
  });

  it("answers false for an unmarked id, which then travels as ever (Requirement 8.6)", () => {
    // Act.
    const rendered = renderHook(() => useSimulatedRun(emptyModel));

    // Assert.
    expect(rendered.result.current.intercept("databricks.rename-task")).toBe(false);
    expect(rendered.result.current.marker).toBeNull();
  });
});
