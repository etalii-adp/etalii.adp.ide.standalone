# Screenshots

The images the root readme shows, and how each was taken — precisely enough that a second person (or an agent) retakes a comparable image after a UI change. **When the UI changes so that one of these no longer shows what a user sees, retake it**; a stale screenshot is a false claim with a picture attached.

## The shared setup, for every image

- **Source material**: only documents from [`src/examples/`](../../src/examples/) appear in any image, so every capture is reproducible from repository content alone. The project is added in ADP under the name `Examples`.
- **App**: run from source per the root readme's getting-started steps (this is deliberate — capturing the screenshots doubles as verifying those steps). From a worktree, use a free port pair per CLAUDE.md's port rule; the committed set was captured on backend `5480` / client `5474`.
- **Browser**: headless Chrome, viewport **1600×900 CSS px, device pixel ratio 1, 100% zoom**, full-viewport crop, no browser chrome.
- **Theme**: the app's default (dark). Nothing toggled.
- **Login**: the `developer` environment credentials (`admin` / `changeme`) - typed only if a
  sign-in form appears. A developer build opens already authenticated
  (developer-sign-in-bypass), and the script types nothing in that case.
- **Known artefact of that**: a build that bypassed the sign-in renders a quiet `developer
  session` marker at the right-hand end of the header, which a user of a release build never
  sees. The committed set carries it, because the capture was taken without typing a
  credential. To retake without it, start the backend with
  `LocalAuthenticator__DeveloperSessionDisabled=true` and let the script sign in; everything
  else about the images is unchanged.
- **Format and budget**: PNG; each image ≤ 300 KB, the workspace overview ≤ 1 MB.

The whole procedure is executable: [`capture.mjs`](capture.mjs) drives all of the above with puppeteer-core (`npm i puppeteer-core`, then `node capture.mjs http://localhost:5480 .` — it expects Chrome at its standard Windows path; adjust the constant for another machine). Two optional arguments follow: the project's name, when `src/examples/` was added under another name than `Examples`, and a comma-separated list of image names, so `node capture.mjs http://localhost:5480 . Examples owl.png` retakes one image alone. Retaking an image means re-running the script and committing the changed file; the entries below say what each image must show, which is what to check before committing a retake. The script opens each document by its path in the explorer, not by the row's text, since row text repeats (four corpora have an `example 1`, and every folder-subject diagram is a row named `.adp`), and it exits non-zero naming any document that drew nothing.

## The images

