import type { ActionDeclaration, ActionInvocation } from "../definition/actions";
import type { Binding } from "../definition/binding";
import type { CalendarStep, RulerDeclaration, RulerRung } from "../definition/chrome";
import type {
  AnchorSet,
  BuiltInShape,
  ClassDeclaration,
  CompassPosition,
  DiagramDefinition,
  ElementTypeDefinition,
  EndpointConstraint,
  FilterDeclaration,
  LabelDeclaration,
  LayoutDefinition,
  LayoutMode,
  RelationTypeDefinition,
  SegmentDeclaration,
  SnapDeclaration,
} from "../definition/diagramDefinition";
import { BEFORE_GAP } from "../definition/labels";
import {
  typeList,
  type Bindable,
  type DislAxis,
  type DislContextMenu,
  type DislCoordinateSystem,
  type DislCustomShape,
  type DislDocument,
  type DislLabel,
  type DislNodeNotation,
  type DislRelation,
  type DislViewpoint,
} from "./disTypes";
import { libraryMarkerOf, libraryRouteOf, libraryShapeOf, shapeNameOf, type CustomShapeBinding } from "./shapeCatalog";

/**
 * A DISL 0.2 notation compiled into the library's {@link DiagramDefinition}.
 *
 * <b>One file, two readers.</b> A diagram module bundles its `.dis` (`src/diagrams/<module>/definition/`),
 * the backend runs it, and the client imports the same bytes with Vite's `?raw` and compiles them
 * here at module load. There is no generated file, so nothing can go stale between the two tiers.
 *
 * <b>Structural, and refusing.</b> The notation's nodes, edges, anchors, labels, sizes, placements,
 * the coordinates' snapping and ruler, the canvas's filter and legend, the context menus' shortcuts
 * and the viewpoints are mapped onto the library's vocabulary key by key. A construct this compiler
 * does not map is refused with an error naming it, never skipped: a skipped key is a drawing that
 * silently differs from its specification.
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
   */
  wireIds: `x-${string}`;
  /**
   * The payload path the backend computes for each CEL expression the notation binds, keyed by the
   * expression as written. `self.<attribute>` needs no entry: it is `payload.<attribute>`.
   */
  celPaths: Readonly<Record<string, string>>;
  /** The classes an element type's group and shape carry, which the module's stylesheet paints. */
  classNames: (type: string, node: DislNodeNotation) => readonly ClassDeclaration[];
  /** A label's class. */
  labelClassName?: (type: string, label: DislLabel) => string | undefined;
  /** A label's inline editor box, where it should not cover the label's own line. */
  labelEditorBox?: (type: string, label: DislLabel) => { top: number; height: number } | undefined;
  /** A relation's class. */
  relationClassName?: (relation: string) => string | undefined;
  /** An endpoint's anchor rule, where the module states one. */
  endpointAnchors?: (relation: string, end: "source" | "target") => EndpointConstraint["anchors"] | undefined;
  /** What each custom shape of the specification is drawn as. */
  customShapes?: Readonly<Record<string, CustomShapeBinding>>;
  /** The payload paths of each element's own snap origin. */
  snapOrigins?: { x: string; y: string };
  /** The class a legend swatch takes for one enum value. */
  legendSwatchClass?: (enumName: string, value: string) => string;
  /** Library declarations the specification states only under a module's `x-` key. */
  extras?: (spec: DislDocument) => Partial<DiagramDefinition>;
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

