export interface CenteredBoxElementProps extends React.SVGProps<SVGGElement> {
  /** The box's centre, in canvas units. */
  x: number;
  y: number;
  halfWidth: number;
  halfHeight: number;
  rx?: number;
  text: string;
  /** A short indicator line in the top-right corner - glyphs saying "has notes", "is folded". */
  indicators?: string;
  indicatorsClassName?: string;
  /** Extra content drawn after the box and text. */
  children?: React.ReactNode;
}

/**
 * A centre-origin rounded rectangle with centred text: the mindmap's node. The corner
 * indicator line renders only when there is something to say.
 *
 * All remaining props (handlers, aria, `data-*`) spread onto the wrapping `<g>`.
 */
export function CenteredBoxElement({
  x,
  y,
  halfWidth,
  halfHeight,
  rx = 6,
  text,
  indicators,
  indicatorsClassName,
  children,
  ...groupProps
}: CenteredBoxElementProps) {
  return (
    <g transform={`translate(${x} ${y})`} {...groupProps}>
      <rect x={-halfWidth} y={-halfHeight} width={halfWidth * 2} height={halfHeight * 2} rx={rx} />
      {/*
        NO TEXT NODE AT ALL when there is nothing to say. The empty-string placeholder dates
        from when every shape drew its own single label; a type that declares `labels` passes
        an empty one and draws its lines as siblings, so the placeholder became a blank `<text>`
        sitting BEFORE the real ones - which is what a reader, and `querySelector("text")`,
        finds first.
      */}
      {text === "" ? null : (
        <text textAnchor="middle" dominantBaseline="central">
          {text}
        </text>
      )}
      {indicators ? (
        <text className={indicatorsClassName} x={halfWidth - 4} y={-halfHeight + 4} textAnchor="end">
          {indicators}
        </text>
      ) : null}
      {children}
    </g>
  );
}
