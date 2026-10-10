import type { ActionDeclaration, ActionInvocation } from "../definition/actions";
import type { Binding, Condition } from "../definition/binding";
import type { CompartmentDeclaration } from "../definition/compartments";
import type { CalendarStep, RulerDeclaration, RulerRung, SwitchDeclaration } from "../definition/chrome";
import type {
  AnchorSet,
  BuiltInShape,
  ClassDeclaration,
  CompassPosition,
  CustomRouteRef,
  DecorationDeclaration,
  DiagramDefinition,
  ElementTypeDefinition,
  EndpointConstraint,
  FilterDeclaration,
  LabelDeclaration,
  LayoutDefinition,
  LayoutMode,
  LinkDeclaration,
  RelationTypeDefinition,
  RouteLabelRule,
  SegmentDeclaration,
  SideFraction,
  SnapAxis,
  SnapDeclaration,
} from "../definition/diagramDefinition";
import { BEFORE_GAP } from "../definition/labels";
import {
  typeList,
  type Bindable,
  type DislAxis,
  type DislBadge,
  type DislCompartment,
  type DislContextMenu,
  type DislCoordinateSystem,
  type DislCustomShape,
  type DislDocument,
  type DislEdgeNotation,
  type DislLabel,
  type DislNodeNotation,
  type DislRelation,
  type DislViewpoint,
} from "./disTypes";
import { libraryMarkerOf, libraryRouteOf, libraryShapeOf, shapeNameOf, type CustomShapeBinding } from "./shapeCatalog";

/**
 * A DISL notation compiled into the library's {@link DiagramDefinition}: DISL 0.3, and of DISL 0.4
 * the lists inside a node, its badges, a force layout's rings and a kept switch.
 *
 * <b>One file, two readers.</b> A diagram module bundles its `.dis` (`src/diagrams/<module>/definition/`),
 * the backend runs it, and the client imports the same bytes with Vite's `?raw` and compiles them
 * here at module load. There is no generated file, so nothing can go stale between the two tiers.
 *
 * <b>Structural, and refusing what it reads.</b> The notation's nodes, edges, anchors, labels, sizes,
 * placements, the coordinates' snapping and ruler, the canvas's filter and legend, the context menus'
 * shortcuts and the viewpoints are mapped onto the library's vocabulary key by key. A value it reads
 * and cannot map - a shape, a marker, a label position, a CEL term - is refused with an error naming
 * it. Keys it never reads (styles, conditions, a viewpoint's styling) are not checked here: the
 * modules' compiled-definition tests hold the result to the hand-written drawing instead.
 *
 * <b>What stays code - {@link NotationBindings}.</b> The library has no CEL, so a CEL label becomes a
 * template over the payload the backend computes, through the module's table of expression-to-path
 * entries; class names, which tie the drawing to the module's stylesheet; and what a custom shape is
 * drawn as. Colours stay in CSS: a module's test proves its theme tokens equal the custom properties.
 */

/** What a module states beside its specification, because the library cannot read it from there. */
export interface NotationBindings {
  /**
   * The specification's `x-` block holding today's wire ids, shaped as `x-abm` is: `types` (DISL
   * name to wire id; a type it does not list is its name in lower case) and `actions` (an entry's
   * operation, else its kind, to the action id; `"<Type>/<key>"` overrides that for one type).
   * A module whose wire ids are the specification's own names states none: a type is then its name
   * in lower case and an action its operation, else its kind - as the backend's derivation has them.
   */
  wireIds?: `x-${string}`;
  /**
   * The payload path the backend computes for each CEL expression the notation binds, keyed by the
   * expression as written. `self.<attribute>` needs no entry: it is `payload.<attribute>`.
   */
  celPaths: Readonly<Record<string, string>>;
  /** The classes an element type's group and shape carry, which the module's stylesheet paints. */
  classNames: (type: string, node: DislNodeNotation) => readonly ClassDeclaration[];
  /** A label's class: a name, or a binding where the class follows the payload. */
  labelClassName?: (type: string, label: DislLabel) => string | Binding | undefined;
  /**
   * The path of each CEL term a list's row binds, within the row, keyed by the term as written.
   * `item.<attribute>` needs no entry: it is `<attribute>`.
   */
  rowPaths?: Readonly<Record<string, string>>;
  /** The class of a badge the library draws as a mark, where the stylesheet paints it. */
  badgeClassName?: (type: string, badge: DislBadge) => string | undefined;
  /** A label's inline editor box, where it should not cover the label's own line. */
  labelEditorBox?: (type: string, label: DislLabel) => { top: number; height: number } | undefined;
  /** A relation's class. */
  relationClassName?: (relation: string) => string | undefined;
  /** A relation's line's own class, where the stylesheet paints the line rather than the whole relation. */
  relationLineClassName?: (relation: string) => string | undefined;
  /** A relation's wide, invisible hit line's class, where the stylesheet dresses it. */
  relationHitClassName?: (relation: string) => string | undefined;
  /** An endpoint's anchor rule, where the module states one. */
  endpointAnchors?: (relation: string, end: "source" | "target") => EndpointConstraint["anchors"] | undefined;
  /** What each custom shape of the specification is drawn as. */
  customShapes?: Readonly<Record<string, CustomShapeBinding>>;
  /** The payload paths of each element's own snap origin. */
  snapOrigins?: { x: string; y: string };
  /**
   * An axis's resting lines where they are the module's arithmetic rather than a step of the axis: a
   * timeline's day, whose width in canvas units follows the scale the canvas froze, rides each element's
   * payload. Given for an axis, it replaces what the snapping rule would state.
   */
  snapAxes?: Partial<Record<"x" | "y", SnapAxis>>;
  /**
   * The value a drag shows, where the default system's snapping asks for one (`feedback.showValue`, DISL
   * §5.6): what the value is - a time, a row - is the axes' arithmetic over the live bounds, which no
   * template over the payload carries. It is added to every type that moves.
   */
  dragHint?: LabelDeclaration;
  /**
   * Whether the module draws its axes' rulers itself, as the timeline does beside the canvas: the
   * compiler then leaves the specification's rulers to it rather than refusing one it cannot map.
   */
  ownRulers?: boolean;
  /** The class a legend swatch takes for one enum value. */
  legendSwatchClass?: (enumName: string, value: string) => string;
  /** Library declarations the specification states only under a module's `x-` key, which DISL has no construct for. */
  extras?: (spec: DislDocument) => Partial<DiagramDefinition>;
  /**
   * The condition each CEL `visible` of a label stands for, keyed by the expression as written, as
   * {@link celPaths} does for a value: the library has no CEL, so the payload carries what it tests.
   */
  celConditions?: Readonly<Record<string, Condition>>;
  /**
   * What a DISL built-in shape is drawn as, where a module has stated it: one this library has no drawing
   * of (`roundedRect`) as a shared shape of its own family, such as the dependency graphs' `span`; or one
   * it draws otherwise in the module's family, such as the timeline's `diamond` as its `moment` marker.
   */
  builtInShapes?: Readonly<Record<string, BuiltInShape>>;
  /** Whether every element is in the tab order, which DISL leaves to the runtime (§6.15: keyboard navigation). */
  focusable?: boolean;
  /**
   * The route each edge is drawn with where the library's built-in route for its DISL routing is not
   * the drawing: a bezier's exact reach and its way back round, which DISL states only in outline.
   */
  customRoutes?: Readonly<Record<string, CustomRouteRef>>;
  /**
   * An action as the module dispatches it, given the compiled declaration and the shortcut its menu
   * entry writes (as a `KeyboardEvent.key`): where a module's backend resolves an action by its key
   * (`backendKey`), or a second key invokes it, which DISL's one `shortcut` per entry cannot say.
   */
  action?: (action: ActionDeclaration, entry: { shortcut?: string }) => ActionDeclaration;
}

export interface CompileOptions {
  /** The diagram attributes the notation binds (`{ attribute: "unit" }`), when not their default. */
  diagram?: Readonly<Record<string, string>>;
}

interface WireIds {
  types?: Readonly<Record<string, string>>;
  actions?: Readonly<Record<string, string>>;
}

