"""What the five measurement scripts share: the projects of the solution and the C# files in them.

A project is classified the way docs/solution-structure.md says it must be: core by the absence of a
leading '../' in its solution path, otherwise by the tool kind folder it sits under (diagrams, designers,
editors). A project is a test project when its name ends in '.Tests'. Test code is the test projects and
the testing projects they reference, named '*.Tests.Support' (one per area, such as EtAlii.Adp.Tests.Support
and EtAlii.Adp.Backend.Tests.Support): a helper moved out of a test project into them is still test code.
"""

import argparse
import json
import os
import re
import sys

SOLUTION = os.path.join("src", "backend", "EtAlii.Adp.slnx")
SKIPPED_FOLDERS = {"bin", "obj", "node_modules", ".git"}
KINDS = {"diagrams": "diagram", "designers": "designer", "editors": "editor"}
# Not named *.Tests, so not test projects, and nothing in them is production code.
TESTING_PROJECT_SUFFIX = ".Tests.Support"


class Project:
    def __init__(self, root, solution_path):
        self.solution_path = solution_path.replace("\\", "/")
        self.path = os.path.normpath(os.path.join(root, "src", "backend", self.solution_path))
        self.folder = os.path.dirname(self.path)
        self.name = os.path.splitext(os.path.basename(self.path))[0]
        self.test = self.name.endswith(".Tests")
        # Test code: a test project or one of the testing projects. lines.py, methods.py and waits.py split by
        # this; clones.py keeps scanning the test projects alone, by self.test.
        self.test_code = self.test or self.name.endswith(TESTING_PROJECT_SUFFIX)
        if not self.solution_path.startswith("../"):
            self.kind = "core"
        else:
            parts = self.solution_path.split("/")
            self.kind = KINDS.get(parts[1], "other")
        # The module a tool project belongs to: the folder under diagrams/, designers/ or editors/.
        self.module = self.solution_path.split("/")[2] if self.kind not in ("core", "other") else None
        self.group = ("test" if self.test_code else "production")


def projects(root):
    with open(os.path.join(root, SOLUTION), encoding="utf-8-sig") as handle:
        text = handle.read()
    return [Project(root, path) for path in re.findall(r'<Project\s+Path="([^"]+)"', text)]


def cs_files(project):
    """The C# files of a project, in a stable order, without build output."""
    found = []
    for folder, subfolders, files in os.walk(project.folder):
        subfolders[:] = sorted(f for f in subfolders if f not in SKIPPED_FOLDERS)
        found.extend(os.path.join(folder, f) for f in sorted(files) if f.endswith(".cs"))
    return found


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as handle:
        return handle.read()


def relative(root, path):
    return os.path.relpath(path, root).replace("\\", "/")


def blank_comments_and_strings(text):
    """The text with comments and the contents of string and character literals replaced by spaces.

    Line breaks are kept, so line numbers and line counts stay valid. Quotes are kept, so a line that
    holds a literal is still a line of code. Interpolation holes are blanked with the string around them,
    which is enough for counting braces.
    """
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
        elif c == "/" and i + 1 < n and text[i + 1] == "*":
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            out.append(re.sub(r"[^\n]", " ", text[i:end]))
            i = end
        elif c == '"' or (c in "$@" and re.match(r'[$@]+"', text[i:i + 4])):
            prefix = re.match(r'[$@]*', text[i:]).group(0)
            j = i + len(prefix)
            quotes = re.match(r'"+', text[j:]).group(0)
            if len(quotes) >= 3:
                # A raw string literal ends at the same run of quotes.
                end = text.find(quotes, j + len(quotes))
                end = n if end < 0 else end + len(quotes)
            elif "@" in prefix:
                end = j + 1
                while end < n:
                    if text[end] == '"':
                        if end + 1 < n and text[end + 1] == '"':
                            end += 2
                            continue
                        end += 1
                        break
                    end += 1
            else:
                end = j + 1
                depth = 0
                interpolated = "$" in prefix
                while end < n:
                    d = text[end]
                    if d == "\\":
                        end += 2
                        continue
                    if interpolated and d == "{":
                        if end + 1 < n and text[end + 1] == "{":
                            end += 2
                            continue
                        depth += 1
                    elif interpolated and d == "}" and depth > 0:
                        depth -= 1
                    elif d == '"' and depth == 0:
                        end += 1
                        break
                    elif d == "\n" and depth == 0:
                        break
                    end += 1
            body = text[i:end]
            out.append('"' + re.sub(r"[^\n]", " ", body[1:-1]) + '"' if len(body) >= 2 else body)
            i = end
        elif c == "'":
            end = i + 1
            while end < n and text[end] != "'" and text[end] != "\n":
                end += 2 if text[end] == "\\" else 1
            end = min(end + 1, n)
            out.append("'" + " " * max(0, end - i - 2) + "'" if end - i >= 2 else text[i:end])
            i = end
        else:
            out.append(c)
            i += 1
    return "".join(out)


def is_code_line(line):
    return line.strip() != ""


def arguments(description):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("--root", default=".", help="the repository root (default: the current folder)")
    return parser


def emit(result):
    json.dump(result, sys.stdout, indent=2, sort_keys=False)
    sys.stdout.write("\n")
