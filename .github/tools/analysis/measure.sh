#!/usr/bin/env bash
# Runs the five measurement scripts of spec 025 on the checked-out commit and writes
# measurements.json and measurements.md. See readme.md beside this script.
#
#   measure.sh [--root <repository>] [--out <folder>]
#   measure.sh --self-test
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root=""
out="."
self_test=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --root) root="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --self-test) self_test=1; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

measure() {
  local tree="$1" folder="$2" commit="$3"
  mkdir -p "$folder"
  local parts="$folder/.parts"
  mkdir -p "$parts"
  python3 -B "$here/lines.py" --root "$tree" > "$parts/lines.json"
  python3 -B "$here/clones.py" --root "$tree" > "$parts/clones_production.json"
  python3 -B "$here/clones.py" --root "$tree" --tests > "$parts/clones_test.json"
  python3 -B "$here/methods.py" --root "$tree" > "$parts/methods.json"
  python3 -B "$here/graph.py" --root "$tree" > "$parts/graph.json"
  python3 -B "$here/waits.py" --root "$tree" > "$parts/waits.json"
  python3 -B - "$parts" "$commit" "$folder/measurements.json" <<'PY'
import json, os, sys
parts, commit, target = sys.argv[1:]
result = {"commit": commit}
for name in ("lines", "clones_production", "clones_test", "methods", "graph", "waits"):
    with open(os.path.join(parts, name + ".json"), encoding="utf-8") as handle:
        result[name] = json.load(handle)
with open(target, "w", encoding="utf-8", newline="\r\n") as handle:
    json.dump(result, handle, indent=2)
    handle.write("\n")
PY
  rm -r "$parts"
  python3 -B "$here/summary.py" "$folder/measurements.json" "$folder/measurements.md"
}

if [[ $self_test -eq 1 ]]; then
  scratch="$(mktemp -d)"
  trap 'rm -rf "$scratch"' EXIT
  measure "$here/fixtures/tree" "$scratch" "fixture"
  # Paths are compared relative to the fixture tree, and the line endings of the checkout do not count.
  python3 -B - "$scratch/measurements.json" "$here/fixtures/expected.json" <<'PY'
import json, sys
actual, expected = (json.load(open(path, encoding="utf-8")) for path in sys.argv[1:])
if actual != expected:
    for key in expected:
        if actual.get(key) != expected[key]:
            print(f"self-test: '{key}' differs from fixtures/expected.json", file=sys.stderr)
    sys.exit(1)
print("self-test: passed")
PY
  exit 0
fi

if [[ -z "$root" ]]; then
  root="$(git -C "$here" rev-parse --show-toplevel)"
fi
commit="$(git -C "$root" rev-parse --short=9 HEAD 2>/dev/null || echo unknown)"
measure "$root" "$out" "$commit"
echo "measure.sh: wrote $out/measurements.json and $out/measurements.md for $commit"
