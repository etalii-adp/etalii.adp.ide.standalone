/**
 * A wavy line under a line of SVG text, drawn as a shape of its own.
 *
 * <b>Why it is drawn rather than styled.</b> CSS says `text-decoration: underline wavy`, and an
 * HTML label honours it; Chromium ignores `text-decoration-style` on SVG `<text>`, so the canvas,
 * which draws every label as SVG text, got a STRAIGHT line from that rule. A notation that marks
 * something by a wavy line under a label - a finding, a disagreement - lost the mark's meaning in
 * the one browser the standalone tool runs in. So the library draws the wave itself.
 *
 * <b>Why a filled ribbon rather than a stroked wave.</b> A label's colour is a `fill`, painted by a
 * theme token or by a module's class. The underline carries the label's classes and fill, so a
 * ribbon - the wave's upper edge out and its lower edge back, closed - is painted in the label's own
 * colour by whatever paints the label, with no second rule per colour to keep in step.
 *
 * Pure - a position and a width in, a path out - so the geometry is asserted without a canvas.
 */

/** Where the underline goes: the text's position and anchor as the renderer drew them, and its width. */
export interface WavyUnderlineAt {
  /** The text's `x`, at its anchor. */
  x: number;
  /** The text's baseline, its `y`. */
  baseline: number;
  /** How wide the text is, measured where the browser can say and estimated where it cannot. */
  width: number;
  anchor: "start" | "middle" | "end";
  fontSize: number;
}

/** The wave's dimensions at a font size: a crest every half wavelength, a little below the descenders. */
export function wavyUnderlineMetrics(fontSize: number): { amplitude: number; wavelength: number; thickness: number; drop: number } {
  const amplitude = Math.max(1, fontSize * 0.12);
  return {
    amplitude,
    wavelength: Math.max(4, fontSize * 0.5),
    thickness: Math.max(1, fontSize / 12),
    // From the baseline to the wave's centre line: below the descenders, then the crest's height.
    drop: fontSize * 0.25 + amplitude,
  };
}

/** Two decimals, so a path stays short and a test can compare it. */
function round(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * The closed path of a wavy underline under the text at {@link WavyUnderlineAt}, spanning exactly the
 * text's width; empty where the text has no width, since a mark under nothing marks nothing.
 */
export function wavyUnderlinePath(at: WavyUnderlineAt): string {
  if (!(at.width > 0) || !Number.isFinite(at.width)) {
    return "";
  }

  const { amplitude, wavelength, thickness, drop } = wavyUnderlineMetrics(at.fontSize);
  const left = at.anchor === "start" ? at.x : at.anchor === "end" ? at.x - at.width : at.x - (at.width / 2);
  const centre = at.baseline + drop;
  // Whole half-waves, at least two, so the line both starts and ends on the centre line and reads as a
  // wave however short the label: a single bump would read as an arc.
  const halves = Math.max(2, Math.round(at.width / (wavelength / 2)));
  const step = at.width / halves;
  // A quadratic's curve reaches half way to its control point, so the control sits at twice the crest.
  const lift = (index: number) => (index % 2 === 0 ? -2 : 2) * amplitude;

  const top = centre - (thickness / 2);
  const bottom = centre + (thickness / 2);
  const parts = [`M${round(left)} ${round(top)}`];
  for (let index = 0; index < halves; index += 1) {
    parts.push(`Q${round(left + (step * (index + 0.5)))} ${round(top + lift(index))} ${round(left + (step * (index + 1)))} ${round(top)}`);
  }
  parts.push(`L${round(left + at.width)} ${round(bottom)}`);
  for (let index = halves - 1; index >= 0; index -= 1) {
    parts.push(`Q${round(left + (step * (index + 0.5)))} ${round(bottom + lift(index))} ${round(left + (step * index))} ${round(bottom)}`);
  }
  parts.push("Z");
  return parts.join(" ");
}
