# Names the failing tests of a `dotnet test` run, from the JUnit reports its test assemblies wrote
# (`--report-xunit-junit --results-directory <folder>`). The Build workflow runs it after a red
# "Backend tests" step.
#
# Why it exists: a failed run's console output can carry thousands of lines of captured backend
# logging after the failure, so the failing tests' names end up outside any tail of the log, and a
# reader who can only see the tail (a cloud agent reading job logs sees the last 5000 lines) cannot
# tell which test failed. This prints them last, writes them to the run's summary and raises one
# annotation per test, which the pull request's checks page shows without opening the log.
#
# It never decides the run's verdict: the test step's own exit code did that. It exits 0 whatever it
# finds, and says so when it finds no failing test, because then the failure was not a test (a
# build error, a crashed test host) and the log above says what it was.
import glob
import os
import sys
import xml.etree.ElementTree as ElementTree

MESSAGE_LINES = 6


def failures(folder):
    for path in sorted(glob.glob(os.path.join(folder, "**", "*.xml"), recursive=True)):
        try:
            root = ElementTree.parse(path).getroot()
        except ElementTree.ParseError as error:
            print(f"failing-tests: {path} is not readable XML ({error}); skipped")
            continue
        for suite in root.iter("testsuite"):
            assembly = os.path.basename(suite.get("name", path))
            for case in suite.iter("testcase"):
                for kind in ("failure", "error"):
                    found = case.find(kind)
                    if found is not None:
                        text = (found.get("message") or found.text or "").strip()
                        yield assembly, case.get("name", "?"), text
                        break


def escape(value):
    # Workflow-command escaping, so a message cannot end the annotation early.
    return value.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def escape_property(value):
    # A property (the title) also ends at a comma or a colon, and a theory's name holds both.
    return escape(value).replace(":", "%3A").replace(",", "%2C")


def main(folder):
    found = list(failures(folder))
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if not found:
        print(f"failing-tests: no failing test in the reports under {folder}; the failure was not a test result, so read the step's log.")
        return 0

    print(f"failing-tests: {len(found)} failing test(s)")
    lines = [f"### {len(found)} failing backend test(s)", ""]
    for assembly, name, message in found:
        first = "\n".join(message.splitlines()[:MESSAGE_LINES])
        print(f"  {name}  ({assembly})")
        for line in first.splitlines():
            print(f"      {line}")
        print(f"::error title=Failing test {escape_property(name)}::{escape(first)}")
        lines.append(f"- `{name}` ({assembly})")
        if first:
            lines.append("")
            lines.append("  ```")
            lines.extend(f"  {line}" for line in first.splitlines())
            lines.append("  ```")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("usage: failing-tests.py <results folder>")
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
