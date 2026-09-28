// Captures the readme screenshots from a running ADP instance, reproducibly.
// Usage: node capture.mjs <appUrl> <outDir> [projectName] [only]
// Viewport 1600x900 CSS px, DPR 1, headless Chrome, default (dark) theme.
// `projectName` is the name the examples folder was added under (default `Examples`); `only`
// is a comma-separated list of image names to retake, so one image can be redone alone.
import puppeteer from "puppeteer-core";
import path from "node:path";

const appUrl = process.argv[2] ?? "http://localhost:5480";
const outDir = process.argv[3] ?? ".";
const projectName = process.argv[4] ?? "Examples";
const only = process.argv[5] ? new Set(process.argv[5].split(",")) : null;
const chrome = "C:/Program Files/Google/Chrome/Application/chrome.exe";

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const browser = await puppeteer.launch({
  executablePath: chrome,
  headless: "new",
  defaultViewport: { width: 1600, height: 900, deviceScaleFactor: 1 },
});
const page = await browser.newPage();

/** Clicks the element whose visible text is exactly `text` (project cards, the sign-in button). */
async function clickText(text) {
  const finder = (t) => {
    const matches = [...document.querySelectorAll("body *")]
      .filter((e) => e.textContent.trim() === t && e.getBoundingClientRect().width > 0);
    return matches.at(-1) ?? null; // the deepest exact-text element
  };
  await page.waitForFunction((t) => {
    const matches = [...document.querySelectorAll("body *")]
      .filter((e) => e.textContent.trim() === t && e.getBoundingClientRect().width > 0);
    return matches.length > 0;
  }, { timeout: 15000 }, text);
  const handle = await page.evaluateHandle(finder, text);
  await handle.asElement().click();
  await sleep(400);
}

/** Clicks the aria-labelled button, e.g. "Fit to View"; tolerates it being absent. */
async function clickAria(label, { optional = false } = {}) {
  const selector = `::-p-aria(${label})`;
  try {
    await page.waitForSelector(selector, { timeout: optional ? 2500 : 15000 });
  } catch (error) {
    if (optional) return false;
    throw error;
  }
  await page.click(selector);
  await sleep(400);
  return true;
}

/**
 * Walks the explorer tree along `segments`, one level at a time, expanding each row that is
 * collapsed, and double-clicks the last. Returns false when a segment is missing.
 *
 * The walk is by path rather than by row text because row text is not unique: four corpora have
 * an `example 1`, and the folder-subject diagrams (Ansible, Helm) are all a row named `.adp`. A
 * text search that picks "the last match" opens whichever one the tree happens to show last.
 */
async function openPath(segments) {
  for (let depth = 0; depth < segments.length; depth++) {
    const isLeaf = depth === segments.length - 1;
    // Wait for the row to arrive: a folder's children stream in after its chevron is clicked.
    const found = await page.waitForFunction((path) => {
      let items = [...document.querySelectorAll('[role="treeitem"]')].filter((li) => !li.parentElement.closest('[role="treeitem"]'));
      let row = null;
      for (const name of path) {
        row = items.find((li) => li.querySelector(":scope > .explorer-tree-row .explorer-tree-node-name")?.textContent.trim() === name) ?? null;
        if (row === null) return false;
        items = [...row.querySelectorAll(':scope > [role="group"] > [role="treeitem"]')];
      }
      return true;
    }, { timeout: 15000 }, segments.slice(0, depth + 1)).then(() => true, () => false);
    if (!found) {
      console.log(`  missing tree row: ${segments.slice(0, depth + 1).join(" / ")}`);
      return false;
    }
    await page.evaluate((path, leaf) => {
      let items = [...document.querySelectorAll('[role="treeitem"]')].filter((li) => !li.parentElement.closest('[role="treeitem"]'));
      let row = null;
      for (const name of path) {
        row = items.find((li) => li.querySelector(":scope > .explorer-tree-row .explorer-tree-node-name")?.textContent.trim() === name);
        items = [...row.querySelectorAll(':scope > [role="group"] > [role="treeitem"]')];
      }
      row.scrollIntoView({ block: "center" });
      if (leaf) {
        // The dblclick is raised in the page itself: CDP-synthesized double-clicks proved
        // version-sensitive (a fresh puppeteer-core install opened nothing on any of them),
        // and the app only needs the DOM event.
        const node = row.querySelector(":scope > .explorer-tree-row .explorer-tree-node");
        node.click();
        node.dispatchEvent(new MouseEvent("dblclick", { bubbles: true, cancelable: true, view: window, detail: 2 }));
      } else if (row.getAttribute("aria-expanded") === "false") {
        row.querySelector(":scope > .explorer-tree-row .explorer-tree-chevron-button").click();
      }
    }, segments.slice(0, depth + 1), isLeaf);
    await sleep(isLeaf ? 400 : 300);
  }
  return true;
}

