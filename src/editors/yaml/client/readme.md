# yaml - client

The YAML editor's client, registered for `editor/yaml` through `register.ts`, which the shell discovers by the same glob that finds every module's registrations.

| File | Does |
| --- | --- |
| `YamlEditorPanel.tsx` | The shared `TextEditorPanel` with the grammar, the colour, the keys and the analysis; mounts `SchemaFiles` for the schemas the analysis names, and analyses again when one arrives or changes |
| `yamlGrammar.ts` | `@codemirror/lang-yaml`, with the tags the shared scheme has no colour for named by ones it has. No colour is declared here |
| `yamlScalarColour.ts` | Gives each plain value in the visible lines the shared class of the type YAML 1.2 reads it as: `1.10` a number, `yes` a string |
| `yamlKeys.ts` | Tab, Shift+Tab and Enter in spaces, at the indentation width read from the document |
| `yamlAnalysis.ts` | The one file that knows the language service. Loads it, `yaml`, `splitDocuments` and `yamlReading` with `import()` on first use, so the shell's bundle does not carry them |
| `splitDocuments.ts` | Each document of a file, by the `yaml` parser's own markers, with the schema line before its first content |
| `yamlReading.ts` | YAML's own mistakes in one document, by the parser's codes; reads a schema written in JSON or YAML |
| `completionWindow.ts` | The lines of a document longer than 300 lines that a completion depends on, so a list on a 5,000-line document comes within 300 ms |

The service imports Node's `path`; one alias in `src/client/vite.config.ts` puts `path-browserify` in its place, and `src/client/src/viteBrowserExternals.test.ts` fails when a build leaves a module of Node out again.
