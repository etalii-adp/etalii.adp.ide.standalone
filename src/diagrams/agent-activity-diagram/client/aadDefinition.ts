import type { DiagramDefinition, ElementTypeDefinition, RelationTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { AAD_PULL_REQUESTS_GROUP, AAD_SHOW_ARCHIVED_SWITCH, AAD_TASK_GROUPS, AadElementTypes, AadRelationTypes } from "./aadIds";

/**
 * What the agent activity diagram allows, declared.
 *
 * <b>Written by hand for now, and that is the open half of the specification's task 15.</b> The
 * definition in etalii.adp states all of this in DISL 0.4, and the end state is this object
 * compiled from the bundled `.dis` by `compileNotation`, as the hype cycle graph's is. The
 * compiler does not read DISL 0.4's lists, links, rings and kept switch yet; until it does, what
 * is declared here must be kept equal to the bundled definition by whoever changes either.
 *
 * Five element types, each with its own shape so that a kind is told apart without colour
 * (Requirement 7.2): a project is a hexagon, a specification a rounded card, an agent a pill, a
 * location a box split in two, an environment a cylinder.
 */

// Every label names its colour by a class: an SVG text with none is black, whatever the theme.
const name = { text: { path: "payload.name" }, className: "aad-label", typography: { fontWeight: "bold" as const, fontSize: 13 }, truncate: true, tooltip: { path: "payload.name" } };

const lock = {
  glyph: "circle" as const,
  anchor: "canvas" as const,
  from: { x: { path: "bounds.left", number: { plus: 9 } }, y: { path: "bounds.top", number: { plus: 9 } } },
  radius: 3.5,
  className: "aad-lock",
  tooltip: { template: "Position locked" },
  accessibility: { role: "img", label: { template: "Position locked" } },
  when: { path: "payload.pinned", is: "true" as const },
};

function elementType(id: string, shape: ElementTypeDefinition["shape"], extra: Partial<ElementTypeDefinition> = {}): ElementTypeDefinition {
  return {
    id,
    shape,
    anchors: { kind: "edge", visible: false },
    sizing: "model",
    classNames: [{ className: `aad-${id}` }],
    data: { kind: id },
    tooltip: { path: "payload.name" },
    labels: [name],
    decorations: [lock],
    links: [{ id: "link", link: "payload.link", at: { right: 20, top: 5 } }],
    ...extra,
  };
}

const elementTypes: ElementTypeDefinition[] = [
  elementType(AadElementTypes.project, "hexagon"),
  elementType(AadElementTypes.specification, "rounded-rectangle", {
    labels: [
      { ...name, anchorTo: "top", offset: { x: 0, y: 20 } },
      // The status in words, and by colour through its class - never by colour alone.
      {
        text: { path: "payload.statusLabel" },
        anchorTo: "top",
        offset: { x: 0, y: 38 },
        className: { template: "aad-status aad-status-{payload.status}" },
        typography: { fontSize: 11 },
      },
    ],
    compartments: [
      {
        id: "tasks",
        rows: "payload.tasks",
        rowId: "id",
        text: { path: "title" },
        link: "link",
        orderBy: { path: "updated", direction: "descending" },
        groupBy: { path: "status", groups: AAD_TASK_GROUPS, otherTitle: "Other" },
        collapsed: "payload.collapsed",
        top: 48,
        headingHeight: 20,
        rowHeight: 18,
        bottom: 8,
        insetX: 10,
        rowIndent: 12,
      },
    ],
  }),
  elementType(AadElementTypes.agent, "pill"),
  elementType(AadElementTypes.location, "box", {
    tooltip: { template: "{payload.name} in {payload.folder}" },
    labels: [
      { ...name, anchorTo: "top", offset: { x: 0, y: 21 } },
      { text: { path: "payload.folder" }, className: "aad-detail", anchorTo: "top", offset: { x: 0, y: 53 }, truncate: true, tooltip: { path: "payload.folder" }, typography: { fontSize: 11 } },
    ],
    decorations: [
      lock,
      // The line that splits the branch above from the folder below.
      {
        glyph: "line",
        anchor: "canvas",
        from: { x: { path: "bounds.left" }, y: { path: "bounds.top", number: { plus: 34 } } },
        to: { x: { path: "bounds.right" }, y: { path: "bounds.top", number: { plus: 34 } } },
        className: "aad-split",
      },
    ],
    links: [
      { id: "branch", link: "payload.branchLink", at: { right: 20, top: 9 }, label: "branch" },
      { id: "folder", link: "payload.folderLink", at: { right: 20, top: 42 }, label: "folder" },
    ],
    compartments: [
      {
        id: AAD_PULL_REQUESTS_GROUP,
        rows: "payload.pullRequests",
        rowId: "id",
        text: { path: "title" },
        link: "link",
        orderBy: { path: "updated", direction: "descending" },
        title: "Pull requests",
        collapsed: "payload.collapsed",
        top: 70,
        headingHeight: 20,
        rowHeight: 18,
        bottom: 8,
        insetX: 10,
        rowIndent: 12,
      },
    ],
  }),
  elementType(AadElementTypes.environment, "cylinder", {
    labels: [
      { ...name, offset: { x: 0, y: 2 } },
      { text: { path: "payload.kindLabel" }, className: "aad-detail", offset: { x: 0, y: 17 }, typography: { fontSize: 10 } },
    ],
  }),
];

/** A relation is a plain line: its two ends say what it means (Requirement 7.4). */
function relation(id: string, source: string, target: string, cardinality: RelationTypeDefinition["endpoints"]["cardinality"]): RelationTypeDefinition {
  return {
    id,
    route: "straight",
    className: "aad-relation",
    selectable: true,
    endpoints: {
      source: { elementTypes: [source], anchors: "edge" },
      target: { elementTypes: [target], anchors: "edge" },
      allowSelf: false,
      cardinality: { perPair: "unordered", ...cardinality },
    },
  };
}

const relationTypes: RelationTypeDefinition[] = [
  // A specification has one project; a project any number of specifications.
  relation(AadRelationTypes.projectSpecification, AadElementTypes.project, AadElementTypes.specification, { maxIntoTarget: 1 }),
  // An agent has one specification at a time; a specification any number of agents.
  relation(AadRelationTypes.specificationAgent, AadElementTypes.specification, AadElementTypes.agent, { maxIntoTarget: 1 }),
  // A location has one agent; an agent any number of locations.
  relation(AadRelationTypes.agentLocation, AadElementTypes.agent, AadElementTypes.location, { maxIntoTarget: 1 }),
  // A location has one environment; an environment any number of locations.
  relation(AadRelationTypes.locationEnvironment, AadElementTypes.location, AadElementTypes.environment, { maxFromSource: 1 }),
];

export const AAD_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes,
  relationTypes,
  layout: {
    modes: ["tiered-force"],
    tiers: [
      [AadElementTypes.project],
      [AadElementTypes.specification],
      [AadElementTypes.agent],
      [AadElementTypes.location],
      [AadElementTypes.environment],
    ],
    pinned: "payload.pinned",
  },
  dragging: "enabled",
  connectOnRightDrag: true,
  backgroundMenu: true,
  chrome: { switches: [{ id: AAD_SHOW_ARCHIVED_SWITCH, caption: "Show archived", on: "payload.showArchived" }] },
});
