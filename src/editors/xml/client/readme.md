# xml - client

The XML editor's canvas: `XmlEditorPanel.tsx`, the shared `TextEditorPanel` with `@codemirror/lang-xml`'s grammar, registered for `editor/xml` through `register.ts` - which the shell discovers by the same glob that finds every diagram module's registrations. `@codemirror/lang-xml` is this module's own dependency, declared in `package.json`.
