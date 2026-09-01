import type { ConnectorBox } from "../../connectors";

export interface SpanElementClasses {
  /** The extent rectangle. */
  span: string;
  /** The single-point diamond. */
  moment: string;
  label: string;
  hint: string;
  /** The resize strips on the edges. */
  adorner: string;
  /** The visible connection anchor dot. */
  anchor: string;
  /** The invisible fat circle that actually takes the anchor's pointer. */
  anchorHit: string;
}

export interface SpanElementProps extends React.SVGProps<SVGGElement> {
  /** The element's box, positioned by its centre. */
  box: ConnectorBox;
  /** A single point in time rather than an extent: drawn as a diamond, no right edge to resize. */
  moment?: boolean;
  label: string;
  /** What the gesture in flight would land on, drawn above the element; null draws nothing. */
  hint?: string | null;
  /** Selection renders the resize adorners and connection anchors. */
  selected?: boolean;
  /** The diamond's radius. */
  pointRadius?: number;
  /** The per-character width the label-fit estimate uses; it errs on trimming a little early. */
  labelCharWidth?: number;
  classes: SpanElementClasses;
  onResizeStart?: (event: React.MouseEvent, side: "left" | "right") => void;
  /** A press on a connection anchor, saying which side it sits on - the begin or the end. */
  onAnchorStart?: (event: React.MouseEvent, side: "left" | "right") => void;
  children?: React.ReactNode;
}

/**
 * A horizontal extent on a row - or a diamond for a single point - with the timeline's
 * selection furniture: resize strips on the edges, and connection anchors painted OVER them.
 *
 * The paint order is deliberate: SVG paints in order, and the full-height adorner strip used
 * to cover the anchor down to a one-pixel sliver - which is why starting a relation felt
 * unreliable, and why a grab meant as a relate became a resize. Now the dot and its generous
 * hit circle sit on top; the strip stays grabbable above and below the dot. The left edge
 * edits the beginning; the right edits the end; a moment has no right edge to offer.
 *
 * A span's label stays centred in its box; one wider than the box is trimmed to what fits
 * with an ellipsis, using the same per-character estimate that decides the fit, rather than
 * overflowing invisibly under the neighbours. A moment's label sits beside its diamond.
 *
 * All remaining props (handlers, aria, `data-*`) spread onto the wrapping `<g>`.
 */
export function SpanElement({
  box,
  moment = false,
  label,
  hint = null,
  selected = false,
  pointRadius = 9,
  labelCharWidth = 7,
  classes,
  onResizeStart,
  onAnchorStart,
  children,
  ...groupProps
}: SpanElementProps) {
  const left = box.x - box.width / 2;
  const top = box.y - box.height / 2;

  return (
    <g {...groupProps}>
      {moment ? (
        <path className={classes.moment} d={diamond(box.x, box.y, pointRadius)} />
      ) : (
        <rect className={classes.span} x={left} y={top} width={box.width} height={box.height} rx={6} />
      )}
      <text
        className={classes.label}
        {...(moment
          ? { x: box.x + pointRadius + 6, textAnchor: "start" as const }
          : { x: box.x, textAnchor: undefined })}
        y={box.y + 4}
      >
        {moment ? label : trimmedToWidth(label, box.width, labelCharWidth)}
      </text>
      {hint ? (
        <text className={classes.hint} x={box.x} y={top - 8}>
          {hint}
        </text>
      ) : null}
      {selected ? (
        <>
          <rect
            className={classes.adorner}
            x={left - 4}
            y={top}
            width={8}
            height={box.height}
            onMouseDown={(event) => onResizeStart?.(event, "left")}
          />
          {!moment ? (
            <rect
              className={classes.adorner}
              x={left + box.width - 4}
              y={top}
              width={8}
              height={box.height}
              onMouseDown={(event) => onResizeStart?.(event, "right")}
            />
          ) : null}
          <circle className={classes.anchor} cx={left} cy={box.y} r={5} />
          <circle
            className={classes.anchorHit}
            cx={left}
            cy={box.y}
            r={14}
            onMouseDown={(event) => onAnchorStart?.(event, "left")}
          />
          <circle className={classes.anchor} cx={left + box.width} cy={box.y} r={5} />
          <circle
            className={classes.anchorHit}
            cx={left + box.width}
            cy={box.y}
            r={14}
            onMouseDown={(event) => onAnchorStart?.(event, "right")}
          />
        </>
      ) : null}
      {children}
    </g>
  );
}

function diamond(cx: number, cy: number, r: number): string {
  return `M ${cx - r} ${cy} L ${cx} ${cy - r} L ${cx + r} ${cy} L ${cx} ${cy + r} Z`;
}

/**
 * The label, trimmed to what its box can hold with an ellipsis when it cannot hold it all.
 * The capacity comes from the same per-character estimate the old beside-the-box placement
 * used, so what fits untrimmed is unchanged.
 */
function trimmedToWidth(label: string, width: number, charWidth: number): string {
  const capacity = Math.floor(Math.max(width - 8, 0) / charWidth);
  if (label.length <= capacity) {
    return label;
  }

  return capacity <= 1 ? "…" : `${label.slice(0, capacity - 1)}…`;
}
