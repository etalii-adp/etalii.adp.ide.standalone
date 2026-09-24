/**
 * Reading the theme's declared colours, and the WCAG contrast between two of them.
 *
 * <b>This exists because jsdom applies no CSS.</b> A contrast assertion written against a
 * rendered component measures nothing there and passes green - which is the worst shape a
 * guard can have, because it converts "unverified" into "verified" while nothing has been
 * checked. So the check is made at the token level instead: both themes declare literal hex
 * in `index.css`, so the numbers a browser would compute can be computed from the source.
 *
 * The one implementation in the repository, deliberately. An acceptance number stated as a
 * ratio is only meaningful if everyone computes it the same way; two implementations of
 * `contrastRatio` are two different acceptance criteria wearing one name.
 */

/** A token name mapped to the literal it is declared as, per theme. */
export interface ThemeTokens {
  light: Map<string, string>;
  dark: Map<string, string>;
}

/**
 * The tokens `index.css` declares, split by theme.
 *
 * The dark theme is a `@media (prefers-color-scheme: dark)` block that redeclares a subset,
 * so a dark token falls back to its light declaration when the block does not restate it -
 * exactly as the cascade does. `--radius` and `--shadow` ride along harmlessly.
 */
export function themeTokens(indexCss: string): ThemeTokens {
  const darkStart = indexCss.indexOf("@media (prefers-color-scheme: dark)");
  const lightSource = darkStart === -1 ? indexCss : indexCss.slice(0, darkStart);
  const darkSource = darkStart === -1 ? "" : indexCss.slice(darkStart);

  const light = declarations(lightSource);
  const dark = new Map(light);
  for (const [name, value] of declarations(darkSource)) {
    dark.set(name, value);
  }

  return { light, dark };
}

function declarations(css: string): Map<string, string> {
  const found = new Map<string, string>();
  // A declaration may open a line, follow the `{` of a one-line rule, or follow a `;`. Anchoring
  // on the line start alone read `.y { --mine: #0f0; }` as declaring nothing, which a canary
  // caught: it is how a minified or compact stylesheet would have slipped a local palette past
  // the guard as an undefined token.
  for (const match of css.matchAll(/(?:^|[{;])\s*(--[A-Za-z0-9_.-]+)\s*:\s*([^;}]+)[;}]/gm)) {
    found.set(match[1], match[2].trim());
  }

  return found;
}

/**
 * The colour a `var(--x, fallback)` resolves to in one theme, or `undefined` when the token
 * is not declared there.
 *
 * <b>The fallback is deliberately not honoured.</b> A fallback is what makes an undefined
 * token invisible in review - it renders something plausible in the one mode its author had
 * in mind, and the bug shows only in the other theme. A guard that read the fallback would
 * measure the literal rather than the theme, and would have passed on every defect this
 * specification exists to fix.
 */
export function resolveToken(tokens: Map<string, string>, name: string): string | undefined {
  const declared = tokens.get(name);
  if (declared === undefined) {
    return undefined;
  }

  const nested = /^var\(\s*(--[A-Za-z0-9_.-]+)/.exec(declared);
  return nested === null ? declared : resolveToken(tokens, nested[1]);
}

/** Every `var(--token)` a stylesheet reads, fallbacks discarded. */
export function tokensRead(css: string): string[] {
  return [...css.matchAll(/var\(\s*(--[A-Za-z0-9_.-]+)/g)].map((match) => match[1]);
}

function channel(value: number): number {
  const c = value / 255;

  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

/** WCAG 2.x relative luminance of a `#rgb` or `#rrggbb` colour. */
export function relativeLuminance(colour: string): number {
  const hex = colour.trim().replace(/^#/, "");
  const full = hex.length === 3 ? [...hex].map((digit) => digit + digit).join("") : hex;
  if (!/^[0-9a-f]{6}$/i.test(full)) {
    throw new Error(`Not a hex colour: ${colour}`);
  }

  const [r, g, b] = [0, 2, 4].map((at) => channel(Number.parseInt(full.slice(at, at + 2), 16)));

  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** WCAG 2.x contrast ratio, 1 to 21. The order of the two colours does not matter. */
export function contrastRatio(one: string, other: string): number {
  const a = relativeLuminance(one);
  const b = relativeLuminance(other);

  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}
