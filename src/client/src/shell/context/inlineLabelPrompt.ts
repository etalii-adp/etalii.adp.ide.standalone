import type { ContextPrompt } from "../../generated/context_pb";

/**
 * The element whose visible label this prompt edits, or null for every prompt that edits
 * something else - which is most of them.
 *
 * One reading, used by both the shell's dialog host and by any canvas, so the two can never
 * disagree about whether a prompt belongs in place. The answer comes entirely from data the
 * backend put on the prompt: shared client code never looks at an action id, because only the
 * module knows whether the value it is asking for is the text on screen (inline-rename
 * Requirement 2.1).
 */
export function inlineLabelElementIdOf(prompt: ContextPrompt | null): string | null {
  if (prompt?.prompt.case !== "inputDialog") {
    return null;
  }

  const elementId = prompt.prompt.value.inlineLabelEdit?.elementId?.value ?? "";
  // An empty id is not a marker. The backend leaves the field unset rather than sending an
  // empty one, and treating "" as a claim would make a malformed prompt claim every element.
  return elementId.length > 0 ? elementId : null;
}
