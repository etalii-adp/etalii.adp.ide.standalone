// Captures the readme screenshots from a running ADP instance, reproducibly.
// Usage: node capture.mjs <appUrl> <outDir>
// Viewport 1600x900 CSS px, DPR 1, headless Chrome, default (dark) theme.
import puppeteer from "puppeteer-core";
import path from "node:path";

const appUrl = process.argv[2] ?? "http://localhost:5480";
const outDir = process.argv[3] ?? ".";
const chrome = "C:/Program Files/Google/Chrome/Application/chrome.exe";

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const browser = await puppeteer.launch({
  executablePath: chrome,
  headless: "new",
  defaultViewport: { width: 1600, height: 900, deviceScaleFactor: 1 },
});
const page = await browser.newPage();

/** Clicks the element whose visible text is exactly `text` (tree rows, project cards). */
async function clickText(text, { double = false } = {}) {
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
  const element = handle.asElement();
  await element.click();
  if (double) {
    // The dblclick is raised in the page itself: CDP-synthesized double-clicks proved
    // version-sensitive (a fresh puppeteer-core install opened nothing on any of them),
    // and the app only needs the DOM event.
    await element.evaluate((node) => {
      node.dispatchEvent(new MouseEvent("dblclick", { bubbles: true, cancelable: true, view: window, detail: 2 }));
    });
  }
  await sleep(400);
}

/** Clicks the aria-labelled button, e.g. "Expand c4"; tolerates it being absent (already expanded). */
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

async function shoot(name) {
  await sleep(1200);
  await page.screenshot({ path: path.join(outDir, name) });
  console.log("captured " + name);
}

/** Opens a diagram: expands the folder chain, double-clicks the leaf, optionally fits. */
async function openDiagram(chain, leaf, { fit = true } = {}) {
  for (const folder of chain) {
    await clickAria(`Expand ${folder}`, { optional: true });
  }
  await clickText(leaf, { double: true });
  await sleep(1500);
  if (fit) {
    await clickAria("Fit to View", { optional: true });
  }
}

/** Closes the current diagram tab so the next shot starts clean. */
async function closeTab() {
  const closed = await page.evaluate(() => {
    const button = document.querySelector('[role="tab"] button, .diagram-tab-close, [aria-label^="Close"]');
    if (button) { button.click(); return true; }
    return false;
  });
  await sleep(500);
  return closed;
}

// ---- sign in, open the Examples project ----------------------------------------------------
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
await clickText("Examples");
await sleep(2000);

// ---- workspace overview: the C4 container diagram -------------------------------------------
await openDiagram(["diagrams", "c4", "industrial-plant", "architecture", "bottling-mes.dsl"], "bottling-mes.mes-containers.adp");
await shoot("workspace.png");
await closeTab();

// ---- the individual diagram types ------------------------------------------------------------
await openDiagram(["mindmap", "example 1", "mindmap.mm"], "mindmap.adp");
await shoot("mindmap.png");
await closeTab();

await openDiagram(["wardley-map", "example 1", "tea.owm"], "tea.adp");
await shoot("wardley-map.png");
await closeTab();

await openDiagram(["timeline", "example-2", "roadmap.tml"], "roadmap.adp");
await shoot("timeline.png");
await closeTab();

await openDiagram(["azure-pipeline", "example 1", "multi-stage.yml"], "multi-stage.adp");
await shoot("azure-pipeline.png");
await closeTab();

await openDiagram(["dependency-graph", "example-1", "services.dgr"], "services.adp");
await shoot("dependency-graph.png");
await closeTab();

// ---- the markdown editor ---------------------------------------------------------------------
await openDiagram(["editors", "markdown"], "guide.md", { fit: false });
await shoot("markdown-editor.png");

await browser.close();
console.log("done");
