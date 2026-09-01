# Editors

Text-editor modules, one folder per editor, mirroring `src/diagrams/`' layout exactly:
`backend/` (`EtAlii.Adp.Editor.<Editor>` + `.Tests`), `api/`, `client/`, `examples/`.
An editor claims files by extension or exact name through its `Editor.Definitions`;
the `plain` editor claims nothing and is the fallback every other file lands on.
