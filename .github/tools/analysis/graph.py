"""The project graph (spec 025, T005): every ProjectReference, PackageReference, FrameworkReference and
InternalsVisibleTo of the solution's projects as an edge list, the projects without a .cs file, and the
core projects that name a tool module, by reference or by InternalsVisibleTo.

The items of src/Directory.Build.targets are edges too: MSBuild imports it into every project under src/, and
it attaches EtAlii.Adp.TestSupport to every project whose name ends in .Tests. Such an edge carries
"via": "src/Directory.Build.targets". Only the file's top-level item groups count; items inside a target are
evaluated when the target runs, not when the project is read. A condition other than none or
'$(MSBuildProjectName.EndsWith(`...`))' == 'true' stops the scan rather than being guessed at."""

import os
import re
import xml.etree.ElementTree as ElementTree

import common

ITEMS = ("ProjectReference", "PackageReference", "FrameworkReference", "InternalsVisibleTo")
TARGETS = os.path.join("src", "Directory.Build.targets")
ENDS_WITH = re.compile(r"^'\$\(MSBuildProjectName\.EndsWith\(`([^`]+)`\)\)'\s*==\s*'true'$")


def reference_name(folder, target, by_path):
    path = os.path.normpath(os.path.join(folder, target.replace("\\", "/")))
    return by_path[path].name if path in by_path else os.path.splitext(os.path.basename(path))[0]


def targets_items(root):
    """(applies to a project name, item, include, folder it is relative to) for every top-level item of src/Directory.Build.targets."""
    path = os.path.join(root, TARGETS)
    if not os.path.exists(path):
        return []
    folder = os.path.dirname(path)
    found = []
    for group in ElementTree.fromstring(common.read(path)).findall("ItemGroup"):
        condition = group.get("Condition", "").strip()
        match = ENDS_WITH.match(condition)
        if condition and not match:
            raise SystemExit(f"graph.py: {TARGETS} has an item group whose condition it cannot read: {condition}")
        suffix = match.group(1) if match else ""
        for element in group:
            if element.tag in ITEMS and element.get("Include"):
                include = element.get("Include").replace("$(MSBuildThisFileDirectory)", "")
                found.append((lambda name, s=suffix: name.endswith(s), element.tag, include, folder))
    return found


def edges_of(project, by_path, inherited):
    text = common.read(project.path)
    edges = []
    for item in ITEMS:
        for target in re.findall(r"<" + item + r"\s+Include=\"([^\"]+)\"", text):
            if item == "ProjectReference":
                target = reference_name(project.folder, target, by_path)
            edges.append({"from": project.name, "kind": item, "to": target})
    for applies, item, target, folder in inherited:
        if applies(project.name):
            if item == "ProjectReference":
                target = reference_name(folder, target, by_path)
            edges.append({"from": project.name, "kind": item, "to": target, "via": TARGETS.replace("\\", "/")})
    for path in common.cs_files(project):
        for target in re.findall(r"\[assembly:\s*InternalsVisibleTo\(\s*\"([^\"]+)\"", common.read(path)):
            edges.append({"from": project.name, "kind": "InternalsVisibleTo", "to": target})
    return edges


def measure(root):
    projects = common.projects(root)
    by_path = {p.path: p for p in projects}
    by_name = {p.name: p for p in projects}
    inherited = targets_items(root)
    edges = [edge for p in projects for edge in edges_of(p, by_path, inherited)]
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
