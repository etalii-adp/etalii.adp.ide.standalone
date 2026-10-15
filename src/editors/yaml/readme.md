# yaml

The YAML editor: `.yaml` and `.yml` files no diagram claims, in the shared text panel, with YAML's own mistakes marked and, for each document that names a JSON Schema on a `# yaml-language-server: $schema=` line, findings, completion and hover from that schema. The analysis runs on Red Hat's YAML language service, the one inside VS Code's YAML extension, loaded the first time it is needed. Specified by the spec-workflow specification `yaml-editor`.

A `.yml` file that is a diagram's body, an Azure DevOps pipeline registered by its `.adp`, still opens its diagram; "Open as text" opens it here.

| Folder | Holds |
| --- | --- |
| `backend/` | `EtAlii.Adp.Editor.Yaml`: the definition and the session, which hold nothing of YAML's; and its tests |
| `api/` | Nothing: see its readme |
| `client/` | The panel, the grammar's colour, the keys, and the analysis: see its readme |
| `examples/` | The documents the editor is for, replicated byte for byte under `src/examples/editors/yaml/` |

Browser checks: [tests.md](tests.md).

## What an EDSL specification of this editor would state

EDSL, the editor specification language, is a placeholder in [etalii.adp](https://github.com/etalii-adp/etalii.adp). Until it has content, this is the list of what a specification of this editor would have to say, each item beside the file that says it in code today. No `.eds` file exists for this editor and no code reads one; `client/edsl.test.ts` holds that none exists.

| What it would state | Where it is today |
| --- | --- |
| The editor's identity and its claims: `yaml`, "YAML editor", `.yaml` and `.yml` | `backend/EtAlii.Adp.Editor.Yaml/Editor.cs` |
| Which kind of shared colour each token of the grammar takes, and that a plain value takes the colour of the type YAML 1.2 reads it as | `client/yamlGrammar.ts`, `client/yamlScalarColour.ts` |
| **YAML adds:** the keyboard's indentation rules: the width read from the document, Tab to the next stop in spaces, Enter one level deeper after a key or a dash that opens a block | `client/yamlKeys.ts` |
| **YAML adds:** more than one document in a file, each read on its own | `client/splitDocuments.ts` |
| **YAML adds:** the comment line that names a document's schema, and that it applies to its own document only | `client/splitDocuments.ts`, `client/yamlAnalysis.ts` |
| The mistakes of the format itself and their severities: a parser error, a tab used for indentation, a repeated key, an alias that names no anchor, aliases past the expansion bound | `client/yamlReading.ts` |
| How a schema's findings map to severities: a violation an error, a deprecated key a warning, a reference that cannot be followed information on the schema line | `client/yamlAnalysis.ts` |
| Completion and hover from the schema, and that nothing is offered without one | `client/yamlAnalysis.ts`, `client/completionWindow.ts` |
| The size past which nothing is checked | `client/yamlAnalysis.ts` (`largestChecked`) |
| That nothing the editor reads is written back, and the file's encoding, BOM and line endings are kept | `backend/EtAlii.Adp.Editor.Yaml/YamlEditorSession.cs`, the shared `TextEditorSession` on the shared `TextFileBuffer`; `client/nothingWrittenBack.test.tsx` |

This list is posted in [etalii-adp/etalii.adp#154](https://github.com/etalii-adp/etalii.adp/issues/154), the issue that collects every editor's EDSL list (opened 2026-10-13 on Peter's ruling).
