# json

The JSON editor: `.json` files coloured, checked for the mistakes of JSON itself, checked against the JSON Schema their `$schema` names, and completed from that schema. Specified by spec-workflow's `json-editor`.

| Folder | Holds |
| --- | --- |
| `backend/` | The definition (`Editor.cs`: id `json`, title "JSON editor", the claim `.json`), the session and their tests |
| `client/` | The panel, the analysis and their tests |
| `examples/` | Documents the editor is for, replicated byte for byte under `src/examples/editors/json/` |
| `api/` | Nothing: the editor needs no wire of its own |

Manual checks that only a browser can answer are in [tests.md](tests.md).

## What an EDSL specification of this editor would state

The editor is written in code today. When EDSL, the Editor Specification Language, has content, a specification of this editor would have to state each of the following; beside each item is the code that implements it now (json-editor Requirement 10.1). No `.eds` file exists for this editor, and no code reads one.

| What the specification would state | Where it is implemented now |
| --- | --- |
| **The claims**: the extension `.json`, no exact file names, not the fallback, and no default for a shared extension | [`backend/EtAlii.Adp.Editor.Json/Editor.cs`](backend/EtAlii.Adp.Editor.Json/Editor.cs) |
| **The identity**: the id `json`, the title, the description and the icon `mdi-code-json` | [`backend/EtAlii.Adp.Editor.Json/Editor.cs`](backend/EtAlii.Adp.Editor.Json/Editor.cs) |
| **The grammar that colours**: JSON as Lezer parses it (RFC 8259), including how it recovers around a mistake | [`client/JsonEditorPanel.tsx`](client/JsonEditorPanel.tsx), `json()` from `@codemirror/lang-json` |
| **The kinds of token and their colour roles**: a property name is a *name*; a string value a *string*; a number a *number*; `true`, `false` and `null` a *keyword*; braces, brackets, colons and commas *punctuation* | the grammar's highlight tags, mapped to the nine roles once for every editor in [`src/client/src/editors/editorHighlight.ts`](../../client/src/editors/editorHighlight.ts) |
| **Where a schema reference is read from the document**: the root object's string property `$schema`, resolved against the document's folder, never above the project root, never from the network; `$ref` inside a schema by the same rules; a JSON Schema dialect's own address makes the document a schema | `schemaProperty`, `decide` and the `resolveRelativePath` hook in [`client/jsonAnalysis.ts`](client/jsonAnalysis.ts), on the shared rules of [`src/client/src/editors/schemaReference.ts`](../../client/src/editors/schemaReference.ts) |
| **What findings exist and their severities**: a syntax error (error); a repeated property name (warning); a comment or a trailing comma (error); a break of the schema (error); a deprecated property (warning); a schema that cannot be used, with the reason (information, once, on the `$schema` line) | `findings` in [`client/jsonAnalysis.ts`](client/jsonAnalysis.ts) |
| **What the status line says**: the schema's title, else its reference; "No schema"; why nothing is checked; "Too large to check" above 1,000,000 characters | `decide`, `statusOf` and `findings` in [`client/jsonAnalysis.ts`](client/jsonAnalysis.ts) |
| **What triggers completion and what it offers**: typing where a name or value may begin, or Ctrl+Space; only when a schema applies; the properties not yet present with required ones first, the values a schema limits a value to, inserts that are well-formed where they land | `completions` in [`client/jsonAnalysis.ts`](client/jsonAnalysis.ts); the list and its keys are shared, in [`src/client/src/editors/analysisExtensions.ts`](../../client/src/editors/analysisExtensions.ts) |
| **What hover text shows**: the schema's title and description of the property under the pointer or the caret | `hover` in [`client/jsonAnalysis.ts`](client/jsonAnalysis.ts) |

The list is reported to etalii-adp/etalii.adp as input for EDSL (Requirement 10.2), as a comment on [etalii-adp/etalii.adp#154](https://github.com/etalii-adp/etalii.adp/issues/154#issuecomment-6142508389), the issue that collects every editor's list.
