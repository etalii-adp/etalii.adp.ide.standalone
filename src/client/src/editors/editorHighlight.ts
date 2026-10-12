import { HighlightStyle } from "@codemirror/language";
import { tags } from "@lezer/highlight";

/**
 * The one colour scheme of every editor module (json-editor Requirement 9.2): Lezer's highlight
 * tags mapped to the nine `--color-syntax-*` tokens of `index.css`. A module brings a grammar and
 * nothing else; no module writes a colour. Bound to CSS variables rather than literals, so the
 * scheme follows a theme switch without the view being rebuilt (Requirement 3.3), and the
 * contrast of every colour is held at the token level by `theme.contrast.test.ts`, because jsdom
 * applies no CSS.
 *
 * The kinds are named once, here, for every format: JSON's property names and YAML's keys are a
 * `name`; XML's attribute values are a `string`, as JSON's string values are; `true`, `false`,
 * `null` and a declaration's keywords are a `keyword`.
 */
export const editorHighlight = HighlightStyle.define([
  { tag: [tags.propertyName, tags.definition(tags.propertyName)], color: "var(--color-syntax-name)" },
  { tag: [tags.string, tags.special(tags.string), tags.attributeValue], color: "var(--color-syntax-string)" },
  { tag: [tags.number, tags.integer, tags.float], color: "var(--color-syntax-number)" },
  { tag: [tags.bool, tags.null, tags.keyword, tags.atom, tags.meta, tags.processingInstruction], color: "var(--color-syntax-keyword)" },
  { tag: [tags.comment, tags.lineComment, tags.blockComment], color: "var(--color-syntax-comment)", fontStyle: "italic" },
  {
    tag: [tags.punctuation, tags.separator, tags.bracket, tags.squareBracket, tags.brace, tags.angleBracket, tags.paren],
    color: "var(--color-syntax-punctuation)",
  },
  { tag: [tags.tagName], color: "var(--color-syntax-tag)" },
  { tag: [tags.attributeName], color: "var(--color-syntax-attribute)" },
  { tag: [tags.invalid], color: "var(--color-syntax-invalid)" },
  // Markdown's prose kinds take the nearest of the nine, with weight and slant carrying the rest:
  // a heading reads as a name, a link and inline code as a string.
  { tag: [tags.heading], color: "var(--color-syntax-name)", fontWeight: "bold" },
  { tag: [tags.strong], fontWeight: "bold" },
  { tag: [tags.emphasis], fontStyle: "italic" },
  { tag: [tags.link, tags.url, tags.monospace], color: "var(--color-syntax-string)" },
]);
