import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import disText from "../definition/agent-behavior-modelling.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ElementTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { ABM_DEFINITION, ABM_SHAPES as COMPILED_SHAPES } from "./AbmCanvas";
import { ABM_BINDINGS } from "./abmBindings";
import { ABM_CATEGORY, ABM_CHILD_RELATION, ABM_NODE_KINDS, AbmActions, AbmShortcuts, type AbmNodeKind } from "./abmIds";

/**
 * The behavior model's canvas definition, compiled from its bundled DISL specification, is the
 * definition the module stated by hand until the switch-over.
 *
 * <b>The oracle below is that hand-written definition, moved here unchanged</b> from `AbmCanvas.tsx`
 * (`ABM_SHAPES`, `familyOf`, `nodeType`, `PARENTS`, `ABM_DEFINITION`). It is what the canvas drew
 * before; the compiled definition must equal it to the last key. Where the two differed, the code
 * won and the specification was corrected upstream in etalii.adp: a new node does not open its label
 * editor on drop (`after: "none"`). The parent line's filled arrowhead (`arrowFilled`) and its parents
 * (only kinds that may hold children start a new line, though a line is drawn from any node) were
 * already what the canvas does, read through the marker catalog and the containment rule.
 */

/**
 * The shape each kind is drawn as - the library's built-ins, and nothing of this module's own. A
 * composite is a squircle and a wrapper a hexagon, as behavior tree editors in games set the two
 * families apart; among the leaves, a Check is a pill, a Do a box, an Ask the user the
 * parallelogram flowcharts give input, and a Delegate the diode that points onward.
 */
const ABM_SHAPES: Readonly<Record<AbmNodeKind, ElementTypeDefinition["shape"]>> = {
  sequence: "superellipse",
  fallback: "superellipse",
  parallel: "superellipse",
  retry: "hexagon",
  repeat: "hexagon",
  guard: "hexagon",
  approval: "hexagon",
  check: "pill",
  action: "box",
  ask: "parallelogram",
  delegate: "diode",
};

/** The fill family a kind takes its colour from; `abm.css` maps each to a theme token. */
function familyOf(kind: AbmNodeKind): string {
  if (kind === "check" || kind === "action") {
    return kind;
  }

  return ABM_CATEGORY[kind] === "leaf" ? "other" : ABM_CATEGORY[kind];
}

/**
 * One kind: its shape and fill, the keyword on the top line and the label beneath it. Only the label
 * is editable - the keyword is the kind, changed through the property grid, where it can be refused
 * when the node's children would not fit the new kind.
 */
function nodeType(kind: AbmNodeKind): ElementTypeDefinition {
  return {
    id: kind,
    shape: ABM_SHAPES[kind],
    classNames: [
      { className: "canvas-element abm-node", on: "element" },
      { className: "abm-implicit", on: "element", when: { path: "payload.implicit", is: "true" } },
      { className: `canvas-node abm-${familyOf(kind)}`, on: "shape" },
    ],
    accessibility: { role: "button", label: { template: "{payload.keyword}: {payload.label}" } },
    labels: [
      {
        text: { path: "payload.keyword" },
        anchorTo: "top",
        offset: { x: 0, y: 20 },
        truncate: true,
        className: "abm-keyword",
      },
      {
        text: { path: "payload.label" },
        anchorTo: "top",
        offset: { x: 0, y: 40 },
        truncate: true,
        editable: true,
        editorBox: { top: 27, height: 22 },
        tooltip: { path: "payload.label" },
        className: "canvas-node-label abm-label",
      },
    ],
    // A parent line leaves the middle of the parent's bottom and arrives at the middle of the
    // child's top, as a tree drawn top-down reads; the orthogonal route then runs vertically out
    // and in. A true edge intersection would put both ends wherever the slant between the two
    // centres crossed the outline, and the route would lie along the borders.
    anchors: { kind: "edge", edgeSides: "vertical" },
    sizing: "model",
  };
}

/** The kinds a parent line may start from: the ones that hold children. */
const PARENTS: readonly AbmNodeKind[] = ABM_NODE_KINDS.filter((kind) => ABM_CATEGORY[kind] !== "leaf");

/**
 * What a behavior model allows, stated once. The tree is computed by the backend from the Markdown,
 * so the canvas lays out nothing; a drag moves a node's row and may reorder its siblings, and a
 * parent line drawn from one node to another moves the second under the first, which the cycle rule
 * keeps from ever running a node beneath itself.
 */
