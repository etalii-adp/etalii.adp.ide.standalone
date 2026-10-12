"""The project graph (spec 025, T005): every ProjectReference, PackageReference, FrameworkReference and
InternalsVisibleTo of the solution's projects as an edge list, the projects without a .cs file, and the
core projects that name a tool module, by reference or by InternalsVisibleTo."""

import os
import re

import common

ITEMS = ("ProjectReference", "PackageReference", "FrameworkReference", "InternalsVisibleTo")


def edges_of(project, by_path):
    text = common.read(project.path)
    edges = []
    for item in ITEMS:
        for target in re.findall(r"<" + item + r"\s+Include=\"([^\"]+)\"", text):
            if item == "ProjectReference":
                path = os.path.normpath(os.path.join(project.folder, target.replace("\\", "/")))
                target = by_path[path].name if path in by_path else os.path.splitext(os.path.basename(path))[0]
            edges.append({"from": project.name, "kind": item, "to": target})
    for path in common.cs_files(project):
        for target in re.findall(r"\[assembly:\s*InternalsVisibleTo\(\s*\"([^\"]+)\"", common.read(path)):
            edges.append({"from": project.name, "kind": "InternalsVisibleTo", "to": target})
    return edges


def measure(root):
    projects = common.projects(root)
    by_path = {p.path: p for p in projects}
    by_name = {p.name: p for p in projects}
    edges = [edge for p in projects for edge in edges_of(p, by_path)]
    naming_modules = sorted({
        (edge["from"], edge["kind"], edge["to"]) for edge in edges
        if by_name[edge["from"]].kind == "core" and edge["to"] in by_name and by_name[edge["to"]].kind != "core"
    })
    return {
        "projects": len(projects),
        "edges_per_kind": {kind: sum(1 for e in edges if e["kind"] == kind) for kind in ITEMS},
        "referenced_by": dict(sorted(
            ((name, sum(1 for e in edges if e["kind"] == "ProjectReference" and e["to"] == name)) for name in by_name),
            key=lambda item: (-item[1], item[0]))[:15]),
        "without_cs_file": sorted(p.name for p in projects if not common.cs_files(p)),
        "core_naming_a_tool_module": [{"from": f, "kind": k, "to": t} for f, k, t in naming_modules],
        "edges": edges,
    }


if __name__ == "__main__":
    common.emit(measure(common.arguments(__doc__).parse_args().root))
