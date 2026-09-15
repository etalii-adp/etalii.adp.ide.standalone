import { useCallback, useEffect, useRef, useState } from "react";
import type { DatabricksModel } from "./databricksModel";

/** Where one element currently stands in the pretend run. */
export type SimulatedState = "pending" | "running" | "succeeded" | "failed" | "skipped";

/** The knobs a mock run turns; all optional, all client-side (Requirement 8.2). */
export interface SimulationProfile {
  /** The one task the run fails at - the configurable failure path. Empty: everything succeeds. */
  failTaskId?: string;
  /** What every condition task answers; outcome edges follow it (Requirement 8.2). */
  conditionOutcome?: "true" | "false";
  /** Milliseconds per step of the show. */
  stepMs?: number;
}

export interface SimulatedRun {
  /** Per-element badge state the canvas draws; empty when nothing is playing. */
  states: Map<string, SimulatedState>;
  /** The banner naming what plays - every simulated surface carries its marker (Requirement 8). */
  marker: string | null;
  /**
   * The canvas's interception seam (Requirement 8.6): recognizes the backend-discovered
   * simulated action ids by their `.simulated.` marker, starts the local show, and answers
   * true so the caller dispatches nothing - a simulation never reaches the history or a file
   * (Requirement 11.6). Unmarked ids answer false and travel as ever.
   */
  intercept: (actionId: string) => boolean;
  /** Clears the show. */
  dismiss: () => void;
}

/** The marker every simulated action id carries - the same constant the backend discovers with. */
export const SIMULATED_MARKER = ".simulated.";

/**
 * The shows this engine plays, by the suffix of the action id that starts each. The one list: the
 * interception below reads it, and the canvas declares these ids as menu entries it runs itself,
 * so neither side retypes them (centralized-selection design A, "Menu entries a module runs
 * itself").
 */
export const SIMULATED_SHOWS = { runJob: "run-job", deploy: "deploy", pipelineUpdate: "pipeline-update" } as const;

/**
 * The simulated action ids the backend discovers - `databricks.simulated.run-job` and the rest -
 * built from the marker and the shows, the way DatabricksContextActionProvider names them.
 */
export const SIMULATED_ACTION_IDS: readonly string[] = Object.values(SIMULATED_SHOWS).map((show) => `databricks${SIMULATED_MARKER}${show}`);

const DEFAULT_STEP_MS = 700;

/**
 * The client-only mock runs of Requirement 8: pending → running → succeeded/failed played over
 * the parsed DAG, honoring `run_if` and condition outcomes, plus the deploy and pipeline-update
 * ripples. A state machine over `setInterval`; nothing here touches a file, the history or the
 * backend - which is the design's whole point: simulations stay off the command bus by
 * construction.
 */
