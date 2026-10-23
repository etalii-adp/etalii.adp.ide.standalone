import type { HierarchySurfaceDefinition } from "@client/hierarchySurface/library/definition/hierarchySurfaceDefinition";

/**
 * What the Hierarchical Requirement Designer declares for the hierarchy surface (spec 029 R9): its levels are items and
 * its entries requirements, the labels its `.des` gives under `surface.labels`, the icon of each assessment kind from
 * its `AssessmentKind`, the icon R10 settles for several open assessments, and the operations its gestures send with
 * their labels and the words of a delete's question and of the assessments' list. `register.test.ts` holds each to the definition as it is checked
 * in.
 */
export const hierarchicalRequirementSurface: HierarchySurfaceDefinition = {
  levelType: "Item",
  entryType: "Requirement",
  labels: [
    { label: "tags", prefix: "#", many: true, on: ["Item", "Requirement"] },
    { label: "category", prefix: "category:", many: false, on: ["Requirement"] },
    { label: "state", prefix: "state:", many: false, on: ["Requirement"] },
  ],
  noteIcons: {
    Question: "mdi-help-circle-outline",
    Remark: "mdi-comment-text-outline",
    Warning: "mdi-alert-outline",
    Idea: "mdi-lightbulb-outline",
  },
  severalNotesIcon: "mdi-comment-multiple-outline",
  operations: {
    toggleCollapsed: "toggleCollapsed",
    setTitle: "setTitle",
    setDescription: "setDescription",
    addTopLevel: { operation: "addTopItem", label: "New item" },
    addEntry: { operation: "addRequirement", label: "New requirement" },
    addLevel: { operation: "addItem", label: "New item" },
    moveLevel: "moveItem",
    moveEntry: "moveRequirement",
    delete: "deleteEntry",
  },
  // The assessment operations, their params and labels; Resolve and Reopen are the two sides of resolveAssessment's CEL.
  notes: {
    operations: { add: "addAssessment", change: "changeAssessment", resolve: "resolveAssessment", remove: "removeAssessment" },
    params: { kind: "kind", text: "text", detail: "detail" },
    words: { list: "Assessments", add: "New assessment", change: "Change", resolve: "Resolve", reopen: "Reopen", remove: "Remove" },
  },
  moveParams: { into: "item", after: "after" },
  // The `confirm` of deleteEntry, its CEL written out: the count is the page's, from the model's references.
  deletion: {
    threshold: 1,
    message: (number, count) =>
      `Delete ${number} with everything in it? ${count} entries, references or assessments concern it; the references and assessments are kept, and reported.`,
  },
  // Ctrl+Enter and Ctrl+Shift+Enter, the context actions of an entry.
  shortcutKeys: ["Enter"],
};
