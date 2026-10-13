import type { EditorAnalysis, EditorCompletion, EditorCompletionList, EditorFinding, EditorHover } from "@client/editors/editorAnalysis";
import { resolveSchemaReference } from "@client/editors/schemaReference";
import { schemaFileKey, type SchemaFile } from "@client/editors/SchemaFiles";
import type { YamlPart } from "./splitDocuments";
import { completionWindow } from "./completionWindow";
import type { PartReading } from "./yamlReading";

/**
 * What the YAML editor knows about its format, behind the shared {@link EditorAnalysis}
 * (yaml-editor Requirements 4 to 7). **The only file that knows the language service**, and it
 * loads it - with the `yaml` parser and the module's own reading of it - with `import()`, so that
 * about 300 KB gzipped are fetched when a YAML file is first opened and never with the shell.
 *
 * A file is split into its documents and each is analysed on its own under a URI of its own,
 * every range shifted back by the document's start: the service takes one schema per URI, and a
 * mistake in one document must not hide the findings of another (4.5, 5.2).
 *
 * The service never finds a schema itself. Each document's schema line is resolved by the shared
 * resolver, the file is read by `SchemaFiles` over the tab's stream, and the answer is handed to
 * the service under an identifier of `https://adp.invalid/` plus the project path, which its
 * request callback answers from what was read. Nothing is fetched from the network (Security).
 */

/** The JSON editor's bound: above it the editor colours and no longer checks (Performance). */
export const largestChecked = 1_000_000;

/** The identifier a project schema is handed to the service under; `.invalid` never resolves. */
const schemaHost = "https://adp.invalid/";

/** Where a `$ref` the shared rules refuse is pointed, so the service's request says why. */
const refusedScheme = "adp-refused:";

/**
 * Prefixed to every deprecation message handed to the service, so that its deprecation warnings
 * can be told from its other warnings by what the module put there rather than by its sentences:
 * the service gives both the same severity, source and code (recorded in the design).
 */
const deprecationMark = "";

/** The library's `ErrorCode.SchemaResolveError`: a code from here up says a schema could not be followed. */
const schemaResolveError = 0x10000;

/** The dialects the service's own files carry a validator for, held by the spike's tests. */
const supportedDialects = new Map<string, string>([
  ["http://json-schema.org/draft-04/schema", "draft-04"],
  ["http://json-schema.org/draft-07/schema", "draft-07"],
  ["https://json-schema.org/draft/2019-09/schema", "2019-09"],
  ["https://json-schema.org/draft/2020-12/schema", "2020-12"],
]);

export interface YamlAnalysisHost {
  /** The document's path in the project, its file name last: schema references are resolved against it. */
  readonly documentPath: readonly string[];
  /** The schema files as `SchemaFiles` last reported them. A new map is a new reading of them. */
  schemaFiles(): ReadonlyMap<string, SchemaFile>;
  /** The project files the analysis needs read: the schemas the text names and the ones they refer to. */
  needSchemas(paths: readonly (readonly string[])[]): void;
  /** The status line's text has changed. */
  statusChanged?(): void;
}

/** What the analysis loads on first use. Injectable so a test can stand in a service that throws. */
export interface YamlEngine {
  getLanguageService: typeof import("yaml-language-server/lib/esm/languageservice/yamlLanguageService").getLanguageService;
  TextDocument: typeof import("vscode-languageserver-textdocument").TextDocument;
  splitDocuments: (text: string) => YamlPart[];
  readPart: (text: string) => PartReading;
  readSchema: (text: string) => unknown;
}

type LanguageService = ReturnType<YamlEngine["getLanguageService"]>;
type TextDocumentOf = ReturnType<YamlEngine["TextDocument"]["create"]>;

export async function loadYamlEngine(): Promise<YamlEngine> {
  const [service, textDocument, split, reading] = await Promise.all([
    import("yaml-language-server/lib/esm/languageservice/yamlLanguageService"),
    import("vscode-languageserver-textdocument"),
    import("./splitDocuments"),
    import("./yamlReading"),
  ]);
  return {
    getLanguageService: service.getLanguageService,
    TextDocument: textDocument.TextDocument,
    splitDocuments: split.splitDocuments,
    readPart: reading.readPart,
    readSchema: reading.readSchema,
  };
}

