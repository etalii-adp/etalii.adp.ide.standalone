import type { HierarchySurfaceDefinition } from "@client/hierarchySurface/library/definition/hierarchySurfaceDefinition";

/**
 * What the Hierarchical Requirement Designer declares for the hierarchy surface (spec 029 R9): its levels are items and
 * its entries requirements, the labels its `.des` gives under `surface.labels`, the icon of each assessment kind from
 * its `AssessmentKind`, and the icon R10 settles for several open assessments. `register.test.ts` holds each to the
 * definition as it is checked in.
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
  },
};
