#!/bin/bash
# gate.test.sh - the shared gate's own tests. Run by GateScriptTests inside `dotnet test`, so a
# regression in the gate fails a gate rather than a landing:
#
#   bash .github/tools/gate/gate.test.sh
#
# Hermetic. Everything it changes is built in a throwaway repository under a temp directory, with
# its own git configuration; the real repository is only READ - processes.md for the guard, and
# the scripts and fixtures under test. No npm or dotnet runs: the end-to-end cases gate a repository
# without a src/ folder, so every gate command fails at its `cd`.
#
# Every defect the gate exists to refuse is a case here, beside a case that MUST PASS through the
# same capture - a check seen only to refuse proves nothing, since a harness that never ran refuses
# too. Preconditions are asserted, not assumed: a planted husk that does not resolve to its main
# checkout, or a fixture that lost its CRLF, stops the run as broken rather than letting a case
# report on a question it never asked.
#
# Ends in RESULT=selftest-green only when every case ran - the count must equal EXPECTED, so a
# case that silently stops running is a failure too - and none was wrong.
#
# The fixtures are real dotnet logs, CRLF as dotnet writes it, from 2026-09-10 and before:
# dotnet-green.log (Architect 1's scratch gate), dotnet-green-2.log and dotnet-red-flaky.log
# (Developer 1: a genuine flaky failure, runner exit 2; one account name in a failure message
# replaced, nothing else touched), dotnet-truncated-midflight.log and dotnet-invocation-error.log
# (Developer 3: a run cut off while still listing assemblies, and dotnet's own one-line invocation
# error - a run that tested nothing and never says "Zero tests ran"). Real logs alone would pass a
# naive "green unless a failure is mentioned" verdict (Developer 1 measured it), so the variants
# built from them, each differing in exactly one property, are what do the catching.
set -u
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
DOC="$ROOT/.spec-workflow/steering/processes.md"
FIX="$HERE/fixtures"
. "$HERE/gate-lib.sh"

case "$(uname -s)" in MINGW* | MSYS* | CYGWIN*) MSYS=1 ;; *) MSYS=0 ;; esac
if [ "$MSYS" = 1 ]; then EXPECTED=85; else EXPECTED=81; fi

W=$(mktemp -d) || { echo "RESULT=selftest-broken (no temp dir)"; exit 2; }
trap 'rm -rf "$W"' EXIT
W=$(cd "$W" && pwd -P)

# Git, isolated from the machine: no system or global configuration leaks in, and commits have an
# identity without anyone's being borrowed.
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL="$W/gitconfig"
git config --global user.name gate-selftest
git config --global user.email gate-selftest@invalid
git config --global init.defaultBranch develop
git config --global advice.detachedHead false

N=0
WRONG=0
broken() {
  echo "HARNESS BROKEN: $*"
  echo "RESULT=selftest-broken"
  exit 2
}
report() { # <want> <got> <name> [detail]
  N=$((N + 1))
  if [ "$1" = "$2" ]; then
    echo "ok     $3"
  else
    WRONG=$((WRONG + 1))
    echo "WRONG  $3 (want $1, got $2) ${4:-}"
  fi
}
REAL_TOP=$(git -C "$ROOT" rev-parse --show-toplevel)
mkrepo() {
  # Asserted, not assumed: a git init that failed would leave a path that resolves to whatever
  # repository contains it - the husk hazard, inside the harness.
  git init -q "$1" && git -C "$1" commit -q --allow-empty -m init && git -C "$1" config extensions.worktreeConfig true &&
    [ -z "$(git -C "$1" rev-parse --show-cdup)" ] && [ "$(git -C "$1" rev-parse --show-toplevel)" != "$REAL_TOP" ]
}
fingerprint() { # a main checkout's state: its branch, its tip, its configuration, its tracked files
  printf '%s|%s|%s|%s' "$(git -C "$1" symbolic-ref -q HEAD)" "$(git -C "$1" rev-parse HEAD)" \
    "$(cksum < "$1/.git/config")" "$(git -C "$1" status --porcelain --untracked-files=no | cksum)"
}
result_of() { printf '%s\n' "$1" | sed -n 's/^RESULT=\([a-z-]*\).*/\1/p' | tail -1; }
winform() { cygpath -m "$1"; }

