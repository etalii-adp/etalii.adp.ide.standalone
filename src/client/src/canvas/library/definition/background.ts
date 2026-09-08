import {
  holds,
  resolveNumber,
  resolveNumberAt,
  resolveOne,
  resolveOneAt,
  type Binding,
  type BindingSource,
  type Condition,
  type FieldBinding,
  type TemplateBinding,
} from "./binding";
import type { DeclaredNumber, ShapeBounds } from "./diagramDefinition";

/**
 * `background` — the coordinate-space backdrop a notation draws beneath everything.
 *
 * <b>The measured need is wardley-map.</b> Its canvas is the largest in the tree and the only
 * one with substantial raw SVG — and of its twenty-four raw-SVG lines, <em>three</em> are an
 * element and <b>twenty-one are its background</b>. A specification reading "wardley is the hard
 * shape" would have solved the wrong problem: its shapes are a symbol and a dot.
 *
 * Four kinds, which are the design's own list: <b>bands</b> (a labelled slab between two
 * coordinates, one per entry of a model collection), <b>axes</b> (a line with end labels and a
 * rotated title), <b>gridlines</b>, and <b>regions</b> bound to a collection.
 *
 * <b>Coordinates are fractions of the canvas extent, never canvas units.</b> The library cannot
 * know a module's own scale — wardley thinks in a 0..1 evolution/value space — so a background
 * says where things sit as a proportion of the drawn area and the library multiplies. It is also
 * why a band's `start` and `end` drop straight in from wardley's `stage.start`/`stage.end`,
 * which are already 0..1.
 *
 * <b>Beneath everything, `aria-hidden`, and takes no gestures</b> — which is what
 * `wardley-chrome` already is today. Resolution is pure, as the other two additions are.
 */

/** One band: a labelled slab across the canvas, drawn once per entry of a collection. */
export interface BandDeclaration {
  /** A collection path: one band per entry. Omitted, one band is drawn. */
  each?: Binding;
  orientation: "vertical" | "horizontal";
  /** 0..1 across the extent, resolved against the ITEM when `each` is given. */
  start: DeclaredNumber;
  end: DeclaredNumber;
  label?: Binding;
  /** A rule at the band's leading edge - wardley's boundary between two stages. */
  edge?: boolean;
  className?: string;
  when?: Condition;
}

/** One axis: a line along an edge, with a rotated title and a label at each end. */
export interface AxisDeclaration {
  orientation: "vertical" | "horizontal";
  title?: Binding;
  startLabel?: Binding;
  endLabel?: Binding;
  className?: string;
}

/** One rule across the canvas at a fixed proportion. */
export interface GridlineDeclaration {
  orientation: "vertical" | "horizontal";
  at: DeclaredNumber;
  className?: string;
}

/** A rectangle per entry of a collection - wardley's attitude regions. */
export interface RegionDeclaration {
  each: Binding;
  x: DeclaredNumber;
  y: DeclaredNumber;
  width: DeclaredNumber;
  height: DeclaredNumber;
  label?: Binding;
  className?: string;
}

export interface BackgroundDeclaration {
  bands?: readonly BandDeclaration[];
  axes?: readonly AxisDeclaration[];
  gridlines?: readonly GridlineDeclaration[];
  regions?: readonly RegionDeclaration[];
}

export interface BackgroundRect {
  x: number;
  y: number;
  width: number;
  height: number;
  className?: string;
  key: string;
}

export interface BackgroundLine {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  className?: string;
  key: string;
}

export interface BackgroundText {
  x: number;
  y: number;
  text: string;
  anchor: "start" | "middle" | "end";
  rotate?: number;
  className?: string;
  key: string;
}

/** What the renderer draws: absolute canvas geometry, with no arithmetic left to do. */
export interface ResolvedBackground {
  rects: BackgroundRect[];
  lines: BackgroundLine[];
  texts: BackgroundText[];
}

