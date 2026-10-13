# xml - api

Deliberately empty: the XML editor rides the family's wire. Its text arrives on the shared stream through the core session adapter, and its findings and completions are asked on `EditorService.Analyze` and `Complete` in `src/api/editors.proto`, which carry no format; the module answers them through `IEditorAnalyzer` (ruling A1 of its design). It carries no proto of its own.
