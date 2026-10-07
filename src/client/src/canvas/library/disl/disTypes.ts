/**
 * The part of a DISL 0.3 diagram specification (`*.dis`) the client reads, typed.
 *
 * <b>A subset, on purpose.</b> The backend loads the whole specification and runs its CEL; the
 * client reads only what turns into a {@link DiagramDefinition}: the metamodel's types and
 * relations, the coordinates' axes and rulers, the notation's nodes, edges and shapes, the toolbox's
 * context menus (for shortcuts and wire ids), the notation canvas's filters and legend, the viewpoints and the
 * layout algorithms. Everything else is typed `unknown` or left out, so a reader of
 * `compileNotation.ts` sees exactly which keys it depends on.
 *
 * Keys starting with `x-` are proposals DISL 0.3 does not define yet. They are kept as data
 * (`[key: `x-${string}`]: unknown`) and read only by name.
 *
 * [`DiagramDefinition`]: ../definition/diagramDefinition.ts
 */

/** A value given literally, or bound to an attribute or to a CEL expression (DISL §2.6 Bindable). */
export type Bindable<T> = T | { attribute: string } | { cel: string } | { layout: true };

/** An `x-` extension key, kept as data. */
export type Extensions = { [key: `x-${string}`]: unknown };

export interface DislDocument extends Extensions {
  disl: string;
  language: { id: string; version: string; label?: string };
  metamodel: DislMetamodel;
  coordinates?: DislCoordinates;
  notation: DislNotation;
  toolbox?: DislToolbox;
  viewpoints?: Readonly<Record<string, DislViewpoint>>;
  layout?: DislLayout;
  /** The operations a node's `doubleClick` may name (DISL §9.3). */
  behavior?: { operations?: Readonly<Record<string, unknown>> };
}

export interface DislMetamodel {
  diagram?: { attributes?: Readonly<Record<string, DislAttribute>> };
  enums?: Readonly<Record<string, DislEnum>>;
  types: Readonly<Record<string, DislType>>;
  relations?: Readonly<Record<string, DislRelation>>;
}

export interface DislAttribute {
  type: string;
  default?: unknown;
  many?: boolean;
}

export interface DislEnum {
  ordered?: boolean;
  /** A value's `color` is a fixed colour, or a theme token resolved in the current mode (DISL 0.3). */
  values: Readonly<Record<string, { label?: string; color?: string | { token: string } } & Extensions>>;
}

export interface DislType extends Extensions {
  abstract?: boolean;
  extends?: string;
  label?: string;
  attributes?: Readonly<Record<string, DislAttribute>>;
  /** Present when the type may hold children (DISL §4.6 containment). */
  children?: { allowed: readonly string[]; ordered?: boolean; min?: number; max?: number };
}

/** A relation end: a type name, a list of them, or an end object naming its types and role (DISL §4.9). */
export type DislRelationEnd = string | readonly string[] | { types: string | readonly string[]; role?: string };

export interface DislRelation extends Extensions {
  label?: string;
  source: DislRelationEnd;
  target: DislRelationEnd;
  directed?: boolean;
  allowSelfLoops?: boolean;
  allowParallel?: boolean;
  acyclic?: boolean;
  attributes?: Readonly<Record<string, DislAttribute>>;
  /** A relation computed from the model rather than stored (DISL §4.11). */
  derived?: unknown;
}

export interface DislCoordinates {
  axes?: Readonly<Record<string, DislAxis>>;
  systems: Readonly<Record<string, DislCoordinateSystem>>;
  default?: string;
}

export interface DislAxis {
  kind: string;
  valueType?: string;
  origin?: string | number;
  unit?: string;
  scale?: number | { unit: Bindable<string>; size: number };
  ruler?: DislRuler;
}

export interface DislRuler extends Extensions {
  visible?: boolean;
  position?: "top" | "bottom" | "left" | "right";
  attach?: "view" | "canvas";
  levels: readonly { unit: string; format: string; minSpacingPx?: number }[];
  /** The finest level shown (DISL 0.3): levels finer than this unit are left out. */
  minUnit?: Bindable<string>;
}

export interface DislCoordinateSystem {
  kind: string;
  /** An axis name, or an inline axis. */
  x: string | DislAxis;
  y: string | DislAxis;
}

export interface DislNotation {
  theme?: DislTheme;
  styles?: Readonly<Record<string, unknown>>;
  shapes?: Readonly<Record<string, DislCustomShape>>;
  nodes: Readonly<Record<string, DislNodeNotation>>;
  edges?: Readonly<Record<string, DislEdgeNotation>>;
  /** The canvas's own chrome: filters and the legend (DISL §6.13). */
  canvas?: DislCanvas;
}