function numberOf(value: DeclaredNumber | undefined, source: BindingSource, fallback: number): number {
  if (value === undefined) {
    return fallback;
  }

  if (typeof value === "number") {
    return value;
  }

  return resolveNumber(value, source) ?? fallback;
}

/**
 * A number on a collection ITEM. Rooted at the item, exactly as `labels`' `each` is, so a band
 * writes `{ path: "start" }` against `stage.start` rather than a path through `payload`.
 */
function itemNumberOf(value: DeclaredNumber | undefined, item: unknown, fallback: number): number {
  if (value === undefined) {
    return fallback;
  }

  if (typeof value === "number") {
    return value;
  }

  return resolveNumberAt(value as FieldBinding | TemplateBinding, item) ?? fallback;
}

/** Text on a collection item, rooted at the item for the same reason. */
function itemTextOf(binding: Binding | undefined, item: unknown): string | null {
  return binding ? resolveOneAt(binding as FieldBinding | TemplateBinding, item) : null;
}

/**
 * The raw entries a collection path yields.
 *
 * `resolveMany` gives strings, which is right for a label and wrong for a band whose `start` is
 * a number on the item. This is the one place the background needs the model's own shape rather
 * than its text, so it walks the path itself and hands back the objects.
 */
function itemsOf(binding: Binding | undefined, source: BindingSource): readonly unknown[] {
  if (!binding || !("path" in binding)) {
    return [null];
  }

  let current: unknown = { element: source.element, payload: source.payload };
  for (const step of binding.path.split(".")) {
    if (typeof current !== "object" || current === null || Array.isArray(current)) {
      return [];
    }

    current = (current as Record<string, unknown>)[step];
  }

  return Array.isArray(current) ? current : [];
}

function suffixed(className: string | undefined, suffix: string): string | undefined {
  return className === undefined ? undefined : `${className}-${suffix}`;
}

/**
 * Every primitive the background draws, in absolute canvas units.
 *
 * The extent is the drawn area and every declared coordinate is a fraction of it. Nothing here
 * throws: an unresolvable number falls back, an empty collection draws nothing. Those are the
 * label and decoration resolvers' rules, applied again rather than re-invented.
 */
