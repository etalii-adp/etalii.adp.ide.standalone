# Screenshots

The images the root readme shows, and how each was taken — precisely enough that a second person (or an agent) retakes a comparable image after a UI change. **When the UI changes so that one of these no longer shows what a user sees, retake it**; a stale screenshot is a false claim with a picture attached.

## The shared setup, for every image

- **Source material**: only documents from [`src/examples/`](../../src/examples/) appear in any image, so every capture is reproducible from repository content alone. The project is added in ADP under the name `Examples`.
- **App**: run from source per the root readme's getting-started steps (this is deliberate — capturing the screenshots doubles as verifying those steps). From a worktree, use a free port pair per CLAUDE.md's port rule; the committed set was captured on backend `5480` / client `5474`.
- **Browser**: headless Chrome, viewport **1600×900 CSS px, device pixel ratio 1, 100% zoom**, full-viewport crop, no browser chrome.
- **Theme**: dark, unless the image's name ends in `-light`. The app follows the browser's `prefers-color-scheme`, so the script emulates the theme rather than toggling anything in the app.
- **Login**: the `developer` environment credentials (`admin` / `changeme`) - typed only if a
  sign-in form appears. A developer build opens already authenticated
  (developer-sign-in-bypass), and the script types nothing in that case.
- **Known artefact of that**: a build that bypassed the sign-in renders a quiet `developer
  session` marker at the right-hand end of the header, which a user of a release build never
  sees. Most of the committed set carries it, because the capture was taken without typing a
  credential; the five images added on 2026-10-12 (`c4-dynamic.png`, `c4-deployment.png`,
  `skos.png`, `agent-activity-diagram.png` and `knowledge.png`), `json-editor.png` added on 2026-10-13, and `xml-editor.png` and `yaml-editor.png` added on 2026-10-14, were taken without it. To retake without it, start the backend with
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
| `c4-dynamic.png` | `diagrams/c4/industrial-plant/architecture/` → `bottling-mes.dsl` → `bottling-mes.batch-release.adp` | The batch release scenario: the OPC UA Gateway, Plant Event Bus, Quality Service, Batch Execution Service, Batch Record Archive and ERP Adapter as containers, LIMS and SAP S/4HANA as external systems, joined by numbered interactions; the key along the bottom and Toolbox showing Person, Software System, Container and Component. Nothing selected. |
| `c4-deployment.png` | `diagrams/c4/reference/architecture/` → `courier.dsl` → `courier.production.adp` | Courier Tracking's production deployment: dashed deployment nodes nested around the load balancer and the container instances they host, fitted and then **zoomed in once** so the nesting reads, so the top and bottom of the drawing are cut off; Toolbox showing Deployment Node, Infrastructure Node, Container Instance and Software System Instance. The industrial plant's deployment is denser and fits to a picture too small to read, hence the other corpus. Nothing selected. |
| `functional-decomposition-graph.png` | `diagrams/functional-decomposition-graph/field-service/` → `field-service.fdg` → `field-service.adp` | UI elements, actions, data elements and functions in their four shapes, linked parent to child by bezier arrows, with the two comment notes; Toolbox showing all five kinds. Nothing selected. |
| `causal-loop.png` | `diagrams/causal-loop-diagram/on-call/` → `on-call.cld` → `on-call.adp` | The on-call variables joined by polarised links, the delay marks on the slow ones, and the three named loops (R1-R3) at their centres; Toolbox showing Variable, Causal link and Feedback loop. Nothing selected. |
| `causal-loop-flow-page.png` | `diagrams/causal-loop-diagram/mental-health/mental-health.cld`, exported from the desktop host as its flow page (etalii.adp spec 074) and opened in a browser, not in this host, so `capture.mjs` does not take it; retaken with the `flow_page` example of `modules/diagrams/causal-loop-diagram` in `etalii.adp.ide.desktop` | The page after 20 seconds under Backlog at the size above: dots on the links, Mental health dropout, Exhaustion and the delayed links among those hottest, the external variables' sliders, the dynamics and the chart along the bottom. Nothing selected. |
| `hype-cycle.png` | `diagrams/gartner-hype-cycle-graph/electric-vehicles/` → `electric-vehicles.ghg` → `electric-vehicles.adp` | The trends as banners on the year axis (1900 and 2000 labelled along the bottom), coloured by phase with the Peak/Trough/Slope/Plateau key and the tag filter above; Toolbox showing Trend, Trigger and Note. **Not fitted**: fitting a century-long axis squeezes every banner into a sliver, so the document's stored view is kept. Nothing selected. |
| `ansible-structure.png` | `diagrams/ansible-structure/example 1/infrastructure/` → `.adp` | The playbooks, roles and inventories of the folder, with import, role and dependency edges labelled; the Toolbox saying the type offers no elements, since it is read from the folder. Nothing selected. |
| `helm-chart.png` | `diagrams/helm-chart/hello-world/` → `.adp` | The chart, its values file, the templates and the helper they include, with the include edges labelled. Nothing selected. |
| `dotnet-dependency-graph.png` | `diagrams/dotnet-dependency-graph/pipeline-toolkit/` → `PipelineToolkit.slnx` → `PipelineToolkit.adp` | The four projects with their target frameworks on the left, the NuGet packages with their versions on the right, and the reference edges between them; Serilog outlined, showing the two versions referenced (3.1.1 and 4.4.0). Nothing selected. |
| `databricks-bundle.png` | `diagrams/databricks/lakehouse/` → `databricks.yml` → `databricks.adp` | The bundle, its pipeline resource, and the `dev` and `prod` targets as regions, `prod` naming its two overrides; Toolbox showing Job and Pipeline. Nothing selected. |
| `databricks-pipeline.png` | `diagrams/databricks/lakehouse/` → `databricks.yml` → `databricks.pipeline.adp` | Three source libraries flowing into the Bronze to gold pipeline and on to its target catalog, with the compute and notification cards below. Nothing selected. |
| `databricks-job.png` | `diagrams/databricks/lakehouse/resources/` → `nightly-ingest.yml` → `nightly-ingest.adp` | The job's task DAG, the condition task's `true` and `false` branches labelled, and the job cluster card; Toolbox showing the three task kinds. Nothing selected. |
| `rdf.png` | `diagrams/rdf/w3c-turtle/` → `example-1.ttl` → `example-1.adp` | The two resources of the Turtle specification's first example as cards, their types and literal properties as rows (the Russian label with its `@ru` tag), and the `rel:enemyOf` edge between them. Nothing selected. |
| `owl.png` | `diagrams/owl/prov-o/` → `prov-o.ttl` → `prov-o.adp` | PROV-O's classes as VOWL circles with property edges between them, fitted and then **zoomed in twice** so the labels read, so the rim of the drawing is cut off. Toolbox showing Class, Object property, Datatype property and Individual. Nothing selected. |
| `skos.png` | `diagrams/skos/stw/` → `geographic-names.ttl` → `geographic-names.adp` | The STW geographic names as concept boxes on wrapped rows with their broader and related links between them, fitted and then **zoomed in five times** so the names read, so only the middle of the scheme is shown; Toolbox showing Concept. Nothing selected. |
| `shacl.png` | `diagrams/shacl/fair-data-point/` → `navigation-shapes.ttl` → `navigation-shapes.adp` | The five navigation node shapes as cards with their targets and property rows (path and cardinality), and the edges between shapes. Nothing selected. |
| `sparql.png` | `diagrams/sparql/w3c-sparql/` → `optional.rq` → `optional.adp` | The `SELECT` frame, the `?x` pattern with its `foaf:name` edge, and the `OPTIONAL` group as a region holding `?mbox`. Nothing selected. |
| `supply-chain-automotive.png` | `diagrams/supply-chain/automotive/` → `automotive.supply` → `automotive.adp` | The automotive chain from raw materials in their regional groups through suppliers, manufacturers and assemblers to the distributor, retailers and consumers, every flow a band as wide as its volume; Toolbox showing the seven element kinds. Nothing selected. |
| `supply-chain-gpu-memory.png` | `diagrams/supply-chain/gpu-memory/` → `gpu-memory.supply` → `gpu-memory.adp` | The GPU memory chain in its named groups, its flows drawn as bands whose width follows their volume. Nothing selected. |
| `supply-chain-automotive-light.png` | as `supply-chain-automotive.png` | The same automotive chain in the light theme. Nothing selected. |
| `sankey-brightwater-coffee.png` | `diagrams/sankey/brightwater-coffee/` → `brightwater-coffee.skv` → `brightwater-coffee.adp` | The coffee company's income statement as columns of nodes joined by translucent bands as thick as their value: four revenue streams on the left flowing into revenue, then gross profit and cost of revenue, and on to operating expenses, tax and net profit on the right; Toolbox showing Node. Nothing selected. |
| `sankey-uk-energy.png` | `diagrams/sankey/uk-energy/` → `uk-energy.skv` → `uk-energy.adp` | The UK energy flows from supplies on the left, through conversion, to the uses and losses on the right, each node and band in its own colour with its value in TWh. Nothing selected. |
| `sankey-recent-graduates-light.png` | `diagrams/sankey/recent-graduates/` → `recent-graduates.skv` → `recent-graduates.adp` | In the light theme: the fields of study on the left flowing into employed, unemployed and not in the labour force, and the employed on into jobs that do or do not require a degree. Nothing selected. |
| `agent-behavior-modelling-pull-request-reviewer.png` | `diagrams/agent-behavior-modelling/pull-request-reviewer/` → `pull-request-reviewer.md` → `pull-request-reviewer.adp` | The reviewer's behavior tree top-down: Try in order at the root, Do in order and Do together composites, the Retry up to 2 times and Ask approval before wrappers, and the Check and Do leaves, each labelled with its kind; Toolbox showing the eleven kinds. Nothing selected. |
| `agent-behavior-modelling-research-assistant.png` | `diagrams/agent-behavior-modelling/research-assistant/` → `research-assistant.md` → `research-assistant.adp` | The research assistant's tree: Ask the user while settling the question, three Delegate leaves under Do together, and a Repeat until around drafting and checking. Nothing selected. |
| `agent-behavior-modelling-bug-fixer-light.png` | `diagrams/agent-behavior-modelling/bug-fixer/` → `bug-fixer.md` → `bug-fixer.adp` | In the light theme: the bug fixer's tree with its Only while, Repeat until, Retry up to 2 times and Ask approval before wrappers, a Delegate and an Ask the user leaf. Nothing selected. |
| `agent-activity-diagram.png` | `diagrams/agent-activity-diagram/adp-one-day/` → `adp-one-day.aad` → `adp-one-day.adp` | One day of ADP work: the projects in the middle, their specifications around them with tasks listed by status, the agents, the locations with their branches and pull requests, and the environments at the rim, with the Show archived toggle at the top right; Toolbox showing Project, Specification, Agent, Location and Environment. Nothing selected. |
| `knowledge.png` | `designers/knowledge/yaml/` → `cities.yaml` → `cities.adp` | The knowledge designer's table of cities in its view *Map*: the *All cities* and *Map* view tabs, the columns Country, Name, Population, Tags, Province and Visited with their type icons, selection values as coloured chips, the Province relation as links, and the column summary along the bottom. Not fitted: a table has no canvas. Nothing selected. |
| `hierarchical-requirement.png` | `designers/hierarchical-requirement/web-shop/` → `web-shop.md` → `web-shop.adp` | The Hierarchical Requirement Designer's page of the web shop, from the small example (never the NIST one): the filter bar, the finding about `REQ-NEW` above the page, the item *Storefront* as a box on the left with its requirements to its right and *Checkout* nested in it with the mark of its hidden level (the next item, *Backend*, starts just below the fold); numbers beside the titles, tags with their `#`, categories as badges and states with a dot, *REQ-0002* collapsed, the note icons of the open assessments, and the add icons at the end of each level. Not fitted: the page has no canvas. Nothing selected, and no box under the pointer, so no box shows its hover controls. |
| `markdown-editor.png` | `editors/markdown/` → `guide.md` | The three-part editor: heading outline above, CodeMirror text left, rendered preview right — and the status line reading **Saved**, since nothing was edited. |
| `plain-text-editor.png` | `editors/plain/` → `crlf-notes.txt` | The plain-text editor: line numbers and the four lines of the note in a monospace face, and the status line reading **Saved**. |
| `json-editor.png` | `editors/json/` → `service-broken.json` | The JSON editor on a document that breaks the schema beside it: the keys and values coloured, a red marker in the gutter on each line the schema rejects and the yellow warning on the deprecated `verbose`, wavy underlines under the offending values, and the status line reading **Saved**, **7 errors, 1 warning** and the schema's title *Service settings*. |
| `xml-editor.png` | `editors/xml/` → `library-broken.xml` | The XML editor on a document that breaks the XSD it names: tags, attributes and values coloured, a red marker in the gutter on each line the schema rejects (a book without its required `isbn`, a book without its required year, a year that is not a year and an element the schema does not allow there), wavy underlines under the offending names and values, and the status line reading **Saved**, **4 errors** and **Schema: library.xsd**. |
| `yaml-editor.png` | `editors/yaml/` → `service-broken.yaml` | The YAML editor on a document that breaks the JSON Schema its first line names: keys and values coloured, a red marker in the gutter on each line the schema rejects and the yellow warning on the deprecated `memory`, wavy underlines under the offending keys and values, including the value an alias brings in, and the status line reading **Saved**, **10 errors, 1 warning** and the schema's title *Service*. |

Each diagram capture clicks **Fit to View** after opening (where the toolbar offers it), so the content's framing does not depend on the previous session's pan and zoom; the exceptions, `hype-cycle.png`, `owl.png`, `skos.png`, `c4-deployment.png`, `knowledge.png` and `hierarchical-requirement.png`, say so in their rows. Two captures that follow one row of this table should differ only in rendering noise.

## Not captured, 2026-09-28; captured 2026-10-12

Three diagram types the catalogue lists as Prototype or better had no image on 2026-09-28, because every example the repository held rendered in a way that would misrepresent the diagram: `c4/dynamic` drew an empty canvas, `c4/deployment` laid every node out in one flat row, and `w3c/skos` drew every concept on one horizontal line. Their rendering was fixed on 2026-09-30 (a dynamic view draws its numbered participants, a deployment view nests each node around what it hosts, and a wide SKOS layer wraps onto centred rows in `SkosLayout.cs`), and `c4-dynamic.png`, `c4-deployment.png` and `skos.png` were captured on 2026-10-12.

One symptom remains: `business-economics.adp` still draws nothing within 20 seconds, so `skos.png` shows `geographic-names.adp`.

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