echo "== extracting the guard from processes.md"
extract_guard < "$DOC" > "$W/guard.sh"
rc=$?
report 0 "$rc" "the procedure's guard extracts as exactly one intact block"
[ "$rc" = 0 ] && [ -s "$W/guard.sh" ] || broken "no guard to test - every guard case below depends on it"
extract_guard < /dev/null > /dev/null
report 2 "$?" "empty input is refused as no block"
printf 'a document\nwith no guard in it\n' | extract_guard > /dev/null
report 2 "$?" "a document without the block is refused"
{ cat "$DOC"; printf '\n```sh\n'; cat "$W/guard.sh"; printf '```\n'; } | extract_guard > /dev/null
report 3 "$?" "a document carrying the block twice is refused"
sed 's/symbolic-ref/symbolic_ref/g' "$DOC" | extract_guard > /dev/null
report 4 "$?" "a block without the detached check (an older block) is refused"
sed 's/MAIN_TOP/MAIN_TIP/g' "$DOC" | extract_guard > /dev/null
report 4 "$?" "a block without the main-checkout refusal is refused"

echo "== the guard, against a repository with every kind of directory it must tell apart"
R="$W/repo"
mkrepo "$R" || broken "cannot create a repository"
WT="$R/.claude/worktrees"
mkdir -p "$WT"
git -C "$R" worktree add -q --detach "$WT/mrgd1" develop || broken "cannot create a scratch tree"
git -C "$R" worktree add -q -b feature "$WT/feat1" || broken "cannot create a feature worktree"
mkdir -p "$WT/husk1/src" && echo source > "$WT/husk1/src/file.txt"
mkdir -p "$WT/dangle1" && echo "gitdir: $W/nowhere" > "$WT/dangle1/.git"
RT=$(git -C "$R" rev-parse --show-toplevel)
[ "$(git -C "$WT/husk1" rev-parse --show-toplevel 2> /dev/null)" = "$RT" ] ||
  broken "the planted husk does not resolve to its main checkout, so it would test nothing"
git -C "$WT/feat1" symbolic-ref -q HEAD > /dev/null || broken "the planted feature worktree is not on a branch"
git -C "$WT/mrgd1" symbolic-ref -q HEAD > /dev/null && broken "the planted scratch tree is not detached"
FP_R=$(fingerprint "$R")

guard_on() { # <path> - PASS if the guard verifies the path, ABORT if it refuses, ERROR otherwise
  local out rc
  out=$({ MAIN="$R"; MRG="$1"; . "$W/guard.sh"; echo "VERIFIED=$MRG_TOP"; } 2>&1)
  rc=$?
  case "$rc:$out" in
    0:*VERIFIED=?*) echo PASS ;;
    3:*ABORT*) echo ABORT ;;
    *) echo "ERROR($rc)" ;;
  esac
}
report PASS "$(guard_on "$WT/mrgd1")" "a detached scratch tree is verified"
report PASS "$(guard_on "$WT/mrgd1/")" "... and with a trailing slash"
report PASS "$(guard_on "$WT/mrgn1")" "an absent scratch path is created and verified"
created=no
if [ "$(git -C "$WT/mrgn1" rev-parse --show-toplevel 2> /dev/null)" = "$(git -C "$R" rev-parse --show-toplevel)/.claude/worktrees/mrgn1" ] &&
  ! git -C "$WT/mrgn1" symbolic-ref -q HEAD > /dev/null; then created=yes; fi