/** How many months each DISL time unit spans, for a month-indexed axis. */
const UNIT_MONTHS: Readonly<Record<string, number>> = { month: 1, quarter: 3, year: 12, decade: 120, century: 1200, millennium: 12000 };

/** The library's calendar rung for a DISL ruler level: a century is ten decades, a millennium a hundred. */
const RUNG_STEPS: Readonly<Record<string, { calendar: CalendarStep; count?: number }>> = {
  month: { calendar: "month" },
  quarter: { calendar: "quarter" },
  year: { calendar: "year" },
  decade: { calendar: "decade" },
  century: { calendar: "decade", count: 10 },
  millennium: { calendar: "decade", count: 100 },
};

/** DISL's key names (§7.3) that are not their `KeyboardEvent.key`. */
const KEY_VALUES: Readonly<Record<string, string>> = { Space: " " };

/** A shortcut as the library matches it: the key a DISL entry names, as `KeyboardEvent.key` spells it. */
function keyOf(shortcut: string): string {
  return KEY_VALUES[shortcut] ?? shortcut;
}

/** The compass positions as fractions of the bounds. */
const COMPASS: Readonly<Record<CompassPosition, readonly [number, number]>> = {
  n: [0.5, 0], ne: [1, 0], e: [1, 0.5], se: [1, 1], s: [0.5, 1], sw: [0, 1], w: [0, 0.5], nw: [0, 0],
};

/** Where the first line of text inside a node sits beneath its top, and half of it above its bottom. */
const LABEL_LINE = 20;

/** A list's own measures, which DISL leaves to the host: the gap above it, its lines and its insets. */
const LIST_GAP = 12;
const LIST_HEADING = 20;
const LIST_ROW = 18;
const LIST_BOTTOM = 8;
const LIST_INSET = 10;
const LIST_INDENT = 12;

/** A badge's measures: its distance from the corner, a mark's radius, and a link symbol's size and top. */
const BADGE_INSET = 9;
const BADGE_RADIUS = 3.5;
const BADGE_TOP = 5;
const LINK_SIZE = 11;

/** Whether a filter is a switch whose value is kept with the diagram (DISL 0.4 §6.13.1). */
function isKeptSwitch(filter: { control?: string; persist?: boolean }): boolean {
  return filter.control === "switch" && filter.persist === true;
}

function fail(message: string): never {
  throw new Error(`compileNotation: ${message}`);
}

function isObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Splits a CEL expression at its top-level `+`, outside parentheses and string literals - the one
 * operator a label that concatenates text uses.
 */
function concatenationTerms(expression: string): string[] {
  const terms: string[] = [];
  let depth = 0;
  let quote: string | null = null;
  let start = 0;
  for (let index = 0; index < expression.length; index++) {
    const character = expression[index]!;
    if (quote !== null) {
      if (character === "\\") {
        fail(`the CEL "${expression}" escapes a character in a string, which no template can carry.`);
      }
      if (character === quote) {
        quote = null;
      }
    } else if (character === "'" || character === "\"") {
      quote = character;
    } else if (character === "(" || character === "[") {
      depth++;
    } else if (character === ")" || character === "]") {
      depth--;
    } else if (character === "+" && depth === 0) {
      terms.push(expression.slice(start, index).trim());
      start = index + 1;
    }
  }

  terms.push(expression.slice(start).trim());
  return terms;
}

/** The compiler's state for one specification and one set of bindings. */
class Compiler {
  private readonly wire: WireIds;
  private readonly nodeNames: readonly string[];
  private readonly system: DislCoordinateSystem | undefined;
  private readonly defaultViewpoint: [string, DislViewpoint] | undefined;

  constructor(
    private readonly spec: DislDocument,
    private readonly bindings: NotationBindings,
    private readonly options: CompileOptions,
  ) {
    const wire = bindings.wireIds === undefined ? {} : spec[bindings.wireIds];
    if (!isObject(wire)) {
      fail(`the specification has no "${bindings.wireIds}" block of wire ids.`);
    }

    this.wire = wire as WireIds;
    this.nodeNames = Object.keys(spec.notation.nodes);
    const viewpoints = Object.entries(spec.viewpoints ?? {});
    this.defaultViewpoint = viewpoints.find(([, viewpoint]) => viewpoint.default === true) ?? viewpoints[0];
    this.system = this.systemNamed(this.defaultViewpoint?.[1].coordinateSystem ?? spec.coordinates?.default);
  }

  compile(): DiagramDefinition {
    const spec = this.spec;
    const elementTypes = this.nodeNames.map((name) => this.elementType(name, spec.notation.nodes[name]!));
    const relationTypes = Object.entries(spec.notation.edges ?? {}).map(([name]) => this.relationType(name));
    const acyclic = Object.entries(spec.metamodel.relations ?? {})
      .filter(([, relation]) => relation.acyclic === true)
      .map(([name]) => ({ relationTypes: [this.typeId(name)] }));
    const actions = this.actions();
    const snap = this.snap();
    const rulers = this.rulers(this.system);
    const filter = this.filter();
    const switches = this.switches();
    const backgroundMenu = this.backgroundMenu(this.defaultViewpoint?.[0]);
    const dragging = this.dragging(elementTypes);

    const definition: DiagramDefinition = {
      elementTypes,
      relationTypes,
      ...(acyclic.length > 0 ? { acyclic } : {}),
      ...(actions.length > 0 ? { actions } : {}),
      ...(snap !== undefined ? { snap } : {}),
      ...(rulers.length > 0 || switches.length > 0 ? { chrome: { ...(rulers.length > 0 ? { rulers } : {}), ...(switches.length > 0 ? { switches } : {}) } } : {}),
      ...(filter !== undefined ? { filter } : {}),
      layout: this.layout(rulers, backgroundMenu, dragging),
      dragging,
      ...(backgroundMenu ? { backgroundMenu } : {}),
      ...(this.connectsOnRightDrag() ? { connectOnRightDrag: true } : {}),
    };

    return { ...definition, ...this.bindings.extras?.(spec) };
  }

  /**
   * Whether a connection is drawn with the right button from a node's body: the edges' `connect.pointer`
   * (DISL 0.3 §6.10). The library has one switch for the whole canvas, so every edge that states a pointer
   * must state that one; any other pointer is refused.
   */
  private connectsOnRightDrag(): boolean {
    const pointers = Object.entries(this.spec.notation.edges ?? {}).flatMap(([name, edge]) => (edge.connect?.pointer === undefined ? [] : [[name, edge.connect.pointer] as const]));
    for (const [name, pointer] of pointers) {
      if (pointer.button !== "secondary" || pointer.start !== "body" || (pointer.modifiers ?? []).length > 0) {
        fail(`edge "${name}" connects with the pointer ${JSON.stringify({ button: pointer.button, start: pointer.start, modifiers: pointer.modifiers })}; the library connects only on a right drag from the body.`);
      }
    }
    return pointers.length > 0;
  }

  // ---- names --------------------------------------------------------------------------------

  private typeId(name: string): string {
    return this.wire.types?.[name] ?? name.toLowerCase();
  }

  /** The concrete node types a metamodel type name stands for: itself, or its descendants with a notation. */
  private concreteTypes(name: string): readonly string[] {
    return this.nodeNames.filter((node) => this.isA(node, name));
  }

  private isA(type: string, ancestor: string): boolean {
    for (let current: string | undefined = type; current !== undefined; current = this.spec.metamodel.types[current]?.extends) {
      if (current === ancestor) {
        return true;
      }
    }

    return false;
  }

  /** Whether a type, or a type it extends, may hold children (DISL §4.6 containment). */
  private holdsChildren(type: string): boolean {
    for (let current: string | undefined = type; current !== undefined; current = this.spec.metamodel.types[current]?.extends) {
      if (this.spec.metamodel.types[current]?.children !== undefined) {
        return true;
      }
    }

    return false;
  }

  // ---- bindings -----------------------------------------------------------------------------

  /** A Bindable text or number as the library's binding: an attribute's payload path, or a CEL's template. */
  private binding(value: unknown, where: string): Binding {
    if (isObject(value) && typeof value.attribute === "string") {
      return { path: `payload.${value.attribute}` };
    }

    if (isObject(value) && typeof value.cel === "string") {
      return this.celBinding(value.cel, where);
    }

    return fail(`${where} binds ${JSON.stringify(value)}, which is neither an attribute nor CEL.`);
  }