| Image | Document opened | What must be visible |
|---|---|---|
| `workspace.png` | `diagrams/c4/industrial-plant/architecture/` → `bottling-mes.dsl` → `bottling-mes.mes-containers.adp` | The whole workspace in one shot: explorer expanded to the document, the container diagram rendered in its tab, the Toolbox panel with the C4 element kinds, the problems strip at the bottom. Nothing selected. |
| `mindmap.png` | `diagrams/mindmap/example 1/` → `mindmap.mm` → `mindmap.adp` | The mindmap laid out left-to-right in its tab; Toolbox showing the Node entry. Nothing selected. |
| `wardley-map.png` | `diagrams/wardley-map/example 1/` → `tea.owm` → `tea.adp` | The bounded map: both axes labelled, the four evolution bands named along the bottom, components and links drawn. Nothing selected. |
| `timeline.png` | `diagrams/timeline/example-2/` → `roadmap.tml` → `roadmap.adp` | Periods and moments on rows, the year ruler along the bottom, connections drawn; Toolbox showing Element and Moment. Nothing selected. |
| `azure-pipeline.png` | `diagrams/azure-devops-pipeline/example 1/` → `multi-stage.yml` → `multi-stage.adp` | The stage graph left-to-right with its dependency edges. Nothing selected. |
| `dependency-graph.png` | `diagrams/dependency-graph/example-1/` → `services.dgr` → `services.adp` | The service graph with labelled edges. Nothing selected. |
| `c4-context.png` | `diagrams/c4/industrial-plant/architecture/` → `bottling-mes.dsl` → `bottling-mes.adp` | The system context: the Manufacturing Execution System in the middle, the five people above it and the external systems below; Toolbox showing Person and Software System. Nothing selected. |
| `c4-component.png` | `diagrams/c4/industrial-plant/architecture/` → `bottling-mes.dsl` → `bottling-mes.order-service-components.adp` | The Order Service's components inside their dashed container boundary, with the containers and external system they talk to outside it; Toolbox adding Component. Nothing selected. |
| `c4-system-landscape.png` | `diagrams/c4/reference/architecture/` → `courier.dsl` → `courier.landscape.adp` | The courier landscape: Courier Tracking between its people and the three external systems, with the key along the bottom. The industrial plant's own landscape holds one system and draws the same picture as its context view, hence the other corpus. Nothing selected. |
| `functional-decomposition-graph.png` | `diagrams/functional-decomposition-graph/field-service/` → `field-service.fdg` → `field-service.adp` | UI elements, actions, data elements and functions in their four shapes, linked parent to child by bezier arrows, with the two comment notes; Toolbox showing all five kinds. Nothing selected. |
| `causal-loop.png` | `diagrams/causal-loop-diagram/on-call/` → `on-call.cld` → `on-call.adp` | The on-call variables joined by polarised links, the delay marks on the slow ones, and the three named loops (R1-R3) at their centres; Toolbox showing Variable, Causal link and Feedback loop. Nothing selected. |
| `hype-cycle.png` | `diagrams/gartner-hype-cycle-graph/electric-vehicles/` → `electric-vehicles.ghg` → `electric-vehicles.adp` | The trends as banners on the year axis (1900 and 2000 labelled along the bottom), coloured by phase with the Peak/Trough/Slope/Plateau key and the tag filter above; Toolbox showing Trend, Trigger and Note. **Not fitted**: fitting a century-long axis squeezes every banner into a sliver, so the document's stored view is kept. Nothing selected. |
| `ansible-structure.png` | `diagrams/ansible-structure/example 1/infrastructure/` → `.adp` | The playbooks, roles and inventories of the folder, with import, role and dependency edges labelled; the Toolbox saying the type offers no elements, since it is read from the folder. Nothing selected. |
| `helm-chart.png` | `diagrams/helm-chart/hello-world/` → `.adp` | The chart, its values file, the templates and the helper they include, with the include edges labelled. Nothing selected. |
| `dotnet-dependency-graph.png` | `diagrams/dotnet-dependency-graph/pipeline-toolkit/` → `PipelineToolkit.slnx` → `PipelineToolkit.adp` | The four projects with their target frameworks on the left, the NuGet packages with their versions on the right, and the reference edges between them; Serilog outlined, showing the two versions referenced (3.1.1 and 4.4.0). Nothing selected. |
| `databricks-bundle.png` | `diagrams/databricks/lakehouse/` → `databricks.yml` → `databricks.adp` | The bundle, its pipeline resource, and the `dev` and `prod` targets as regions, `prod` naming its two overrides; Toolbox showing Job and Pipeline. Nothing selected. |
| `databricks-pipeline.png` | `diagrams/databricks/lakehouse/` → `databricks.yml` → `databricks.pipeline.adp` | Three source libraries flowing into the Bronze to gold pipeline and on to its target catalog, with the compute and notification cards below. Nothing selected. |
| `databricks-job.png` | `diagrams/databricks/lakehouse/resources/` → `nightly-ingest.yml` → `nightly-ingest.adp` | The job's task DAG, the condition task's `true` and `false` branches labelled, and the job cluster card; Toolbox showing the three task kinds. Nothing selected. |
| `rdf.png` | `diagrams/rdf/w3c-turtle/` → `example-1.ttl` → `example-1.adp` | The two resources of the Turtle specification's first example as cards, their types and literal properties as rows (the Russian label with its `@ru` tag), and the `rel:enemyOf` edge between them. Nothing selected. |
| `owl.png` | `diagrams/owl/prov-o/` → `prov-o.ttl` → `prov-o.adp` | PROV-O's classes as VOWL circles with property edges between them, fitted and then **zoomed in twice** so the labels read, so the rim of the drawing is cut off. Toolbox showing Class, Object property, Datatype property and Individual. Nothing selected. |
| `shacl.png` | `diagrams/shacl/fair-data-point/` → `navigation-shapes.ttl` → `navigation-shapes.adp` | The five navigation node shapes as cards with their targets and property rows (path and cardinality), and the edges between shapes. Nothing selected. |
| `sparql.png` | `diagrams/sparql/w3c-sparql/` → `optional.rq` → `optional.adp` | The `SELECT` frame, the `?x` pattern with its `foaf:name` edge, and the `OPTIONAL` group as a region holding `?mbox`. Nothing selected. |
| `markdown-editor.png` | `editors/markdown/` → `guide.md` | The three-part editor: heading outline above, CodeMirror text left, rendered preview right — and the status line reading **Saved**, since nothing was edited. |
| `plain-text-editor.png` | `editors/plain/` → `crlf-notes.txt` | The plain-text editor: line numbers and the four lines of the note in a monospace face, and the status line reading **Saved**. |

