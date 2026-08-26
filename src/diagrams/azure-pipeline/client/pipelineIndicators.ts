// What an element is telling you about itself before you open anything.
//
// Requirement 8.4 asks that a condition, a continueOnError, an `enabled: false`, a matrix
// multiplicity or a manual trigger all be visible without entering an edit mode - so these are
// worked out here as plain data, and the canvas only decides where to put them. Keeping the two
// apart is what lets the rules be tested without rendering anything.

import type { PipelineElementPayload } from "@client/generated/azure-pipeline_pb";
import type { Problem } from "@client/generated/context_pb";
import { ProblemSeverity } from "@client/generated/context_pb";
import type { PipelineNode } from "./pipelineModel";

/** One thing worth saying about an element, as a badge on it. */
export interface PipelineIndicator {
  /** Stable within an element, so React has a key and a test has something to ask for. */
  key: string;
  /** The glyph drawn on the canvas. */
  glyph: string;
  /** What it means, in words, for the tooltip and for a screen reader. */
  title: string;
}

/**
 * The badges an element carries.
 *
 * Order is fixed rather than incidental: the ones that change *whether* an element runs come
 * before the ones that change *how*, because that is the order a reader asks the questions in.
 */
export function indicatorsOf(payload: PipelineElementPayload): PipelineIndicator[] {
  const indicators: PipelineIndicator[] = [];

  if (!payload.enabled) {
    indicators.push({ key: "disabled", glyph: "⊘", title: "Disabled: this will not run." });
  }

  if (payload.triggerIsManual) {
    indicators.push({
      key: "manual",
      glyph: "▶",
      title: "Manual trigger: this waits to be started by a person.",
    });
  }

  if (payload.condition.length > 0) {
    indicators.push({
      key: "condition",
      glyph: "?",
      // The condition itself, verbatim - a reader who knows Azure's functions can read it, and
      // one who does not is no worse off than with a paraphrase this module invented.
      title: `Runs only when: ${payload.condition}`,
    });
  }

  if (payload.continuesOnError) {
    indicators.push({
      key: "continue-on-error",
      glyph: "!",
      title: "Continues on error: a failure here does not fail the run.",
    });
  }

  if (payload.multiplicity > 1) {
    indicators.push({
      key: "multiplicity",
      glyph: `×${payload.multiplicity}`,
      title: `Runs ${payload.multiplicity} times, once per matrix entry.`,
    });
  } else if (payload.indeterminate && payload.multiplicity === 1 && payload.kind !== 0) {
    // A count decided at run time. Shown as a question rather than a number, because putting a
    // number here that nobody knows yet would be worse than admitting it depends.
    indicators.push({
      key: "multiplicity-unknown",
      glyph: "×?",
      title: "How many of these run is decided when the pipeline runs.",
    });
  }

  if (payload.fromTemplate) {
    indicators.push({
      key: "from-template",
      glyph: "⇱",
      title: `From ${payload.templatePath}: edit it there.`,
    });
  }

  return indicators;
}

/**
 * The problems sitting on a given element.
 *
 * Problems arrive for the whole project, so they are filtered by both the file and the element -
 * two pipelines in one project may each have a stage called Build, and marking the wrong one would
 * be worse than marking neither.
 */
export function problemsOn(
  problems: readonly Problem[],
  path: readonly string[],
  elementId: string,
): Problem[] {
  const key = path.join("/");
  return problems.filter(
    (problem) =>
      problem.location?.location.case === "elementId" &&
      problem.location.location.value.value === elementId &&
      (problem.path?.segments ?? []).join("/") === key,
  );
}

/** The mark an element wears when something is wrong with it: the worst of what was reported. */
export function problemMarkOf(problems: readonly Problem[]): { severity: string; title: string } | null {
  if (problems.length === 0) {
    return null;
  }

  // An error outranks a warning: a stage with both is a stage that will not run, and that is the
  // thing to say about it.
  const worst = problems.some((problem) => problem.severity === ProblemSeverity.ERROR) ? "error" : "warning";
  return {
    severity: worst,
    title: problems.map((problem) => problem.message).join("\n"),
  };
}

/** Whether this element should be drawn as carrying a problem at all. */
export function hasProblem(
  problems: readonly Problem[],
  path: readonly string[],
  node: PipelineNode,
): boolean {
  return problemsOn(problems, path, node.id).length > 0;
}
