import { holds, resolveEntries, resolveMany, resolveNumber, resolveOneAt, type Binding, type BindingSource } from "./binding";
import type { BuiltInShape, DeclaredNumber, LabelDeclaration, LabelSlot, LabelTypography, ShapeBounds } from "./diagramDefinition";
import { outlineOf, textRegionOf } from "../shapes/outline";
import { capacityOf, continuedWithin, fitToCapacity, fitToWidth, LABEL_FONT_SIZE } from "../../label/textMetrics";

/**
 * `labels` — what an element says, declared rather than drawn.
 *
 * <b>This is the addition that closes the measured gap.</b> Every built-in shape carries
 * exactly one label; only `styled-box` carries more, and its three are fixed as name, type-line
 * and description. So a module drawing a card with a second line has had no declarative way to
 * say so, and ten of the twenty-eight renderers hand-roll raw `<text>` for exactly that. This
 * replaces the hand-rolling rather than adding a second way to do it.
 *
 * <b>The collection case is the acceptance, not a variant of it.</b> A fixed set of named slots
 * — which `styled-box` already is — would serve c4 and leave the rdf family exactly where it
 * is. `owl-card` is the shape this was specified against: a header, a badge line present only
 * when its list is non-empty, and <em>one line per entry of a model collection</em>, each
 * composed as `predicate: value annotation` and truncated to the box. A design that passes with
 * three fixed slots has not satisfied this.
 *
 * Layout here is <b>pure</b> — declarations and a source in, positioned lines out — so the
 * acceptance can be asserted without a canvas, and so the same function serves the renderer, a
 * test and a future measurement.
 */

/** One line, positioned and ready to draw. The renderer adds no geometry of its own. */
export interface LaidOutLabel {
  text: string;
  x: number;
  y: number;
  anchor: "start" | "middle" | "end";
  typography?: LabelTypography;
  editable: boolean;
  className?: string;
  tooltip?: string;
  /** Which declaration produced it, and which line of it - what an editor and a test address. */
  declarationIndex: number;
  lineIndex: number;
  /** Which of the declaration's `columns` drew it; absent for the line itself. */
  columnIndex?: number;
}

/**
 * A key unique among one element's laid-out lines, for React to tell them apart.
 *
 * Declaration and line alone are not enough: a declaration with `columns` draws its line AND each
 * column at the same declaration and line index, so SHACL's three-cell constraint row gave three
 * children one key and React warned that it could duplicate or drop them on update.
 */
export function labelKey(line: LaidOutLabel): string {
  const base = `${line.declarationIndex}-${line.lineIndex}`;
  return line.columnIndex === undefined ? base : `${base}-c${line.columnIndex}`;
}

/**
 * The text broken into lines no wider than `width`, at spaces and at explicit newlines.
 *
 * A word longer than the line is broken rather than allowed to overflow: a URL in a comment is
 * still text somebody has to read, and a line that runs out of the shape is not a kinder answer.
 */
function wrappedLines(text: string, width: number, fontSize: number): readonly string[] {
  const capacity = capacityOf(width, fontSize);
  const lines: string[] = [];
  for (const paragraph of text.split("\n")) {
    let current = "";
    for (const word of paragraph.split(" ")) {
      let remaining = word;
      while (remaining.length > capacity) {
        // A word that cannot fit any line at all: break it at the capacity.
        if (current.length > 0) {
          lines.push(current);
          current = "";
        }
        lines.push(remaining.slice(0, capacity));
        remaining = remaining.slice(capacity);
      }
      const candidate = current.length === 0 ? remaining : `${current} ${remaining}`;
      if (candidate.length <= capacity) {
        current = candidate;
        continue;
      }
      if (current.length > 0) {
        lines.push(current);
      }
      current = remaining;
    }
    lines.push(current);
  }
  return lines;
}

/**
 * The wrapped lines of one declaration, fitted to the shape rather than to its bounding box.
 *
 * The band the text occupies decides the region, and the region decides how many lines the text
 * breaks into - so the two are solved together, by fitting until the line count stops changing.
 * A fixed band would be wrong in both directions: the shape's full height collapses the region of
 * a rounded shape to nothing, and one line's height refuses the second line a comment needs.
 */
