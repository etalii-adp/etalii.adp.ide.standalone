# Azure Pipelines — client

The web client's pipeline view: the canvas, its model and stream hook, its stylesheet, and the
`register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-azure-devops-pipeline-client`). It lives outside `src/client/`,
so it declares its own dependencies and is installed by the workspace root at `src/package.json` —
without that, nothing here could resolve `react`.

- **The backend decides everything drawn here.** Where each box goes, how big it is, whether an
  arrow is implicit or broken, whether an element is uncertain. The canvas is a renderer, not a
  second opinion about what a pipeline is — the same division the C4 and mindmap canvases keep.
- **Positions are top-left corners, not centres.** C4 puts centres on the wire; this module puts
  corners, because a stage is a container sized from what it holds. The two are not
  interchangeable, and `pipelineModel.ts` says so where it would matter.
- **A collapsed stage carries its own job count.** It sends no jobs, so there is nothing on the
  canvas to count — the count travels on the stage (`job_count` in the proto) for exactly that
  reason.
- **Folding rides `Group`/`Ungroup`**, the third independent use of those two delta actions after
  the mindmap's, which is what Requirement 8.6 asks for rather than a new action.
- **Imports back into the shell** go through the `@client` alias, defined in `vite.config.ts`,
  `vitest.config.ts` and `tsconfig.json`.
- **`azure-pipeline.css`** is namespaced under `.pipeline-*` throughout, so nothing here can
  collide with a shell rule whatever order the bundler emits.
- **Run `npm run typecheck` as well as the tests.** Vitest transpiles without typechecking, so a
  wrong name from the generated protobuf code passes its tests and fails `tsc`. The TypeScript
  generator keeps enum members in `SCREAMING_SNAKE_CASE` where the C# one camel-cases them, which
  is exactly the sort of thing only `tsc` catches.

Not drawn here: any particular *run* of the pipeline — which stage passed, which failed, how long
it took. That needs a live Azure DevOps connection and credentials ADP does not have, and
Requirement 8.8 excludes it deliberately rather than half-building it.

See [../../readme.md](../../readme.md) for what this folder is for, and
`azure-pipeline-diagram` (removed from the tree; in history before `ece03c36`) for this
diagram type's spec.