/** One document of the text, prepared for the service. */
interface PreparedPart {
  part: YamlPart;
  /** The document's text as the service sees it: its schema line blanked, its length unchanged. */
  text: string;
  uri: string;
  /** The version the service knows this text by; a new text is a new version. */
  version: number;
  /** The identifier its schema is handed over under, when one applies. */
  schemaId?: string;
}

interface Prepared {
  text: string;
  files: ReadonlyMap<string, SchemaFile>;
  parts: PreparedPart[];
  /** Findings about the schema lines themselves: a reference that cannot be followed, a schema that cannot be read. */
  notes: EditorFinding[];
  /** What the status line says when nothing has gone wrong. */
  status: string;
}

let documentVersion = 0;

/** The schema identifier of a project path, and back. */
function schemaIdOf(path: readonly string[]): string {
  return schemaHost + path.map(encodeURIComponent).join("/");
}

function pathOfSchemaId(id: string): string[] | null {
  if (!id.startsWith(schemaHost)) {
    return null;
  }
  const address = id.slice(schemaHost.length).split("#")[0];
  return address.split("/").map(decodeURIComponent);
}

export function createYamlAnalysis(host: YamlAnalysisHost, load: () => Promise<YamlEngine> = loadYamlEngine): EditorAnalysis {
  let engine: Promise<YamlEngine> | undefined;
  let service: { files: ReadonlyMap<string, SchemaFile>; instance: LanguageService } | undefined;
  let prepared: Prepared | undefined;
  let status = "";
  // The URI of each document to the schema the service is to apply to it, read by the provider.
  const schemaOfUri = new Map<string, string>();
  const bundled: ReadonlySet<string> = new Set();

  const setStatus = (next: string) => {
    if (next !== status) {
      status = next;
      host.statusChanged?.();
    }
  };

  /**
   * A schema as handed to the service: every `$ref` decided by the shared rules (project and
   * bundle, never beyond the project root), every deprecation marked, and the paths it refers to
   * collected so they are read too.
   */
  const handOver = (schema: unknown, schemaPath: readonly string[], needed: Map<string, readonly string[]>, problems: string[] = []): unknown => {
    const walk = (value: unknown): unknown => {
      if (Array.isArray(value)) {
        return value.map(walk);
      }
      if (value === null || typeof value !== "object") {
        return value;
      }
      const result: Record<string, unknown> = {};
      for (const [key, item] of Object.entries(value)) {
        if (key === "$ref" && typeof item === "string" && !item.startsWith("#")) {
          const hash = item.indexOf("#");
          const fragment = hash === -1 ? "" : item.slice(hash);
          const target = resolveSchemaReference(item, schemaPath, bundled);
          if (target.kind === "project") {
            needed.set(schemaFileKey(target.path), target.path);
            result[key] = schemaIdOf(target.path) + fragment;
          } else {
            const reason = `refers to "${item}", which ${target.kind === "none" ? target.reason : "is a bundled schema, and ADP bundles none for YAML"}`;
            problems.push(reason);
            result[key] = refusedScheme + encodeURIComponent(reason);
          }
        } else if (key === "deprecationMessage" && typeof item === "string") {
          result[key] = deprecationMark + item;
        } else {
          result[key] = walk(item);
        }
      }
      if (result.deprecated === true && typeof result.deprecationMessage !== "string") {
        result.deprecationMessage = `${deprecationMark}This key is deprecated.`;
      }
      return result;
    };
    return walk(schema);
  };

  const serviceFor = (current: YamlEngine, files: ReadonlyMap<string, SchemaFile>): LanguageService => {
    // A new reading of the schema files is a new service: the service keeps what it was given
    // under each identifier, and a schema that changed on disk must be read again (5.3, by 5.8).
    if (service?.files === files) {
      return service.instance;
    }

    const instance = current.getLanguageService({
      schemaRequestService: (id) => {
        if (id.startsWith(refusedScheme)) {
          return Promise.reject(decodeURIComponent(id.slice(refusedScheme.length)));
        }
        const path = pathOfSchemaId(id);
        const file = path ? files.get(schemaFileKey(path)) : undefined;
        if (!path || !file) {
          return Promise.reject(`The schema ${id} is not a file of the project.`);
        }
        if (file.state !== "loaded") {
          return Promise.reject(file.state === "failed" ? `The schema ${path.join("/")} ${file.reason}.` : `The schema ${path.join("/")} is still being read.`);
        }
        try {
          return Promise.resolve(JSON.stringify(handOver(current.readSchema(file.text), path, new Map())));
        } catch (error) {
          return Promise.reject(`The schema ${path.join("/")} could not be read: ${(error as Error).message}`);
        }
      },
      // Unused: the module hands the service absolute identifiers only.
      workspaceContext: { resolveRelativePath: (relativePath, resource) => new URL(relativePath, resource).href },
    });
    instance.configure({ validate: true, hover: true, completion: true, format: false, hoverSchemaSource: false, yamlVersion: "1.2", customTags: [] });
    instance.registerCustomSchemaProvider((uri) => Promise.resolve(schemaOfUri.get(uri) ?? []));
    service = { files, instance };
    return instance;
  };

  const prepare = (current: YamlEngine, text: string): Prepared => {
    const files = host.schemaFiles();
    if (prepared && prepared.text === text && prepared.files === files) {
      return prepared;
    }

    const needed = new Map<string, readonly string[]>();
    const notes: EditorFinding[] = [];
    const titles: string[] = [];
    let reading = false;
    const fileName = host.documentPath[host.documentPath.length - 1] ?? "document.yaml";

    const parts = current.splitDocuments(text).map((part, index): PreparedPart => {
      const uri = `adp-yaml:/${index}/${encodeURIComponent(fileName)}`;
      const prepared: PreparedPart = { part, text: text.slice(part.from, part.to), uri, version: ++documentVersion };
      if (part.schemaLine) {
        // The service would follow the line itself, relative to a URI that is not the file's: it
        // is blanked to a comment of the same length, so every offset stays where it was.
        const from = part.schemaLine.from - part.from;
        const to = part.schemaLine.to - part.from;
        prepared.text = prepared.text.slice(0, from) + "#" + " ".repeat(to - from - 1) + prepared.text.slice(to);
      }
      schemaOfUri.delete(uri);
      const reference = part.schemaReference;
      if (reference === undefined) {
        return prepared;
      }

      const line = part.schemaLine ?? { from: part.from, to: part.from };
      const note = (reason: string) => notes.push({ from: line.from, to: line.to, severity: "info", message: `The schema "${reference}" ${reason}.`, source: "yaml-schema" });
      const target = resolveSchemaReference(reference, host.documentPath, bundled);
      if (target.kind !== "project") {
        note(target.kind === "none" ? target.reason : "names a bundled schema, and ADP bundles none for YAML");
        return prepared;
      }

      needed.set(schemaFileKey(target.path), target.path);
      const file = files.get(schemaFileKey(target.path));
      if (!file || file.state === "loading") {
        reading = true;
        return prepared;
      }
      if (file.state === "failed") {
        note(file.reason);
        return prepared;
      }

      let schema: unknown;
      try {
        schema = current.readSchema(file.text);
      } catch (error) {
        note(`could not be read: ${(error as Error).message}`);
        return prepared;
      }

      const dialect = typeof schema === "object" && schema !== null ? (schema as { $schema?: unknown }).$schema : undefined;
      if (typeof dialect === "string" && !supportedDialects.has(dialect.replace(/#$/, ""))) {
        note(`is written in the dialect ${dialect}, which the YAML editor does not support; it supports ${[...supportedDialects.values()].join(", ")}`);
        return prepared;
      }

      // Every schema it refers to, and theirs, is read too; what cannot be followed is said here,
      // on the line, because the service then validates nothing and says only that it could not.
      const problems: string[] = [];
      const visited = new Set<string>([schemaFileKey(target.path)]);
      const queue: [unknown, readonly string[]][] = [[schema, target.path]];
      while (queue.length > 0) {
        const [next, nextPath] = queue.shift()!;
        const referred = new Map<string, readonly string[]>();
        handOver(next, nextPath, referred, problems);
        for (const [key, path] of referred) {
          needed.set(key, path);
          if (visited.has(key)) {
            continue;
          }
          visited.add(key);
          const referredFile = files.get(key);
          if (referredFile?.state === "failed") {
            problems.push(`refers to "${path.join("/")}", which ${referredFile.reason}`);
          } else if (referredFile?.state === "loaded") {
            try {
              queue.push([current.readSchema(referredFile.text), path]);
            } catch (error) {
              problems.push(`refers to "${path.join("/")}", which could not be read: ${(error as Error).message}`);
            }
          } else {
            reading = true;
          }
        }
      }
      for (const problem of problems) {
        note(problem);
      }
      const id = schemaIdOf(target.path);
      schemaOfUri.set(uri, id);
      const title = typeof schema === "object" && schema !== null && typeof (schema as { title?: unknown }).title === "string" ? (schema as { title: string }).title : reference;
      if (!titles.includes(title)) {
        titles.push(title);
      }
      return { ...prepared, schemaId: id };
    });

    host.needSchemas([...needed.values()]);
    const status = titles.length > 0 ? titles.join(", ") : reading ? "Reading the schema" : "No schema";
    prepared = { text, files, parts, notes, status };
    return prepared;
  };

  const partAt = (current: Prepared, offset: number): PreparedPart | undefined =>
    [...current.parts].reverse().find((candidate) => candidate.part.from <= offset && offset <= candidate.part.to);

  // One version per prepared text: the service keeps its parse of a URI by version, so a completion
  // or a hover asked for the text that was just validated reuses that parse instead of making its
  // own (Performance). The counter is shared by every editor, so two never meet on one version.
  const documentOf = (current: YamlEngine, part: PreparedPart): TextDocumentOf => current.TextDocument.create(part.uri, "yaml", part.version, part.text);

  const engineOrNull = async (): Promise<YamlEngine | null> => {
    engine ??= load();
    try {
      return await engine;
    } catch (error) {
      setStatus(`Checking is unavailable: ${(error as Error).message}`);
      return null;
    }
  };

  return {
    async findings(text) {
      if (text.length > largestChecked) {
        setStatus("Too large to check");
        return [];
      }
      const current = await engineOrNull();
      if (!current) {
        return [];
      }

      const ready = prepare(current, text);
      const found: EditorFinding[] = [...ready.notes];
      let stopped: string | undefined;

      for (const prepared of ready.parts) {
        const reading = current.readPart(prepared.text);
        const shift = prepared.part.from;
        found.push(...reading.findings.map((finding) => ({ ...finding, from: finding.from + shift, to: finding.to + shift })));

        // No schema finding is invented for a document the parser could not read (6.5).
        if (!prepared.schemaId || !reading.readable || stopped) {
          continue;
        }

        let diagnostics;
        const document = documentOf(current, prepared);
        try {
          diagnostics = await serviceFor(current, ready.files).doValidation(document, false);
        } catch (error) {
          // Said once; the parser's own findings above stay (Reliability).
          stopped = (error as Error).message;
          continue;
        }

        const schemaFindings: EditorFinding[] = [];
        for (const diagnostic of diagnostics) {
          // A schema that could not be followed (the library's own test: a code from 0x10000 up)
          // is said on the schema line, once; where the module already said why, not again.
          if (typeof diagnostic.code === "number" && diagnostic.code >= schemaResolveError) {
            if (!ready.notes.some((note) => note.from === prepared.part.schemaLine?.from)) {
              const line = prepared.part.schemaLine ?? { from: shift, to: shift };
              found.push({ from: line.from, to: line.to, severity: "info", message: diagnostic.message, source: "yaml-schema" });
            }
            continue;
          }
          // The parser's findings are the module's own reading above; only the schema's are taken.
          if (diagnostic.source === "YAML") {
            continue;
          }
          const deprecated = diagnostic.message.startsWith(deprecationMark);
          schemaFindings.push({
            from: document.offsetAt(diagnostic.range.start),
            to: document.offsetAt(diagnostic.range.end),
            severity: deprecated ? "warning" : diagnostic.source?.startsWith("yaml-schema") ? "error" : "info",
            message: deprecated ? diagnostic.message.slice(deprecationMark.length) : diagnostic.message,
            source: "yaml-schema",
          });
        }

        found.push(...withAliasCopies(schemaFindings, reading).map((finding) => ({ ...finding, from: finding.from + shift, to: finding.to + shift })));
      }

      setStatus(stopped ? `Schema support stopped: ${stopped}` : ready.status);
      return found;
    },

    async completions(text, offset) {
      if (text.length > largestChecked) {
        return null;
      }
      const current = await engineOrNull();
      if (!current) {
        return null;
      }

      const ready = prepare(current, text);
      const prepared = partAt(ready, offset);
      // Without a schema nothing is offered, and nothing is guessed (7.7).
      if (!prepared?.schemaId) {
        return null;
      }

      // A long document is answered from the lines the caret's completion depends on (Performance).
      const full = documentOf(current, prepared);
      const caret = full.positionAt(offset - prepared.part.from);
      const lines = prepared.text.split("\n");
      const window = completionWindow(lines, caret.line, caret.character);
      const document = window ? current.TextDocument.create(prepared.uri, "yaml", ++documentVersion, window.text) : full;
      const position = window ? { line: window.caretLine, character: caret.character } : caret;
      const lineStarts = window ? lineStartsOf(lines) : [];
      const toOffset = (at: ServicePosition): number =>
        prepared.part.from + (window ? lineStarts[window.lines[Math.min(at.line, window.lines.length - 1)]] + at.character : full.offsetAt(at));

      let list;
      try {
        list = await serviceFor(current, ready.files).doComplete(document, position, false);
      } catch (error) {
        setStatus(`Schema support stopped: ${(error as Error).message}`);
        return null;
      }
      return toCompletionList(list.items, document.getText().split("\n")[position.line], position, toOffset);
    },

    async hover(text, offset) {
      if (text.length > largestChecked) {
        return null;
      }
      const current = await engineOrNull();
      if (!current) {
        return null;
      }

      const ready = prepare(current, text);
      const prepared = partAt(ready, offset);
      if (!prepared?.schemaId) {
        return null;
      }

      const document = documentOf(current, prepared);
      let hover;
      try {
        hover = await serviceFor(current, ready.files).doHover(document, document.positionAt(offset - prepared.part.from));
      } catch (error) {
        setStatus(`Schema support stopped: ${(error as Error).message}`);
        return null;
      }
      if (!hover) {
        return null;
      }

      const shown = hoverText(hover.contents);
      if (shown === "") {
        return null;
      }
      const range = hover.range ?? { start: document.positionAt(offset - prepared.part.from), end: document.positionAt(offset - prepared.part.from) };
      return { from: document.offsetAt(range.start) + prepared.part.from, to: document.offsetAt(range.end) + prepared.part.from, text: shown } satisfies EditorHover;
    },

    status(text) {
      return text.length > largestChecked ? "Too large to check" : status;
    },
  };
}

/**
 * A finding on a value the document reaches through an alias or a merge key sits where the
 * document uses it (6.3). The service validates the data an alias stands for and places the
 * finding at the anchor; each alias that reaches the anchored value gets the same finding.
 */
function withAliasCopies(findings: EditorFinding[], reading: PartReading): EditorFinding[] {
  const result = [...findings];
  const seen = new Set(findings.map((finding) => `${finding.from}:${finding.to}:${finding.message}`));
  for (const finding of findings) {
    for (const [anchor, range] of reading.anchors) {
      if (range.from > finding.from || finding.to > range.to) {
        continue;
      }
      for (const alias of reading.aliases) {
        const copy = { ...finding, from: alias.from, to: alias.to, message: `${finding.message} (through the alias *${anchor})` };
        const key = `${copy.from}:${copy.to}:${copy.message}`;
        if (alias.anchor === anchor && !seen.has(key)) {
          seen.add(key);
          result.push(copy);
        }
      }
    }
  }
  return result;
}

/** The service's completion item, as far as the module reads it. */
interface ServicePosition {
  line: number;
  character: number;
}

interface ServiceRange {
  start: ServicePosition;
  end: ServicePosition;
}

/** The offset each line starts at. */
function lineStartsOf(lines: readonly string[]): number[] {
  const starts: number[] = [];
  let offset = 0;
  for (const line of lines) {
    starts.push(offset);
    offset += line.length + 1;
  }
  return starts;
}

interface ServiceCompletion {
  label: string;
  kind?: number;
  filterText?: string;
  insertText?: string;
  insertTextFormat?: number;
  /** A plain edit has a `range`; an insert-or-replace edit has both, and replacing is what the list does. */
  textEdit?: { range?: ServiceRange; replace?: ServiceRange; newText: string };
  documentation?: string | { kind: string; value: string };
  detail?: string;
  sortText?: string;
}

/** The kind the service gives a skeleton of a whole mapping: its insert holds the required keys. */
const skeletonKind = 7;
const propertyKind = 10;

function toCompletionList(items: ServiceCompletion[], lineText: string, caret: ServicePosition, toOffset: (at: ServicePosition) => number): EditorCompletionList {
  // Which keys are required is not a field of an item; the service's skeleton of the mapping
  // carries exactly them, one to a line at its own top level (7.1: required ones first).
  const required = new Set<string>();
  for (const item of items) {
    if (item.kind === skeletonKind) {
      for (const line of (item.textEdit?.newText ?? item.insertText ?? "").split("\n")) {
        const key = /^-?\s?([^\s:#][^:]*):/.exec(line);
        if (key && !/^\s{2,}/.test(line)) {
          required.add(key[1].trim());
        }
      }
    }
  }

  const indentation = /^[ ]*/.exec(lineText)![0];

  const result: EditorCompletion[] = [];
  const inserts = new Set<string>();
  for (const item of items) {
    const raw = item.textEdit?.newText ?? item.insertText ?? item.label;
    const snippet = item.insertTextFormat === 2;
    // The service writes bare `$1` tab stops; the shared presentation reads the `${1}` form.
    let insert = snippet ? raw.replace(/\$(\d+)/g, "$${$1}") : raw;
    if (!snippet || !/\$\{\d/.test(insert)) {
      // A plain insert is not re-indented by the shared presentation, so its later lines are
      // given the caret line's indentation here, as the snippet path gives them by itself (7.4).
      insert = insert.replace(/\n/g, `\n${indentation}`);
    }
    // Two items that would insert the same text are shown once (7.3's list, json-editor 7.5).
    if (inserts.has(insert)) {
      continue;
    }
    inserts.add(insert);

    const range = item.textEdit?.range ?? item.textEdit?.replace ?? { start: caret, end: caret };
    result.push({
      label: item.label,
      filterText: item.filterText,
      detail: item.kind === skeletonKind ? "every required key" : item.detail,
      info: documentationText(item.documentation),
      from: toOffset(range.start),
      to: toOffset(range.end),
      insert,
      boost: item.kind === propertyKind && required.has(item.label) ? 10 : item.kind === skeletonKind ? 5 : undefined,
    });
  }
  return { items: result };
}

function documentationText(documentation: ServiceCompletion["documentation"]): string | undefined {
  if (documentation === undefined) {
    return undefined;
  }
  const text = markdownToText(typeof documentation === "string" ? documentation : documentation.value);
  return text === "" ? undefined : text;
}

function hoverText(contents: unknown): string {
  const parts = Array.isArray(contents) ? contents : [contents];
  return parts
    .map((part) => (typeof part === "string" ? part : typeof part === "object" && part !== null && "value" in part ? String((part as { value: unknown }).value) : ""))
    .map(markdownToText)
    .filter((text) => text !== "")
    .join("\n\n");
}

/**
 * The service's markdown reduced to text, shown as text and never as markup (Security): heading
 * marks, emphasis and code fences dropped, its escapes undone, and the "Source:" line it adds
 * about the schema dropped (the design's Hover).
 */
export function markdownToText(markdown: string): string {
  return markdown
    .split("\n")
    .filter((line) => !/^Source: \[/.test(line) && !/^```/.test(line))
    .map((line) => line.replace(/^#{1,6}\s+/, ""))
    .join("\n")
    .replace(/\\([\\`*_{}[\]()#+\-.!|<>])/g, "$1")
    .replace(/\*\*|__/g, "")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}