export interface DislTheme {
  tokens: Readonly<Record<string, string>>;
  modes?: Readonly<Record<string, Readonly<Record<string, string>>>>;
  defaultMode?: string;
  followSystem?: boolean;
}

/** A GeomExpr: a number, or a CEL expression over the shape context `w`, `h` and `p` (DISL §6.8). */
export type GeomExpr = number | string;

/** One path segment of a custom shape (DISL §6.8). */
export type DislPathSegment =
  | { op: "M" | "L"; x: GeomExpr; y: GeomExpr }
  | { op: "A"; rx: GeomExpr; ry: GeomExpr; rotation: GeomExpr; largeArc: boolean; sweep: boolean; x: GeomExpr; y: GeomExpr }
  | { op: "Z" };

export interface DislBox {
  x: GeomExpr;
  y: GeomExpr;
  w: GeomExpr;
  h: GeomExpr;
}

export interface DislShapePart extends Extensions {
  id: string;
  when?: string;
  hit?: boolean;
  style?: unknown;
  box?: DislBox;
  shape?: string | { path: { segments: readonly DislPathSegment[] } };
  /** The part's own tooltip (DISL 0.3). */
  tooltip?: Bindable<string>;
}

export interface DislCustomShape {
  label?: string;
  params?: Readonly<Record<string, { type: string; default?: unknown; min?: number; max?: number }>>;
  /** A single outline (`outline`), or a path (`path`): DISL allows either name for the body. */
  outline?: { segments: readonly DislPathSegment[] };
  path?: { segments: readonly DislPathSegment[] };
  textArea?: DislBox;
  parts?: readonly DislShapePart[];
  handles?: readonly { param: string; x: GeomExpr; y: GeomExpr; axis?: string; visible?: string }[];
}

/** A shape reference: a name, or a name with parameters (DISL §6.7). */
export type DislShapeRef = string | { type: string; params?: Readonly<Record<string, Bindable<unknown>>> };

export interface DislLabel extends Extensions {
  id: string;
  text: Bindable<string>;
  position?: string | { anchor: readonly [number, number]; offset?: readonly [number, number]; align?: "start" | "center" | "end" };
  distance?: number;
  editable?: boolean | "inline" | "multiline";
  wrap?: "none" | "word";
  maxWidth?: number | "parent";
  overflow?: "visible" | "ellipsis" | "clip";
  style?: string | Readonly<Record<string, unknown>>;
  tooltip?: Bindable<string>;
  /** The text the inline editor opens on, when not the label's own text (DISL 0.3). */
  editText?: Bindable<string>;
  /** Whether the label is drawn: a boolean, or CEL (DISL §6.12). */
  visible?: boolean | string | { cel: string };
}

export interface DislSize {
  default?: readonly [number | null, number | null];
  fixed?: readonly [number, number];
  min?: readonly [number | null, number | null];
  max?: readonly [number | null, number | null];
  width?: Bindable<number>;
  height?: Bindable<number>;
  resizable?: boolean | "horizontal" | "vertical";
}

export interface DislPlacement {
  system?: string;
  anchor?: string;
  movable?: boolean | { x: boolean; y: boolean };
  resizable?: boolean | { x: boolean; y: boolean };
}

export interface DislAnchors extends Extensions {
  mode?: "outline" | "center" | "fixed" | "sides";
  points?: readonly { id: string; x: number; y: number }[];
  sides?: readonly ("top" | "right" | "bottom" | "left")[];
  /** Where an edge's end is drawn (DISL 0.3 §6.9): at its anchor (`point`, the default), or where the line crosses the outline. */
  drawnFrom?: "point" | "outline";
}

export interface DislNodeNotation extends Extensions {
  shape: DislShapeRef;
  style?: string | Readonly<Record<string, unknown>>;
  size?: DislSize;
  placement?: DislPlacement;
  snapping?: unknown;
  labels?: readonly DislLabel[];
  tooltip?: Bindable<string>;
  anchors?: DislAnchors;
  accessibility?: { role?: string; name?: Bindable<string> };
  doubleClick?: string;
  connectable?: boolean;
  deletable?: boolean;
  conditions?: readonly { when: string; style: string }[];
}

