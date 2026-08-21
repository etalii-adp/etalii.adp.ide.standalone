type AdpMarkProps = {
  /** Rendered width and height, in pixels. The mark is drawn on a 32x32 grid. */
  size?: number;
  className?: string;
};

/**
 * The ADP mark: a commit line whose branch terminates in a diagram - architectural
 * artifacts living in, and travelling with, the repository.
 *
 * The trunk and its commits paint with `currentColor` so the mark follows
 * `--color-text` in both themes. The diagram node is the single highlight and takes
 * `--color-primary` through the `adp-mark-accent` class: a `var()` in an SVG
 * presentation attribute does not resolve, so the accent has to come from CSS.
 */
export function AdpMark({ size = 32, className }: AdpMarkProps) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 32 32"
      fill="none"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d="M9 4v24" stroke="currentColor" />
      <path d="M9 24c0-8 3-11 8.5-11" stroke="currentColor" />
      <circle cx="9" cy="8" r="2.5" fill="currentColor" />
      <circle cx="9" cy="24" r="2.5" fill="currentColor" />
      <rect className="adp-mark-accent" x="17.5" y="7.5" width="11" height="11" rx="2" />
    </svg>
  );
}