/** Collapses the explorer row at `segments`, so the next capture's tree is not the sum of all before it. */
async function collapsePath(segments) {
  await page.evaluate((path) => {
    let items = [...document.querySelectorAll('[role="treeitem"]')].filter((li) => !li.parentElement.closest('[role="treeitem"]'));
    let row = null;
    for (const name of path) {
      row = items.find((li) => li.querySelector(":scope > .explorer-tree-row .explorer-tree-node-name")?.textContent.trim() === name);
      if (!row) return;
      items = [...row.querySelectorAll(':scope > [role="group"] > [role="treeitem"]')];
    }
    if (row.getAttribute("aria-expanded") === "true") {
      row.querySelector(":scope > .explorer-tree-row .explorer-tree-chevron-button").click();
    }
  }, segments);
  await sleep(300);
}

async function shoot(name) {
  await sleep(1200);
  await page.screenshot({ path: path.join(outDir, name) });
  console.log("captured " + name);
}

/**
 * Opens a document by its explorer path, waits for the drawing to arrive, and optionally fits.
 *
 * The wait is for drawn content rather than for a duration. A diagram streams from the backend,
 * and a fixed sleep is a bet on how long that takes: the C4 container diagram lost that bet on a
 * cold backend and was captured as an empty canvas with only its title - a picture that looks
 * like a broken product and would have been committed as one, since nothing about it fails.
 */
async function openDocument(segments, { fit = true, expectDrawing = true, zoomIn = 0 } = {}) {
  if (!(await openPath(segments))) return false;
  if (expectDrawing) {
    const drawn = await page.waitForFunction(() => document.querySelectorAll("[data-element-id]").length > 0, { timeout: 20000 })
      .then(() => true, () => false);
    if (!drawn) {
      console.log(`  nothing drawn for ${segments.join(" / ")}`);
      return false;
    }
  }
  await sleep(1500);
  if (fit) {
    await clickAria("Fit to View", { optional: true });
  }
  // A dense diagram fitted whole is a texture, not a picture: `zoomIn` steps back in from the fit
  // so the labels read, at the price of showing only the middle of the drawing.
  for (let step = 0; step < zoomIn; step++) {
    await clickAria("Zoom In", { optional: true });
  }
  return true;
}

/** Closes the current document tab so the next shot starts clean. */
async function closeTab() {
  const closed = await page.evaluate(() => {
    const button = document.querySelector('[role="tab"] button, .diagram-tab-close, [aria-label^="Close"]');
    if (button) { button.click(); return true; }
    return false;
  });
  await sleep(500);
  return closed;
}

/**
 * Every image, in capture order: the file name, the explorer path to the document, and the
 * options for opening it. `collapse` is the row folded away afterwards. The readme's table has one
 * row per entry, saying what the image must show.
 */