report yes "$created" "... as a detached worktree of its own"
report ABORT "$(guard_on "$WT/husk1")" "a husk is refused"
report ABORT "$(guard_on "$WT/husk1/")" "... and with a trailing slash"
report ABORT "$(guard_on "$R")" "the main checkout is refused"
report ABORT "$(guard_on "$R/")" "... and with a trailing slash"
report ABORT "$(guard_on "$WT/feat1")" "a feature worktree (on a branch) is refused"
report ABORT "$(guard_on "$WT/dangle1")" "a directory with a dangling .git file is refused"
if [ "$MSYS" = 1 ]; then
  report PASS "$(guard_on "$(winform "$WT/mrgd1")")" "a scratch tree spelled C:/... is verified"
  up=$(winform "$WT/mrgd1" | tr '[:lower:]' '[:upper:]')
  report PASS "$(guard_on "$up")" "... and spelled in upper case"
  report ABORT "$(guard_on "$(winform "$WT/husk1")")" "a husk spelled C:/... is refused"
  mkdir -p "$W/fakebin" && printf '#!/bin/sh\nexit 127\n' > "$W/fakebin/cygpath" && chmod +x "$W/fakebin/cygpath"
  [ "$(PATH="$W/fakebin:$PATH" bash -c 'command -v cygpath')" = "$W/fakebin/cygpath" ] ||
    broken "the fake cygpath is not the one a child resolves, so the missing-cygpath case would not run"
  report ABORT "$(PATH="$W/fakebin:$PATH" guard_on "$WT/mrgd1")" "with cygpath missing the guard fails closed"
fi
report "$FP_R" "$(fingerprint "$R")" "the main checkout is untouched by all of the above"

echo "== the verdict, against a real dotnet log and variants of it"
REAL="$FIX/dotnet-green.log"
[ -s "$REAL" ] || broken "the real log fixture is missing"
CRS=$(tr -cd '\r' < "$REAL" | wc -c)
LFS=$(tr -cd '\n' < "$REAL" | wc -c)
[ "$CRS" -gt 0 ] && [ "$CRS" = "$LFS" ] || broken "the real log is not CRLF (CR=$CRS LF=$LFS) - is .gitattributes' -text rule in force?"
V="$W/verdict"
mkdir -p "$V"
tr -d '\r' < "$REAL" > "$V/lf.log"
: > "$V/empty.log"
printf 'Zero tests ran\r\n  total: 0\r\n  failed: 0\r\n' > "$V/zero.log"
head -150 "$REAL" > "$V/truncated.log"
sed -b 's/failed: 0/failed: 3/' "$REAL" > "$V/failed3.log"
sed -b 's/total: [0-9]*/total: 0/' "$REAL" > "$V/total0.log"
sed -b '/^ *failed: /d' "$REAL" > "$V/nofailedline.log"
for f in truncated failed3 total0 nofailedline; do
  [ "$(tr -cd '\r' < "$V/$f.log" | wc -c)" -gt 0 ] || broken "variant $f lost its CRLF"
  cmp -s "$V/$f.log" "$REAL" && broken "variant $f is a copy of the real log, so it would test nothing"
done
grep -q 'failed: 3' "$V/failed3.log" || broken "variant failed3 does not say failed: 3"
tr -d '\r' < "$V/truncated.log" | grep -q 'total:' && broken "variant truncated still has its summary"
# Developer 1's shape: cut at the summary, then an exit line appended after the runner returned.
sed -b '/Test run summary/,$d' "$REAL" > "$V/trunc-exit.log" && printf 'GATE4_DOTNET_TEST_EXIT=0\n' >> "$V/trunc-exit.log"
tr -d '\r' < "$V/trunc-exit.log" | grep -q 'total:' && broken "variant trunc-exit still has its summary"
GREEN2="$FIX/dotnet-green-2.log"
RED="$FIX/dotnet-red-flaky.log"
MID="$FIX/dotnet-truncated-midflight.log"
INV="$FIX/dotnet-invocation-error.log"
for f in "$GREEN2" "$RED" "$MID" "$INV"; do
  [ "$(tr -cd '\r' < "$f" 2> /dev/null | wc -c)" -gt 0 ] || broken "fixture $f is missing or not CRLF"
