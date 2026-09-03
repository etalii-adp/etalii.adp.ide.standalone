# markdown - client

The markdown editor's canvas: `MarkdownEditorPanel.tsx`, a CodeMirror-based editor with a
rendered preview and a clickable heading outline, registered for `editor/markdown` through
`register.ts` - which the shell discovers by the same glob that finds every diagram module's
registrations.
