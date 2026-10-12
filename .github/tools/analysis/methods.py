"""Method lengths and type sizes (spec 025, T004).

A method is a declaration with a parameter list and a block body: methods, constructors, operators,
local functions and accessors with parameters. Its length is its code lines, from the line of its name
to the line of its closing brace, comments and blank lines left out. Braces are matched on the text
with comments and literals blanked, so a brace in a string does not count. Expression-bodied members
are left out: they are one expression, however long.

Only production code is measured: the test projects and the testing projects are left out.

A type is a class, record, struct or interface; its size is its code lines, nested types included.
"""

import collections
import re

import common

NOT_A_NAME = {
    "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "new", "nameof",
    "typeof", "sizeof", "default", "when", "checked", "unchecked", "base", "this", "await", "throw", "else",
    "do", "in", "is", "as", "and", "or", "not", "with", "stackalloc", "var", "get", "set", "init", "add",
    "remove", "where", "select", "from", "let", "on", "equals", "by", "into", "orderby", "join", "group",
}
NAME = re.compile(r"(?<![\w.])(@?[A-Za-z_]\w*)\s*(<[^(){};=]*>)?\s*\(")
TYPE = re.compile(r"\b(class|record|struct|interface)\s+(?:class\s+|struct\s+)?([A-Za-z_]\w*)")


def matching(text, start, opening, closing):
    """The index just past the bracket that closes the one at start, or -1."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == opening:
            depth += 1
        elif text[i] == closing:
            depth -= 1
            if depth == 0:
                return i + 1
    return -1


def code_lines(lines, first, last):
    return sum(1 for line in lines[first:last + 1] if common.is_code_line(line))


def previous_word(text, index):
    match = re.search(r"([\w?\]>]+)\s*$", text[max(0, index - 200):index])
    return match.group(1) if match else ""


def methods_in(text):
    """(name, first line, code lines) for every method with a block body."""
    blanked = common.blank_comments_and_strings(text)
    lines = blanked.split("\n")
    line_of = [0] * (len(blanked) + 1)
    number = 0
    for i, c in enumerate(blanked):
        line_of[i] = number
        if c == "\n":
            number += 1
    line_of[len(blanked)] = number
    found = []
    for match in NAME.finditer(blanked):
        name = match.group(1).lstrip("@")
        if name in NOT_A_NAME:
            continue
        before = previous_word(blanked, match.start())
        # A declaration has a return type or a modifier in front of its name; a call does not.
        # A type with a primary constructor is not a method.
        if not before or before in NOT_A_NAME or before in ("class", "record", "struct", "interface"):
            continue
        if before[-1] not in ">]?" and not re.match(r"[\w]+$", before):
            continue
        close = matching(blanked, match.end() - 1, "(", ")")
        if close < 0:
            continue
        rest = blanked[close:close + 400]
        header = re.match(r"\s*(?::\s*(?:base|this)\s*\()?", rest)
        position = close + header.end()
        if header.group(0).strip().startswith(":"):
            inner = matching(blanked, position - 1, "(", ")")
            if inner < 0:
                continue
            position = inner
        constraints = re.match(r"(\s*where\s+[^{;=]*)*\s*", blanked[position:position + 400])
        position += constraints.end()
        if position >= len(blanked) or blanked[position] != "{":
            continue
        end = matching(blanked, position, "{", "}")
        if end < 0:
            continue
        first, last = line_of[match.start()], line_of[end - 1]
        found.append((name, first + 1, code_lines(lines, first, last)))
    return found


def types_in(text):
    blanked = common.blank_comments_and_strings(text)
    lines = blanked.split("\n")
    found = []
    for match in TYPE.finditer(blanked):
        brace = blanked.find("{", match.end())
        semicolon = blanked.find(";", match.end())
        if brace < 0 or (0 <= semicolon < brace):
            continue
        end = matching(blanked, brace, "{", "}")
        if end < 0:
            continue
        first = blanked.count("\n", 0, match.start())
        last = blanked.count("\n", 0, end - 1)
        found.append((match.group(2), code_lines(lines, first, last)))
    return found


def measure(root, longest=20):
    methods = []
    types = collections.defaultdict(list)
    for project in common.projects(root):
        if project.test_code:
            continue
        for path in common.cs_files(project):
            text = common.read(path)
            name = common.relative(root, path)
            for method, line, length in methods_in(text):
                methods.append({"method": method, "file": name, "line": line, "code_lines": length})
            for type_name, length in types_in(text):
                types[project.name].append({"type": type_name, "file": name, "code_lines": length})
    methods.sort(key=lambda m: (-m["code_lines"], m["file"], m["line"]))
    return {
        "scope": "production",
        "methods": len(methods),
        "over_50": sum(1 for m in methods if m["code_lines"] > 50),
        "over_80": sum(1 for m in methods if m["code_lines"] > 80),
        "longest": methods[:longest],
        "largest_types_per_project": {
            project: sorted(found, key=lambda t: -t["code_lines"])[:3] for project, found in sorted(types.items())
        },
    }


if __name__ == "__main__":
    common.emit(measure(common.arguments(__doc__).parse_args().root))
