export type StyledBoxShape = "RoundedBox" | "Box" | "Person" | "Cylinder";

export interface StyledBoxElementProps extends React.SVGProps<SVGGElement> {
  /** The box's centre, in canvas units. */
  x: number;
  y: number;
  width: number;
  height: number;
  /** Which silhouette to draw; anything unrecognised falls back to the rounded box. */
  shape?: string;
  /** The fill and text colours, resolved by the document's own styling rules. */
  background: string;
  color: string;
  name: string;
  /** The bracketed type-and-technology line under the name. */
  typeLine?: string;
  description?: string;
  nameClassName?: string;
  typeClassName?: string;
  descriptionClassName?: string;
  children?: React.ReactNode;
}

/**
 * A centre-origin element whose silhouette and colours come from the document's own styling:
 * a person with a head above the box, a data store drawn as a cylinder, a plain or rounded
 * box - each with name, type line and description centred inside. The C4 canvas grew this;
 * it is the shape of any notation whose documents carry their own element styles.
 *
 * All remaining props (handlers, aria, `data-*`) spread onto the wrapping `<g>`.
 */
export function StyledBoxElement({
  x,
  y,
  width,
  height,
  shape = "RoundedBox",
  background,
  color,
  name,
  typeLine,
  description,
  nameClassName,
  typeClassName,
  descriptionClassName,
  children,
  ...groupProps
}: StyledBoxElementProps) {
  const halfWidth = width / 2;
  const halfHeight = height / 2;

  return (
    <g transform={`translate(${x} ${y})`} {...groupProps}>
      {shape === "Person" ? (
        <>
          {/* The person shape: a head above the box, as the reference diagrams draw it. */}
          <circle cx={0} cy={-halfHeight - 8} r={10} fill={background} />
          <rect x={-halfWidth} y={-halfHeight} width={width} height={height} rx={8} fill={background} />
        </>
      ) : shape === "Cylinder" ? (
        <>
          {/* A data store, drawn as the cylinder the notation uses for one. */}
          <rect x={-halfWidth} y={-halfHeight + 6} width={width} height={height - 12} fill={background} />
          <ellipse cx={0} cy={-halfHeight + 6} rx={halfWidth} ry={6} fill={background} />
          <ellipse cx={0} cy={halfHeight - 6} rx={halfWidth} ry={6} fill={background} />
        </>
      ) : (
        <rect
          x={-halfWidth}
          y={-halfHeight}
          width={width}
          height={height}
          rx={shape === "Box" ? 0 : 8}
          fill={background}
        />
      )}

      <text className={nameClassName} y={-halfHeight + 22} textAnchor="middle" fill={color}>
        {name}
      </text>
      {typeLine && (
        <text className={typeClassName} y={-halfHeight + 38} textAnchor="middle" fill={color}>
          {typeLine}
        </text>
      )}
      {description && (
        <text className={descriptionClassName} y={-halfHeight + 58} textAnchor="middle" fill={color}>
          {description}
        </text>
      )}
      {children}
    </g>
  );
}