const HAND_WRITTEN_ABM_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: ABM_NODE_KINDS.map(nodeType),
  relationTypes: [
    {
      id: ABM_CHILD_RELATION,
      route: "orthogonal",
      style: { endMarker: "arrow" },
      className: "abm-child",
      endpoints: {
        source: { elementTypes: PARENTS },
        target: { elementTypes: ABM_NODE_KINDS, anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  acyclic: [{ relationTypes: [ABM_CHILD_RELATION] }],
  actions: [
    {
      id: AbmActions.rename,
      invokedBy: [{ kind: "shortcut", key: AbmShortcuts.rename }, { kind: "gesture", gesture: "activate" }],
      appliesTo: [{ kind: "element" }],
    },
    { id: AbmActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
    { id: AbmActions.moveEarlier, invokedBy: [{ kind: "shortcut", key: AbmShortcuts.moveEarlier }], appliesTo: [{ kind: "element" }] },
    { id: AbmActions.moveLater, invokedBy: [{ kind: "shortcut", key: AbmShortcuts.moveLater }], appliesTo: [{ kind: "element" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // Edge anchors draw no handle, so a parent line is drawn by dragging with the right button from
  // the parent's body to the child's - the gesture every edge-anchored module offers.
  connectOnRightDrag: true,
  // Arrange diagram and "Add … here" on empty canvas, from the backend's own list.
  backgroundMenu: true,
});

describe("the behavior model's compiled definition", () => {
  it("is the hand-written definition", () => {
    expect(ABM_DEFINITION).toEqual(HAND_WRITTEN_ABM_DEFINITION);
    expect(COMPILED_SHAPES).toEqual(ABM_SHAPES);
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the test above: were the canvas still holding its own definition, the
    // equality would compare the oracle with a copy of itself.
    expect(ABM_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), ABM_BINDINGS)));
  });
});

/** The CSS custom property each theme token of the specification is painted with. */
const TOKEN_PROPERTIES: Readonly<Record<string, string>> = {
  "color.text": "--color-text",
  "color.text.muted": "--color-text-muted",
  "color.surface": "--color-surface",
  "color.border": "--color-border",
  "color.abm.composite": "--color-diagram-abm-composite",
  "color.abm.decorator": "--color-diagram-abm-decorator",
  "color.abm.check": "--color-diagram-abm-check",
  "color.abm.action": "--color-diagram-abm-action",
  "color.abm.other": "--color-diagram-abm-other",
};

/** The custom properties a block of `index.css` declares, by name. */
function customPropertiesIn(block: string): Record<string, string> {
  return Object.fromEntries([...block.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((match) => [match[1]!, match[2]!.trim().toLowerCase()]));
}

/** The light theme (`:root`) and the dark one (`:root` under `prefers-color-scheme: dark`) of `index.css`. */
function themes(): { light: Record<string, string>; dark: Record<string, string> } {
  const css = readFileSync(join(__dirname, "..", "..", "..", "client", "src", "index.css"), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
  const light = /^:root\s*\{([^}]*)\}/m.exec(css);
  const dark = /@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{([^}]*)\}/.exec(css);
  if (light === null || dark === null) {
    throw new Error("index.css no longer has the light :root block and the dark one this test reads.");
  }

  return { light: customPropertiesIn(light[1]!), dark: customPropertiesIn(dark[1]!) };
}

describe("the behavior model's theme tokens", () => {
  const theme = parseDisl(disText).notation.theme!;
  const css = themes();

  it("are each painted by a custom property", () => {
    expect(Object.keys(theme.tokens).sort()).toEqual(Object.keys(TOKEN_PROPERTIES).sort());
  });

  it("equal the custom properties in the light theme", () => {
    const light = { ...theme.tokens, ...theme.modes?.light };
    for (const [token, property] of Object.entries(TOKEN_PROPERTIES)) {
      expect(css.light[property], `${token} (${property})`).toBe(light[token]!.toLowerCase());
    }
  });

  it("equal the custom properties in the dark theme", () => {
    const dark = { ...theme.tokens, ...theme.modes?.dark };
    for (const [token, property] of Object.entries(TOKEN_PROPERTIES)) {
      expect(css.dark[property], `${token} (${property})`).toBe(dark[token]!.toLowerCase());
    }
  });
});
