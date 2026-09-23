export interface BoxElementProps extends React.SVGProps<SVGGElement> {
  /** Top-left corner, in canvas units. */
  x: number;
  y: number;
  width: number;
  height: number;
  rx?: number;
  label: string;
  boxClassName?: string;
  labelClassName?: string;
  /** Where the label sits inside the box; defaults to left-aligned at mid-height. */
  labelX?: number;
  labelY?: number;
  /** Extra content - badges, marks, counts, tooltips - drawn after the box and label. */
  children?: React.ReactNode;
}

/**
 * A top-left-origin rectangle with a left-aligned label: the plainest way to draw an element,
 * and what the Ansible structure's nodes and the Azure pipeline's stages, jobs and steps all
 * are. Everything beyond the box and its label - indicators, problem marks, job counts -
 * comes in as children, so what an element *says* stays the module's business.
 *
 * All remaining props (handlers, aria, `data-*`) spread onto the wrapping `<g>`.
 */
export function BoxElement({
  x,
  y,
  width,
  height,
  rx = 4,
  label,
  boxClassName,
  labelClassName,
  labelX = 8,
  labelY,
  children,
  style,
  ...groupProps
}: BoxElementProps) {
  return (
    <g transform={`translate(${x} ${y})`} {...groupProps}>
      {/* The paint goes on the RECT, never on this group: a child that states its own stroke - and
          `.canvas-node` states one for every node - ignores an inherited one (task 28). */}
      <rect className={boxClassName} style={style} x={0} y={0} width={width} height={height} rx={rx} />
      <text className={labelClassName} x={labelX} y={labelY ?? height / 2 + 4}>
        {label}
      </text>
      {children}
    </g>
  );
}
