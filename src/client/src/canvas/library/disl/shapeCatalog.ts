import type { Binding } from "../definition/binding";
import type { BuiltInRoute, BuiltInShape, MarkerKind, SegmentDeclaration } from "../definition/diagramDefinition";
import type { DislCustomShape, DislShapeRef } from "./disTypes";

/**
 * What DISL's built-in shapes, markers and routings are called in this library.
 *
 * <b>A shape is admitted only with the parameters the library draws.</b> The library's hexagon is
 * inset a quarter with flat ends, its parallelogram leans a fifth to the right, its superellipse is
 * the squircle of exponent 4 (`shapes/outline.ts`). DISL lets a specification ask for any inset,
 * skew or exponent (§6.7), and a reference asking for one the library does not draw is refused here
 * rather than drawn as something else: a quiet substitution is the drift this compiler exists to
 * make impossible. A parameter left out takes DISL's default (Appendix B.2), which is then checked
 * the same way.
 */
interface CatalogEntry {
  shape: BuiltInShape;
  /** The parameters the library's drawing has, which a reference must ask for or leave at their default. */
  draws: Readonly<Record<string, unknown>>;
  /** DISL's defaults for the same parameters (Appendix B.2). */
  defaults: Readonly<Record<string, unknown>>;
}

const SHAPES: Readonly<Record<string, CatalogEntry>> = {
  rect: { shape: "box", draws: {}, defaults: {} },
  ellipse: { shape: "ellipse", draws: {}, defaults: {} },
  pill: { shape: "pill", draws: {}, defaults: {} },
  diamond: { shape: "diamond", draws: {}, defaults: {} },
  hexagon: { shape: "hexagon", draws: { inset: 0.25, orientation: "flat" }, defaults: { inset: 0.25, orientation: "flat" } },
  parallelogram: { shape: "parallelogram", draws: { skew: 0.2, direction: "right" }, defaults: { skew: 0.2, direction: "right" } },
  trapezoid: { shape: "trapezoid", draws: { inset: 0.15, direction: "down" }, defaults: { inset: 0.2, direction: "up" } },
  superellipse: { shape: "superellipse", draws: { exponent: 4 }, defaults: { exponent: 4 } },
};

/** The DISL built-in shape names this library can draw. */
export const DISL_SHAPE_NAMES: readonly string[] = Object.keys(SHAPES);

/** The shape a reference names, split into its name and its parameters. */
export function shapeNameOf(ref: DislShapeRef): { name: string; params: Readonly<Record<string, unknown>> } {
  return typeof ref === "string" ? { name: ref, params: {} } : { name: ref.type, params: ref.params ?? {} };
}

/**
 * The library shape for a DISL built-in, or `undefined` when the name is no built-in this catalog
 * knows (a custom shape, which a module binding answers for). Throws for a built-in asked for with
 * parameters the library does not draw.
 */
export function libraryShapeOf(ref: DislShapeRef): BuiltInShape | undefined {
  const { name, params } = shapeNameOf(ref);
  const entry = SHAPES[name];
  if (entry === undefined) {
    return undefined;
  }

  for (const key of new Set([...Object.keys(params), ...Object.keys(entry.draws)])) {
    if (!(key in entry.draws)) {
      throw new Error(`The library draws "${name}" with no "${key}" parameter; the specification sets one.`);
    }

    const asked = key in params ? params[key] : entry.defaults[key];
    if (asked !== entry.draws[key]) {
      throw new Error(`The library draws "${name}" with ${key} ${JSON.stringify(entry.draws[key])}; the specification asks for ${JSON.stringify(asked)}.`);
    }
  }

  return entry.shape;
}

/**
 * DISL's markers (Appendix B.3) by what the library draws. DISL's `arrow` is the OPEN V and
 * `arrowFilled` the solid triangle; the library's `arrow` is the solid triangle and `open-arrow`
 * the V, so the names cross over.
 */
const MARKERS: Readonly<Record<string, MarkerKind>> = {
  none: "none",
  arrow: "open-arrow",
  arrowFilled: "arrow",
};

export function libraryMarkerOf(marker: string): MarkerKind {
  const kind = MARKERS[marker];
  if (kind === undefined) {
    throw new Error(`The library draws no marker for DISL's "${marker}".`);
  }

  return kind;
}

/** DISL's edge routings (§6.10) by the library's route. */
const ROUTES: Readonly<Record<string, BuiltInRoute>> = {
  straight: "straight",
  polyline: "polyline",
  orthogonal: "orthogonal",
  bezier: "cubic-bezier",
  spline: "spline",
};

export function libraryRouteOf(routing: string): BuiltInRoute {
  const route = ROUTES[routing];
  if (route === undefined) {
    throw new Error(`The library draws no route for DISL's "${routing}" routing.`);
  }

  return route;
}

/**
 * What a module says a custom shape of its specification is drawn as: a library shape, and for a
 * banner cut into segments, the segment declaration. The GeomExprs of the custom shape are never
 * read at run time; a geometry test per module evaluates them and compares them with the library
 * shape named here, which is what keeps this binding honest.
 */
export interface CustomShapeBinding {
  shape: BuiltInShape;
  /**
   * The segment declaration, given the custom shape as the specification states it, the parameters
   * the node (or the viewpoint's override) binds it with, and the compiler's own reading of a
   * Bindable as a library binding.
   */
  segments?: (context: { shape: DislCustomShape; params: Readonly<Record<string, unknown>>; bind: (value: unknown) => Binding }) => SegmentDeclaration;
}
