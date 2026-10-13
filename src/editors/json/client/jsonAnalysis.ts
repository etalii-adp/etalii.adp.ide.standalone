import { jsonLanguage } from "@codemirror/lang-json";
import type { EditorAnalysis, EditorCompletion, EditorFinding, EditorFindingSeverity } from "@client/editors/editorAnalysis";
import { resolveSchemaReference } from "@client/editors/schemaReference";
import { schemaFileKey, type SchemaFile } from "@client/editors/SchemaFiles";

/**
 * What the JSON editor knows about a JSON text (json-editor Requirements 4 to 7): its mistakes, the
 * schema its `$schema` names, where it breaks that schema, what may be written at the caret and
 * what is under the pointer. The only file that knows `vscode-json-languageservice`, loaded with
 * `import()` on first use so the service is a chunk of its own and never in the shell's bundle.
 *
 * <b>No schema is read from outside the project.</b> Every reference the service meets - the
 * document's `$schema`, and each `$ref` a schema holds - arrives at `resolveRelativePath` raw and
 * is decided there by `resolveSchemaReference`, before any URI arithmetic. A reference with a
 * scheme never reaches that hook, so the request service decides it the same way. What is refused
 * becomes one information finding on the `$schema` line, with the reason.
 */

/** The service module, as `import()` answers it; the tests hand in a loader of their own. */
type JsonService = typeof import("vscode-json-languageservice");
type LanguageService = import("vscode-json-languageservice").LanguageService;
type JSONDocument = import("vscode-json-languageservice").JSONDocument;
type TextDocument = import("vscode-json-languageservice").TextDocument;
type Diagnostic = import("vscode-json-languageservice").Diagnostic;
type Range = import("vscode-json-languageservice").Range;

/**
 * The parse as the service holds it. Its published type keeps the syntax errors and the comments
 * private, while its own validation reads both; read here so a schema cannot switch them off.
 */
type ParsedJson = JSONDocument & { syntaxErrors: Diagnostic[]; comments: Range[] };

/** A diagnostic's sentence: the service may give markup, which is shown as its text. */
function messageOf(diagnostic: Diagnostic): string {
  return typeof diagnostic.message === "string" ? diagnostic.message : diagnostic.message.value;
}

export interface JsonAnalysisOptions {
  /** The document's path in the project, its file name last. */
  documentPath: readonly string[];
  /** The schema files read so far, keyed by `schemaFileKey`; a new map whenever one changes. */
  schemaFiles: () => ReadonlyMap<string, SchemaFile>;
  /** Told the full set of project files the last analysis needed, whenever that set changes. */
  onNeeds?: (paths: string[][]) => void;
  /** Told when the status line's text has changed, so the panel can show it. */
  onStatus?: (status: string) => void;
  /** The schemas ADP ships, by address. Empty in this specification (Requirement 5.3). */
  bundled?: ReadonlyMap<string, string>;
  /** Loads the service. Defaults to `import()`; a test makes it fail or throw. */
  loadService?: () => Promise<JsonService>;
}

/** Above this many characters nothing is validated (the design's Performance bound). */
export const largestCheckedText = 1_000_000;

/** The dialects the service carries a meta-schema for, as their normalised addresses. */
const carriedDialects = new Set([
  "https://json-schema.org/draft-04/schema",
  "https://json-schema.org/draft-07/schema",
  "https://json-schema.org/draft/2019-09/schema",
  "https://json-schema.org/draft/2020-12/schema",
]);

/** A json-schema.org address: a dialect, carried or not. */
const dialectAddress = /^https:\/\/json-schema\.org\/(draft-\d\d|draft\/\d{4}-\d\d)\/schema$/;

/** The service's syntax codes, 0x101 to 0x2FF: the mistakes of JSON itself (Requirement 4). */
const isSyntaxCode = (code: unknown) => typeof code === "number" && code >= 0x100 && code < 0x300;
const schemaResolveError = 0x10000;
const schemaUnsupportedFeature = 0x301;

const scheme = "adp-project:";

function documentUri(path: readonly string[]): string {
  return `${scheme}/${path.map(encodeURIComponent).join("/")}`;
}

/** The project path of an `adp-project:` URI, or undefined for any other. */
function pathOf(uri: string): string[] | undefined {
  if (!uri.startsWith(scheme)) {
    return undefined;
  }
  return uri.slice(scheme.length).replace(/^\/+/, "").split("/").map(decodeURIComponent);
}

