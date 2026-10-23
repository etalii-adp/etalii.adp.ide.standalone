import type { HierarchySurfaceDefinition } from "@client/hierarchySurface/library/definition/hierarchySurfaceDefinition";

/**
 * What the Hierarchical Requirement Designer declares for the hierarchy surface (spec 029 R9): its levels are items and
 * its entries requirements, the labels its `.des` gives under `surface.labels`, the icon of each assessment kind from
 * its `AssessmentKind`, the icon R10 settles for several open assessments, and the operations its gestures send with
 * their labels and the words of a delete's question and of the assessments' list, the tag operations of its tag
 * field, the category and state operations of its pickers, and the filter operations of its filter bar.
 * `register.test.ts` holds each to the definition as it is checked in.
 */
export const hierarchicalRequirementSurface: HierarchySurfaceDefinition = {
  levelType: "Item",
  entryType: "Requirement",
  labels: [
    {
      label: "tags",
      prefix: "#",
      many: true,
      on: ["Item", "Requirement"],
      // The six tag operations, their params and labels, and the `confirm` of deleteTag with its CEL written out.
      edit: {
        operations: { add: "addTag", remove: "removeTag", rename: "renameTag", recolour: "recolourTag", move: "moveTag", delete: "deleteTag" },
        params: { name: "name", colour: "colour", after: "after" },
        words: { list: "Tags", add: "Add tag", remove: "Remove tag" },
        deletion: { threshold: 1, message: (name, count) => `${count} entries carry #${name}. Delete it and remove it from them?` },
      },
    },
    {
      label: "category",
      prefix: "category:",
      many: false,
      on: ["Requirement"],
      // The seven category operations, their params and labels, and the `confirm` of deleteCategory with its CEL written out.
      pick: {
        operations: {
          set: "setCategory",
          clear: "clearCategory",
          add: "addCategory",
          rename: "renameCategory",
          recolour: "recolourCategory",
          move: "moveCategory",
          delete: "deleteCategory",
        },
        params: { value: "category", name: "name", colour: "colour", after: "after" },
        words: { list: "Categories", value: "Category", clear: "No category" },
        deletion: { threshold: 1, message: (name, count) => `${count} requirements are ${name}. Delete it and clear them?` },
      },
    },
    {
      label: "state",
      prefix: "state:",
      many: false,
      on: ["Requirement"],
      // The seven state operations, their params and labels, and the `confirm` of deleteState with its CEL written out.
      pick: {
        operations: {
          set: "setState",
          clear: "clearState",
          add: "addState",
          rename: "renameState",
          recolour: "recolourState",
          move: "moveState",
          delete: "deleteState",
        },
        params: { value: "state", name: "name", colour: "colour", after: "after" },
        words: { list: "States", value: "State", clear: "No state" },
        deletion: { threshold: 1, message: (name, count) => `${count} requirements are ${name}. Delete it and clear them?` },
      },
    },
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
  // The filter operations and their params; a sort by number or by title, besides the labels of one value (DESL section 7.7.3).
  filters: {
    operations: { store: "storeFilter", rename: "renameFilter", remove: "removeFilter" },
    params: { name: "name", query: "query" },
    sortKeys: [
      { key: "number", label: "Number" },
      { key: "title", label: "Title" },
    ],
    entries: "requirements",
  },
  // Ctrl+Enter and Ctrl+Shift+Enter, the context actions of an entry.
  shortcutKeys: ["Enter"],
};
