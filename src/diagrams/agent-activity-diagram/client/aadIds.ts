/**
 * The agent activity diagram's ids, as the backend spells them. One place, so the canvas, its
 * model and its tests cannot each hold a slightly different copy.
 */

/** The diagram type's mime type: its origin. */
export const AAD_MIME = "etalii/agent-activity-diagram";

/** What precedes an element's or a relation's type on the wire. */
export const AAD_TYPE_PREFIX = `${AAD_MIME}+`;

/** The five kinds of element, and the one that carries what is set for the diagram as a whole. */
export const AadElementTypes = {
  project: "project",
  specification: "specification",
  agent: "agent",
  location: "location",
  environment: "environment",
  view: "view",
} as const;

/** On the wire there is one relation type; which of the four it is follows from its two ends. */
export const AadWireRelationType = "relation";

/** The canvas switch that shows archived specifications: the specification's name for the filter. */
export const AAD_SHOW_ARCHIVED_SWITCH = "archived";

/** The id of the element that carries the diagram's own settings, and the target of its actions. */
export const AadViewId = "diagram view";

/** The actions the canvas asks for by a gesture, as the backend names them. */
export const AadActions = {
  addProject: "add-project",
  addSpecification: "add-specification",
  addAgent: "add-agent",
  addLocation: "add-location",
  addEnvironment: "add-environment",
  connect: "connect",
  collapseGroup: "collapse-group",
  expandGroup: "expand-group",
  showArchived: "show-archived",
  hideArchived: "hide-archived",
} as const;
