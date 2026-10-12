"""The normalized clone scan (spec 025, T003).

Every C# file is reduced to its code lines (comments and blank lines dropped, whitespace collapsed),
with the module's own name replaced by a placeholder, string literals by "S" and numbers by "0". Every
window of ten consecutive such lines is hashed. A window is shared when it occurs in two or more files.

The report gives, per role pair, how many distinct windows files of those roles share and how many
file pairs share them: a role is the file's name with the module's prefix removed (SankeyDocumentStore.cs and TimelineDocumentStore.cs are
both DocumentStore). It also lists the twenty largest clone families: sets of files that share windows,
by the number of windows they share. --tests scans the test projects instead of production code.
"""

import collections
import hashlib
import os
import re

import common

WINDOW = 10


def pascal(words):
    return "".join(word[:1].upper() + word[1:] for word in words if word)


def module_names(project):
    """The spellings by which a module names itself: its project's last segment, its folder in Pascal
    case and the initials of that (sankey: Sankey; gartner-hype-cycle-graph: GartnerHypeCycleGraph and
    Ghg)."""
    names = set()
    last = project.name[:-len(".Tests")] if project.test else project.name
    last = last.split(".")[-1]
    if last not in ("Adp",):
        names.add(last)
    if project.module:
        words = project.module.split("-")
        names.add(pascal(words))
        if len(words) > 1:
            names.add(pascal(["".join(w[0] for w in words)]))
    # The prefix the module's files actually use (Ghg, Wardley, CausalLoop) is the most common run of
    # one to three leading words of their names, the longer run winning a tie.
    prefixes = collections.Counter()
    for path in common.cs_files(project):
        words = re.findall(r"[A-Z][a-z0-9]*", os.path.basename(path)[:-len(".cs")])
        for length in range(1, min(3, len(words) - 1) + 1):
            prefixes["".join(words[:length])] += 1
    if prefixes:
        prefix, count = max(prefixes.items(), key=lambda item: (item[1], len(item[0])))
        if count >= 3:
            names.add(prefix)
    return sorted((n for n in names if len(n) >= 2), key=len, reverse=True)


def normalized_lines(text, names):
    blanked = common.blank_comments_and_strings(text)
    pattern = re.compile("|".join(map(re.escape, names)), re.IGNORECASE) if names else None
    lines = []
    for number, line in enumerate(blanked.split("\n"), start=1):
        line = line.strip()
        if not line:
            continue
        line = re.sub(r'"[^"\n]*"', "S", line)
        line = re.sub(r"'[^'\n]*'", "S", line)
        line = re.sub(r"\b\d[\d_]*(\.\d+)?([eE][+-]?\d+)?[a-zA-Z]*\b|\b0x[0-9a-fA-F_]+\b", "0", line)
        if pattern:
            line = pattern.sub("MOD", line)
        line = re.sub(r"\s+", " ", line)
        lines.append((number, line))
    return lines


def role(path, names):
    stem = os.path.basename(path)[:-len(".cs")]
    for name in names:
        if stem.lower().startswith(name.lower()) and len(stem) > len(name):
            return stem[len(name):]
    return stem


def measure(root, tests=False):
    windows = collections.defaultdict(set)
    roles = {}
    for project in common.projects(root):
        if project.test != tests:
            continue
        names = module_names(project)
        for path in common.cs_files(project):
            name = common.relative(root, path)
            roles[name] = role(path, names)
            lines = [line for _, line in normalized_lines(common.read(path), names)]
            for start in range(0, len(lines) - WINDOW + 1):
                window = "\n".join(lines[start:start + WINDOW])
                # Windows of nothing but braces and usings say nothing about duplication.
                if len(re.sub(r"[{}();\s]|using [\w.]+", "", window)) < 40:
                    continue
                windows[hashlib.sha1(window.encode()).hexdigest()].add(name)

    shared = {digest: files for digest, files in windows.items() if len(files) > 1}
    windows_per_pair = collections.Counter()
    file_pairs_per_pair = collections.Counter()
    families = collections.Counter()
    for files in shared.values():
        ordered = sorted(files)
        seen = set()
        for i, a in enumerate(ordered):
            for b in ordered[i + 1:]:
                key = " / ".join(sorted({roles[a], roles[b]}))
                file_pairs_per_pair[key] += 1
                seen.add(key)
        for key in seen:
            windows_per_pair[key] += 1
        families[tuple(ordered)] += 1

    shared_files = collections.Counter()
    for files in shared.values():
        for f in files:
            shared_files[f] += 1
    return {
        "scope": "test" if tests else "production",
        "window_lines": WINDOW,
        "shared_windows": len(shared),
        "files_with_shared_windows": len(shared_files),
        # Windows: distinct windows that two files of those roles share. File pairs: the same counted
        # once per pair of files, so a window that fifteen resolvers share counts 105 times.
        "role_pairs": [
            {"roles": key, "windows": windows_per_pair[key], "file_pairs": count}
            for key, count in file_pairs_per_pair.most_common(40)
        ],
        "largest_clone_families": [
            {"shared_windows": count, "files": list(files)}
            for files, count in sorted(families.items(), key=lambda item: (-item[1] * len(item[0]), item[0]))[:20]
        ],
    }


if __name__ == "__main__":
    parser = common.arguments(__doc__)
    parser.add_argument("--tests", action="store_true", help="scan the test projects instead of production code")
    options = parser.parse_args()
    common.emit(measure(options.root, options.tests))
