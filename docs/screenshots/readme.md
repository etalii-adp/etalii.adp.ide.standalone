# Screenshots

The images the root readme shows, and how each was taken — precisely enough that a second person (or an agent) retakes a comparable image after a UI change. **When the UI changes so that one of these no longer shows what a user sees, retake it**; a stale screenshot is a false claim with a picture attached.

## The shared setup, for every image

- **Source material**: only documents from [`src/examples/`](../../src/examples/) appear in any image, so every capture is reproducible from repository content alone. The project is added in ADP under the name `Examples`.
- **App**: run from source per the root readme's getting-started steps (this is deliberate — capturing the screenshots doubles as verifying those steps). From a worktree, use a free port pair per CLAUDE.md's port rule; the committed set was captured on backend `5480` / client `5474`.
- **Browser**: headless Chrome, viewport **1600×900 CSS px, device pixel ratio 1, 100% zoom**, full-viewport crop, no browser chrome.
- **Theme**: the app's default (dark). Nothing toggled.
- **Login**: the `developer` environment credentials (`admin` / `changeme`).
- **Format and budget**: PNG; each image ≤ 300 KB, the workspace overview ≤ 1 MB.

The whole procedure is executable: [`capture.mjs`](capture.mjs) drives all of the above with puppeteer-core (`npm i puppeteer-core`, then `node capture.mjs http://localhost:5480 .` — it expects Chrome at its standard Windows path; adjust the constant for another machine). Retaking one image means re-running the script and committing the changed file; the entries below say what each image must show, which is what to check before committing a retake.

## The images

| Image | Document opened | What must be visible |
|---|---|---|
| `workspace.png` | `diagrams/c4/industrial-plant/architecture/` → `bottling-mes.dsl` → `bottling-mes.mes-containers.adp` | The whole workspace in one shot: explorer expanded to the document, the container diagram rendered in its tab, the Toolbox panel with the C4 element kinds, the problems strip at the bottom. Nothing selected. |
| `mindmap.png` | `diagrams/mindmap/example 1/` → `mindmap.mm` → `mindmap.adp` | The mindmap laid out left-to-right in its tab; Toolbox showing the Node entry. Nothing selected. |
| `wardley-map.png` | `diagrams/wardley-map/example 1/` → `tea.owm` → `tea.adp` | The bounded map: both axes labelled, the four evolution bands named along the bottom, components and links drawn. Nothing selected. |
| `timeline.png` | `diagrams/timeline/example-2/` → `roadmap.tml` → `roadmap.adp` | Periods and moments on rows, the year ruler along the bottom, connections drawn; Toolbox showing Element and Moment. Nothing selected. |
| `azure-pipeline.png` | `diagrams/azure-pipeline/example 1/` → `multi-stage.yml` → `multi-stage.adp` | The stage graph left-to-right with its dependency edges. Nothing selected. |
| `dependency-graph.png` | `diagrams/dependency-graph/example-1/` → `services.dgr` → `services.adp` | The service graph with labelled edges. Nothing selected. |
| `markdown-editor.png` | `editors/markdown/` → `guide.md` | The three-part editor: heading outline above, CodeMirror text left, rendered preview right — and the status line reading **Saved**, since nothing was edited. |

Each diagram capture clicks **Fit to View** after opening (where the toolbar offers it), so the content's framing does not depend on the previous session's pan and zoom. Two captures that follow one row of this table should differ only in rendering noise.

## Known blemish, deliberately shown

At capture time the Toolbox panel stays on "Open a diagram to see the elements its type offers" for the wardley-map and azure-pipeline tabs, while every other type's toolbox fills — deterministic across runs, and recorded as a manual check in [`tests.md`](../../tests.md). The screenshots show the app as it is; when that check is fixed, retake those two images.
