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
  children,
  ...groupProps
}: FrameElementProps) {
  return (
    <g transform={`translate(${x} ${y})`} {...groupProps}>
      <rect x={-width / 2} y={-height / 2} width={width} height={height} rx={rx} />
      <text className={labelClassName} x={-width / 2 + 12} y={height / 2 - 12}>
        {label}
      </text>
      {children}
    </g>
  );
}