const shots = [
  // The workspace overview: the C4 container diagram.
  { name: "workspace.png", path: ["diagrams", "c4", "industrial-plant", "architecture", "bottling-mes.dsl", "bottling-mes.mes-containers.adp"] },
  { name: "c4-context.png", path: ["diagrams", "c4", "industrial-plant", "architecture", "bottling-mes.dsl", "bottling-mes.adp"] },
  { name: "c4-component.png", path: ["diagrams", "c4", "industrial-plant", "architecture", "bottling-mes.dsl", "bottling-mes.order-service-components.adp"] },
  // The industrial plant's landscape holds one system of its own, so it draws the same picture as
  // its context view; the courier reference shows what a landscape adds.
  { name: "c4-system-landscape.png", path: ["diagrams", "c4", "reference", "architecture", "courier.dsl", "courier.landscape.adp"], collapse: ["diagrams", "c4"] },
  // No c4/dynamic or c4/deployment image: see the readme's "Not captured" section.

  { name: "mindmap.png", path: ["diagrams", "mindmap", "example 1", "mindmap.mm", "mindmap.adp"], collapse: ["diagrams", "mindmap"] },
  { name: "wardley-map.png", path: ["diagrams", "wardley-map", "example 1", "tea.owm", "tea.adp"], collapse: ["diagrams", "wardley-map"] },
  { name: "timeline.png", path: ["diagrams", "timeline", "example-2", "roadmap.tml", "roadmap.adp"], collapse: ["diagrams", "timeline"] },
  { name: "azure-pipeline.png", path: ["diagrams", "azure-pipeline", "example 1", "multi-stage.yml", "multi-stage.adp"], collapse: ["diagrams", "azure-pipeline"] },
  { name: "dependency-graph.png", path: ["diagrams", "dependency-graph", "example-1", "services.dgr", "services.adp"], collapse: ["diagrams", "dependency-graph"] },
  { name: "functional-decomposition-graph.png", path: ["diagrams", "functional-decomposition-graph", "field-service", "field-service.fdg", "field-service.adp"], collapse: ["diagrams", "functional-decomposition-graph"] },
  { name: "causal-loop.png", path: ["diagrams", "causal-loop", "on-call", "on-call.cld", "on-call.adp"], collapse: ["diagrams", "causal-loop"] },
  // Fitted, a hype cycle's century-long axis squeezes every banner into a sliver; the document's
  // own stored view is the readable one.
  { name: "hype-cycle.png", path: ["diagrams", "gartner-hypecycle-graph", "electric-vehicles", "electric-vehicles.ghg", "electric-vehicles.adp"], fit: false, collapse: ["diagrams", "gartner-hypecycle-graph"] },

  { name: "ansible-structure.png", path: ["diagrams", "ansible-structure", "example 1", "infrastructure", ".adp"], collapse: ["diagrams", "ansible-structure"] },
  { name: "helm-chart.png", path: ["diagrams", "helm-charts", "hello-world", ".adp"], collapse: ["diagrams", "helm-charts"] },
  { name: "dotnet-dependency-graph.png", path: ["diagrams", "dotnet-dependency-graph", "pipeline-toolkit", "PipelineToolkit.slnx", "PipelineToolkit.adp"], collapse: ["diagrams", "dotnet-dependency-graph"] },
  { name: "databricks-bundle.png", path: ["diagrams", "databricks", "lakehouse", "databricks.yml", "databricks.adp"] },
  { name: "databricks-pipeline.png", path: ["diagrams", "databricks", "lakehouse", "databricks.yml", "databricks.pipeline.adp"] },
  { name: "databricks-job.png", path: ["diagrams", "databricks", "lakehouse", "resources", "nightly-ingest.yml", "nightly-ingest.adp"], collapse: ["diagrams", "databricks"] },

  { name: "rdf.png", path: ["diagrams", "rdf", "w3c-turtle", "example-1.ttl", "example-1.adp"], collapse: ["diagrams", "rdf"] },
  { name: "owl.png", path: ["diagrams", "owl", "prov-o", "prov-o.ttl", "prov-o.adp"], zoomIn: 2, collapse: ["diagrams", "owl"] },
  // No w3c/skos image: see the readme's "Not captured" section.
  { name: "shacl.png", path: ["diagrams", "shacl", "fair-data-point", "navigation-shapes.ttl", "navigation-shapes.adp"], collapse: ["diagrams", "shacl"] },
  { name: "sparql.png", path: ["diagrams", "sparql", "w3c-sparql", "optional.rq", "optional.adp"], collapse: ["diagrams", "sparql"] },

  // The text editors: no canvas, so nothing to wait for or fit.
  { name: "markdown-editor.png", path: ["editors", "markdown", "guide.md"], fit: false, expectDrawing: false },
  { name: "plain-text-editor.png", path: ["editors", "plain", "crlf-notes.txt"], fit: false, expectDrawing: false, collapse: ["editors"] },
];

// ---- sign in, open the examples project ------------------------------------------------------
await page.goto(appUrl, { waitUntil: "networkidle2" });
// A developer build opens already authenticated and never renders the form
// (developer-sign-in-bypass), so the credential is typed only where there is something to type
// it into. Waiting unconditionally for an input hung here the moment that bypass landed.
await sleep(1500);
const passwordField = await page.$('input[type="password"]');
if (passwordField !== null) {
  const inputs = await page.$$("input");
  await inputs[0].type("admin");
  await inputs[1].type("changeme");
  await clickText("Sign in");
  await sleep(1500);
}
await clickText(projectName);
await sleep(2000);

const failed = [];
for (const shot of shots) {
  if (only !== null && !only.has(shot.name)) continue;
  if (await openDocument(shot.path, shot)) {
    await shoot(shot.name);
  } else {
    failed.push(shot.name);
  }
  await closeTab();
  if (shot.collapse) await collapsePath(shot.collapse);
}

await browser.close();
if (failed.length > 0) {
  console.log("NOT captured: " + failed.join(", "));
  process.exitCode = 1;
}
console.log("done");
