import type * as Wire from "@client/generated/designers_pb";
import type { HierarchySurfaceStreamEvent } from "@client/shell/context/workspaceStreams";
import type {
  HierarchySurfaceFinding,
  HierarchySurfaceModel,
  HierarchySurfaceReference,
} from "@client/hierarchySurface/library/api/hierarchySurfaceModel";
import { editKey } from "./tableWire";

/**
 * A hierarchy surface stream's wire messages as the library's own shapes. The one file that reads a generated
 * hierarchy surface message for the library's sake, as `tableWire.ts` is for a table: a change to the wire stops here.
 */

/** What became of a gesture the backend accepted, by the gesture's id as text. */
export interface HierarchySurfaceGestureOutcome {
  editId: string;
  written: boolean;
  error: string;
}

/** A stream event as the whole model, or as a gesture's outcome, which is the hook's to keep. */
export function fromWire(
  event: HierarchySurfaceStreamEvent,
): { kind: "model"; model: HierarchySurfaceModel } | { kind: "outcome"; outcome: HierarchySurfaceGestureOutcome } | undefined {
  switch (event.kind) {
    case "model":
      return { kind: "model", model: modelOf(event.model) };
    case "outcome":
      return { kind: "outcome", outcome: { editId: editKey(event.outcome.editId?.value), written: event.outcome.written, error: event.outcome.error } };
    default:
      // A kind this client does not know yet is left out rather than misread.
      return undefined;
  }
}

function modelOf(model: Wire.HierarchySurfaceModel): HierarchySurfaceModel {
  return {
    title: model.title,
    readOnlyReason: model.readOnlyReason,
    cause: model.cause,
    nodes: model.nodes.map((node) => ({
      id: node.id,
      number: node.number,
      role: node.role === "level" ? "level" : "entry",
      type: node.type,
      parentId: node.parentId,
      title: node.title,
      description: node.description,
      labels: node.labels.map((label) => ({ label: label.label, values: [...label.values] })),
      references: node.references.map(referenceOf),
      collapsed: node.collapsed,
    })),
    optionLists: model.optionLists.map((list) => ({
      label: list.label,
      options: list.options.map((option) => ({ id: option.id, name: option.name, colour: option.colour, implicit: option.implicit })),
    })),
    filters: model.filters.map((filter) => ({ id: filter.id, name: filter.name, query: filter.query })),
    notes: model.notes.map((note) => ({
      id: note.id,
      kind: note.kind,
      about: note.about,
      aboutId: note.aboutId,
      text: note.text,
      detail: note.detail,
      resolved: note.resolved,
      references: note.references.map(referenceOf),
    })),
    findings: model.findings.map(findingOf),
  };
}

function referenceOf(reference: Wire.HierarchySurfaceReference): HierarchySurfaceReference {
  return { start: reference.start, length: reference.length, targetId: reference.targetId };
}

function findingOf(finding: Wire.HierarchySurfaceFinding): HierarchySurfaceFinding {
  const severity = finding.severity === "warning" || finding.severity === "info" ? finding.severity : "error";
  return { code: finding.code, severity, message: finding.message, nodeId: finding.nodeId, line: finding.line };
}
