// Judges one JetBrains InspectCode run: whether it could see the code, and only then what it found.
//
//   node evaluate.mjs <report> <log> <tracked-count> [--report]
//
//   report          the SARIF file `jb inspectcode` wrote (SARIF JSON whatever its extension)
//   log             the inspector's console log, which names every file it inspected
//   tracked-count   how many C# files git tracks under src/, outside Fixtures folders
//   --report        findings are listed but exit 0; a blind run still exits 2
//
// Exit 2 - BLIND: the report is missing or unparseable, it carries C# compiler errors (the
//          inspector could not resolve the code, so its silence about the rest means nothing), or
//          the log names fewer inspected C# files than git tracks.
// Exit 1 - FINDINGS: any result at Error, Warning or Suggestion severity, listed as
//          `path:line  Inspection  message`, grouped by inspection with a count.
// Exit 0 - CLEAN.
//
// Health is judged BEFORE findings, and the two failures have different codes on purpose: "could
// not see" and "saw a finding" call for different actions, and one shared red would teach readers
// to treat a broken instrument as a dirty tree. This tool once reported zero warnings here while
// carrying 327 unresolved-symbol errors.
//
// Node's standard library only, so it runs wherever the client gates already run.
import { createReadStream, existsSync, readFileSync, statSync } from "node:fs";
import { createInterface } from "node:readline";
import { relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const CLEAN = 0;
const FINDINGS = 1;
const BLIND = 2;

// The rule ids under which the inspector reports what the C# compiler would refuse, unresolved
// symbols included. Anything else at error level is a finding like any other.
const COMPILER_RULES = new Set([".CSharpErrors"]);

// SARIF levels for the three severities this check refuses. A HINT is written as "none" or not
// at all at --severity=SUGGESTION, and is not a finding.
const FINDING_LEVELS = new Map([
  ["error", "Error"],
  ["warning", "Warning"],
  ["note", "Suggestion"],
]);

const INSPECTED_LINE = /^Inspecting .+\.cs\s*$/;

function blind(reason) {
  console.log(`BLIND: ${reason}`);
  console.log("The inspection could not see the code, so it says nothing about the tree. Build the solution and run again.");
  return BLIND;
}

function readReport(path) {
  if (!existsSync(path) || statSync(path).size === 0) {
    return { error: `the report ${path} is missing or empty` };
  }
  try {
    const text = readFileSync(path, "utf8").replace(/^﻿/, "");
    const sarif = JSON.parse(text);
    const run = sarif?.runs?.[0];
    if (!run || !Array.isArray(run.results)) {
      return { error: `the report ${path} is not SARIF (no runs[0].results)` };
    }
    return { run };
  } catch (exception) {
    return { error: `the report ${path} does not parse: ${exception.message}` };
  }
}

async function countInspected(path) {
  if (!existsSync(path)) {
    return -1;
  }
  let count = 0;
  const lines = createInterface({ input: createReadStream(path, "utf8"), crlfDelay: Infinity });
  for await (const line of lines) {
    if (INSPECTED_LINE.test(line)) {
      count++;
    }
  }
  return count;
}

// A result's file, as a path relative to the working directory where the inspector's base
// directory is known, and as the report wrote it otherwise.
function locate(run, result) {
  const physical = result.locations?.[0]?.physicalLocation;
  const uri = physical?.artifactLocation?.uri ?? "(no file)";
  const line = physical?.region?.startLine ?? 0;
  const base = run.originalUriBaseIds?.[physical?.artifactLocation?.uriBaseId]?.uri;
  let path = uri;
  if (base?.startsWith("file:")) {
    try {
      path = relative(process.cwd(), resolve(fileURLToPath(base), decodeURIComponent(uri))).replaceAll("\\", "/");
    } catch {
      path = uri;
    }
  }
  return `${path}:${line}`;
}

async function evaluate(reportPath, logPath, tracked, reportMode) {
  // 1. The report exists and parses.
  const { run, error } = readReport(reportPath);
  if (error) {
    return blind(error);
  }

  // 2. No compiler error among the results.
  const compilerErrors = run.results.filter((result) => COMPILER_RULES.has(result.ruleId));
  if (compilerErrors.length > 0) {
    for (const result of compilerErrors) {
      console.log(`  ${locate(run, result)}  ${result.ruleId}  ${result.message?.text ?? ""}`);
    }
    return blind(`${compilerErrors.length} C# compiler error(s) among the results`);
  }

  // 3. At least as many C# files inspected as git tracks.
  const inspected = await countInspected(logPath);
  if (inspected < 0) {
    return blind(`the console log ${logPath} is missing, so what was inspected cannot be told`);
  }
  if (!(inspected >= tracked)) {
    return blind(`${inspected} C# file(s) inspected, below the ${tracked} git tracks under src/`);
  }
  console.log(`Seen: ${inspected} C# file(s) inspected (git tracks ${tracked}), no compiler errors.`);

  // 4. Findings, grouped by inspection.
  const findings = run.results.filter((result) => FINDING_LEVELS.has(result.level ?? "warning"));
  if (findings.length === 0) {
    console.log("CLEAN: no finding at Suggestion severity or above.");
    return CLEAN;
  }

  const byInspection = new Map();
  for (const result of findings) {
    const group = byInspection.get(result.ruleId) ?? [];
    group.push(result);
    byInspection.set(result.ruleId, group);
  }
  const groups = [...byInspection.entries()].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0]));
  for (const [inspection, results] of groups) {
    const severity = FINDING_LEVELS.get(results[0].level ?? "warning");
    console.log(`${inspection} (${severity}, ${results.length})`);
    for (const result of results) {
      console.log(`  ${locate(run, result)}  ${inspection}  ${result.message?.text ?? ""}`);
    }
  }

  const bySeverity = new Map();
  for (const result of findings) {
    const severity = FINDING_LEVELS.get(result.level ?? "warning");
    bySeverity.set(severity, (bySeverity.get(severity) ?? 0) + 1);
  }
  console.log("");
  console.log("Counts per inspection:");
  for (const [inspection, results] of groups) {
    console.log(`  ${String(results.length).padStart(5)}  ${inspection}`);
  }
  const totals = [...FINDING_LEVELS.values()].map((severity) => `${bySeverity.get(severity) ?? 0} ${severity}`).join(", ");
  console.log(`FINDINGS: ${findings.length} (${totals}) in ${groups.length} inspection(s).`);
  if (reportMode) {
    console.log("Report mode: findings are listed, not refused.");
    return CLEAN;
  }
  return FINDINGS;
}

const [reportPath, logPath, trackedText, ...rest] = process.argv.slice(2);
const tracked = Number.parseInt(trackedText ?? "", 10);
if (!reportPath || !logPath || !Number.isInteger(tracked) || rest.some((option) => option !== "--report")) {
  console.log("usage: node evaluate.mjs <report> <log> <tracked-count> [--report]");
  process.exit(BLIND);
}
process.exitCode = await evaluate(reportPath, logPath, tracked, rest.includes("--report"));