/** `http://json-schema.org/draft-07/schema#` and its https form are one address. */
function normaliseAddress(reference: string): string {
  return reference.trim().replace(/^http:\/\/json-schema\.org\//, "https://json-schema.org/").replace(/#$/, "");
}

/** Why a reference gave no schema, kept until the run that asked for it maps its findings. */
interface Refusal {
  reference: string;
  reason: string;
}

/** What one run of the service learned about the document's schema. */
interface SchemaState {
  /** The document's raw `$schema`, or undefined when it has none. */
  reference?: string;
  /** The `$schema` property's key and whole range, for placing and filtering findings. */
  keyRange?: [number, number];
  propertyRange?: [number, number];
  /** Whether a schema applies: one was read, or the document is a schema of a carried dialect. */
  applies: boolean;
  /** Why none applies, when the document named one. */
  refusal?: Refusal;
  /** A file the service asked for that has not arrived yet. */
  waiting: boolean;
  /** The status line's text. */
  status: string;
}

/** The root `$schema` property, read from the parsed document as the service reads it. */
function schemaProperty(json: JSONDocument) {
  const root = json.root;
  if (root?.type !== "object") {
    return undefined;
  }
  return root.properties.find((property) => property.keyNode.value === "$schema" && property.valueNode?.type === "string");
}

function severityOf(severity: number | undefined): EditorFindingSeverity {
  return severity === 1 ? "error" : severity === 2 ? "warning" : "info";
}

/** An LSP snippet as the shared presentation applies one: tab stops as `${n}`, literals escaped. */
export function toEditorSnippet(text: string): string {
  let out = "";
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (char === "\\" && index + 1 < text.length) {
      // An escaped character is a literal; a literal brace stays escaped for the snippet engine.
      const next = text[++index];
      out += next === "{" || next === "}" ? `\\${next}` : next;
      continue;
    }
    if (char === "$") {
      const stop = /^\d+/.exec(text.slice(index + 1));
      if (stop) {
        out += `\${${stop[0]}}`;
        index += stop[0].length;
        continue;
      }
      if (text[index + 1] === "{") {
        // `${1:default}` is the same field in both forms; copy it up to its closing brace.
        const close = text.indexOf("}", index);
        out += text.slice(index, close + 1);
        index = close;
        continue;
      }
    }
    if (char === "#" && text[index + 1] === "{") {
      // `#{` opens a field for the snippet engine and is a literal here.
      out += "#\\{";
      index++;
      continue;
    }
    out += char;
  }
  return out;
}

