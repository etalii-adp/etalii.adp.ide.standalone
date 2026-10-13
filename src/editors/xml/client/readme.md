# xml - client

The XML editor's canvas: `XmlEditorPanel.tsx`, the shared `TextEditorPanel` with `@codemirror/lang-xml`'s grammar, registered for `editor/xml` through `register.ts` - which the shell discovers by the same glob that finds every diagram module's registrations. `@codemirror/lang-xml` is this module's own dependency, declared in `package.json`.

- `xmlAnalysis.ts` is the panel's `EditorAnalysis`: it asks the editor service's `Analyze` and `Complete` about the text as typed, abandons an older request for findings when a newer one starts, and says once in the status line when checking is unavailable. Hover text is the completion item the schema offers at a name already written.
- `XmlEditorPanel.tsx` clears the findings on every edit, because the answer about the new text comes from a call, and hands the files an answer read to `SchemaFiles`, so a schema changed on disk is checked again.
- `xmlPrefixColour.ts` marks a name's prefix with the shared scheme's namespace class; `xmlFolding.ts` folds comments and CDATA sections beside the elements the grammar folds.