/** The compass positions as fractions of the bounds. */
const COMPASS: Readonly<Record<CompassPosition, readonly [number, number]>> = {
  n: [0.5, 0], ne: [1, 0], e: [1, 0.5], se: [1, 1], s: [0.5, 1], sw: [0, 1], w: [0, 0.5], nw: [0, 0],
};

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
    const wire = spec[bindings.wireIds];
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
    const backgroundMenu = this.backgroundMenu(this.defaultViewpoint?.[0]);
    const dragging = this.dragging(elementTypes);

    const definition: DiagramDefinition = {
      elementTypes,
      relationTypes,
      ...(acyclic.length > 0 ? { acyclic } : {}),
      ...(actions.length > 0 ? { actions } : {}),
      ...(snap !== undefined ? { snap } : {}),
      ...(rulers.length > 0 ? { chrome: { rulers } } : {}),
      ...(filter !== undefined ? { filter } : {}),
      layout: this.layout(rulers, backgroundMenu, dragging),
      dragging,
      ...(backgroundMenu ? { backgroundMenu } : {}),
    };

    return { ...definition, ...this.bindings.extras?.(spec) };
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
    const labels = (node.labels ?? []).map((label) => this.label(name, label));
    const { sizing, resize } = this.sizing(node);
    const editOnDrop = (this.spec.toolbox?.groups ?? []).some((group) => group.tools.some((tool) => tool.creates === name && tool.after === "editLabel"));

    return {
      id: this.typeId(name),
      shape,
      ...(classNames.length > 0 ? { classNames } : {}),
      ...(labels.length > 0 ? { labels } : {}),
      ...(node.tooltip !== undefined ? { tooltip: this.binding(node.tooltip, `${where} tooltip`) } : {}),
      ...(node.accessibility !== undefined ? { accessibility: this.accessibility(node, where) } : {}),
      anchors: this.anchors(name, node),
      sizing,
      ...(resize !== undefined ? { resize } : {}),
      ...(this.movable(node) ? {} : { draggable: false }),
      ...(editOnDrop ? { editOnDrop } : {}),
      ...(segments !== undefined ? { segments } : {}),
    };
  }

  private shape(name: string, node: DislNodeNotation): { shape: BuiltInShape; segments?: SegmentDeclaration } {
    const builtIn = libraryShapeOf(node.shape);
    if (builtIn !== undefined) {
      return { shape: builtIn };
    }

    const { name: shapeName, params } = shapeNameOf(node.shape);
    const custom: DislCustomShape | undefined = this.spec.notation.shapes?.[shapeName];
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
    } else if (isObject(position)) {
      const [ax, ay] = position.anchor as readonly [number, number];
      const anchorTo = ax !== 0.5 ? undefined : ay === 0 ? "top" : ay === 0.5 ? "centre" : ay === 1 ? "bottom" : undefined;
      if (anchorTo === undefined) {
        fail(`${where} anchors at ${JSON.stringify(position.anchor)}; the library anchors a label at the top, centre or bottom middle.`);
      }
      declaration.anchorTo = anchorTo;
      const offset = position.offset as readonly [number, number] | undefined;
      if (offset !== undefined) {
        declaration.offset = { x: offset[0], y: offset[1] };
      }
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

  private accessibility(node: DislNodeNotation, where: string): NonNullable<ElementTypeDefinition["accessibility"]> {
    const { role, name } = node.accessibility!;
    return {
      ...(role !== undefined ? { role } : {}),
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
      const positions = (anchors.points ?? []).map((point) => {
        const fraction = COMPASS[point.id as CompassPosition];
        if (fraction === undefined || fraction[0] !== point.x || fraction[1] !== point.y) {
          return fail(`node "${name}" anchor "${point.id}" at (${point.x}, ${point.y}) is no compass position the library has.`);
        }
        return point.id as CompassPosition;
      });
      const drawnFrom = anchors["x-anchors.drawnFrom"];
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
      return fail(`node "${name}" states no anchors and no edge attaches to a part of it.`);
    }

    return { kind: "along", edges: [...sides] as ("top" | "bottom" | "left" | "right")[], regions: "segments", visible: false };
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

    // Only a type that may hold children can be the parent end of a relation derived from containment.
    const derivedFromParent = isObject(relation.derived) && relation.derived.source === "item.parent";
    const sourceTypes = typeList(relation.source).flatMap((type) => this.concreteTypes(type)).filter((type) => !derivedFromParent || this.holdsChildren(type));
    const targetTypes = typeList(relation.target).flatMap((type) => this.concreteTypes(type));
    const endpoint = (types: readonly string[], end: "source" | "target"): EndpointConstraint => {
      const anchors = this.bindings.endpointAnchors?.(name, end);
      return { elementTypes: types.map((type) => this.typeId(type)), ...(anchors !== undefined ? { anchors } : {}) };
    };

    // A stored relation that allows no parallel instances is one per pair: per direction when it
    // is directed. A derived relation's count follows from its derivation and is not declared.
    const perPair = relation.derived === undefined && relation.allowParallel === false ? (relation.directed === false ? "unordered" : "ordered") : undefined;

    return {
      id: this.typeId(name),
      route: libraryRouteOf(routing),
      style: { ...(source !== "none" ? { startMarker: libraryMarkerOf(source) } : {}), endMarker: libraryMarkerOf(target) },
      ...(className !== undefined ? { className } : {}),
      ...(ends.some((end) => end?.movable === true) ? { movableEnds: true } : {}),
      ...(ends.some((end) => end?.mode === "part") ? { hideWhenAttachmentHidden: true } : {}),
      endpoints: {
        source: endpoint(sourceTypes, "source"),
        target: endpoint(targetTypes, "target"),
        allowSelf: relation.allowSelfLoops === true,
        ...(perPair !== undefined ? { cardinality: { perPair } } : {}),
      },
    };
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

    return { kind: "element", types: menu.for.flatMap((name) => this.concreteTypes(name)) };
  }

  /**
   * The actions a key or a canvas gesture invokes, from the context menus' entries. A double-click is
   * the library's `activate` gesture and invokes the node's `doubleClick` entry (DISL's default:
   * `editLabel`); the Delete key is the library's `delete` gesture. Gesture-invoked actions come
   * first, then those only a shortcut invokes, each in menu order.
   */
  private actions(): ActionDeclaration[] {
    const found: { id: string; kind: "element" | "connection"; types: Set<string>; keys: string[]; gestures: ("activate" | "delete")[]; rank: number }[] = [];
    for (const menu of this.spec.toolbox?.contextMenus ?? []) {
      const targets = this.menuTargets(menu);
      if (targets === null) {
        continue;
      }

      for (const tool of menu.tools) {
        const activates = tool.kind === (targets.kind === "element" ? this.doubleClickOf(targets.types) : undefined);
        const deletes = tool.kind === "delete";
        const keys = tool.shortcut !== undefined && !(deletes && tool.shortcut === "Delete") ? [tool.shortcut] : [];
        if (!activates && !deletes && keys.length === 0) {
          continue;
        }

        const key = tool.operation ?? tool.kind;
        for (const forType of menu.for) {
          const id = this.wire.actions?.[`${forType}/${key}`] ?? this.wire.actions?.[key] ?? fail(`the "${key}" entry of the menu for ${forType} has no wire id in ${this.bindings.wireIds}.actions.`);
          let action = found.find((entry) => entry.id === id && entry.kind === targets.kind);
          if (action === undefined) {
            action = { id, kind: targets.kind, types: new Set(), keys: [], gestures: [], rank: activates ? 0 : deletes ? 1 : 2 };
            found.push(action);
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

    const allNodes = new Set(this.nodeNames);
    return [...found].sort((a, b) => a.rank - b.rank).map((action): ActionDeclaration => {
      const invokedBy: ActionInvocation[] = [
        ...action.keys.map((key): ActionInvocation => ({ kind: "shortcut", key })),
        ...action.gestures.map((gesture): ActionInvocation => ({ kind: "gesture", gesture })),
      ];
      const narrowed = action.kind === "element" && [...allNodes].some((type) => !action.types.has(type));
      return {
        id: action.id,
        invokedBy,
        appliesTo: [
          action.kind === "element"
            ? { kind: "element", ...(narrowed ? { elementTypes: [...action.types].map((type) => this.typeId(type)) } : {}) }
            : { kind: "connection" },
        ],
      };
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

    const axisSnap = (axisName: "x" | "y") => ({ step: step(axisName), ...(origins !== undefined ? { origin: { path: origins[axisName] } } : {}) });
    return { x: axisSnap("x"), y: axisSnap("y") };
  }

  /** The view-fixed rulers of a coordinate system's axes. */
  private rulers(system: DislCoordinateSystem | undefined): RulerDeclaration[] {
    if (system === undefined) {
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
      const minUnit = ruler["x-ruler.minUnit"];
      const finest = minUnit === undefined ? 1 : this.unitMonths(this.diagramValue(minUnit as Bindable<string>));
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
    const filters = Object.entries(this.spec.notation.canvas?.filters ?? {});
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

  /** The canvas's layout mode for a layout algorithm: a lanes layout with `x-layout.rowPacked`, or by hand. */
  private layoutMode(algorithmName: string | undefined): LayoutMode {
    if (algorithmName === undefined) {
      return "manual";
    }

    const algorithm = this.spec.layout?.algorithms?.[algorithmName] ?? fail(`layout "${algorithmName}" is not declared.`);
    if (algorithm["x-layout.rowPacked"] !== undefined) {
      return "row-packed";
    }

    // The host computes a plugin's layout and sends positions; the canvas draws them where they are.
    if (algorithm.algorithm.startsWith("plugin:") || algorithm.algorithm === "none") {
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

  /** The row-packed layout: the width the viewpoint binds, the lanes' gap, the rows' step. */
  private rowPacked(variant: DislViewpoint, nodes: readonly (readonly [string, DislNodeNotation])[]): NonNullable<LayoutDefinition["rowPacked"]> {
    const algorithm = this.spec.layout!.algorithms![variant.layout!]!;
    const packed = algorithm["x-layout.rowPacked"] as { gap?: number; followConnections?: string } | undefined;
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