function fittedWrap(
  text: string,
  shape: BuiltInShape,
  bounds: ShapeBounds,
  lineHeight: number,
  fontSize: number,
): { lines: readonly string[]; region: ShapeBounds; overflowed: boolean } {
  let lineCount = 1;
  let region = textRegionOf(shape, bounds, lineHeight);
  let lines = wrappedLines(text, region.width, fontSize);

  // Six rounds is far more than any real label needs; the cap is here so a shape whose region
  // shrinks as it grows cannot oscillate forever.
  for (let round = 0; round < 6 && lines.length !== lineCount; round++) {
    lineCount = lines.length;
    const wanted = textRegionOf(shape, bounds, lineCount * lineHeight);
    if (wanted.width <= 0 || wanted.height <= 0) {
      break;
    }
    region = wanted;
    lines = wrappedLines(text, region.width, fontSize);
  }

  const room = Math.max(1, Math.floor(region.height / lineHeight));
  if (lines.length <= room) {
    return { lines, region, overflowed: false };
  }

  // What does not fit says so, rather than being drawn outside the shape or silently dropped.
  const visible = lines.slice(0, room);
  const last = visible[room - 1] ?? "";
  const capacity = capacityOf(region.width, fontSize);
  visible[room - 1] = continuedWithin(last, capacity);
  return { lines: visible, region, overflowed: true };
}

/** Vertical offsets for the named slots, as fractions of the box height. */
const SLOT_FRACTION: Record<LabelSlot, number> = { header: 0.28, body: 0.5, footer: 0.78 };

/**
 * The text region a TRUNCATED single line is fitted to, or null to keep trimming to the box.
 *
 * <b>Only where the region is the right answer.</b> A shape with an outline is narrower than its
 * box somewhere - a parallelogram's slanted sides, a diode's curved end - and a line trimmed to the
 * box put its ink within a unit or two of that edge, where a wrapped label in the same shape keeps
 * the region's padding. So a truncated, middle-aligned line in the body of such a shape is trimmed
 * to the region and centred in it, as a wrapped one is. Everything else keeps the box, and that is
 * deliberate rather than unfinished: a box's region is only the box less padding, so fitting there
 * would narrow the truncated label of every module that draws boxes; the region is measured around
 * the vertical centre, so it describes the body slot and not the header or footer; and a line
 * aligned to a side, or placed outside the shape, is not inside the outline at all.
 */
function fittedRegionOf(declaration: LabelDeclaration, bounds: ShapeBounds, shape: BuiltInShape, lineHeight: number): ShapeBounds | null {
  const inside = (declaration.placement ?? "inside") === "inside" || declaration.placement === "inset";
  const fits =
    declaration.truncate === true &&
    inside &&
    declaration.offset === undefined &&
    (declaration.align === undefined || declaration.align === "middle") &&
    (declaration.slot ?? "body") === "body" &&
    outlineOf(shape, bounds).length > 0;
  if (!fits) {
    return null;
  }

  const region = textRegionOf(shape, bounds, lineHeight);
  // A band the shape cannot hold collapses the region; the box's trim is the better answer there.
  return region.width > 0 ? region : null;
}

/** A class stated outright, or resolved against the root a line came from. */
function classOf(className: string | Binding | undefined, root: unknown): string | undefined {
  if (className === undefined || typeof className === "string") {
    return className;
  }

  return resolveOneAt(className as never, root) ?? undefined;
}

/** A declared number, resolved against the element - a stack's start, which a card computes. */
function numberOf(value: DeclaredNumber | undefined, source: BindingSource, fallback: number): number {
  if (value === undefined) {
    return fallback;
  }

  if (typeof value === "number") {
    return value;
  }

  return resolveNumber(value, source) ?? fallback;
}

