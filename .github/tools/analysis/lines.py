"""Lines and files per project of the solution, split into production and test and into core and the
three tool kinds (spec 025, T002). The testing projects, named *.Tests.Support,
are test code, not production code (common.py). Lines are physical lines of the .cs files; code lines leave out blank
lines and lines holding only a comment."""

import common


def measure(root):
    rows = []
    for project in common.projects(root):
        files = common.cs_files(project)
        lines = code = 0
        for path in files:
            text = common.read(path)
            lines += text.count("\n") + (0 if text.endswith("\n") or text == "" else 1)
            code += sum(1 for line in common.blank_comments_and_strings(text).split("\n") if common.is_code_line(line))
        rows.append({"project": project.name, "kind": project.kind, "group": project.group,
                     "module": project.module, "files": len(files), "lines": lines, "code_lines": code})
    totals = {}
    for row in rows:
        for key in ("all", row["group"], row["kind"], row["kind"] + "/" + row["group"]):
            total = totals.setdefault(key, {"projects": 0, "files": 0, "lines": 0, "code_lines": 0})
            total["projects"] += 1
            for field in ("files", "lines", "code_lines"):
                total[field] += row[field]
    modules = {}
    for row in rows:
        if row["module"] and row["group"] == "production":
            modules[row["kind"] + "/" + row["module"]] = modules.get(row["kind"] + "/" + row["module"], 0) + row["lines"]
    return {"totals": dict(sorted(totals.items())), "production_lines_per_module":
            dict(sorted(modules.items(), key=lambda item: -item[1])), "projects": rows}


if __name__ == "__main__":
    common.emit(measure(common.arguments(__doc__).parse_args().root))