export function resolveBackground(
  background: BackgroundDeclaration | undefined,
  source: BindingSource,
  extent: ShapeBounds,
): ResolvedBackground {
  const rects: BackgroundRect[] = [];
  const lines: BackgroundLine[] = [];
  const texts: BackgroundText[] = [];

  if (!background) {
    return { rects, lines, texts };
  }

  const atX = (fraction: number) => extent.x + fraction * extent.width;
  const atY = (fraction: number) => extent.y + fraction * extent.height;

  (background.bands ?? []).forEach((band, bandIndex) => {
    if (!holds(band.when, source)) {
      return;
    }

    itemsOf(band.each, source).forEach((item, index) => {
      const start = band.each ? itemNumberOf(band.start, item, 0) : numberOf(band.start, source, 0);
      const end = band.each ? itemNumberOf(band.end, item, 0) : numberOf(band.end, source, 0);
      const key = `band-${bandIndex}-${index}`;
      const vertical = band.orientation === "vertical";

      rects.push(
        vertical
          ? { x: atX(start), y: extent.y, width: (end - start) * extent.width, height: extent.height, className: band.className, key }
          : { x: extent.x, y: atY(start), width: extent.width, height: (end - start) * extent.height, className: band.className, key },
      );

      // The leading edge, skipped for the first band: wardley draws a boundary BETWEEN stages,
      // and a rule on the outermost edge would double the axis already drawn there.
      if (band.edge === true && index > 0) {
        lines.push(
          vertical
            ? { x1: atX(start), y1: extent.y, x2: atX(start), y2: extent.y + extent.height, className: suffixed(band.className, "edge"), key: `${key}-edge` }
            : { x1: extent.x, y1: atY(start), x2: extent.x + extent.width, y2: atY(start), className: suffixed(band.className, "edge"), key: `${key}-edge` },
        );
      }

      const label = band.each ? itemTextOf(band.label, item) : band.label ? resolveOne(band.label, source) : null;
      if (label !== null) {
        texts.push(
          vertical
            ? { x: atX((start + end) / 2), y: extent.y + extent.height + 34, text: label, anchor: "middle", className: suffixed(band.className, "label"), key: `${key}-label` }
            : { x: extent.x - 8, y: atY((start + end) / 2), text: label, anchor: "end", className: suffixed(band.className, "label"), key: `${key}-label` },
        );
      }
    });
  });

  (background.axes ?? []).forEach((axis, index) => {
    const key = `axis-${index}`;
    const vertical = axis.orientation === "vertical";

    lines.push(
      vertical
        ? { x1: extent.x, y1: extent.y, x2: extent.x, y2: extent.y + extent.height, className: axis.className, key }
        : { x1: extent.x, y1: extent.y + extent.height, x2: extent.x + extent.width, y2: extent.y + extent.height, className: axis.className, key },
    );

    const title = axis.title ? resolveOne(axis.title, source) : null;
    if (title !== null) {
      texts.push(
        vertical
          ? { x: extent.x - 34, y: extent.y + extent.height / 2, text: title, anchor: "middle", rotate: -90, className: suffixed(axis.className, "title"), key: `${key}-title` }
          : { x: extent.x + extent.width / 2, y: extent.y + extent.height + 66, text: title, anchor: "middle", className: suffixed(axis.className, "title"), key: `${key}-title` },
      );
    }

    const startLabel = axis.startLabel ? resolveOne(axis.startLabel, source) : null;
    if (startLabel !== null) {
      texts.push(
        vertical
          ? { x: extent.x - 14, y: extent.y + 12, text: startLabel, anchor: "end", className: suffixed(axis.className, "end"), key: `${key}-start` }
          : { x: extent.x, y: extent.y + extent.height + 18, text: startLabel, anchor: "start", className: suffixed(axis.className, "end"), key: `${key}-start` },
      );
    }

    const endLabel = axis.endLabel ? resolveOne(axis.endLabel, source) : null;
    if (endLabel !== null) {
      texts.push(
        vertical
          ? { x: extent.x - 14, y: extent.y + extent.height, text: endLabel, anchor: "end", className: suffixed(axis.className, "end"), key: `${key}-end` }
          : { x: extent.x + extent.width, y: extent.y + extent.height + 18, text: endLabel, anchor: "end", className: suffixed(axis.className, "end"), key: `${key}-end` },
      );
    }
  });

  (background.gridlines ?? []).forEach((gridline, index) => {
    const at = numberOf(gridline.at, source, 0);
    const key = `grid-${index}`;
    lines.push(
      gridline.orientation === "vertical"
        ? { x1: atX(at), y1: extent.y, x2: atX(at), y2: extent.y + extent.height, className: gridline.className, key }
        : { x1: extent.x, y1: atY(at), x2: extent.x + extent.width, y2: atY(at), className: gridline.className, key },
    );
  });

  (background.regions ?? []).forEach((region, regionIndex) => {
    itemsOf(region.each, source).forEach((item, index) => {
      const key = `region-${regionIndex}-${index}`;
      const x = itemNumberOf(region.x, item, 0);
      const y = itemNumberOf(region.y, item, 0);

      rects.push({
        x: atX(x),
        y: atY(y),
        width: itemNumberOf(region.width, item, 0) * extent.width,
        height: itemNumberOf(region.height, item, 0) * extent.height,
        className: region.className,
        key,
      });

      const label = itemTextOf(region.label, item);
      if (label !== null) {
        texts.push({
          x: atX(x) + 6,
          y: atY(y) + 16,
          text: label,
          anchor: "start",
          className: suffixed(region.className, "label"),
          key: `${key}-label`,
        });
      }
    });
  });

  return { rects, lines, texts };
}