function baselineOf(
  declaration: LabelDeclaration,
  bounds: ShapeBounds,
  element: { labelAt?: { x: number; y: number } },
): { x: number; y: number; anchor: "start" | "middle" | "end" } {
  const placed = placementOf(declaration, bounds, element);
  // ALIGNMENT SETS X, AND AN OFFSET SETS Y. They were conflated - an offset suppressed the
  // alignment entirely - so azure-pipeline's stage name, which states both, drew CENTRED over
  // a card whose name has sat at the top left since the module was written. Found in a browser;
  // every one of its sixty-eight tests passed, because none of them asks where the name is.
  if (declaration.align === undefined) {
    return placed;
  }

  return { x: alignedX(declaration.align, bounds, declaration.insetX), y: placed.y, anchor: declaration.align };
}

/** Where a column or an aligned label sits horizontally, for the alignment it declares. */
function alignedX(align: "start" | "middle" | "end", bounds: ShapeBounds, insetX: number = LABEL_INSET): number {
  switch (align) {
    case "start":
      return bounds.x + insetX;
    case "end":
      return bounds.x + bounds.width - insetX;
    case "middle":
      return bounds.x + bounds.width / 2;
  }
}

/** How far a `before` label's right edge sits from the element's left edge. */
export const BEFORE_GAP = 8;

/** How far from an edge an aligned label or column sits, when it does not say. */
const LABEL_INSET = 8;

/**
 * The width a truncated line or column may fill: the whole box when centred, and from its inset
 * to the far edge when aligned to a side. Trimming a side-aligned line to the whole box let a
 * column starting 110 into a 260 card keep 252 of text, and run a hundred past the edge.
 */
function roomOf(align: "start" | "middle" | "end" | undefined, bounds: ShapeBounds, insetX: number = LABEL_INSET): number {
  return align === "start" || align === "end" ? bounds.width - insetX : bounds.width;
}

function placementOf(
  declaration: LabelDeclaration,
  bounds: ShapeBounds,
  element: { labelAt?: { x: number; y: number } },
): { x: number; y: number; anchor: "start" | "middle" | "end" } {
  const centreX = bounds.x + bounds.width / 2;
  const centreY = bounds.y + bounds.height / 2;

  if (declaration.offset) {
    // Measured from the edge the declaration names, so a line pinned below a card's top stays
    // there however tall the card is.
    const originY =
      declaration.anchorTo === "top" ? bounds.y : declaration.anchorTo === "bottom" ? bounds.y + bounds.height : centreY;
    return { x: centreX + declaration.offset.x, y: originY + declaration.offset.y, anchor: "middle" };
  }

  switch (declaration.placement ?? "inside") {
    case "above":
      return { x: centreX, y: bounds.y - 6, anchor: "middle" };
    case "below":
      return { x: centreX, y: bounds.y + bounds.height + 14, anchor: "middle" };
    case "beside": {
      // THE ELEMENT'S OWN LABEL ORIGIN WHERE IT HAS ONE. A Wardley map stores each mark's label
      // offset in the document - in pixels, which is a property of the format that ADP
      // reproduces rather than corrects - and the inline editor has always opened at exactly
      // this point. A label that drew somewhere else would put the editor over text that is
      // not there.
      const at = declaration.offset ?? { x: 0, y: 0 };
      // The baseline, not the box top: `labelAt` is where the EDITOR opens - the top-left of
      // the text it replaces - and a drawn line sits four below that, the same four every other
      // placement here adds.
      return element.labelAt !== undefined
        ? { x: element.labelAt.x + at.x, y: element.labelAt.y + 4 + at.y, anchor: "start" }
        : { x: bounds.x + bounds.width + 6 + at.x, y: centreY + 4 + at.y, anchor: "start" };
    }
    case "before":
      // The mirror of `beside`, end-anchored so the text's RIGHT edge is what is placed: 8 units
      // left of the element whatever the name's length, so names read as a ragged-left column.
      return { x: bounds.x - BEFORE_GAP, y: centreY + 4, anchor: "end" };
    case "inset":
    case "inside":
    default: {
      const fraction = SLOT_FRACTION[declaration.slot ?? "body"];
      return { x: centreX, y: bounds.y + bounds.height * fraction + 4, anchor: "middle" };
    }
  }
}

