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
