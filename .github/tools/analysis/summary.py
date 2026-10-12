"""Writes measurements.md from measurements.json: the figures the milestone reviews of spec 025 compare."""

import json
import sys


def table(rows, header):
    out = ["| " + " | ".join(header) + " |", "|" + "---|" * len(header)]
    out += ["| " + " | ".join(str(cell) for cell in row) + " |" for row in rows]
    return out


def main(source, target):
    with open(source, encoding="utf-8") as handle:
        m = json.load(handle)
    out = ["# Measurements", "", "Commit `" + m["commit"] + "`. What each figure means: [readme.md](readme.md) beside the scripts.", ""]
    out += ["## Lines and files", ""]
    out += table([(key, t["projects"], t["files"], t["lines"], t["code_lines"]) for key, t in m["lines"]["totals"].items()],
                 ["Group", "Projects", "Files", "Lines", "Code lines"])
    out += ["", "## Production lines per tool module", ""]
    out += table(list(m["lines"]["production_lines_per_module"].items()), ["Module", "Lines"])
    for scope in ("clones_production", "clones_test"):
        c = m[scope]
        out += ["", "## Clone scan, " + c["scope"] + " code", "",
                f"{c['shared_windows']} shared {c['window_lines']}-line windows in {c['files_with_shared_windows']} files.", ""]
        out += table([(r["roles"], r["windows"], r["file_pairs"]) for r in c["role_pairs"][:20]], ["Roles", "Windows", "File pairs"])
    me = m["methods"]
    out += ["", "## Method lengths", "", f"{me['methods']} methods; {me['over_50']} over 50 code lines, {me['over_80']} over 80.", ""]
    out += table([(x["method"], f"`{x['file']}:{x['line']}`", x["code_lines"]) for x in me["longest"]], ["Method", "Where", "Code lines"])
    g = m["graph"]
    out += ["", "## Project graph", "", f"{g['projects']} projects. Edges: " +
            ", ".join(f"{k} {v}" for k, v in g["edges_per_kind"].items()) + ".", "",
            "Without a .cs file: " + (", ".join(g["without_cs_file"]) or "none") + ".", "",
            "Core projects naming a tool module: " + str(len(g["core_naming_a_tool_module"])) + "."]
    w = m["waits"]
    out += ["", "## Waits", "", f"Task.Delay and Thread.Sleep: {w['waits']['test']} in test code, "
            f"{w['waits']['production']} in production code. Polling helpers: {w['polling_helpers']}.", ""]
    with open(target, "w", encoding="utf-8", newline="\r\n") as handle:
        handle.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
