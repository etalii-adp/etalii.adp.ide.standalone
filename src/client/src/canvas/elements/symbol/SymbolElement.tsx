export type SymbolVariant = "circle" | "square" | "double-circle";

export interface SymbolElementProps extends React.SVGProps<SVGGElement> {
  /** The mark's centre, in canvas units. */
  x: number;
  y: number;
  /** The mark's radius (a square uses it as its half-side). */
  radius?: number;
  /** Which mark: a plain circle, a square, or a double ring saying "there is more behind this". */
  variant?: SymbolVariant;
  label: string;
  /** Where the label sits, absolute - some notations store the offset in the document. */
  labelX: number;
  labelY: number;
  /** Short words drawn under the label - what the author claimed about this element. */
  badges?: readonly string[];
  /** The wall this element is pushed against: a bar drawn where movement would be resisted. */
  inertia?: boolean;
  markClassName?: string;
  outerClassName?: string;
  labelClassName?: string;
  badgesClassName?: string;
  inertiaClassName?: string;
  children?: React.ReactNode;
}

/**
 * A small mark with its label beside it: how a Wardley map draws a component (circle), an
 * anchor (square) and a submap (double ring), with decorator badges and an optional inertia
 * bar. The mark says which kind of element it is; the badges say what the author claimed
 * about it - both without entering an edit mode.
 *
 * All remaining props (handlers, aria, `data-*`) spread onto the wrapping `<g>`.
 */
export function SymbolElement({
  x,
  y,
  radius = 9,
  variant = "circle",
  label,
  labelX,
  labelY,
  badges = [],
  inertia = false,
  markClassName,
  outerClassName,
  labelClassName,
  badgesClassName,
  inertiaClassName,
  children,
  style,
  ...groupProps
}: SymbolElementProps) {
  return (
    <g {...groupProps}>
      {/* The paint goes on the drawn shape, never on the wrapping group: a child that states its
          own stroke - and every node class states one - ignores an inherited one (task 28). */}
      {variant === "square" ? (
        <rect className={markClassName} style={style} x={x - radius} y={y - radius} width={radius * 2} height={radius * 2} />
      ) : variant === "double-circle" ? (
        <g>
          <circle className={markClassName} style={style} cx={x} cy={y} r={radius} />
          <circle className={outerClassName} style={style} cx={x} cy={y} r={radius + 4} />
        </g>
      ) : (
        // The DEFAULT variant, and the one this file got wrong: `square` and `double-circle` were
        // painted while the plain circle - a wardley component, the commonest mark of the three -
        // was left bare. Every branch takes the paint, so adding a fourth cannot quietly skip it.
        <circle className={markClassName} style={style} cx={x} cy={y} r={radius} />
      )}

      {inertia ? (
        <line className={inertiaClassName} x1={x + radius + 4} y1={y - radius - 2} x2={x + radius + 4} y2={y + radius + 2} />
      ) : null}

      <text className={labelClassName} x={labelX} y={labelY}>
        {label}
      </text>

      {badges.length > 0 ? (
        <text className={badgesClassName} x={labelX} y={labelY + 16}>
          {badges.join(" · ")}
        </text>
      ) : null}
      {children}
    </g>
  );
}