done
tr -d '\r' < "$GREEN2" | grep -q '^ *failed: 0$' || broken "the second green log does not say failed: 0"
tr -d '\r' < "$RED" | grep -q '^ *failed: 1$' || broken "the red log does not say failed: 1"
tr -d '\r' < "$MID" | grep -qE 'total:|Test run summary' && broken "the mid-flight log has a summary after all"
grep -q 'Specify either' "$INV" || broken "the invocation-error log is not the invocation error"
grep -q 'Zero tests ran' "$INV" && broken "the invocation-error log says Zero tests ran, so it would not test the phrase check's blind spot"

verdict_of() { # <log> [NAME=value | -NAME ...] - the verdict with every gate at 0; -NAME unsets
  local log=$1 kv
  shift
  (
    export NPM_INSTALL_EXIT=0 NPM_TEST_EXIT=0 TC_EXIT=0 FMT_EXIT=0 DT_EXIT=0
    for kv in "$@"; do
      case "$kv" in -*) unset "${kv#-}" ;; *) export "$kv" ;; esac
    done
    gate_verdict "$log"
    echo "$VERDICT"
  )
}
report green "$(verdict_of "$REAL")" "all gates 0 and the real CRLF log: green"
report green "$(verdict_of "$V/lf.log")" "... and the same log as LF: green"
report refused "$(verdict_of "$W/no-such.log")" "the log is missing (deleted mid-run)"
report refused "$(verdict_of "$V/empty.log")" "the log is empty"
report refused "$(verdict_of "$V/zero.log")" "the log says Zero tests ran"
report refused "$(verdict_of "$V/truncated.log")" "the log is truncated before its summary"
report refused "$(verdict_of "$V/failed3.log")" "the log says failed: 3 behind an exit code of 0"
report refused "$(verdict_of "$V/total0.log")" "the log says total: 0"
report refused "$(verdict_of "$V/nofailedline.log")" "the log has no failed: line"
report refused "$(verdict_of "$REAL" DT_EXIT=1)" "dotnet test exits 1"
# A status that was never recorded - a step that did not run, an assignment lost in a subshell, a
# misspelled name - with a complete, green log beside it (Developer 1's case, moved from the log to
# the variable). Every status, every way of being absent: none may be read as 0.
for s in NPM_INSTALL_EXIT NPM_TEST_EXIT TC_EXIT FMT_EXIT DT_EXIT; do
  report refused "$(verdict_of "$REAL" "-$s")" "$s unset, beside a green log"
  report refused "$(verdict_of "$REAL" "$s=")" "$s empty, beside a green log"
  report refused "$(verdict_of "$REAL" "$s=x")" "$s not a number, beside a green log"
done
report refused "$(verdict_of "$REAL" TC_EXIT=2)" "typecheck exits 2"
report refused "$(verdict_of "$REAL" FMT_EXIT=1)" "format exits 1"
report refused "$(verdict_of "$REAL" NPM_INSTALL_EXIT=1)" "npm install exits 1"
report refused "$(verdict_of "")" "no log path at all"
report refused "$(verdict_of "$V/trunc-exit.log")" "truncated at its summary with an exit line appended after"
report green "$(verdict_of "$GREEN2")" "a second real green run, another author's: green"
report refused "$(verdict_of "$RED" DT_EXIT=2)" "a real red run (a flaky test, runner exit 2)"
report refused "$(verdict_of "$RED")" "... and with its exit recorded as 0 the log alone refuses"
report refused "$(verdict_of "$MID")" "a real run cut off mid-flight, every exit 0"
report refused "$(verdict_of "$INV")" "a real invocation error that tested nothing and never says Zero tests ran"