export interface DislEndAnchoring {
  mode: "part" | "fixed" | "outline" | "sides" | "center";
  part?: Bindable<string>;
  side?: Bindable<string>;
  at?: Bindable<number>;
  movable?: boolean;
}

export interface DislEdgeNotation extends Extensions {
  style?: string | Readonly<Record<string, unknown>>;
  line?: { routing?: string; stroke?: unknown; bendpoints?: { editable?: boolean } } & Readonly<Record<string, unknown>>;
  sourceMarker?: string;
  targetMarker?: string;
  anchoring?: { source?: DislEndAnchoring; target?: DislEndAnchoring };
  variants?: readonly unknown[];
  deletable?: boolean;
  reconnectable?: boolean;
  /** Whether a press on the edge selects it (DISL §6.10); `false` makes a press on it a press on the background. */
  selectable?: boolean;
  /**
   * How a new connection is drawn (DISL 0.3 §6.10): the pointer button, where it starts and the modifier keys
   * held; which end of the relation each named anchor starts (`from`); and the tool the gesture runs (`tool`).
   */
  connect?: {
    pointer?: { button?: "primary" | "secondary" | "middle"; start?: "anchor" | "body"; modifiers?: readonly ("Alt" | "Shift" | "Ctrl" | "Meta")[] };
    from?: Readonly<Record<string, "source" | "target">>;
    tool?: string;
  };
  /** The labels drawn along the edge (DISL §6.10). */
  labels?: readonly DislEdgeLabel[];
}

/** A label along an edge (DISL §6.10): where along it, on which side and how far off the line. */
export interface DislEdgeLabel extends Extensions {
  id: string;
  text: Bindable<string>;
  at?: "start" | "middle" | "end" | number;
  side?: "above" | "below" | "on";
  distance?: number;
  editable?: boolean | "inline" | "multiline";
}

export interface DislContextTool {
  kind: string;
  operation?: string;
  label?: Bindable<string>;
  shortcut?: string;
}

export interface DislContextMenu {
  for: readonly string[];
  when?: string;
  placement?: string;
  tools: readonly DislContextTool[];
}

export interface DislTool {
  id: string;
  creates?: string;
  mode?: string;
  after?: string;
  /** What a connect gesture released on empty canvas creates at its missing end (DISL 0.3 §7.2). */
  createTarget?: unknown;
  createSource?: unknown;
}

export interface DislToolbox {
  groups?: readonly { id: string; tools: readonly DislTool[] }[];
  /** Tools no palette group shows, which a gesture or an edge's `connect.tool` names (DISL 0.3 §7.2). */
  tools?: Readonly<Record<string, DislTool>>;
  contextMenus?: readonly DislContextMenu[];
}

export interface DislFilter {
  label: string;
  control?: string;
  appliesTo?: readonly string[];
}

export interface DislCanvas {
  filters?: Readonly<Record<string, DislFilter>>;
  legend?: { visible?: boolean; position?: string; entries: readonly string[] };
}

export interface DislViewpoint {
  label?: string;
  default?: boolean;
  variantOf?: string;
  toggle?: { label: string; position?: string };
  coordinateSystem?: string;
  layout?: string;
  notation?: { nodes?: Readonly<Record<string, Partial<DislNodeNotation>>> };
}

/** One layout algorithm: a standard one (DISL 0.3 §10) with its options under its own name, or a plugin's. */
interface DislLayoutAlgorithm extends Extensions {
  algorithm: string;
  scope?: string;
  rowPacked?: { gap?: number; followConnections?: string } & Readonly<Record<string, unknown>>;
  rows?: Readonly<Record<string, unknown>>;
  tidyTree?: Readonly<Record<string, unknown>>;
}

export interface DislLayout {
  algorithms?: Readonly<Record<string, DislLayoutAlgorithm>>;
  default?: string;
}

/**
 * The bundled text as a document, refusing a DISL version this client was not written against.
 * A `.dis` is JSON (DISL §2.1); the bundle is the backend's own copy, so both tiers read one file.
 */
export function parseDisl(text: string): DislDocument {
  const document = JSON.parse(text) as DislDocument;
  if (document.disl !== "0.3") {
    throw new Error(`This client reads DISL 0.3; the bundled specification declares "${String(document.disl)}".`);
  }

  return document;
}

/** A relation end's types as a list, whichever form the specification wrote: a name, a list, or an end object's `types` (DISL §4.9). */
export function typeList(value: DislRelationEnd): readonly string[] {
  return typeof value === "string" ? [value] : "types" in value ? typeList(value.types) : value as readonly string[];
}
