export interface EllipseElementProps extends React.SVGProps<SVGGElement> {
  /** The ellipse's centre, in canvas units. */
  x: number;
  y: number;
  radiusX: number;
  radiusY: number;
  /** The label, drawn centred; wrapped onto `lines` when the caller has split it. */
  text: string;
  /**
   * A second ring just inside the first - the "these are one and the same" mark notations draw
   * for equivalence. Off by default.
   */
  doubled?: boolean;
  ellipseClassName?: string;
  innerClassName?: string;
  labelClassName?: string;
  /** Extra content drawn after the ellipse and its label. */
  children?: React.ReactNode;
}

/**
 * A centre-origin ellipse with centred text: the round node shape. Written for the RDF
 * family's ontology reading, whose notation draws classes as circles, and kept generic - it
 * knows nothing of any notation's vocabulary, only that some diagrams draw round things.
 *
 * All remaining props (handlers, aria, `data-*`) spread onto the wrapping `<g>`, as every
 * element here does, so interaction and styling stay the calling module's own.
 */
export function EllipseElement({
  x,
  y,
  radiusX,
  radiusY,
  text,
  doubled = false,
  ellipseClassName,
  innerClassName,
  labelClassName,
  children,
  ...groupProps
}: EllipseElementProps) {
  return (
    <g transform={`translate(${x} ${y})`} {...groupProps}>
      <ellipse className={ellipseClassName} rx={radiusX} ry={radiusY} />
      {doubled ? (
        <ellipse className={innerClassName} rx={Math.max(radiusX - 4, 1)} ry={Math.max(radiusY - 4, 1)} />
      ) : null}
      {/*
        NO TEXT NODE AT ALL when there is nothing to say. The empty-string placeholder dates
        from when every shape drew its own single label; a type that declares `labels` passes
        an empty one and draws its lines as siblings, so the placeholder became a blank `<text>`
        sitting BEFORE the real ones - which is what a reader, and `querySelector("text")`,
        finds first.
      */}
      {text === "" ? null : (
        <text className={labelClassName} textAnchor="middle" dominantBaseline="central">
          {text}
        </text>
      )}
      {children}
    </g>
  );
}