  /**
   * A CEL expression as a path or template over the payload: string literals, `self.<attribute>` and
   * the module's `celPaths`, joined by `+`. Anything else is refused, naming the expression, because
   * the library cannot evaluate it and a guess would draw something the specification never said.
   */
  private celBinding(expression: string, where: string): Binding {
    const parts = concatenationTerms(expression).map((term): { text: string } | { path: string } => {
      const literal = /^'([^'{}]*)'$/.exec(term);
      if (literal !== null) {
        return { text: literal[1]! };
      }

      const attribute = /^self\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(term);
      if (attribute !== null) {
        return { path: `payload.${attribute[1]}` };
      }

      const path = this.bindings.celPaths[term];
      if (path !== undefined) {
        return { path };
      }

      return fail(`${where}: the CEL term "${term}" (in "${expression}") has no payload path in the module's celPaths.`);
    });

    const only = parts[0]!;
    if (parts.length === 1 && "path" in only) {
      return { path: only.path };
    }

    return { template: parts.map((part) => ("path" in part ? `{${part.path}}` : part.text)).join("") };
  }

  // ---- element types ------------------------------------------------------------------------

  private elementType(name: string, node: DislNodeNotation): ElementTypeDefinition {
    const where = `node "${name}"`;
    const { shape, segments } = this.shape(name, node);
    const classNames = this.bindings.classNames(name, node);
    const hint = this.showsDragValue() && this.movable(node) ? [this.dragHint()] : [];
    const labels = [...(node.labels ?? []).map((label) => this.label(name, label)), ...hint];
    const compartments = this.compartments(name, node);
    const { links, decorations } = this.badges(name, node);
    const { sizing, resize } = this.sizing(node);
    const editOnDrop = (this.spec.toolbox?.groups ?? []).some((group) => group.tools.some((tool) => tool.creates === name && tool.after === "editLabel"));

    return {
      id: this.typeId(name),
      shape,
      ...(classNames.length > 0 ? { classNames } : {}),
      ...(labels.length > 0 ? { labels } : {}),
      ...(decorations.length > 0 ? { decorations } : {}),
      ...(compartments.length > 0 ? { compartments } : {}),
      ...(links.length > 0 ? { links } : {}),
      ...(node.tooltip !== undefined ? { tooltip: this.binding(node.tooltip, `${where} tooltip`) } : {}),
      ...(node.accessibility !== undefined ? { accessibility: this.accessibility(node, where) } : {}),
      anchors: this.anchors(name, node),
      sizing,
      ...(resize !== undefined ? { resize } : {}),
      ...(this.movable(node) ? {} : { draggable: false }),
      ...(node.deletable === false ? { deletable: false } : {}),
      ...(editOnDrop ? { editOnDrop } : {}),
      ...(segments !== undefined ? { segments } : {}),
    };
  }

  private shape(name: string, node: DislNodeNotation): { shape: BuiltInShape; segments?: SegmentDeclaration } {
    const { name: shapeName, params } = shapeNameOf(node.shape);
    const custom: DislCustomShape | undefined = this.spec.notation.shapes?.[shapeName];
    const drawnAs = custom === undefined ? this.bindings.builtInShapes?.[shapeName] : undefined;
    if (drawnAs !== undefined) {
      return { shape: drawnAs };
    }

    const builtIn = libraryShapeOf(node.shape);
    if (builtIn !== undefined) {
      return { shape: builtIn };
    }

    const binding = this.bindings.customShapes?.[shapeName];
    if (custom === undefined || binding === undefined) {
      return fail(`node "${name}" is drawn as "${shapeName}", which is no built-in the library draws and no custom shape the module binds.`);
    }

    const segments = binding.segments?.({ shape: custom, params, bind: (value) => this.binding(value, `node "${name}" shape parameter`) });
    return segments !== undefined ? { shape: binding.shape, segments } : { shape: binding.shape };
  }

  private label(type: string, label: DislLabel): LabelDeclaration {
    const where = `node "${type}" label "${label.id}"`;
    const declaration: Record<string, unknown> = { text: this.binding(label.text, where) };

    const position = label.position;
    if (position === "outside-left") {
      if (label.distance !== undefined && label.distance !== BEFORE_GAP) {
        fail(`${where} sits ${label.distance} before its node; the library's before label sits ${BEFORE_GAP}.`);
      }
      declaration.placement = "before";
    } else if (position === "outside-right") {
      if (label.distance !== undefined) {
        fail(`${where} sits ${label.distance} beside its node; the library's beside label sits at its own gap.`);
      }
      declaration.placement = "beside";
    } else if (isObject(position)) {
      const [ax, ay] = position.anchor as readonly [number, number];
      const anchorTo = ay === 0 ? "top" : ay === 0.5 ? "centre" : ay === 1 ? "bottom" : undefined;
      // A label anchored at the left or right edge is the library's aligned line: its end at the edge,
      // inset by the offset's distance inward, which is what DISL's `align` at that anchor draws.
      const edge = ax === 0 ? "start" : ax === 1 ? "end" : undefined;
      if (anchorTo === undefined || (ax !== 0.5 && edge === undefined)) {
        fail(`${where} anchors at ${JSON.stringify(position.anchor)}; the library anchors a label at the top, centre or bottom of the middle or of an edge.`);
      }
      if (edge !== undefined && position.align !== edge) {
        fail(`${where} anchors at its ${edge === "start" ? "left" : "right"} edge ${position.align === undefined ? "with no align" : `aligned "${position.align}"`}; the library aligns such a line "${edge}".`);
      }
      declaration.anchorTo = anchorTo;
      const offset = position.offset as readonly [number, number] | undefined;
      if (edge !== undefined) {
        declaration.align = edge;
        declaration.insetX = edge === "start" ? (offset?.[0] ?? 0) : -(offset?.[0] ?? 0);
        declaration.offset = { x: 0, y: offset?.[1] ?? 0 };
      } else if (offset !== undefined) {
        declaration.offset = { x: offset[0], y: offset[1] };
      }
    } else if (position === "top" || position === "bottom") {
      // Inside, on the first or the last line of the node (DISL 0.4 §6.9).
      declaration.anchorTo = position;
      declaration.offset = { x: 0, y: position === "top" ? LABEL_LINE : -LABEL_LINE / 2 };
    } else if (position !== undefined && position !== "center") {
      fail(`${where} is positioned "${String(position)}", which the library has no placement for.`);
    }

    if (label.editable === true || label.editable === "inline" || label.editable === "multiline") {
      declaration.editable = true;
    }
    if (label.wrap === "word") {
      declaration.wrap = true;
    } else if (label.overflow === "ellipsis") {
      declaration.truncate = true;
    }
    if (label.tooltip !== undefined) {
      declaration.tooltip = this.binding(label.tooltip, `${where} tooltip`);
    }
    const when = this.labelVisibility(label.visible, where);
    if (when !== undefined) {
      declaration.when = when;
    }

    const className = this.bindings.labelClassName?.(type, label);
    if (className !== undefined) {
      declaration.className = className;
    }
    const editorBox = this.bindings.labelEditorBox?.(type, label);
    if (editorBox !== undefined) {
      declaration.editorBox = editorBox;
    }

    return declaration as unknown as LabelDeclaration;
  }

  /**
   * A label's `visible` as the library's `when`: a text the label binds being non-empty
   * (`<term> != ''`, the term a payload path as {@link celBinding} reads it). Any other condition is
   * refused, naming it; a label without one, or visible `true`, is always drawn.
   */
  private labelVisibility(visible: DislLabel["visible"], where: string): Condition | undefined {
    if (visible === undefined || visible === true) {
      return undefined;
    }

    const expression = typeof visible === "string" ? visible : isObject(visible) && typeof visible.cel === "string" ? visible.cel : undefined;
    const bound = expression === undefined ? undefined : this.bindings.celConditions?.[expression];
    if (bound !== undefined) {
      return bound;
    }

    const term = expression === undefined ? null : /^(.+?)\s*!=\s*''$/.exec(expression.trim());
    const binding = term === null ? undefined : this.celBinding(term[1]!.trim(), `${where} visible`);
    if (binding === undefined || !("path" in binding)) {
      return fail(`${where} is visible when ${JSON.stringify(visible)}; it has no condition in the module's celConditions, and the library draws a label otherwise only when a payload text is non-empty.`);
    }

    return { path: binding.path, is: "non-empty" };
  }

  private accessibility(node: DislNodeNotation, where: string): NonNullable<ElementTypeDefinition["accessibility"]> {
    const { role, name } = node.accessibility!;
    return {
      ...(role !== undefined ? { role } : {}),
      ...(this.bindings.focusable === true ? { focusable: true } : {}),
      ...(name !== undefined ? { label: this.binding(name, `${where} accessible name`) } : {}),
    } as NonNullable<ElementTypeDefinition["accessibility"]>;
  }

  /**
   * Where a line may attach. DISL never draws an anchor as a handle, so the library's handles are
   * hidden wherever its kind would draw them (`compass`, `along`); `edge` draws none to hide.
   */
  private anchors(name: string, node: DislNodeNotation): AnchorSet {
    if (node.connectable === false) {
      return { kind: "edge", enabled: false, visible: false };
    }

    const anchors = node.anchors;
    if (anchors?.mode === "fixed") {
      const points = anchors.points ?? [];
      const atCompass = points.every((point) => {
        const fraction = COMPASS[point.id as CompassPosition];
        return fraction !== undefined && fraction[0] === point.x && fraction[1] === point.y;
      });
      if (!atCompass && points.some((point) => COMPASS[point.id as CompassPosition] !== undefined)) {
        const misplaced = points.find((point) => COMPASS[point.id as CompassPosition] !== undefined)!;
        fail(`node "${name}" anchor "${misplaced.id}" at (${misplaced.x}, ${misplaced.y}) is no compass position the library has.`);
      }
      if (!atCompass) {
        return this.namedSideAnchors(name, points);
      }
      const positions = points.map((point) => point.id as CompassPosition);
      const drawnFrom = anchors.drawnFrom;
      return { kind: "compass", positions, ...(drawnFrom === "outline" ? { attachDrawnBy: "edge" as const } : {}), visible: false };
    }

    if (anchors?.mode === "sides") {
      const sides = [...(anchors.sides ?? ["top", "right", "bottom", "left"])].sort().join(",");
      const edgeSides = sides === "bottom,top" ? "vertical" : sides === "left,right" ? "horizontal" : sides === "bottom,left,right,top" ? undefined : fail(`node "${name}" attaches on ${sides}, which the library's edge anchors cannot narrow to.`);
      return edgeSides !== undefined ? { kind: "edge", edgeSides } : { kind: "edge" };
    }

    if (anchors !== undefined) {
      return fail(`node "${name}" anchors in mode "${String(anchors.mode)}", which this compiler does not map.`);
    }

    // No anchors of its own: an edge that attaches to a part of this node says where - anywhere along
    // the part's stretch of the sides its `side` attribute may name.
    const sides = new Set<string>();
    for (const [edgeName, edge] of Object.entries(this.spec.notation.edges ?? {})) {
      const relation = this.relation(edgeName);
      for (const end of ["source", "target"] as const) {
        const anchoring = edge.anchoring?.[end];
        if (anchoring?.mode !== "part" || !typeList(relation[end]).some((type) => this.isA(name, type))) {
          continue;
        }
        const side = anchoring.side;
        const sideAttribute = isObject(side) && "attribute" in side ? side.attribute : fail(`edge "${edgeName}" ${end} side must be bound to an attribute to say which sides of "${name}" it attaches to.`);
        const attribute = relation.attributes?.[sideAttribute];
        const values = attribute === undefined ? undefined : this.spec.metamodel.enums?.[attribute.type]?.values;
        Object.keys(values ?? fail(`edge "${edgeName}" ${end} side attribute "${sideAttribute}" is no enum of sides.`)).forEach((value) => sides.add(value));
      }
    }

    if (sides.size === 0) {
      // Nothing stated and nothing implied: DISL's default, a line attaching anywhere on the outline.
      return { kind: "edge", visible: false };
    }

    return { kind: "along", edges: [...sides] as ("top" | "bottom" | "left" | "right")[], regions: "segments", visible: false };
  }

  /**
   * Fixed anchors named for what they are rather than where (a timeline's `begin` and `end`): each a
   * named fraction of the side it lies on, so an edge's `connect.from` can name it. A line's other end
   * attaches only on the sides these lie on - the left and right of a notation read left to right.
   */
  private namedSideAnchors(name: string, points: readonly { id: string; x: number; y: number }[]): AnchorSet {
    const fractions = points.map((point): SideFraction => {
      // `at` runs clockwise round the bounds, as the library measures it.
      const side = point.x === 0 ? "left" : point.x === 1 ? "right" : point.y === 0 ? "top" : point.y === 1 ? "bottom" : undefined;
      if (side === undefined) {
        return fail(`node "${name}" anchor "${point.id}" at (${point.x}, ${point.y}) lies on no side of its bounds.`);
      }
      const at = side === "left" ? 1 - point.y : side === "right" ? point.y : side === "top" ? point.x : 1 - point.x;
      return { side, at, name: point.id };
    });
    const sides = new Set(fractions.map((fraction) => fraction.side));
    const horizontal = [...sides].every((side) => side === "left" || side === "right");
    const vertical = [...sides].every((side) => side === "top" || side === "bottom");
    return { kind: "sides", fractions, ...(horizontal ? { edgeSides: "horizontal" as const } : vertical ? { edgeSides: "vertical" as const } : {}) };
  }

  /**
   * `user` when the node may be resized (its size says so and its placement does not forbid it),
   * else `model`; `resize: "both"` when its size is resizable both ways - a horizontal-only size is
   * the library's default.
   */
  private sizing(node: DislNodeNotation): { sizing: ElementTypeDefinition["sizing"]; resize?: "both" } {
    const resizable = node.size?.resizable ?? false;
    if (resizable === "vertical") {
      fail("a node resizable only vertically has no library equivalent.");
    }

    const placement = node.placement?.resizable;
    const placementAllows = placement === undefined || placement === true || (isObject(placement) && (placement.x === true || placement.y === true));
    return {
      sizing: resizable !== false && placementAllows ? "user" : "model",
      ...(resizable === true ? { resize: "both" as const } : {}),
    };
  }

  private movable(node: DislNodeNotation): boolean {
    const movable = node.placement?.movable;
    return movable === undefined || movable === true || (isObject(movable) && (movable.x === true || movable.y === true));
  }

  // ---- relation types -----------------------------------------------------------------------

  private relation(name: string): DislRelation {
    return this.spec.metamodel.relations?.[name] ?? fail(`edge "${name}" has no relation in the metamodel.`);
  }

  private relationType(name: string): RelationTypeDefinition {
    const edge = this.spec.notation.edges![name]!;
    const relation = this.relation(name);
    const routing = edge.line?.routing ?? "straight";
    const target = edge.targetMarker ?? (relation.directed === false ? "none" : "arrowFilled");
    const source = edge.sourceMarker ?? "none";
    const ends = [edge.anchoring?.source, edge.anchoring?.target];
    const className = this.bindings.relationClassName?.(name);
    const lineClassName = this.bindings.relationLineClassName?.(name);
    const hitClassName = this.bindings.relationHitClassName?.(name);
    const customRoute = this.bindings.customRoutes?.[name];
    const label = this.edgeLabel(name, edge);

    // Only a type that may hold children can be the parent end of a relation derived from containment.
    const derivedFromParent = isObject(relation.derived) && relation.derived.source === "item.parent";
    const sourceTypes = typeList(relation.source).flatMap((type) => this.concreteTypes(type)).filter((type) => !derivedFromParent || this.holdsChildren(type));
    const targetTypes = typeList(relation.target).flatMap((type) => this.concreteTypes(type));
    const endpoint = (types: readonly string[], end: "source" | "target"): EndpointConstraint => {
      const anchors = this.bindings.endpointAnchors?.(name, end) ?? this.startingAnchors(edge, types, end);
      return { elementTypes: types.map((type) => this.typeId(type)), ...(anchors !== undefined ? { anchors } : {}) };
    };

    // A stored relation that allows no parallel instances is one per pair: per direction when it
    // is directed. A derived relation's count follows from its derivation and is not declared.
    const perPair = relation.derived === undefined && relation.allowParallel === false ? (relation.directed === false ? "unordered" : "ordered") : undefined;
    const held = this.heldOnce(relation);

    return {
      id: this.typeId(name),
      route: customRoute ?? libraryRouteOf(routing),
      style: { ...(source !== "none" ? { startMarker: libraryMarkerOf(source) } : {}), endMarker: libraryMarkerOf(target) },
      ...(className !== undefined ? { className } : {}),
      ...(lineClassName !== undefined ? { lineClassName } : {}),
      ...(hitClassName !== undefined ? { hitClassName } : {}),
      ...(label !== undefined ? { label } : {}),
      ...(edge.selectable === false ? { selectable: false } : {}),
      ...(ends.some((end) => end?.movable === true) ? { movableEnds: true } : {}),
      ...(ends.some((end) => end?.mode === "part") ? { hideWhenAttachmentHidden: true } : {}),
      endpoints: {
        source: endpoint(sourceTypes, "source"),
        target: endpoint(targetTypes, "target"),
        allowSelf: relation.allowSelfLoops === true,
        ...(perPair !== undefined ? { cardinality: { perPair } } : {}),
        ...(held !== undefined ? { cardinality: held } : {}),
      },
      ...(this.createsOnEmptyRelease(name, edge) ? { emptyRelease: "complete" as const } : {}),
    };
  }

  /**
   * How many of a drawable derived relation one element may have (DISL 0.4 §4.11.4). A relation derived
   * from a key on the item that names its other end is one per item: the item is the end that holds it,
   * and an end holds one key. Only a relation a gesture can draw (`edits.connect`) is bounded here, since
   * the bound exists to refuse the gesture before it is made. A 0.3 specification keeps what it had: there
   * a drawn line onto an element that has one already moves it, which its module answers.
   */
  private heldOnce(relation: DislRelation): { maxFromSource: 1 } | { maxIntoTarget: 1 } | undefined {
    const derived = relation.derived;
    if (this.spec.disl !== "0.4" || !isObject(derived) || !isObject(derived.edits) || typeof derived.edits.connect !== "string") {
      return undefined;
    }

    const reference = /^item\.[A-Za-z_][A-Za-z0-9_]*$/;
    if (derived.target === "item" && typeof derived.source === "string" && reference.test(derived.source)) {
      return { maxIntoTarget: 1 };
    }
    if (derived.source === "item" && typeof derived.target === "string" && reference.test(derived.target)) {
      return { maxFromSource: 1 };
    }

    return undefined;
  }

  // ---- lists and badges (DISL 0.4) ----------------------------------------------------------

  /**
   * A CEL term a row binds, as a path within the row: `item.<attribute>`, or the module's
   * `rowPaths`. Anything else is refused, naming it.
   */
  private rowPath(value: unknown, where: string): string {
    const expression = isObject(value) && typeof value.cel === "string" ? value.cel.trim() : undefined;
    if (expression === undefined) {
      return fail(`${where} binds ${JSON.stringify(value)}; a row binds CEL over its item.`);
    }

    const bound = this.bindings.rowPaths?.[expression];
    if (bound !== undefined) {
      return bound;
    }

    const attribute = /^item\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(expression);
    return attribute !== null ? attribute[1]! : fail(`${where}: the CEL "${expression}" has no path within a row in the module's rowPaths.`);
  }

  /**
   * A node's lists (DISL 0.4 §6.9). Its children in a slot are the payload's list of that name; a CEL
   * list is the payload path the module's `celPaths` gives it. Grouped by an enum attribute of the
   * items, the groups are the enum's values in its order under their labels. Which headings are folded
   * is the payload's `collapsed`: the backend applies the definition's defaults and what the reader set.
   * A list with no title, no groups and nothing to fold it by is drawn as its rows alone.
   */
  private compartments(name: string, node: DislNodeNotation): CompartmentDeclaration[] {
    const declared = node.compartments ?? [];
    if (declared.length === 0) {
      return [];
    }

    // Beneath the lowest line of text the node's labels write.
    const lines = (node.labels ?? []).map((label) => {
      const position = label.position;
      return isObject(position) ? ((position.offset as readonly [number, number] | undefined)?.[1] ?? 0) : position === "top" ? LABEL_LINE : 0;
    });
    const top = Math.max(LABEL_LINE, ...lines) + LIST_GAP;

    return declared.map((compartment: DislCompartment): CompartmentDeclaration => {
      const where = `node "${name}" list "${compartment.id}"`;
      const items = compartment.items;
      const rows = "cel" in items
        ? this.bindings.celPaths[items.cel] ?? fail(`${where}: the CEL list "${items.cel}" has no payload path in the module's celPaths.`)
        : `payload.${items.slot}`;
      const text = compartment.itemText === undefined ? fail(`${where} states no itemText.`) : this.rowPath(compartment.itemText, `${where} itemText`);
      const order = compartment.itemOrder;
      const plain = compartment.title === undefined && compartment.groupBy === undefined && compartment.collapsible !== true;

      return {
        id: compartment.id,
        rows,
        rowId: "id",
        text: { path: text },
        ...(compartment.itemLink !== undefined ? { link: this.rowPath(compartment.itemLink, `${where} itemLink`) } : {}),
        ...(order !== undefined ? { orderBy: { path: this.rowPath({ cel: order.by }, `${where} itemOrder`), direction: order.direction ?? "ascending" } } : {}),
        ...(compartment.groupBy !== undefined ? { groupBy: this.groups(compartment, where) } : {}),
        ...(compartment.title !== undefined ? { title: compartment.title } : {}),
        ...(plain ? { heading: "none" as const } : {}),
        collapsed: "payload.collapsed",
        top,
        headingHeight: LIST_HEADING,
        rowHeight: LIST_ROW,
        bottom: LIST_BOTTOM,
        insetX: LIST_INSET,
        rowIndent: plain ? 0 : LIST_INDENT,
      };
    });
  }

  /** A list's groups: the values of the enum the items' attribute has, in the enum's order. */
  private groups(compartment: DislCompartment, where: string): NonNullable<CompartmentDeclaration["groupBy"]> {
    const items = compartment.items;
    const attribute = compartment.groupBy!.attribute;
    const type = "children" in items ? this.spec.metamodel.types[items.children[0] ?? ""]?.attributes?.[attribute]?.type : undefined;
    const values = type === undefined ? undefined : this.spec.metamodel.enums?.[type]?.values;
    if (values === undefined) {
      return fail(`${where} groups by "${attribute}", which is no enum attribute of its items.`);
    }

    return {
      path: attribute,
      groups: Object.entries(values).map(([value, member]) => ({ value, title: member.label ?? value })),
      otherTitle: "Other",
    };
  }

  /**
   * A node's badges (DISL 0.4 §6.9). One whose press opens a link - its operation's one action is
   * `open` of an attribute - is the library's link symbol for that attribute, drawn where there is a
   * link and nowhere else. One that runs nothing is a mark, shown while its condition holds. Any other
   * badge is refused, naming it.
   */
  private badges(name: string, node: DislNodeNotation): { links: LinkDeclaration[]; decorations: DecorationDeclaration[] } {
    const links: LinkDeclaration[] = [];
    const decorations: DecorationDeclaration[] = [];
    for (const badge of node.badges ?? []) {
      const where = `node "${name}" badge "${badge.id}"`;
      const right = badge.position === "top-right";
      if (!right && badge.position !== "top-left") {
        fail(`${where} sits "${String(badge.position)}"; the library places a badge at the top left or the top right.`);
      }

      if (badge.onClick !== undefined) {
        const operation = this.spec.behavior?.operations?.[badge.onClick];
        const actions = isObject(operation) && Array.isArray(operation.actions) ? (operation.actions as readonly unknown[]) : [];
        const opened = actions.length === 1 && isObject(actions[0]) && typeof actions[0].open === "string" ? /^self\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(actions[0].open) : null;
        if (opened === null || !right) {
          fail(`${where} runs "${badge.onClick}"; the library draws a pressed badge only as a link symbol at the top right, opening one attribute.`);
        }
        links.push({ id: badge.id, link: `payload.${opened[1]}`, at: { right: BADGE_INSET + LINK_SIZE, top: BADGE_TOP } });
        continue;
      }

      const visible = typeof badge.visible === "string" ? badge.visible : isObject(badge.visible) ? badge.visible.cel : undefined;
      const when = visible === undefined ? undefined : this.bindings.celConditions?.[visible] ?? fail(`${where} shows when "${visible}", which has no condition in the module's celConditions.`);
      const className = this.bindings.badgeClassName?.(name, badge);
      const tooltip = badge.tooltip === undefined ? undefined : typeof badge.tooltip === "string" ? { template: badge.tooltip } : this.binding(badge.tooltip, `${where} tooltip`);
      const said = typeof badge.tooltip === "string" ? badge.tooltip.split(".")[0]! : badge.id;
      decorations.push({
        glyph: "circle",
        anchor: "canvas",
        from: {
          x: right ? { path: "bounds.right", number: { plus: -BADGE_INSET } } : { path: "bounds.left", number: { plus: BADGE_INSET } },
          y: { path: "bounds.top", number: { plus: BADGE_INSET } },
        },
        radius: BADGE_RADIUS,
        ...(className !== undefined ? { className } : {}),
        ...(tooltip !== undefined ? { tooltip } : {}),
        accessibility: { role: "img", label: { template: said } },
        ...(when !== undefined ? { when } : {}),
      });
    }

    return { links, decorations };
  }

  /**
   * The anchors a drawn connection may start from at this end: the named anchors the edge's `connect.from`
   * maps (DISL 0.3 §6.10), whichever end of the relation each starts, in the order the end's node lists
   * them. A gesture's direction is the module's to read from the anchor it lifted from.
   */
  private startingAnchors(edge: DislEdgeNotation, types: readonly string[], end: "source" | "target"): readonly string[] | undefined {
    const from = edge.connect?.from;
    if (from === undefined || end !== "source") {
      return undefined;
    }

    const listed = types.flatMap((type) => (this.spec.notation.nodes[type]?.anchors?.points ?? []).map((point) => point.id));
    const named = Object.keys(from);
    return [...new Set([...listed.filter((id) => named.includes(id)), ...named])];
  }

  /**
   * Whether a connect gesture released on empty canvas creates the missing end: the tool the edge's
   * `connect.tool` names, else a library tool creating this relation, has a `createTarget` or a
   * `createSource` (DISL 0.3 §7.2). The library then raises the release for the module to answer.
   */
  private createsOnEmptyRelease(name: string, edge: DislEdgeNotation): boolean {
    const tools = this.spec.toolbox?.tools ?? {};
    const named = edge.connect?.tool;
    const tool = named !== undefined
      ? tools[named] ?? (this.spec.toolbox?.groups ?? []).flatMap((group) => group.tools).find((each) => each.id === named) ?? fail(`edge "${name}" connects with the tool "${named}", which the toolbox does not declare.`)
      : Object.values(tools).find((each) => each.creates === name);
    return tool !== undefined && (tool.createTarget !== undefined || tool.createSource !== undefined);
  }

  /**
   * The edge's label along its route: at its start, middle or end, the side above or below the line at its
   * distance. The text is the connection's own label, which the model carries; only an attribute can be it.
   */
  private edgeLabel(name: string, edge: DislEdgeNotation): RouteLabelRule | undefined {
    const labels = edge.labels ?? [];
    if (labels.length === 0) {
      return undefined;
    }

    const where = `edge "${name}" label`;
    if (labels.length > 1) {
      fail(`${where}s: the library draws one label along a line; the specification declares ${labels.length}.`);
    }

    const label = labels[0]!;
    if (!isObject(label.text) || !("attribute" in label.text) || typeof label.text.attribute !== "string") {
      fail(`${where} "${label.id}" binds ${JSON.stringify(label.text)}; the library draws a line's label from the connection's own label, an attribute.`);
    }

    const at = label.at ?? "middle";
    const placement = at === "start" ? "source" : at === "middle" ? "midpoint" : at === "end" ? "target" : fail(`${where} "${label.id}" sits at ${JSON.stringify(at)}; the library places a line's label at its start, middle or end.`);
    const distance = label.distance ?? 0;
    const side = label.side ?? "on";
    const offset = side === "above" ? -distance : side === "below" ? distance : side === "on" && distance === 0 ? 0 : fail(`${where} "${label.id}" sits ${distance} ${side} its line, which the library cannot place.`);
    const editable = label.editable === true || label.editable === "inline" || label.editable === "multiline";
    return { placement, ...(offset !== 0 ? { offset } : {}), ...(editable ? { editable } : {}) };
  }

  // ---- actions ------------------------------------------------------------------------------

  /** The concrete node types or relation a menu is for, or null for the canvas and gesture menus. */
  private menuTargets(menu: DislContextMenu): { kind: "element" | "connection"; types: readonly string[] } | null {
    if (menu.for.includes("diagram") || menu.for.includes("connection")) {
      return null;
    }

    if (menu.for.every((name) => this.spec.metamodel.relations?.[name] !== undefined)) {
      return { kind: "connection", types: menu.for };
    }

    // A menu for types the notation draws as rows of a list, not as nodes (DISL 0.4 §6.9), declares
    // nothing on the canvas: a row has no gesture of its own, and its menu is the backend's.
    const types = menu.for.flatMap((name) => this.concreteTypes(name));
    return types.length === 0 ? null : { kind: "element", types };
  }

  /**
   * The actions a key or a canvas gesture invokes, from the context menus' entries. A double-click is
   * the library's `activate` gesture and invokes the node's `doubleClick` entry (DISL's default:
   * `editLabel`); the Delete key is the library's `delete` gesture. Gesture-invoked actions come
   * first, then those only a shortcut invokes, each in menu order.
   */
  private actions(): ActionDeclaration[] {
    const found: { id: string; kind: "element" | "connection"; types: Set<string>; keys: string[]; gestures: ("activate" | "delete")[]; rank: number; shortcut?: string }[] = [];
    for (const menu of this.spec.toolbox?.contextMenus ?? []) {
      const targets = this.menuTargets(menu);
      if (targets === null) {
        continue;
      }

      for (const tool of menu.tools) {
        const activates = tool.kind === (targets.kind === "element" ? this.doubleClickOf(targets.types) : undefined);
        const deletes = tool.kind === "delete";
        const keys = tool.shortcut !== undefined && !(deletes && tool.shortcut === "Delete") ? [keyOf(tool.shortcut)] : [];
        if (!activates && !deletes && keys.length === 0) {
          continue;
        }

        const key = tool.operation ?? tool.kind;
        for (const forType of menu.for) {
          const id = this.wire.actions?.[`${forType}/${key}`] ?? this.wire.actions?.[key] ?? (this.bindings.wireIds === undefined ? key : fail(`the "${key}" entry of the menu for ${forType} has no wire id in ${this.bindings.wireIds}.actions.`));
          let action = found.find((entry) => entry.id === id && entry.kind === targets.kind);
          if (action === undefined) {
            action = { id, kind: targets.kind, types: new Set(), keys: [], gestures: [], rank: activates ? 0 : deletes ? 1 : 2 };
            found.push(action);
          }
          if (action.shortcut === undefined && tool.shortcut !== undefined) {
            action.shortcut = keyOf(tool.shortcut);
          }
          (targets.kind === "element" ? this.concreteTypes(forType) : [forType]).forEach((type) => action!.types.add(type));
          keys.filter((each) => !action!.keys.includes(each)).forEach((each) => action!.keys.push(each));
          if (activates && !action.gestures.includes("activate")) {
            action.gestures.push("activate");
          }
          if (deletes && !action.gestures.includes("delete")) {
            action.gestures.push("delete");
          }
        }
      }
    }

    // A double-click that runs an operation no menu lists (DISL §6.9 `doubleClick`) is still the activate gesture.
    for (const type of this.nodeNames) {
      const operation = this.spec.notation.nodes[type]?.doubleClick;
      if (operation === undefined || this.spec.behavior?.operations?.[operation] === undefined
        || found.some((action) => action.kind === "element" && action.types.has(type) && action.gestures.includes("activate"))) {
        continue;
      }

      const id = this.wire.actions?.[`${type}/${operation}`] ?? this.wire.actions?.[operation] ?? (this.bindings.wireIds === undefined ? operation : fail(`the double-click operation "${operation}" of ${type} has no wire id in ${this.bindings.wireIds}.actions.`));
      let action = found.find((entry) => entry.id === id && entry.kind === "element");
      if (action === undefined) {
        action = { id, kind: "element", types: new Set(), keys: [], gestures: ["activate"], rank: 0 };
        found.push(action);
      } else if (!action.gestures.includes("activate")) {
        action.gestures.push("activate");
      }
      action.types.add(type);
    }

    const allNodes = new Set(this.nodeNames);
    return [...found].sort((a, b) => a.rank - b.rank).map((action): ActionDeclaration => {
      const invokedBy: ActionInvocation[] = [
        ...action.keys.map((key): ActionInvocation => ({ kind: "shortcut", key })),
        ...action.gestures.map((gesture): ActionInvocation => ({ kind: "gesture", gesture })),
      ];
      const narrowed = action.kind === "element" && [...allNodes].some((type) => !action.types.has(type));
      const declaration: ActionDeclaration = {
        id: action.id,
        invokedBy,
        appliesTo: [
          action.kind === "element"
            ? { kind: "element", ...(narrowed ? { elementTypes: [...action.types].map((type) => this.typeId(type)) } : {}) }
            : { kind: "connection" },
        ],
      };
      return this.bindings.action?.(declaration, action.shortcut !== undefined ? { shortcut: action.shortcut } : {}) ?? declaration;
    });
  }

  /** The entry a double-click invokes on these types: their shared `doubleClick`, DISL's default `editLabel`. */
  private doubleClickOf(types: readonly string[]): string {
    const chosen = new Set(types.map((type) => this.spec.notation.nodes[type]?.doubleClick ?? "editLabel"));
    if (chosen.size !== 1) {
      fail(`a menu covers types whose double-click invokes different entries (${[...chosen].join(", ")}).`);
    }

    return [...chosen][0]!;
  }

  // ---- canvas -------------------------------------------------------------------------------

  private systemNamed(name: string | undefined): DislCoordinateSystem | undefined {
    return name === undefined ? undefined : this.spec.coordinates?.systems[name] ?? fail(`coordinate system "${name}" is not declared.`);
  }

  private axis(axis: string | DislAxis): DislAxis {
    return typeof axis === "string" ? this.spec.coordinates?.axes?.[axis] ?? fail(`axis "${axis}" is not declared.`) : axis;
  }

  /** A diagram attribute's value: the option given, else the attribute's default. */
  private diagramValue(value: Bindable<string>): string {
    if (typeof value === "string") {
      return value;
    }

    if ("attribute" in value) {
      const attribute = value.attribute;
      const given = this.options.diagram?.[attribute] ?? this.spec.metamodel.diagram?.attributes?.[attribute]?.default;
      return typeof given === "string" ? given : fail(`the diagram attribute "${attribute}" has no value to compile with.`);
    }

    return fail(`${JSON.stringify(value)} is bound to something other than a diagram attribute.`);
  }

  private unitMonths(unit: string): number {
    return UNIT_MONTHS[unit] ?? fail(`the time unit "${unit}" has no length in months.`);
  }

  /** One snap step of the default system per axis, each element snapping from its own origin. */
  private snap(): SnapDeclaration | undefined {
    const system = this.system as (DislCoordinateSystem & { snapping?: Record<string, unknown> }) | undefined;
    const snapping = system?.snapping;
    if (system === undefined || snapping === undefined) {
      return undefined;
    }

    const origins = this.bindings.snapOrigins;
    const step = (axisName: "x" | "y"): number => {
      const rule = snapping[axisName] as Record<string, unknown> | undefined;
      const axis = this.axis(system[axisName]);
      if (isObject(rule?.calendar) && isObject(axis.scale) && JSON.stringify(rule.calendar.unit) === JSON.stringify(axis.scale.unit)) {
        // A calendar snap in the axis's own unit is one step of the axis: `scale.size` canvas units.
        return axis.scale.size;
      }
      if (isObject(rule?.grid) && typeof rule.grid.spacing === "number" && typeof axis.scale === "number") {
        return rule.grid.spacing * axis.scale;
      }
      return fail(`the ${axisName} snapping rule ${JSON.stringify(rule)} has no step this compiler can state.`);
    };

    const axisSnap = (axisName: "x" | "y"): SnapAxis | undefined => {
      const bound = this.bindings.snapAxes?.[axisName];
      if (bound !== undefined) {
        return bound;
      }
      // An axis that does not snap (`"none"`) moves freely: it has no resting lines to state.
      if (snapping[axisName] === "none") {
        return undefined;
      }
      return { step: step(axisName), ...(origins !== undefined ? { origin: { path: origins[axisName] } } : {}) };
    };
    const y = axisSnap("y");
    const x = axisSnap("x");
    return x === undefined && y === undefined ? undefined : { ...(y !== undefined ? { y } : {}), ...(x !== undefined ? { x } : {}) };
  }

  /** Whether a drag shows the value it would rest on: the default system's `snapping.feedback.showValue`. */
  private showsDragValue(): boolean {
    const snapping = (this.system as (DislCoordinateSystem & { snapping?: Record<string, unknown> }) | undefined)?.snapping;
    return isObject(snapping?.feedback) && snapping.feedback.showValue === true;
  }

  private dragHint(): LabelDeclaration {
    return this.bindings.dragHint ?? fail("the snapping shows the value a drag would rest on; the module states no dragHint to show it with.");
  }

  /** The view-fixed rulers of a coordinate system's axes. */
  private rulers(system: DislCoordinateSystem | undefined): RulerDeclaration[] {
    if (system === undefined || this.bindings.ownRulers === true) {
      return [];
    }

    return (["x", "y"] as const).flatMap((axisName): RulerDeclaration[] => {
      const axis = this.axis(system[axisName]);
      const ruler = axis.ruler;
      if (ruler === undefined || ruler.visible === false) {
        return [];
      }

      if (axisName !== "x" || ruler.position !== "bottom" || ruler.attach !== "view" || axis.valueType !== "yearMonth" || !isObject(axis.scale)) {
        return fail("only a view-fixed bottom ruler over a month-indexed horizontal axis is compiled.");
      }

      const unitMonths = this.unitMonths(this.diagramValue(axis.scale.unit));
      const minUnit = ruler.minUnit;
      const finest = minUnit === undefined ? 1 : this.unitMonths(this.diagramValue(minUnit));
      const spacings = new Set(ruler.levels.map((level) => level.minSpacingPx));
      if (spacings.size !== 1) {
        fail("the ruler's levels ask for different spacings; the library spaces every rung alike.");
      }

      const ladder = ruler.levels
        .filter((level) => this.unitMonths(level.unit) >= finest)
        .map((level): RulerRung => ({
          every: RUNG_STEPS[level.unit] ?? fail(`the ruler level "${level.unit}" has no library rung.`),
          label: level.format.replaceAll("uuuu", "yyyy") as RulerRung["label"],
        }));
      const [minSpacingPx] = [...spacings];
      return [{
        orientation: "horizontal",
        edge: "bottom",
        scale: { unit: "month", unitsPerStep: axis.scale.size / unitMonths, origin: String(axis.origin) },
        ladder,
        ...(minSpacingPx !== undefined ? { minSpacingPx } : {}),
      }];
    });
  }

  /** The canvas's one filter box, keyed by the attribute it filters on, with the legend beneath it. */
  private filter(): FilterDeclaration | undefined {
    const filters = Object.entries(this.spec.notation.canvas?.filters ?? {}).filter(([, filter]) => !isKeptSwitch(filter));
    if (filters.length === 0) {
      return undefined;
    }

    if (filters.length > 1) {
      fail("the library draws one filter box; the specification declares several filters.");
    }

    const [attribute, filter] = filters[0]!;
    const legend = this.spec.notation.canvas?.legend;
    const entries = legend === undefined || legend.visible === false
      ? []
      : legend.entries.flatMap((enumName) => {
        const values = this.spec.metamodel.enums?.[enumName]?.values ?? fail(`the legend names "${enumName}", which is no enum.`);
        return Object.entries(values).map(([value, entry]) => ({
          caption: entry.label ?? value,
          swatchClass: this.bindings.legendSwatchClass?.(enumName, value) ?? fail("the legend needs the module's legendSwatchClass."),
        }));
      });

    return {
      field: `payload.${attribute}`,
      label: filter.label,
      ...(filter.appliesTo !== undefined ? { elementTypes: filter.appliesTo.map((type) => this.typeId(type)) } : {}),
      ...(entries.length > 0 ? { legend: entries } : {}),
    };
  }

  /**
   * The canvas's switches: each filter drawn as a switch whose value is kept with the diagram (DISL 0.4
   * §6.13.1). The canvas holds no value for one; it shows what the model's background says under the
   * name the persistence binds the filter to, and raises a request under the filter's own name.
   */
  private switches(): SwitchDeclaration[] {
    return Object.entries(this.spec.notation.canvas?.filters ?? {})
      .filter(([, filter]) => isKeptSwitch(filter))
      .map(([name, filter]) => {
        const bound = this.spec.persistence?.view?.bind?.filters?.values?.[name] ?? fail(`the kept filter "${name}" is bound to no name by persistence.view.bind.filters.`);
        return { id: name, caption: filter.label, on: `payload.${bound}` };
      });
  }

  /** Whether the canvas background has a menu in a viewpoint: a set for `diagram` whose `when` holds there. */
  private backgroundMenu(viewpoint: string | undefined): boolean {
    return (this.spec.toolbox?.contextMenus ?? []).some((menu) => {
      if (!menu.for.includes("diagram")) {
        return false;
      }
      if (menu.when === undefined) {
        return true;
      }
      const match = /^env\.viewpoint\s*==\s*'([^']+)'$/.exec(menu.when.trim());
      return match !== null ? match[1] === viewpoint : fail(`the canvas menu's when "${menu.when}" is not a viewpoint test.`);
    });
  }

  private dragging(elementTypes: readonly ElementTypeDefinition[]): DiagramDefinition["dragging"] {
    return elementTypes.some((type) => type.draggable !== false) ? "enabled" : "disabled";
  }

  // ---- layout and viewpoints ----------------------------------------------------------------

  /**
   * The canvas's layout mode for a layout algorithm: the standard `rowPacked` (DISL 0.3 §10.1) packs rows;
   * the host computes `rows` (§10.2), `tidyTree` (§10.3) and a plugin's layout and sends positions, and
   * `none` leaves them where they are, so the canvas draws them by hand.
   */
  private layoutMode(algorithmName: string | undefined): LayoutMode {
    if (algorithmName === undefined) {
      return "manual";
    }

    const algorithm = this.spec.layout?.algorithms?.[algorithmName] ?? fail(`layout "${algorithmName}" is not declared.`);
    if (algorithm.algorithm === "rowPacked") {
      return "row-packed";
    }

    if (algorithm.algorithm.startsWith("plugin:") || ["none", "rows", "tidyTree"].includes(algorithm.algorithm)) {
      return "manual";
    }

    return fail(`layout algorithm "${algorithm.algorithm}" has no library mode.`);
  }

  private layout(
    rulers: readonly RulerDeclaration[],
    backgroundMenu: boolean,
    dragging: DiagramDefinition["dragging"],
  ): LayoutDefinition {
    const defaultName = this.defaultViewpoint?.[0];
    const rings = this.rings(this.defaultViewpoint?.[1].layout ?? this.spec.layout?.default);
    if (rings !== undefined) {
      return rings;
    }

    const defaultMode = this.layoutMode(this.defaultViewpoint?.[1].layout ?? this.spec.layout?.default);
    const variants = Object.entries(this.spec.viewpoints ?? {}).filter(([, viewpoint]) => viewpoint.variantOf !== undefined);
    if (variants.length === 0) {
      return { modes: [defaultMode] };
    }

    if (variants.length > 1 || variants[0]![1].variantOf !== defaultName) {
      fail("the library toggles between the default viewpoint and one variant of it.");
    }

    const [variantName, variant] = variants[0]!;
    const mode = this.layoutMode(variant.layout);
    const nodes = this.nodeNames.map((name) => [name, { ...this.spec.notation.nodes[name]!, ...variant.notation?.nodes?.[name] } as DislNodeNotation] as const);
    const variantTypes = nodes.map(([name, node]) => this.elementType(name, node));
    const variantRulers = this.rulers(this.systemNamed(variant.coordinateSystem));
    const variantMenu = this.backgroundMenu(variantName);
    const variantDragging = this.dragging(variantTypes);

    const overrides: Partial<Omit<DiagramDefinition, "layout">> = {
      elementTypes: variantTypes,
      ...(variantDragging !== dragging ? { dragging: variantDragging } : {}),
      ...(JSON.stringify(variantRulers) !== JSON.stringify(rulers) ? { chrome: { rulers: variantRulers } } : {}),
      ...(variantMenu !== backgroundMenu ? { backgroundMenu: variantMenu } : {}),
    };

    return {
      modes: [defaultMode, mode],
      ...(variant.toggle !== undefined ? { toggle: { caption: variant.toggle.label, on: mode } } : {}),
      ...(mode === "row-packed" ? { rowPacked: this.rowPacked(variant, nodes) } : {}),
      modeOverrides: { [mode]: overrides },
    };
  }

  /**
   * A force layout with rings (DISL 0.4 §10): the library's radiating layout, each ring the concrete
   * types its tier names, from the centre outwards. Where the layout respects what the reader pinned,
   * a pinned element is the payload's to say.
   */
  private rings(algorithmName: string | undefined): LayoutDefinition | undefined {
    const algorithm = algorithmName === undefined ? undefined : this.spec.layout?.algorithms?.[algorithmName];
    if (algorithm?.algorithm !== "force") {
      return undefined;
    }

    const tiers = algorithm.force?.tiers ?? fail(`the force layout "${algorithmName}" names no tiers; the library's force layout places types on rings.`);
    return {
      modes: ["tiered-force"],
      tiers: tiers.map((tier) => this.concreteTypes(tier).map((type) => this.typeId(type))),
      ...(this.spec.layout?.respect === "pinned" ? { pinned: "payload.pinned" } : {}),
    };
  }

  /** The row-packed layout: the width the viewpoint binds, the lanes' gap, the rows' step. */
  private rowPacked(variant: DislViewpoint, nodes: readonly (readonly [string, DislNodeNotation])[]): NonNullable<LayoutDefinition["rowPacked"]> {
    const algorithm = this.spec.layout!.algorithms![variant.layout!]!;
    const packed = algorithm.rowPacked;
    const sized = nodes.filter(([name]) => variant.notation?.nodes?.[name]?.size?.width !== undefined);
    const widths = new Set(sized.map(([, node]) => JSON.stringify(node.size!.width)));
    if (sized.length === 0 || widths.size !== 1) {
      fail("the row-packed viewpoint must bind one width, shared by the types it sizes.");
    }

    const system = this.systemNamed(variant.coordinateSystem);
    const rows = system === undefined ? undefined : this.axis(system.y);
    if (rows === undefined || typeof rows.scale !== "number") {
      return fail("the row-packed viewpoint's system needs a linear row axis.");
    }

    return {
      width: this.binding(sized[0]![1].size!.width, "the row-packed width"),
      gap: packed?.gap ?? 0,
      types: sized.map(([name]) => this.typeId(name)),
      rowStep: rows.scale,
      followConnections: packed?.followConnections !== undefined,
    };
  }
}

/** A DISL notation as the library's diagram definition. See this file's opening comment. */
export function compileNotation(spec: DislDocument, bindings: NotationBindings, options: CompileOptions = {}): DiagramDefinition {
  return new Compiler(spec, bindings, options).compile();
}