echo "== gate.sh refuses to start without its four arguments"
G="$HERE/gate.sh"
out=$(bash "$G" 2>&1)
report "1:missing-arguments" "$?:$(result_of "$out")" "no arguments"
out=$(bash "$G" mrga1 some-branch some-identity 2>&1)
report "1:missing-arguments" "$?:$(result_of "$out")" "three arguments (no message)"
out=$(bash "$G" mrga1 some-branch some-identity "" 2>&1)
report "1:missing-arguments" "$?:$(result_of "$out")" "an empty message"
out=$(bash "$G" scratch some-branch some-identity "a message" 2>&1)
report "1:bad-scratch-name" "$?:$(result_of "$out")" "a scratch name that is not mrg<N>"
out=$(bash "$G" mrga1234 some-branch some-identity "a message" 2>&1)
report "1:bad-scratch-name" "$?:$(result_of "$out")" "a scratch name too long to stay short"

echo "== gate.sh and land.sh end to end, in a throwaway repository"
R2="$W/repo2"
mkrepo "$R2" || broken "cannot create the second repository"
echo base > "$R2/f.txt" && git -C "$R2" add f.txt && git -C "$R2" commit -q -m "add f"
git -C "$R2" switch -q -c clash develop~1 && echo clash > "$R2/f.txt" && git -C "$R2" add f.txt &&
  git -C "$R2" commit -q -m "clash on f" && git -C "$R2" switch -q develop || broken "cannot build the clash branch"
git -C "$R2" switch -q -c topic develop && echo topic > "$R2/t.txt" && git -C "$R2" add t.txt &&
  git -C "$R2" commit -q -m "topic" && git -C "$R2" switch -q develop || broken "cannot build the topic branch"
T2="$R2/.github/tools/gate"
mkdir -p "$T2" "$R2/.spec-workflow/steering"
cp "$HERE/gate.sh" "$HERE/gate-lib.sh" "$HERE/land.sh" "$T2/"
G2="$T2/gate.sh"
L2="$T2/land.sh"
gate2() { bash "$G2" "$1" "$2" gate-selftest-identity "a merge message" 2>&1; }

out=$(gate2 mrgt1 develop)
report "3:aborted-not-develops-gate" "$?:$(result_of "$out")" "an uncommitted copy of the gate is refused"
git -C "$R2" add .github && git -C "$R2" commit -q -m "the gate" || broken "cannot commit the gate"
out=$(gate2 mrgt1 develop)
report "3:aborted-cannot-read-procedure" "$?:$(result_of "$out")" "develop's gate with no processes.md on develop refuses"
cp "$DOC" "$R2/.spec-workflow/steering/processes.md" && git -C "$R2" add .spec-workflow &&
  git -C "$R2" commit -q -m "the procedure" || broken "cannot commit the procedure"
echo "# drift" >> "$G2"
out=$(gate2 mrgt1 develop)
report "3:aborted-not-develops-gate" "$?:$(result_of "$out")" "a drifted copy of develop's gate is refused"
git -C "$R2" checkout -q -- .github || broken "cannot restore the gate"
WT2="$R2/.claude/worktrees"
mkdir -p "$WT2/mrgh1/src" && echo source > "$WT2/mrgh1/src/file.txt"
git -C "$R2" worktree add -q -b feat2 "$WT2/mrgf1" develop || broken "cannot create a feature worktree"
[ "$(git -C "$WT2/mrgh1" rev-parse --show-toplevel 2> /dev/null)" = "$(git -C "$R2" rev-parse --show-toplevel)" ] ||
  broken "the second husk does not resolve to its main checkout"
