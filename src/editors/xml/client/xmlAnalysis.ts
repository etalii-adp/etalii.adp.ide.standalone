import type { EditorAnalysis, EditorCompletion, EditorFinding, EditorFindingSeverity } from "@client/editors/editorAnalysis";
import { ProblemSeverity } from "@client/generated/problems_pb";
import type { AnalyzeTextResponse, CompleteTextResponse } from "@client/generated/editors_pb";

/**
 * The two calls the XML editor asks the backend, by the text as typed (xml-editor Requirement
 * 9.1). Handed in rather than built here, so a test can answer them without a transport.
 */
export interface XmlAnalysisCalls {
  analyze(text: string, signal: AbortSignal): Promise<AnalyzeTextResponse>;
  complete(text: string, offset: number, explicit: boolean, signal: AbortSignal): Promise<CompleteTextResponse>;
}

/** What an answer tells the panel besides its findings: the status and the files it read. */
export interface XmlAnalysisListener {
  onStatus(status: string): void;
  onDependsOn(paths: readonly (readonly string[])[]): void;
}

/** What the status line says, once, when the backend cannot be reached (Requirement 9.5). */
export const checkingUnavailable = "Checking is unavailable.";

/** The wire's severities, as the shared presentation names them. */
const severities: Record<ProblemSeverity, EditorFindingSeverity> = {
  [ProblemSeverity.PROBLEM_SEVERITY_UNSPECIFIED]: "info",
  [ProblemSeverity.WARNING]: "warning",
  [ProblemSeverity.ERROR]: "error",
  [ProblemSeverity.INFO]: "info",
};

/**
 * The XML editor's {@link EditorAnalysis}: findings, completions and hover text, each one
 * question to the backend. At most one request for findings is waiting: a newer one aborts the
 * older (Requirement 9.4). The shared runner drops an answer for a text that has changed
 * (Requirement 9.3); the status follows only the answer that was not superseded.
 */
export function createXmlAnalysis(calls: XmlAnalysisCalls, listener: XmlAnalysisListener): EditorAnalysis {
  let inFlight: AbortController | undefined;
  let status = "";

  const say = (next: string) => {
    if (next !== status) {
      status = next;
      listener.onStatus(next);
    }
  };

  return {
    async findings(text: string): Promise<EditorFinding[]> {
      inFlight?.abort();
      const controller = new AbortController();
      inFlight = controller;

      let response: AnalyzeTextResponse;
      try {
        response = await calls.analyze(text, controller.signal);
      } catch (error) {
        if (controller.signal.aborted) {
          // Superseded: the newer request answers for the text on screen.
          throw error;
        }

        say(checkingUnavailable);
        return [];
      } finally {
        if (inFlight === controller) {
          inFlight = undefined;
        }
      }

      if (controller.signal.aborted) {
        throw new DOMException("Superseded by a newer request.", "AbortError");
      }

      if (response.error !== "") {
        say(response.error);
        return [];
      }

      say(response.status);
      listener.onDependsOn(response.dependsOn.map((path) => path.segments));
      return response.findings.map((finding) => ({
        from: finding.from,
        to: finding.to,
        severity: severities[finding.severity] ?? "info",
        message: finding.message,
        source: finding.source,
      }));
    },

    async completions(text: string, offset: number, explicit: boolean) {
      const items = await completionsAt(calls, text, offset, explicit);
      return items === null ? null : { items };
    },

    async hover(text: string, offset: number) {
      // A name already in the document is described by the item the schema would offer there
      // (Requirement 7.6): asked at the name, and matched by it.
      const name = nameAt(text, offset);
      if (name === null) {
        return null;
      }

      const items = await completionsAt(calls, text, offset, true);
      const described = items?.find((item) => item.label === name.text && (item.info ?? "") !== "");
      return described ? { from: name.from, to: name.to, text: described.info ?? "" } : null;
    },

    status: () => status,
  };
}

async function completionsAt(calls: XmlAnalysisCalls, text: string, offset: number, explicit: boolean): Promise<EditorCompletion[] | null> {
  let response: CompleteTextResponse;
  try {
    response = await calls.complete(text, offset, explicit, new AbortController().signal);
  } catch {
    // The findings' status already says when checking is unavailable; a list says nothing.
    return null;
  }

  if (response.error !== "") {
    return null;
  }

  return response.items.map((item) => ({
    label: item.label,
    detail: item.detail || undefined,
    info: item.info || undefined,
    from: item.from,
    to: item.to,
    insert: item.insert,
    boost: item.boost || undefined,
  }));
}

const nameCharacter = /[\w.:-]/;

/** The XML name the offset is in, if any. */
function nameAt(text: string, offset: number): { text: string; from: number; to: number } | null {
  let from = offset;
  while (from > 0 && nameCharacter.test(text[from - 1])) {
    from--;
  }

  let to = offset;
  while (to < text.length && nameCharacter.test(text[to])) {
    to++;
  }

  return from < to ? { text: text.slice(from, to), from, to } : null;
}
