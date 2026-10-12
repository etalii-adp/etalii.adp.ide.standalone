# json - client

The JSON editor's canvas, registered for `editor/json` through `register.ts`, which the shell discovers by the same glob that finds every diagram module's registrations.

- `JsonEditorPanel.tsx` - the shared `TextEditorPanel` with JSON's grammar and the analysis below, and `SchemaFiles` reading the schema files the analysis asks for.
- `jsonAnalysis.ts` - the only file that knows `vscode-json-languageservice`, loaded with `import()` on first use so the service is a chunk of its own: findings, the schema a document names, validation, completion and hover text.
- `jsonLanguageService.spike.test.ts` - the spike of the specification's first task, kept as tests: the service loads under this repository's test runner and answers for each supported schema dialect.

The dependencies this module brings are declared in `package.json` beside these files and listed in `docs/dependencies.md`.
