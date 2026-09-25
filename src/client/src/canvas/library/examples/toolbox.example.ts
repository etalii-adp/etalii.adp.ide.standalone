import type { ToolboxDefinition } from "../definition/diagramDefinition";

/**
 * A toolbox declaration, for the readme's toolbox entry.
 *
 * **No shipped module declares `toolbox` today** - measured by parsing every module's
 * `DiagramDefinition`, not by searching text - so this file exists rather than an invented excerpt
 * being passed off as shipped code. It is compiled by the client typecheck gate, so it cannot drift
 * out of the type it claims to satisfy.
 *
 * The toolbox a module gets by default derives from its element types; this is what OVERRIDING that
 * looks like - hiding one derived entry, and adding one the elements do not imply.
 */
export const TOOLBOX_EXAMPLE: ToolboxDefinition = {
  suppress: ["note"],
  add: [{ id: "milestone", title: "Milestone", icon: "flag", payload: "milestone" }],
};
