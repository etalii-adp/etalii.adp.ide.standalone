# helm-charts client

The `helm/chart` canvas: a chart's anatomy in bands (metadata, the values stack, templates,
dependencies, vendored content), streamed from the backend as elements whose boxes were
measured there - the client draws what the backend computed and never guesses at a size.

Content is read-only; the one edit is repositioning, which stores an authored position in the
registration's `layout:` block through the core layout command (undoable, never touching a
chart file). Activation reveals a node's artifact in the explorer; open ends (an unvendored
dependency, an include no local partial defines) draw as labeled stubs.

The canvas definition is compiled from the bundled specification, `../definition/helm-chart.dis`,
with `compileNotation`; `helmBindings.ts` holds what the library cannot read from it, and the
definition the canvas stated by hand before is kept in `handWrittenDefinition.ts` as the oracle
`helmCompiledDefinition.test.tsx` holds the compiled drawing to.

Styling lives entirely in `helm-charts.css`, one hue per element kind - a theme change is a
stylesheet change, never a protocol one.

## Inline renaming

**Exempt, not pending: there is no rename to mark.** Every context row this module shows is
read-only with a stated reason - a chart's anatomy is defined in its files, and the canvas's
own zero-writes guarantee is load-bearing (`ZeroWrites.Tests.cs`). There is no prompt whose
value is a drawn label, so there is nothing to mark. If an editing action ever arrives, it
adopts the ordinary way through the shared label library.