/** The service's hover text is markdown; the shared tooltip shows plain text. */
function plainText(markdown: string): string {
  return markdown.replace(/\\([\\`*_{}[\]()#+\-.!<>|])/g, "$1");
}

/** The grammar's own syntax errors, for when the service cannot be asked (the Reliability section). */
export function grammarFindings(text: string): EditorFinding[] {
  const findings: EditorFinding[] = [];
  jsonLanguage.parser.parse(text).iterate({
    enter: (node) => {
      if (node.type.isError) {
        const to = Math.max(node.to, Math.min(node.from + 1, text.length));
        findings.push({ from: node.from, to, severity: "error", message: "This is not valid JSON here.", source: "json" });
      }
    },
  });
  return findings;
}

export function createJsonAnalysis(options: JsonAnalysisOptions): EditorAnalysis {
  const bundled = options.bundled ?? new Map<string, string>();
  const bundledAddresses = new Set(bundled.keys());
  const loadService = options.loadService ?? (() => import("vscode-json-languageservice"));
  const uri = documentUri(options.documentPath);

  let service: Promise<JsonService> | undefined;
  let stopped: string | undefined;
  let status = "";
  let needs = "";

  // One language service per snapshot of the schema files: the service keeps what it resolved,
  // so a schema that arrives or changes is read by a new one rather than answered from a cache.
  let instance: { files: ReadonlyMap<string, SchemaFile>; language: LanguageService; run: RunState } | undefined;

  /** What the current service instance's requests ran into, reset per instance. */
  interface RunState {
    refusals: Map<string, Refusal>;
    requested: Map<string, string[]>;
    waiting: boolean;
    /** The schema could not be used: no schema completion or hover is offered (5.5). */
    unusable: boolean;
  }

  function setStatus(next: string) {
    if (next !== status) {
      status = next;
      options.onStatus?.(next);
    }
  }

  function reportNeeds(paths: string[][]) {
    const unique = new Map(paths.map((path) => [schemaFileKey(path), path]));
    const key = JSON.stringify([...unique.keys()].sort());
    if (key !== needs) {
      needs = key;
      options.onNeeds?.([...unique.values()]);
    }
  }

  function languageFor(json: JsonService): { language: LanguageService; run: RunState } {
    const files = options.schemaFiles();
    if (instance?.files === files) {
      return instance;
    }

    const run: RunState = { refusals: new Map(), requested: new Map(), waiting: false, unusable: false };
    let refused = 0;
    const refuse = (reference: string, reason: string) => {
      const token = `adp-refused:/${refused++}`;
      run.refusals.set(token, { reference, reason });
      return token;
    };

    const language = json.getLanguageService({
      clientCapabilities: json.ClientCapabilities.LATEST,
      workspaceContext: {
        // Every relative reference arrives here raw: the document's `$schema` and each `$ref`.
        // The shared rules decide it before any URI arithmetic (Requirement 5.2, 5.9).
        resolveRelativePath: (relativePath, resource) => {
          const base = pathOf(resource);
          if (base === undefined) {
            return new URL(relativePath, resource).href;
          }
          const target = resolveSchemaReference(relativePath, base, bundledAddresses);
          switch (target.kind) {
            case "project":
              return documentUri(target.path);
            case "bundled":
              return target.id;
            case "none":
              return refuse(relativePath, target.reason);
          }
        },
      },
      schemaRequestService: (requested) => {
        const refusal = run.refusals.get(requested);
        if (refusal) {
          return Promise.reject(new Error(`${refusal.reference} ${refusal.reason}`));
        }
        const path = pathOf(requested);
        if (path === undefined) {
          // A reference with a scheme never reaches resolveRelativePath; decided here instead.
          const target = resolveSchemaReference(requested, options.documentPath, bundledAddresses);
          if (target.kind === "bundled") {
            return Promise.resolve(bundled.get(target.id) ?? "");
          }
          const reason = target.kind === "none" ? target.reason : "is not a file of the project";
          run.refusals.set(requested, { reference: requested, reason });
          return Promise.reject(new Error(`${requested} ${reason}`));
        }

        const key = schemaFileKey(path);
        run.requested.set(key, path);
        const file = files.get(key);
        if (file?.state === "loaded") {
          return Promise.resolve(file.text);
        }
        if (file?.state === "failed") {
          run.refusals.set(requested, { reference: key, reason: file.reason });
          return Promise.reject(new Error(`${key} ${file.reason}`));
        }
        run.waiting = true;
        return Promise.reject(new Error(`${key} is being read`));
      },
    });
    language.configure({ validate: true, allowComments: false });
    instance = { files, language, run };
    return instance;
  }

  async function serviceOrUndefined(): Promise<JsonService | undefined> {
    service ??= loadService();
    try {
      return await service;
    } catch (error) {
      setStatus(`Checking is unavailable: ${(error as Error).message}`);
      return undefined;
    }
  }

  /** Parses and reads the schema's state; shared by findings, completion and hover. */
  async function prepare(text: string) {
    const json = await serviceOrUndefined();
    if (json === undefined) {
      return undefined;
    }
    const { language, run } = languageFor(json);
    const document: TextDocument = json.TextDocument.create(uri, "json", 1, text);
    const parsed = language.parseJSONDocument(document) as ParsedJson;
    const property = schemaProperty(parsed);
    const state: SchemaState = { applies: false, waiting: false, status: "No schema" };
    if (property?.valueNode) {
      state.reference = String(property.valueNode.value);
      state.keyRange = [property.keyNode.offset, property.keyNode.offset + property.keyNode.length];
      state.propertyRange = [property.offset, property.offset + property.length];
    }
    return { json, language, run, document, parsed, state };
  }

  /** Decides which schema applies, or why none does, before the service is asked to validate. */
  function decide(state: SchemaState, files: ReadonlyMap<string, SchemaFile>): void {
    const reference = state.reference;
    if (reference === undefined) {
      return;
    }
    const address = normaliseAddress(reference);
    if (dialectAddress.test(address)) {
      // The document is itself a schema (Requirement 5.4), checked against its dialect when the
      // service carries that dialect's meta-schema, and said plainly when it does not (6.5).
      if (carriedDialects.has(address)) {
        state.applies = true;
        state.status = reference;
      } else {
        state.refusal = { reference, reason: "is a JSON Schema dialect ADP cannot check against" };
      }
      return;
    }

    const target = resolveSchemaReference(reference, options.documentPath, bundledAddresses);
    if (target.kind === "none") {
      state.refusal = { reference, reason: target.reason };
      return;
    }
    if (target.kind === "bundled") {
      state.applies = true;
      state.status = titleOf(bundled.get(target.id)) ?? reference;
      return;
    }
    const file = files.get(schemaFileKey(target.path));
    if (file === undefined || file.state === "loading") {
      state.waiting = true;
      return;
    }
    if (file.state === "failed") {
      state.refusal = { reference, reason: file.reason };
      return;
    }
    state.applies = true;
    state.status = titleOf(file.text) ?? reference;
  }

  function statusOf(state: SchemaState): string {
    if (state.refusal) {
      return `${state.refusal.reference} ${state.refusal.reason}`;
    }
    if (state.waiting) {
      return `Reading ${state.reference}`;
    }
    return state.status;
  }

  /** One information finding on the `$schema` line, naming the reference and the reason (5.5). */
  function refusalFinding(state: SchemaState, message: string): EditorFinding {
    const [from, to] = state.propertyRange ?? [0, 1];
    return { from, to, severity: "info", message, source: "json-schema" };
  }

  async function findings(text: string): Promise<EditorFinding[]> {
    if (text.length > largestCheckedText) {
      setStatus("Too large to check");
      return [];
    }
    if (stopped !== undefined) {
      return grammarFindings(text);
    }

    try {
      const prepared = await prepare(text);
      if (prepared === undefined) {
        return [];
      }
      const { json, language, run, document, parsed, state } = prepared;
      const files = options.schemaFiles();
      decide(state, files);

      const offsetOf = (diagnostic: Diagnostic): [number, number] => [document.offsetAt(diagnostic.range.start), document.offsetAt(diagnostic.range.end)];
      const result: EditorFinding[] = [];

      // The mistakes of JSON itself, read from the parse whatever a schema says: a schema's
      // allowComments cannot make a comment JSON (Requirement 4.3).
      for (const error of parsed.syntaxErrors) {
        const [from, to] = offsetOf(error);
        result.push({ from, to, severity: error.code === json.ErrorCode.DuplicateKey ? "warning" : "error", message: messageOf(error), source: "json" });
      }
      for (const comment of parsed.comments) {
        result.push({ from: document.offsetAt(comment.start), to: document.offsetAt(comment.end), severity: "error", message: "Comments are not permitted in JSON.", source: "json" });
      }

      if (state.refusal) {
        result.push(refusalFinding(state, `${state.refusal.reference} ${state.refusal.reason}`));
      } else if (state.applies) {
        const diagnostics = await language.doValidation(document, parsed, { comments: "error", trailingCommas: "error", schemaValidation: "error" });
        let resolveReported = false;
        for (const diagnostic of diagnostics) {
          if (isSyntaxCode(diagnostic.code)) {
            continue; // already read from the parse above
          }
          const [from, to] = offsetOf(diagnostic);
          const code = diagnostic.code;
          if (typeof code === "number" && (code >= schemaResolveError || code === schemaUnsupportedFeature)) {
            // A schema that could not be read or is in a dialect the service does not support:
            // said once, as information on the `$schema` line, with no schema findings (5.5, 6.5).
            if (run.waiting) {
              state.waiting = true;
            } else if (!resolveReported) {
              resolveReported = true;
              run.unusable = true;
              state.applies = false;
              // A refusal of ours names the reference and the reason; the service's own sentence
              // would name the token it was handed instead.
              const ours = [...run.refusals].find(([token]) => messageOf(diagnostic).includes(token))?.[1];
              state.refusal = ours ?? { reference: state.reference ?? "", reason: `could not be used: ${messageOf(diagnostic)}` };
              result.push(refusalFinding(state, `${state.refusal.reference} ${state.refusal.reason}`));
            }
            continue;
          }
          // The document's own `$schema` is not a property of the data it describes: the "not
          // allowed" the schema raises on it is dropped, recognised by where it sits.
          if (state.keyRange && from === state.keyRange[0] && to === state.keyRange[1]) {
            continue;
          }
          result.push({ from, to, severity: severityOf(diagnostic.severity), message: messageOf(diagnostic), source: "json-schema" });
        }
        if (state.waiting || state.refusal) {
          // Findings are never given for a schema that did not arrive whole.
          for (let index = result.length - 1; index >= 0; index--) {
            if (result[index].source === "json-schema" && result[index].severity !== "info") {
              result.splice(index, 1);
            }
          }
        }
      }

      reportNeeds([...run.requested.values(), ...pendingDocumentSchema(state)]);
      setStatus(statusOf(state));
      return result;
    } catch (error) {
      // Caught once per document: the grammar's syntax findings stay, and the status says why
      // the rest stopped (the Reliability section).
      stopped = (error as Error).message;
      setStatus(`Schema support stopped: ${stopped}`);
      return grammarFindings(text);
    }
  }

  /** The document's own schema file, asked for before the service ever is. */
  function pendingDocumentSchema(state: SchemaState): string[][] {
    if (state.reference === undefined || dialectAddress.test(normaliseAddress(state.reference))) {
      return [];
    }
    const target = resolveSchemaReference(state.reference, options.documentPath, bundledAddresses);
    return target.kind === "project" ? [target.path] : [];
  }

  /** The required properties of the object at `offset`, from the schemas that match it. */
  async function requiredAt(language: LanguageService, document: TextDocument, parsed: JSONDocument, offset: number): Promise<Set<string>> {
    let node = parsed.getNodeFromOffset(offset, true);
    while (node && node.type !== "object") {
      node = node.parent;
    }
    const required = new Set<string>();
    if (!node) {
      return required;
    }
    for (const match of await language.getMatchingSchemas(document, parsed)) {
      if (match.node === node && Array.isArray(match.schema.required)) {
        for (const name of match.schema.required) {
          required.add(name);
        }
      }
    }
    return required;
  }

  async function completions(text: string, offset: number) {
    if (text.length > largestCheckedText || stopped !== undefined) {
      return null;
    }
    try {
      const prepared = await prepare(text);
      if (prepared === undefined) {
        return null;
      }
      const { json, language, run, document, parsed, state } = prepared;
      decide(state, options.schemaFiles());
      if (!state.applies || run.unusable) {
        return null; // no schema, no completion - and none guessed from elsewhere (7.7)
      }

      const list = await language.doComplete(document, document.positionAt(offset), parsed);
      if (!list) {
        return null;
      }
      const required = await requiredAt(language, document, parsed, offset);
      const items: EditorCompletion[] = [];
      for (const item of list.items) {
        const edit = item.textEdit;
        if (!edit || !("range" in edit)) {
          continue;
        }
        const newText = edit.newText;
        const insert = item.insertTextFormat === json.InsertTextFormat.Snippet ? toEditorSnippet(newText) : newText;
        const documentation = typeof item.documentation === "string" ? item.documentation : item.documentation?.value;
        items.push({
          label: item.label,
          filterText: item.filterText,
          detail: item.detail,
          info: documentation ? plainText(documentation) : undefined,
          from: document.offsetAt(edit.range.start),
          to: document.offsetAt(edit.range.end),
          insert,
          // Required properties first (7.1).
          boost: item.kind === json.CompletionItemKind.Property && required.has(item.label) ? 10 : undefined,
        });
      }
      return { items };
    } catch (error) {
      stopped = (error as Error).message;
      setStatus(`Schema support stopped: ${stopped}`);
      return null;
    }
  }

  async function hover(text: string, offset: number) {
    if (text.length > largestCheckedText || stopped !== undefined) {
      return null;
    }
    try {
      const prepared = await prepare(text);
      if (prepared === undefined) {
        return null;
      }
      const { language, run, document, parsed, state } = prepared;
      decide(state, options.schemaFiles());
      if (!state.applies || run.unusable) {
        return null;
      }
      const found = await language.doHover(document, document.positionAt(offset), parsed);
      if (!found?.range) {
        return null;
      }
      const contents = Array.isArray(found.contents) ? found.contents : [found.contents];
      const shown = contents
        .map((part) => (typeof part === "string" ? part : part.value))
        .filter((part) => part.length > 0)
        .map(plainText)
        .join("\n\n");
      return shown ? { from: document.offsetAt(found.range.start), to: document.offsetAt(found.range.end), text: shown } : null;
    } catch (error) {
      stopped = (error as Error).message;
      setStatus(`Schema support stopped: ${stopped}`);
      return null;
    }
  }

  return {
    findings,
    completions: (text, offset) => completions(text, offset),
    hover,
    status: () => status,
  };
}

/** A schema's `title`, when its text parses and has one. */
function titleOf(text: string | undefined): string | undefined {
  if (!text) {
    return undefined;
  }
  try {
    const parsed = JSON.parse(text) as { title?: unknown };
    return typeof parsed.title === "string" && parsed.title.trim() !== "" ? parsed.title : undefined;
  } catch {
    return undefined;
  }
}