DEV0=$(git -C "$R2" rev-parse develop)
FP2=$(fingerprint "$R2")
out=$(gate2 mrgh1 topic)
report "3:aborted-by-guard" "$?:$(result_of "$out")" "gate.sh aimed at a husk stops at the guard"
out=$(gate2 mrgf1 topic)
report "3:aborted-by-guard" "$?:$(result_of "$out")" "gate.sh aimed at a feature worktree stops at the guard"
report "$FP2" "$(fingerprint "$R2")" "... and neither touched the main checkout"
out=$(gate2 mrgt1 no-such-branch)
report "1:no-such-branch" "$?:$(result_of "$out")" "a branch that does not exist is refused"

out=$(gate2 mrgt1 topic)
rc=$?
report "1:gates-red" "$rc:$(result_of "$out")" "a real run through every stage: the gates cannot pass without src/, so red"
MERGED=$(printf '%s\n' "$out" | sed -n 's/^MERGED=//p')
report "$DEV0" "$(git -C "$R2" rev-parse "${MERGED:-none}^1" 2> /dev/null)" "... the merge was made onto develop's tip"
report "$DEV0" "$(git -C "$R2" rev-parse develop)" "... develop did not move"
report "$FP2" "$(fingerprint "$R2")" "... the main checkout is untouched (no identity, no reset)"
report gate-selftest-identity "$(git -C "$WT2/mrgt1" config --worktree user.name)" "... the identity went into the scratch tree's own config"
GD=$(git -C "$WT2/mrgt1" rev-parse --absolute-git-dir)
report no "$([ -e "$GD/adp-gate.lock" ] && echo yes || echo no)" "... and the lock was released"
mkdir "$GD/adp-gate.lock"
out=$(gate2 mrgt1 topic)
report "1:scratch-busy" "$?:$(result_of "$out")" "a scratch tree whose lock is held is refused"
report yes "$([ -d "$GD/adp-gate.lock" ] && echo yes || echo no)" "... and the refused run left the other gate's lock alone"
rm -rf "$GD/adp-gate.lock"
out=$(gate2 mrgt1 clash)
report "1:merge-conflict" "$?:$(result_of "$out")" "a conflicting branch is refused"
report no "$(git -C "$WT2/mrgt1" rev-parse -q --verify MERGE_HEAD > /dev/null && echo yes || echo no)" "... and the half-merge was aborted"

out=$(bash "$L2" "$DEV0" 2>&1)
report "1:missing-arguments" "$?:$(result_of "$out")" "land.sh without its two arguments"
out=$(bash "$L2" "$DEV0~1" "$MERGED" 2>&1)
report "1:develop-moved-regate" "$?:$(result_of "$out")" "land.sh on a base develop has moved past"
out=$(bash "$L2" "$DEV0" clash 2>&1)
report "1:not-a-descendant" "$?:$(result_of "$out")" "land.sh with a commit that does not descend from the base"
# Cut from develop so the branch carries the gate: switching to one that predates it would take
# land.sh out of the working tree, and the case would test a missing file instead.
git -C "$R2" switch -q -c elsewhere develop || broken "cannot switch the main checkout to another branch"
ELSE0=$(git -C "$R2" rev-parse elsewhere)
out=$(bash "$L2" "$DEV0" "$MERGED" 2>&1)
report "1:main-checkout-not-on-develop" "$?:$(result_of "$out")" "land.sh with the main checkout on another branch"
report "$ELSE0" "$(git -C "$R2" rev-parse elsewhere)" "... and that branch was not moved"
git -C "$R2" switch -q develop
out=$(bash "$L2" "$DEV0" "$MERGED" 2>&1)
report "0:landed" "$?:$(result_of "$out")" "land.sh lands the gated commit"
report "$MERGED" "$(git -C "$R2" rev-parse develop)" "... and develop is exactly that commit"

echo "SELFTEST cases=$N wrong=$WRONG expected=$EXPECTED"
if [ "$N" = "$EXPECTED" ] && [ "$WRONG" = 0 ]; then
  echo "RESULT=selftest-green"
  exit 0
fi
echo "RESULT=selftest-red"
exit 1
