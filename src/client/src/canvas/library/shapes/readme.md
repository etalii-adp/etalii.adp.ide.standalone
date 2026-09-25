# Built-in shapes: one outline each

[`outline.ts`](outline.ts) states each built-in polygon shape's outline **once**, and three readers use that same outline:

- the **drawing**, where `DiagramCanvas` renders the polygon's points;
- the **text region**, where a wrapped label is laid out;
- the **edge point**, where a connector meets the shape.

A shape used to be drawn in one place, given a text position in a second and hit for its edge point in a third. For a box the three agree by accident. For a trapezoid they don't, and the disagreement shows as a label over a slanted edge, or an arrowhead beside the shape rather than on it. Reading one outline is what keeps them from disagreeing.

This readme is about mechanism. **What a module may declare** — the `BuiltInShape` values, and which definition members exist — is [`docs/diagram-module-client-api.md`](../../../../../../docs/diagram-module-client-api.md), which is authoritative for the module-facing surface.

## Which shapes have an outline

`OUTLINED_SHAPES` names them: `diamond`, `hexagon`, `parallelogram`, `trapezoid`, `superellipse` and `diode`.

- **`diamond`, `hexagon`, `parallelogram`** keep the corner lists the canvas already drew. They were moved here unchanged, so their drawing did not shift.
- **`trapezoid`** has corners at the fractions (0,0), (1,0), (0.85,1), (0.15,1), so it is narrower at the bottom than at the top.
- **`superellipse`** is the squircle, |x/a|⁴ + |y/b|⁴ = 1, sampled at 64 points (an even number, so the extremes land on samples).
- **`diode`** is a rectangle from the left edge to `width − height/2`, closed on the right by a semicircle of radius `height/2`, whose arc is sampled at 24 points.

**Every other shape has no outline here, deliberately.** `outlineOf` returns an **empty list** for `box`, `pill`, `ellipse` and the rest. It does not return their bounding box. The empty list means "no polygon outline; use the rectangle": those shapes keep the geometry they already had, because replacing a working circle with a 64-gon would move drawings nobody asked to move.

## The text region

`textRegionOf(shape, bounds, bandHeight)` returns the widest rectangle of the given height, centred vertically, whose four corners all lie inside the outline, less `TEXT_REGION_PADDING` (6 units) on each side. The band's own top **and** bottom decide the width. That is the point: a trapezoid's text has to fit the narrow end of its band, not the wide one. For a shape with no outline it is the bounds less the padding, which is what the canvas already did.

A label declaring `wrap: true` is laid out inside this region by `fittedWrap` in [`../definition/labels.ts`](../definition/labels.ts). The band's height and the line breaks are **solved together**: more lines need a taller band, and a taller band can be narrower in a shape that tapers. Its inline editor opens over **the layout's own region**, rather than a recomputed one, so the box sits exactly over the text it replaces. How the text is broken into lines, the ellipsis on overflow and the editor's keys are described in the label library's readme, [`../../label/readme.md`](../../label/readme.md), where that code lives.

## The edge point

`outlineEdgePoint` gives the point where the ray from the shape's centre, in a given direction, meets the **outline**. A connector attaches there, on the slanted side of a trapezoid rather than on the corner of its bounding box.

It returns **`null`** for a shape with no outline. That is the caller's signal to keep its existing rectangle geometry, so `box` and `pill` connectors do not move. It returns null rather than a fallback so that decision stays at the call site, and this function never gives a second answer.

It takes the **nearest** crossing. That is correct for any outline that is star-shaped about its centre, which every shape here is. **A concave shape added later needs this checked again** rather than silently inheriting it.

## Adding a shape

1. Add the value to `BuiltInShape` and `BUILT_IN_SHAPES` in [`../definition/diagramDefinition.ts`](../definition/diagramDefinition.ts), so the `declarativeModules` guard admits it.
2. Give it an outline here and add it to `OUTLINED_SHAPES`. If it deliberately has no outline, say why beside the empty case.
3. Extend [`outline.test.ts`](outline.test.ts). It checks that every corner of the text region lies inside the outline at height 48 and widths 80, 160 and 400. It was seen to fail against a `textRegionOf` that returned the bounding box, **for the trapezoid and the diode**. The squircle at 80×48 cannot fail that plant, because there the bounds less the padding *is* the correct region. So a new shape needs a case that could actually fail against it, not just a row in the loop.
4. Say what the shape is for in the API document, since a module can only declare what that document says it may.
