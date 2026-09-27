export interface FrameElementProps extends React.SVGProps<SVGGElement> {
  /** The frame's centre, in canvas units. */
  x: number;
  y: number;
  width: number;
  height: number;
  rx?: number;
  /** The corner label - what this enclosure is. */
  label: string;
  labelClassName?: string;
  /**
   * The class the drawn frame itself carries - where a module's stylesheet reaches its outline,
   * rather than by a descendant `rect` selector that would reach whatever else the group holds.
   */
  frameClassName?: string;
  children?: React.ReactNode;
}

/**
 * The enclosure around a group of elements, labelled in its bottom-left corner - a C4
 * boundary around a system's containers, or any notation's grouping frame. Drawn before the
 * elements it holds, so everything it encloses paints on top of it. Its stroke style (dashed,
 * usually) is the stylesheet's business.
 */
export function FrameElement({
  x,
  y,
  width,
  height,
  rx = 6,
  label,
  labelClassName,
  frameClassName,
  children,
  style,
  ...groupProps
}: FrameElementProps) {
  return (
    <g transform={`translate(${x} ${y})`} {...groupProps}>
      {/* The paint goes on the drawn shape, never on the wrapping group: a child that states its
          own stroke - and every node class states one - ignores an inherited one (task 28). */}
      <rect className={frameClassName} style={style} x={-width / 2} y={-height / 2} width={width} height={height} rx={rx} />
      <text className={labelClassName} x={-width / 2 + 12} y={height / 2 - 12}>
        {label}
      </text>
      {children}
    </g>
  );
}
