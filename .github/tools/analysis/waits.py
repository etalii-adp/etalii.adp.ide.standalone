"""Fixed waits and polling helpers (spec 025, T006): every Task.Delay and Thread.Sleep in test and in
production code with its file and line (the testing projects are test code, common.py), and every declaration of a polling helper (a method whose name
says it waits until something holds: Eventually, WaitUntil..., ...UntilAsync, Poll...)."""

import re

import common

WAIT = re.compile(r"\b(Task\.Delay|Thread\.Sleep)\s*\(")
HELPER = re.compile(r"[\w>\]?]\s+((?:Eventually|WaitUntil|Poll)\w*|\w*Until(?:Async)?)\s*(?:<[^(]*>)?\s*\(")


def measure(root):
    waits = {"test": [], "production": []}
    helpers = []
    for project in common.projects(root):
        for path in common.cs_files(project):
            blanked = common.blank_comments_and_strings(common.read(path))
            name = common.relative(root, path)
            for number, line in enumerate(blanked.split("\n"), start=1):
                for match in WAIT.finditer(line):
                    waits[project.group].append({"file": name, "line": number, "call": match.group(1)})
                for match in HELPER.finditer(line):
                    # A declaration ends its line with its body or a parameter list, never in a call chain.
                    if re.search(r"\b(return|await|=|\.)\s*" + re.escape(match.group(1)), line):
                        continue
                    helpers.append({"file": name, "line": number, "helper": match.group(1)})
    return {
        "waits": {group: len(found) for group, found in waits.items()},
        "polling_helpers": len(helpers),
        "wait_sites": waits,
        "helper_sites": helpers,
    }


if __name__ == "__main__":
    common.emit(measure(common.arguments(__doc__).parse_args().root))
