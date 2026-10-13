import { Parser } from "yaml";

/**
 * One YAML document of a file: where it starts and ends in the whole text, and the schema line
 * found in the comments before its first content (yaml-editor Requirement 4.5, 5.1 and 5.2).
 */
export interface YamlPart {
  /** Offset of the part's first character in the whole text. */
  from: number;
  /** Offset just past its last character. Parts are contiguous: one part's `to` is the next one's `from`. */
  to: number;
  /** What the schema line names, as written, when the part has one. */
  schemaReference?: string;
  /** The schema line's comment, as offsets into the whole text, where a finding about the reference is shown. */
  schemaLine?: { from: number; to: number };
}

/**
 * The convention the language service established and editors outside ADP honour (Q2): a comment
 * `# yaml-language-server: $schema=<reference>`. The reference runs to the end of the comment.
 */
const schemaLine = /^#\s*yaml-language-server\s*:\s*\$schema=(.*)$/;

/**
 * Splits a text into its YAML documents by the markers the `yaml` parser itself reports: a `---`
 * starts a document and a `...` ends one, and neither counts inside a block scalar or a quoted
 * string, because there the parser reads text and not a marker. A text of one document, the
 * common case, is one part covering it all; an empty text is one empty part.
 *
 * A schema line counts only before its document's first content - at the head of the file, after
 * a `...`, or between a `---` and the content - so it never reaches past the next marker.
 */
export function splitDocuments(text: string): YamlPart[] {
  const parts: YamlPart[] = [];
  let current: YamlPart = { from: 0, to: text.length };
  // Whether the current part already holds a document, so the next `---` begins another part.
  let holdsDocument = false;

  const noteComment = (offset: number, source: string) => {
    const match = schemaLine.exec(source.trimEnd());
    if (match && current.schemaReference === undefined) {
      current.schemaReference = match[1].trim();
      current.schemaLine = { from: offset, to: offset + source.length };
    }
  };

  for (const token of new Parser().parse(text)) {
    switch (token.type) {
      case "comment":
        if (!holdsDocument) {
          noteComment(token.offset, token.source);
        }
        break;
      case "document":
        if (holdsDocument) {
          parts.push({ ...current, to: token.offset });
          current = { from: token.offset, to: text.length };
        }
        holdsDocument = true;
        for (const property of token.start) {
          if (property.type === "comment") {
            noteComment(property.offset, property.source);
          }
        }
        break;
      case "doc-end": {
        // The `...` and what trails it on its line close the document; what follows is the next one's.
        const end = token.offset + token.source.length + (token.end ?? []).reduce((length, item) => length + item.source.length, 0);
        parts.push({ ...current, to: end });
        current = { from: end, to: text.length };
        holdsDocument = false;
        break;
      }
      default:
        break;
    }
  }

  // A trailing part that holds nothing but blank lines after a `...` is still a part: offsets stay
  // contiguous and its findings, if any, have somewhere to sit.
  if (current.from < text.length || parts.length === 0) {
    parts.push({ ...current, to: text.length });
  }
  return parts;
}
