import { isAlias, parseDocument, visit, type Node } from "yaml";
import type { EditorFinding } from "@client/editors/editorAnalysis";

/**
 * What the `yaml` parser - the one the language service is built on - says about one YAML
 * document on its own, as findings with offsets into that document's text (yaml-editor
 * Requirement 4). Loaded with the service, never with the shell: `yamlAnalysis` imports this file
 * with `import()`.
 *
 * Kinds are told apart by the parser's own `code` field, never by its sentences.
 */

/** The parser's own bound on alias expansion, at its default (the design's Security section). */
export const maxAliasCount = 100;

/** A reading of one document: its findings, and whether it could be read well enough to validate. */
export interface PartReading {
  findings: EditorFinding[];
  /** False when the parser could not read the document: no schema finding is invented for it (6.5). */
  readable: boolean;
  /** Where each alias stands, by the anchor it names: a schema finding inside an anchored value is copied to them (6.3). */
  aliases: { anchor: string; from: number; to: number }[];
  /** Each anchored value's range, by anchor name. */
  anchors: Map<string, { from: number; to: number }>;
}

/** A duplicate key is a mistake the parser reads past: the document is still data. */
const readableDespite = new Set(["DUPLICATE_KEY"]);

/**
 * Application tags (`!Ref`, `!!python/object`) are kept as strings and construct nothing; the
 * parser's warning that it does not know one is not a mistake in the document, so it is not shown.
 */
const notShown = new Set(["TAG_RESOLVE_FAILED"]);

export function readPart(text: string): PartReading {
  const document = parseDocument(text, { version: "1.2", uniqueKeys: true, prettyErrors: false, keepSourceTokens: false });
  const findings: EditorFinding[] = [];

  for (const error of document.errors) {
    findings.push({ from: error.pos[0], to: Math.max(error.pos[1], error.pos[0] + 1), severity: "error", message: sentenceOf(text, error.code, error.message, error.pos[0]), source: "yaml" });
  }
  for (const warning of document.warnings) {
    if (!notShown.has(warning.code)) {
      findings.push({ from: warning.pos[0], to: Math.max(warning.pos[1], warning.pos[0] + 1), severity: "warning", message: warning.message, source: "yaml" });
    }
  }

  // Anchors and aliases in document order: an alias names the last anchor set before it, and
  // one that names none is an error (Requirement 4.4). The parser reads such an alias without
  // complaint and fails only when it builds the data, so the module checks it here.
  const anchors = new Map<string, { from: number; to: number }>();
  const aliases: PartReading["aliases"] = [];
  visit(document, (_key, node) => {
    const anchored = node as Node & { anchor?: string };
    if (anchored.anchor && anchored.range) {
      anchors.set(anchored.anchor, { from: anchored.range[0], to: anchored.range[1] });
    }
    if (isAlias(node) && node.range) {
      const at = { anchor: node.source, from: node.range[0], to: node.range[1] };
      if (anchors.has(node.source)) {
        aliases.push(at);
      } else {
        findings.push({ from: at.from, to: at.to, severity: "error", message: `The alias *${node.source} names no anchor set before it.`, source: "yaml" });
      }
    }
  });

  // Building the data is where an alias bomb would explode; the parser stops at its bound and
  // says so, and the document is not handed on to be validated (Security).
  // An alias that names no anchor fails the build too, for its own reason, already said above.
  let expanded = true;
  if (document.errors.length === 0 && aliases.length > 0 && findings.every((finding) => finding.severity !== "error")) {
    try {
      document.toJS({ maxAliasCount });
    } catch (error) {
      expanded = false;
      const first = aliases[0];
      findings.push({ from: first.from, to: first.to, severity: "error", message: `${(error as Error).message}: the aliases of this document expand past ${maxAliasCount} nodes.`, source: "yaml" });
    }
  }

  const readable = expanded && document.errors.every((error) => readableDespite.has(error.code));
  return { findings, readable, aliases, anchors };
}

/**
 * The parser's sentence, with the column that was expected added where the sentence is about
 * indentation and does not say it (Usability). The expected column is the one the nearest content
 * line above starts at: that is the collection the parser read this line as belonging to.
 */
function sentenceOf(text: string, code: string, message: string, offset: number): string {
  const sentence = message.split("\n")[0];
  if (code === "TAB_AS_INDENT") {
    return `${sentence}; indent with spaces.`;
  }
  if (code !== "BAD_INDENT") {
    return sentence;
  }

  const lineStart = text.lastIndexOf("\n", offset - 1) + 1;
  const actual = indentationOf(text.slice(lineStart, text.indexOf("\n", lineStart) === -1 ? undefined : text.indexOf("\n", lineStart)));
  let end = lineStart - 1;
  while (end > 0) {
    const start = text.lastIndexOf("\n", end - 1) + 1;
    const line = text.slice(start, end);
    const trimmed = line.trim();
    if (trimmed !== "" && !trimmed.startsWith("#")) {
      const expected = indentationOf(line);
      return expected === actual ? sentence : `${sentence}: column ${expected + 1} was expected, this line starts at column ${actual + 1}.`;
    }
    end = start - 1;
  }
  return sentence;
}

function indentationOf(line: string): number {
  return /^ */.exec(line)![0].length;
}

/**
 * A schema as the project stores it, read to data: JSON first, and YAML when it is not JSON,
 * since a JSON Schema is a data structure and may be written in either (Requirement 5.5).
 */
export function readSchema(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    const document = parseDocument(text, { version: "1.2", prettyErrors: false });
    if (document.errors.length > 0) {
      throw new Error(document.errors[0].message.split("\n")[0]);
    }
    return document.toJS({ maxAliasCount });
  }
}