export function useSimulatedRun(model: DatabricksModel, profile: SimulationProfile = {}): SimulatedRun {
  const [states, setStates] = useState<Map<string, SimulatedState>>(new Map());
  const [marker, setMarker] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const modelRef = useRef(model);
  modelRef.current = model;
  const profileRef = useRef(profile);
  profileRef.current = profile;

  const stop = useCallback(() => {
    if (timerRef.current !== null) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  useEffect(() => stop, [stop]);

  const dismiss = useCallback(() => {
    stop();
    setStates(new Map());
    setMarker(null);
  }, [stop]);

  /** Plays one precomputed sequence of frames, one per step. */
  const play = useCallback(
    (name: string, frames: Map<string, SimulatedState>[]) => {
      stop();
      let index = 0;
      setMarker(`Simulated: ${name}`);
      setStates(frames[0] ?? new Map());
      timerRef.current = setInterval(() => {
        index++;
        if (index >= frames.length) {
          stop();
          setMarker(`Simulated: ${name} — finished`);
          return;
        }

        setStates(frames[index]);
      }, profileRef.current.stepMs ?? DEFAULT_STEP_MS);
    },
    [stop],
  );

  const intercept = useCallback(
    (actionId: string): boolean => {
      if (!actionId.includes(SIMULATED_MARKER)) {
        return false;
      }

      const current = modelRef.current;
      const active = profileRef.current;
      if (actionId.endsWith(SIMULATED_SHOWS.runJob)) {
        play("job run", runFrames(current, active));
      } else if (actionId.endsWith(SIMULATED_SHOWS.deploy)) {
        play("deploy", rippleFrames(current, (id) => id.startsWith("resource:") || id.startsWith("unknown:")));
      } else if (actionId.endsWith(SIMULATED_SHOWS.pipelineUpdate)) {
        play("pipeline update", rippleFrames(current, (id) =>
          id.startsWith("library:") || id === "pipeline" || id === "target"));
      } else {
        // A simulated id this engine does not know still plays nothing rather than travelling:
        // the marker means client-side, whatever the show turns out to be.
        play("action", []);
      }

      return true;
    },
    [play],
  );

  return { states, marker, intercept, dismiss };
}

/**
 * The job run: waves over the dependency DAG. Each wave, every pending task whose upstream
 * edges are resolved either runs (its `run_if` satisfied), or skips (its `run_if` can no
 * longer be satisfied) - so the ripple honors outcome edges and failure semantics rather than
 * just flowing left to right.
 */
function runFrames(model: DatabricksModel, profile: SimulationProfile): Map<string, SimulatedState>[] {
  const conditionOutcome = profile.conditionOutcome ?? "true";
  const tasks = [...model.nodes.values()].filter((node) => node.id.startsWith("task:") && !node.unresolved);
  const upstream = new Map<string, { fromId: string; outcome: string }[]>();
  for (const edge of model.edges.values()) {
    if (edge.kind !== "depends") {
      continue;
    }

    const list = upstream.get(edge.toElementId) ?? [];
    list.push({ fromId: edge.fromElementId, outcome: edge.outcome });
    upstream.set(edge.toElementId, list);
  }

  const state = new Map<string, SimulatedState>(tasks.map((task) => [task.id, "pending"]));
  const frames: Map<string, SimulatedState>[] = [new Map(state)];

  const finished = (id: string) => {
    const current = state.get(id);
    return current === "succeeded" || current === "failed" || current === "skipped";
  };

  for (let guard = 0; guard < tasks.length * 2 + 2; guard++) {
    // Finish whatever ran last wave.
    let changed = false;
    for (const task of tasks) {
      if (state.get(task.id) === "running") {
        const fails = profile.failTaskId === task.id || `task:${profile.failTaskId ?? ""}` === task.id;
        state.set(task.id, fails ? "failed" : "succeeded");
        changed = true;
      }
    }

    // Start or skip whatever is now decidable.
    for (const task of tasks) {
      if (state.get(task.id) !== "pending") {
        continue;
      }

      const edges = upstream.get(task.id) ?? [];
      const known = edges.filter((edge) => model.nodes.has(edge.fromId));
      if (!known.every((edge) => finished(edge.fromId))) {
        continue;
      }

      const failures = known.filter((edge) => state.get(edge.fromId) !== "succeeded").length;
      const successes = known.filter((edge) => state.get(edge.fromId) === "succeeded").length;
      // An outcome edge the condition did not answer excludes its branch (Requirement 4.3)...
      const excluded = known.some((edge) => edge.outcome !== "" && edge.outcome !== conditionOutcome);

      // ...unless the task's run_if exists to rescue exactly such paths: a non-default run_if
      // judges what happened upstream rather than which branch was taken (Requirement 8.2).
      const runIf = task.runIf || "ALL_SUCCESS";
      const runs =
        runIf === "ALL_DONE" ? true
          : runIf === "AT_LEAST_ONE_FAILED" ? failures > 0
            : runIf === "AT_LEAST_ONE_SUCCESS" ? successes > 0 || known.length === 0
              : failures === 0 && !excluded; // ALL_SUCCESS, the schema default.

      state.set(task.id, runs ? "running" : "skipped");
      changed = true;
    }

    frames.push(new Map(state));
    if (!changed) {
      break;
    }
  }

  return frames;
}

/** A plain ripple: the matching elements run and succeed one after another, in position order. */
function rippleFrames(model: DatabricksModel, matches: (id: string) => boolean): Map<string, SimulatedState>[] {
  const ids = [...model.nodes.values()]
    .filter((node) => matches(node.id))
    .sort((left, right) => left.x - right.x || left.y - right.y)
    .map((node) => node.id);

  const state = new Map<string, SimulatedState>(ids.map((id) => [id, "pending"]));
  const frames: Map<string, SimulatedState>[] = [new Map(state)];
  for (const id of ids) {
    state.set(id, "running");
    frames.push(new Map(state));
    state.set(id, "succeeded");
    frames.push(new Map(state));
  }

  return frames;
}