Each diagram capture clicks **Fit to View** after opening (where the toolbar offers it), so the content's framing does not depend on the previous session's pan and zoom; the two exceptions, `hype-cycle.png` and `owl.png`, say so in their rows. Two captures that follow one row of this table should differ only in rendering noise.

## Not captured, 2026-09-28

Three diagram types the catalogue lists as Prototype or better have no image, because every example the repository holds renders in a way that would misrepresent the diagram rather than show it. Each is a picture to add once its rendering is fixed.

| Diagram type | What the capture showed |
|---|---|
| `c4/dynamic` | An empty canvas under its title, for both `bottling-mes.batch-release.adp` and `courier.parcel-scanned.adp`: no element arrives within 20 seconds. |
| `c4/deployment` | Every deployment node, infrastructure node and container instance laid out in one flat row with nothing nested, for both `bottling-mes.plant-deployment.adp` and `courier.production.adp`. |
| `w3c/skos` | `geographic-names.adp` draws every concept on one horizontal line, which reads as a rule across the canvas at any zoom; `business-economics.adp` draws nothing within 20 seconds. |

The `w3c/skos` line was the layout's doing: it put each hierarchy layer on one row however wide, and the STW extracts are a few layers deep and hundreds of concepts wide. Since 2026-09-30 a wide layer wraps onto centred rows (`SkosLayout.cs`), so `geographic-names.adp` lays out about three times as wide as it is tall (held by `SkosExamples.Tests.cs`). Neither image has been retaken yet, and the `business-economics.adp` symptom was not re-examined with that change.

## `dependency-graph.png` was judged and NOT retaken, 2026-09-24 (canvas-single-scrollbar task 5)

**The question**: `canvas-single-scrollbar` made the library's drawing surface a block box, removing about
four pixels of descender space that had been spilling into the scrolling pane. CLAUDE.md says a UI change
that makes a screenshot misleading means retaking it, and this image is a capture of one of the two
diagrams the defect was reported on.

**Judged by measurement rather than by eye, and the eye could not have settled it**: the image is 1600px
wide and the defect is four pixels, so whether a second bar is visible in it is at or below what that
image can decide by looking. There is a vertical scroll affordance at the canvas's right edge, and at that
scale a second four-pixel bar beside it would be about the width of the rendering itself.

**NOT RETAKEN, and the reason answers a question prior to the comparison.** `capture.mjs` captures at
exactly **1600x900**. At that viewport, measured in a live browser three times and in both states -
surface `display: block` as shipped, then forced back to `inline`, then restored - **the scrolling pane's
`scrollHeight` equals its `clientHeight` either way**: `div.tabbed-pane-content` measured 740px of client height against 507px of content, so it carries 233px of slack, which absorbs
four. So at this image's own viewport the defect produces **no pane scroll at all**, and no capture taken
there can be showing a scrollbar it caused. The affordance in the image is the diagram's own.

**The instrument is named because the two available ones are not equivalent evidence.** This is the
in-app browser at 1600x900 with a live perturbation, **not** a `capture.mjs` recapture compared against
the committed file. The task proposed the recapture; a pixel comparison would have told me whether two
images differ, which is a weaker question than whether the defect can appear at that viewport at all. It
also needed `puppeteer-core`, which is not in the tree.

**Both answers were acceptable and only an unrecorded one was not.** *Retake it* would not have been
wrong; it would have been a ritual that taught nobody anything, including whoever performed it.