/**
 * Every line a set of declarations draws, positioned.
 *
 * <b>Nothing here throws and nothing here is skipped silently for the wrong reason.</b> A
 * declaration whose `when` fails contributes no lines; a collection over an empty list
 * contributes none; a path that does not resolve contributes none. All three are the binding
 * resolver's rules, not new ones — this function adds position and nothing else, which is what
 * keeps the two testable apart.
 */
/**
 * Typography with a view-dependent size resolved, if it declares one.
 *
 * <b>Sufficiency row 27.</b> A Wardley map's stage and axis labels hold a readable size as the
 * map zooms while the boundaries they name do not, because a position is only meaningful
 * against its own axes - so the text has to grow in canvas units exactly as the view does.
 * The module computes that today from the view width; the library is what knows the view.
 *
 * The clamp is the declaration's, not a default: a label that grew without bound would swallow
 * the map at the far end of a zoom, and one that shrank without bound would vanish.
 */
export function scaledTypography(typography: LabelTypography | undefined, viewScale: number): LabelTypography | undefined {
  const scale = typography?.scaleWithView;
  if (typography === undefined || scale === undefined) {
    return typography;
  }

  const factor = Math.max(scale.min, Math.min(scale.max, viewScale));
  return { ...typography, fontSize: (typography.fontSize ?? 12) * factor };
}

/**
 * The region a wrapped label's text was laid out in, for the inline editor that opens over it.
 *
 * <b>It returns the layout's own region rather than recomputing a band from the outside.</b> The
 * region and the line count are solved together, so anything that guesses the band arrives at a
 * different box - and an editor that is not exactly over the text it replaces is the defect users
 * see, as text jumping the moment the box opens. One fit, read twice.
 *
 * `null` for a declaration that is not wrapped, whose condition fails, or whose text resolves to
 * nothing: all three mean there is no wrapped block here, and the caller keeps its existing
 * single-line answer.
 */
export function wrappedLabelRegion(
  declaration: LabelDeclaration,
  source: BindingSource,
  bounds: ShapeBounds,
  shape: BuiltInShape = "box",
): ShapeBounds | null {
  if (declaration.wrap !== true || !holds(declaration.when, source)) {
    return null;
  }

  const entries = resolveEntries(declaration.text, source);
  if (entries.length === 0) {
    return null;
  }

  const lineHeight = declaration.stack?.lineHeight ?? Math.round((declaration.typography?.fontSize ?? 12) * 1.4);
  const whole = entries.map((entry) => entry.text).join("\n");
  return fittedWrap(whole, shape, bounds, lineHeight, declaration.typography?.fontSize ?? LABEL_FONT_SIZE).region;
}

