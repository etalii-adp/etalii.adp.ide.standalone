#!/bin/bash
# Runs JetBrains InspectCode over the backend solution, the one committed way to do it.
#
#   bash .github/tools/inspect/inspect.sh [--report] [--no-build]
#
#   --report     findings are listed but do not fail the run; a blind run still exits 2
#   --no-build   the solution is already built by this checkout's current sources
#
# Exit 0: the run could see the code and found nothing at Suggestion severity or above.
# Exit 1: the run could see the code and found something; every finding is listed.
# Exit 2: the run could NOT see the code - a missing report, compiler errors among the results, or
#         fewer files inspected than git tracks. Build first; the tree is neither clean nor dirty.
#
# The inspector's version is the one `.config/dotnet-tools.json` pins; run `dotnet tool restore`
# once before the first run. The SDK and its MSBuild are named to the inspector explicitly rather
# than left to its own discovery, because a run that found no toolset once inspected nothing here
# and reported a clean result. Health is judged before findings, by `evaluate.mjs` beside this
# script, so that cannot pass again.
#
# The report and the inspector's console log are kept in `.inspect/` at the repository root
# (git-ignored), so a finding can be read in full after the summary has scrolled away.
set -u

REPORT_MODE=
BUILD=1
for argument in "$@"; do
  case "$argument" in
    --report) REPORT_MODE=--report ;;
    --no-build) BUILD= ;;
    *)
      echo "usage: inspect.sh [--report] [--no-build]"
      exit 2
      ;;
  esac
done

HERE=$(cd "$(dirname "$0")" && pwd) || { echo "inspect.sh: cannot locate the script's own folder"; exit 2; }
ROOT=$(git -C "$HERE" rev-parse --show-toplevel) || { echo "inspect.sh: not inside a git repository"; exit 2; }
SOLUTION="$ROOT/src/backend/EtAlii.Adp.slnx"
OUT="$ROOT/.inspect"
REPORT="$OUT/report.sarif"
LOG="$OUT/inspect.log"

# The SDK global.json selects, as dotnet itself resolves it (rollForward included), and the folder
# `dotnet --list-sdks` gives for exactly that version. Nothing is hard-coded, so this reads the same
# on Windows under Git Bash and on the Linux runner.
SDK_VERSION=$(cd "$ROOT/src" && dotnet --version) || { echo "inspect.sh: dotnet could not resolve the SDK src/global.json names"; exit 2; }
SDK_VERSION=$(printf '%s' "$SDK_VERSION" | tr -d '\r')
SDK_BASE=$(dotnet --list-sdks | tr -d '\r' | awk -v v="$SDK_VERSION" '$1 == v { sub(/^[^[]*\[/, ""); sub(/\]$/, ""); print; exit }')
TOOLSET="$SDK_BASE/$SDK_VERSION/MSBuild.dll"
if [ -z "$SDK_BASE" ] || [ ! -f "$TOOLSET" ]; then
  echo "inspect.sh: no MSBuild.dll for SDK $SDK_VERSION (looked at '$TOOLSET')"
  exit 2
fi

# The floor for the inspected count: every C# file git tracks under src/, less the ones under a
# Fixtures folder, which are test data that belong to no project.
TRACKED=$(git -C "$ROOT" ls-files -- 'src/*.cs' | grep -v '/Fixtures/' | wc -l | tr -d ' ')

mkdir -p "$OUT"
rm -f "$REPORT" "$LOG"

if [ -n "$BUILD" ]; then
  echo "inspect.sh: building $SOLUTION"
  dotnet build "$SOLUTION" -nologo -v:q > "$OUT/build.log" 2>&1
  BUILD_EXIT=$?
  if [ "$BUILD_EXIT" -ne 0 ]; then
    tail -n 30 "$OUT/build.log"
    echo "inspect.sh: the build failed (exit $BUILD_EXIT), so an inspection could not see the code"
    exit 2
  fi
fi

# The one exclusion, Broken.csproj, is malformed on purpose (tech.md, "JetBrains Rider warnings",
# says why). It is excluded twice, and saying so is the point: the file mask in
# src/backend/EtAlii.Adp.sln.DotSettings is what Rider honours, and was measured removing exactly
# these four errors on Windows; on the Linux runner that mask - and two other forms of the same
# setting - left all four in place while a severity entry in the same file did bind. So the
# inspector is also told by path. Same file, same reason; nothing else is excluded here.
EXCLUDE='**/EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests/Fixtures/cpm/Broken.csproj'

echo "inspect.sh: inspecting with SDK $SDK_VERSION ($TOOLSET)"
(cd "$ROOT" && dotnet jb inspectcode "$SOLUTION" \
  --no-build \
  --exclude="$EXCLUDE" \
  --dotnetcoresdk="$SDK_VERSION" \
  --toolset-path="$TOOLSET" \
  --severity=SUGGESTION \
  --output="$REPORT") > "$LOG" 2>&1
INSPECT_EXIT=$?
if [ "$INSPECT_EXIT" -ne 0 ]; then
  tail -n 30 "$LOG"
  echo "inspect.sh: the inspector itself failed (exit $INSPECT_EXIT); has 'dotnet tool restore' run?"
  exit 2
fi

(cd "$ROOT" && node "$HERE/evaluate.mjs" "$REPORT" "$LOG" "$TRACKED" $REPORT_MODE)
EVALUATE_EXIT=$?
exit "$EVALUATE_EXIT"