export function layoutLabels(
  declarations: readonly LabelDeclaration[] | undefined,
  source: BindingSource,
  bounds: ShapeBounds,
  /** The view's width over the definition's own extent - 1 when the diagram declares none. */
  viewScale = 1,
  /**
   * The shape the label sits in, for a wrapped label's text region. Defaulted to `box`, whose
   * region is the bounds less the padding - so an unwrapped label, and every existing caller,
   * behaves exactly as before.
   */
  shape: BuiltInShape = "box",
): readonly LaidOutLabel[] {
  if (!declarations || declarations.length === 0) {
    return [];
  }

  const laidOut: LaidOutLabel[] = [];

  declarations.forEach((declaration, declarationIndex) => {
    if (!holds(declaration.when, source)) {
      return;
    }

    const entries = resolveEntries(declaration.text, source);
    if (entries.length === 0) {
      return;
    }

    const lines = entries.map((entry) => entry.text);

    if (declaration.wrap === true) {
      const typography = scaledTypography(declaration.typography, viewScale);
      const lineHeight = declaration.stack?.lineHeight ?? Math.round((typography?.fontSize ?? 12) * 1.4);
      const whole = lines.join("\n");
      const fitted = fittedWrap(whole, shape, bounds, lineHeight, typography?.fontSize ?? LABEL_FONT_SIZE);
      const align = declaration.align ?? "middle";
      const anchor = align === "start" ? "start" : align === "end" ? "end" : "middle";
      const x = align === "start" ? fitted.region.x : align === "end" ? fitted.region.x + fitted.region.width : fitted.region.x + (fitted.region.width / 2);
      // Centred in the region: the block of text sits where the shape has room for it, which is
      // not the same as the middle of the bounding box for a shape that is not a rectangle.
      const blockHeight = fitted.lines.length * lineHeight;
      const top = fitted.region.y + Math.max(0, (fitted.region.height - blockHeight) / 2);

      fitted.lines.forEach((line, lineIndex) => {
        laidOut.push({
          text: line,
          x,
          y: top + (lineIndex * lineHeight) + lineHeight * 0.75,
          anchor,
          typography,
          // One authored value beneath all of it, so the whole block is what an editor opens.
          editable: declaration.editable ?? false,
          className: classOf(declaration.className, entries[0]!.root),
          // The full text stays reachable where it did not all fit (Requirement 4.4).
          tooltip: fitted.overflowed ? whole : (declaration.tooltip ? (resolveMany(declaration.tooltip, source)[0] ?? undefined) : undefined),
          declarationIndex,
          lineIndex,
        });
      });
      return;
    }

    const base = baselineOf(declaration, bounds, source.element);
    const typography = scaledTypography(declaration.typography, viewScale);
    const region = fittedRegionOf(declaration, bounds, shape, declaration.stack?.lineHeight ?? Math.round((typography?.fontSize ?? 12) * 1.4));
    const stackStart = numberOf(declaration.stack?.start, source, 0);
    const tooltip = declaration.tooltip ? (resolveMany(declaration.tooltip, source)[0] ?? undefined) : undefined;
    const stack = declaration.stack;

    lines.forEach((line, lineIndex) => {
      const y = stack ? base.y + stackStart + lineIndex * stack.lineHeight : base.y;
      (declaration.columns ?? []).forEach((column, columnIndex) => {
        // The entry's OWN root: for a collection, the item this line came from.
        const text = resolveOneAt(column.text, entries[lineIndex]!.root);
        if (text === null) {
          return;
        }

        const align = column.align ?? "start";
        laidOut.push({
          text: column.truncate ? fitToWidth(text, roomOf(align, bounds, column.insetX), typography?.fontSize ?? LABEL_FONT_SIZE) : text,
          x: alignedX(align, bounds, column.insetX),
          y,
          anchor: align,
          typography: scaledTypography(declaration.typography, viewScale),
          // Never editable: a column is a second value on somebody else's line, and an editor
          // over it would commit to a field the line does not name.
          editable: false,
          className: classOf(column.className, entries[lineIndex]!.root),
          declarationIndex,
          lineIndex,
          columnIndex,
        });
      });

      laidOut.push({
        text: region !== null
          ? fitToCapacity(line, capacityOf(region.width, typography?.fontSize ?? LABEL_FONT_SIZE))
          : declaration.truncate ? fitToWidth(line, roomOf(declaration.align, bounds, declaration.insetX), typography?.fontSize ?? LABEL_FONT_SIZE) : line,
        x: region !== null ? region.x + (region.width / 2) : base.x,
        y,
        anchor: base.anchor,
        typography: scaledTypography(declaration.typography, viewScale),
        // A collection line has no single authored value to write back to, so it is never
        // editable however the declaration is written. Stated here rather than trusted to the
        // author, because an editor over a computed line would commit to nothing.
        editable: (declaration.editable ?? false) && lines.length === 1,
        // Resolved against the ENTRY: a row's own class, for a collection that has one.
        className: classOf(declaration.className, entries[lineIndex]!.root),
        tooltip,
        declarationIndex,
        lineIndex,
      });
    });
  });

  return laidOut;
}
