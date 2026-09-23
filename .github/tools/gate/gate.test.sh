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
if [ "$MSYS" = 1 ]; then EXPECTED=167; else EXPECTED=157; fi

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

echo "== the post-gate tree check compares content, not what status reports"
# The shared gate's first real run: four gates green, then refused, because the client's pretest
# rewrote generated files with LF over a CRLF checkout - `git status` said modified, `git diff`
# found nothing. Planted here exactly: same content, other line endings.
R4="$W/repo4"
mkrepo "$R4" || broken "cannot create the fourth repository"
printf '* text=auto eol=crlf\n' > "$R4/.gitattributes" && printf 'line one\nline two\n' > "$R4/g.ts"
git -C "$R4" add .gitattributes g.ts 2> /dev/null && git -C "$R4" commit -q -m generated && rm "$R4/g.ts" &&
  git -C "$R4" checkout -q -- g.ts || broken "cannot build the generated-file repository"
[ "$(tr -cd '\r' < "$R4/g.ts" | wc -c)" -gt 0 ] || broken "the checkout did not write CRLF, so the rewrite would change nothing"
gate_tree_matches_head "$R4"
report 0 "$?" "an untouched tree matches its HEAD"
sleep 1
printf 'line one\nline two\n' > "$R4/g.ts"
[ -n "$(git -C "$R4" status --porcelain --untracked-files=no 2> /dev/null)" ] ||
  broken "status does not report the LF rewrite, so the case would not reproduce the first run's refusal"
gate_tree_matches_head "$R4"
report 0 "$?" "a generated file rewritten with other line endings, same content, matches"
printf 'line one\nline 2\n' > "$R4/g.ts"
gate_tree_matches_head "$R4"
report 1 "$?" "a real change to a tracked file does not"
git -C "$R4" add g.ts 2> /dev/null && git -C "$R4" checkout -q -- g.ts 2> /dev/null
gate_tree_matches_head "$R4"
report 1 "$?" "a staged change does not either"
gate_tree_matches_head "$W/nowhere"
report 2 "$?" "a tree that cannot be read is its own answer, not a match"

echo "== a run's logs: its own directory, kept when refused, and never another run's"
LP="$W/logs"
mkdir -p "$LP"
D1=$(gate_run_logs_dir "$LP" "20260912T100000Z-111")
report 0 "$?" "a run gets its own log directory"
echo green > "$D1/dotnet-test.log"
D2=$(gate_run_logs_dir "$LP" "20260912T100500Z-222")
report 0 "$?" "a second run gets a different one"
report yes "$([ "$D1" != "$D2" ] && echo yes || echo no)" "... and the two are not the same directory"
report no "$([ -e "$D2/dotnet-test.log" ] && echo yes || echo no)" "... the second cannot read the first's log (the defect the deletion once fixed)"
gate_run_logs_dir "$LP" "20260912T100000Z-111" > /dev/null
report 2 "$?" "a directory that already holds files is refused, never written into"
KEPT=$(gate_keep_logs_red "$D1" "gates-red (dotnet-failed='1' )")
report yes "$([ "$KEPT" = "$D1-red" ] && echo yes || echo no)" "a refused run's logs are kept under a -red name"
report yes "$([ -f "$KEPT/dotnet-test.log" ] && echo yes || echo no)" "... with the logs themselves intact"
report yes "$([ -f "$KEPT/WHAT-THIS-IS.txt" ] && grep -q "REFUSED" "$KEPT/WHAT-THIS-IS.txt" && echo yes || echo no)" "... a note inside saying what it is, for a reader who does not know the convention"
report "$KEPT" "$(cat "$LP/last-red-run.txt" 2> /dev/null)" "... and a pointer beside it naming the newest kept run"
report "$KEPT" "$(gate_keep_logs_red "$KEPT" again)" "keeping an already-kept directory leaves it where it is"
# Pruning keeps the NEWEST of each kind. The timestamps are set apart deliberately: created in a
# loop they land in the same second, "newest" becomes arbitrary, and a reversed sort order passes a
# count-only assertion - which is exactly what a planted defect showed on 2026-09-12.
for i in $(seq -w 1 12); do
  mkdir -p "$LP/202609${i}T090000Z-90$i" "$LP/202609${i}T090000Z-91$i-red"
  touch -d "2026-09-$i 09:00:00" "$LP/202609${i}T090000Z-90$i" "$LP/202609${i}T090000Z-91$i-red"
done
mkdir -p "$LP/somebody-investigating"
gate_prune_logs "$LP" 10
report 10 "$(ls -1d "$LP"/[0-9]*T[0-9]*Z-[0-9]* 2> /dev/null | grep -vc -- -red)" "pruning keeps ten plain run directories"
report 10 "$(ls -1d "$LP"/[0-9]*T[0-9]*Z-[0-9]*-red 2> /dev/null | wc -l)" "... and ten kept-red ones"
report yes "$([ -d "$LP/20260912T090000Z-9012" ] && [ -d "$LP/20260912T090000Z-9112-red" ] && echo yes || echo no)" "... keeps the NEWEST of each kind, not merely ten of them"
report no "$([ -d "$LP/20260901T090000Z-9001" ] || [ -d "$LP/20260901T090000Z-9101-red" ] && echo yes || echo no)" "... and the oldest of each is the one that goes"
report yes "$([ -d "$LP/somebody-investigating" ] && [ -f "$LP/last-red-run.txt" ] && echo yes || echo no)" "... and touches nothing that is not a run directory"
# A rename that cannot happen must still say where the logs are. An existing, non-empty <dir>-red
# blocks it the same on every platform - and is the case where plain mv would have moved this run's
# logs INSIDE the other run's directory and printed that directory as if it were this run's.
D3=$(gate_run_logs_dir "$LP" "20260912T110000Z-333") || broken "cannot create the blocked run's directory"
echo mine > "$D3/merge.log"
mkdir -p "$D3-red" && echo theirs > "$D3-red/merge.log" || broken "cannot plant the blocking directory"
KEPT3=$(gate_keep_logs_red "$D3" "gates-red (format='2' )")
report "$D3" "$KEPT3" "a refused run whose rename is blocked still names where its logs are"
report mine "$(cat "$KEPT3/merge.log" 2> /dev/null)" "... and they are this run's own logs, not moved into another run's directory"
report yes "$(grep -q "rename .* FAILED" "$KEPT3/WHAT-THIS-IS.txt" 2> /dev/null && echo yes || echo no)" "... with a note saying the rename failed and pruning will treat them as plain"
report theirs "$(cat "$D3-red/merge.log" 2> /dev/null)" "... and the other run's kept logs are untouched"
# The older gate wrote six logs straight into the parent. Only those six names go.
for f in merge.log npm-install.log npm-test.log typecheck.log format.log dotnet-test.log notes.txt; do echo old > "$LP/$f"; done
gate_remove_legacy_flat_logs "$LP"
report 0 "$(ls -1 "$LP"/merge.log "$LP"/npm-install.log "$LP"/npm-test.log "$LP"/typecheck.log "$LP"/format.log "$LP"/dotnet-test.log 2> /dev/null | wc -l | tr -d ' ')" "the six flat logs of the older gate are removed from the parent"
report yes "$([ -f "$LP/notes.txt" ] && [ -f "$LP/last-red-run.txt" ] && [ -d "$D3-red" ] && echo yes || echo no)" "... and nothing else in the parent is"

echo "== test folders a run could not delete: counted, never judged"
UD="$W/undeleted"
report none "$(gate_undeleted_folders "$UD")" "no report directory means nothing was reported"
mkdir -p "$UD"
report none "$(gate_undeleted_folders "$UD")" "an empty report directory is none too"
echo stray > "$UD/readme.txt"
report none "$(gate_undeleted_folders "$UD")" "a file that is not a process's .log is not a report"
printf 'gave up deleting A\ngave up deleting B\n' > "$UD/4302.log"
report "1 file, 2 lines" "$(gate_undeleted_folders "$UD")" "one reporting process with two folders (the regressed-guard signature)"
printf 'gave up deleting C\r\n' > "$UD/5100.log"
printf 'gave up deleting D' >> "$UD/5100.log"
report "2 files, 4 lines" "$(gate_undeleted_folders "$UD")" "two processes, CRLF lines and a last line without a newline all counted"
report none "$(gate_undeleted_folders "")" "no directory named at all is none, not an error"

echo "== the tell: is a gate running, and on which base"
# The reader has FOUR outputs and silence is none of them. The first draft was
# `cat <glob> 2>/dev/null`, which prints the identical nothing for "nobody is gating", "the
# directory never existed", "the writer moved", "a mistyped path" and "the glob did not expand" -
# four of which mean NO INFORMATION while all five read as clear to proceed. So the cases below
# assert the reader, not only the file: a guard over the file alone is green while the published
# procedure is blind.
TELL=$(mktemp -d)/worktrees
report "TELL_UNREADABLE=$TELL" "$(gate_who_is_gating "$TELL")" "an absent tell directory is loud, not empty"
gate_who_is_gating "$TELL" > /dev/null; report 2 "$?" "... and exits nonzero, so a caller cannot read it as clear"
report "TELL_UNREADABLE=<no directory named>" "$(gate_who_is_gating "")" "no directory named at all is loud too"
mkdir -p "$TELL"
report "GATING=none" "$(gate_who_is_gating "$TELL")" "no lock anywhere says so in words"
gate_who_is_gating "$TELL" > /dev/null; report 0 "$?" "... and exits zero, which is the only reassuring state"
mkdir -p "$TELL/mrga1/adp-gate.lock"
# The lock exists before its line does, and the write is deliberately best effort - so globbing
# the owner FILE would print GATING=none while a gate was genuinely running, which is the
# under-hold whose cost is the unrecoverable one. The reader globs the lock DIRECTORY.
report "mrga1 gating (owner not written yet)" "$(gate_who_is_gating "$TELL")" "a lock with no line yet is still a holder"
gate_tell_write "$TELL/mrga1/adp-gate.lock" mrga1 claude/x pending
report 1 "$(gate_who_is_gating "$TELL" | grep -c "gating claude/x on base pending")" "base pending is a real state, not a malformed line"
gate_tell_write "$TELL/mrga1/adp-gate.lock" mrga1 claude/x abc1234
report 1 "$(gate_who_is_gating "$TELL" | grep -c "on base abc1234")" "the rewrite names the base a develop commit would stale"
# Asserting that the FIELD IS PRESENT printed the identical 1 whether the expiry was two hours
# ahead or already past - green on the working path and on the fail-open one alike. So the value
# is parsed and compared: later than `started`, or the literal `unknown`. The first draft fell
# back to NOW when `date -d` was unavailable, which birthed every line expired.
# The comparison below is a STRING comparison, and it is chronological only because both timestamps
# are fixed-width UTC in one format. That is load-bearing: add a timezone offset, drop the `Z`, move
# to local time, or move to any format with a variable-width field, and `\>` keeps returning a
# boolean, keeps passing, and stops meaning what it says. Named here because a format change would
# otherwise break it silently - the invariant nobody wrote down. (Developer 3, which read the guard
# rather than trusting that it was the one it had asked for.)
TELL_LINE=$(gate_who_is_gating "$TELL" | sed -n 1p)
TELL_STARTED=${TELL_LINE##*started }; TELL_STARTED=${TELL_STARTED%% *}
TELL_IGNORE=${TELL_LINE##*ignore-after }; TELL_IGNORE=${TELL_IGNORE%% *}
report later "$([ "$TELL_IGNORE" = unknown ] && echo unknown || { [ "$TELL_IGNORE" \> "$TELL_STARTED" ] && echo later || echo "NOT-LATER:$TELL_IGNORE"; })" "the expiry is later than the start, or says unknown - never a time already past"
# A line already past its expiry is labelled, so the last judgement leaves the human in a hurry.
mkdir -p "$TELL/mrgc3/adp-gate.lock"
printf "mrgc3 gating claude/old on base aaa1111 started 2020-01-01T00:00:00Z ignore-after 2020-01-01T02:00:00Z\n" > "$TELL/mrgc3/adp-gate.lock/owner"
report 1 "$(gate_who_is_gating "$TELL" | grep -c "(EXPIRED - ask, do not assume)")" "a line past its expiry is labelled, and labelled as ASK rather than as clear"
printf "mrgc3 gating claude/old on base aaa1111 started 2020-01-01T00:00:00Z ignore-after unknown\n" > "$TELL/mrgc3/adp-gate.lock/owner"
report 1 "$(gate_who_is_gating "$TELL" | grep -c "expiry unknown - treat as live")" "an uncomputable expiry reads as LIVE, because the fail-open direction is the unrecoverable one"
report 0 "$(gate_who_is_gating "$TELL" | grep -c "EXPIRED")" "... and is never labelled expired"
rm -rf "$TELL/mrgc3"
mkdir -p "$TELL/mrgb2/adp-gate.lock"; gate_tell_write "$TELL/mrgb2/adp-gate.lock" mrgb2 claude/y def5678
report 2 "$(gate_who_is_gating "$TELL" | grep -c "gating claude/")" "two scratch trees gating at once are two lines"
printf "stale junk\n" > "$TELL/mrga1/adp-gate.lock/owner"
gate_tell_write "$TELL/mrga1/adp-gate.lock" mrga1 claude/z 9999999
report 0 "$(gate_who_is_gating "$TELL" | grep -c "stale junk")" "a killed run leaves a line, and the next run in that tree overwrites it"
report 0 "$(gate_tell_write "" x y z; echo $?)" "a write with no lock is a no-op, never an error - it may not red a gate"
report 0 "$(gate_tell_write "$TELL/absent/adp-gate.lock" x y z; echo $?)" "... and neither is a write to a lock that is gone"

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
KEPT2=$(printf '%s
' "$out" | sed -n 's/^KEPT_LOGS=//p')
report yes "$([ -n "$KEPT2" ] && [ -d "$KEPT2" ] && echo yes || echo no)" "... the refused run's logs are kept, and the run says where"
report yes "$([ -f "$KEPT2/merge.log" ] && echo yes || echo no)" "... with that run's own logs inside them"
report none "$(printf '%s\n' "$out" | sed -n 's/^UNDELETED_FOLDERS=//p')" "... and it reports the test folders it could not delete, here none"
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

echo "== retire.sh, in a throwaway repository"
link_dir() { # <link> <target> - a directory link the way npm makes one here: a junction on Windows
  if [ "$MSYS" = 1 ]; then
    MSYS_NO_PATHCONV=1 cmd /c mklink /J "$(cygpath -w "$1")" "$(cygpath -w "$2")" > /dev/null
  else
    ln -s "$2" "$1"
  fi
}
R3="$W/repo3"
mkrepo "$R3" || broken "cannot create the third repository"
mkdir -p "$R3/.github/tools/gate" && cp "$HERE/gate-lib.sh" "$HERE/retire.sh" "$R3/.github/tools/gate/"
printf 'node_modules/\nobj/\n' > "$R3/.gitignore"
mkdir -p "$R3/src/client/pkg" && echo client > "$R3/src/client/pkg/x.txt"
# A generated file with a CRLF checkout, as src/client/src/generated/*_pb.ts are.
printf '*.ts text eol=crlf\n' > "$R3/.gitattributes" && printf 'export const a = 1;\n' > "$R3/src/client/gen_pb.ts"
# A real module path from this repository: the one whose build output passed 260 characters.
MOD="src/diagrams/diagrams-python-cloud-infrastructure/backend/EtAlii.Adp.Diagram.DiagramsPythonCloudInfrastructure"
mkdir -p "$R3/$MOD" && echo '<Project />' > "$R3/$MOD/EtAlii.Adp.Diagram.DiagramsPythonCloudInfrastructure.csproj"
git -C "$R3" add .github .gitignore .gitattributes src 2> /dev/null && git -C "$R3" commit -q -m "the tools and a workspace package" || broken "cannot commit retire.sh"
RS="$R3/.github/tools/gate/retire.sh"
WT3="$R3/.claude/worktrees"
mkdir -p "$WT3"
retire() { (cd "$W" && bash "$RS" "$@" 2>&1); }
new_wt() { # <name> - a landed worktree on its own branch; its ignored node_modules links to its own package, as npm's do
  git -C "$R3" worktree add -q -b "b-$1" "$WT3/$1" develop && mkdir -p "$WT3/$1/src/node_modules" &&
    link_dir "$WT3/$1/src/node_modules/pkg" "$WT3/$1/src/client/pkg"
}
new_wt ok1 || broken "cannot build a worktree to retire"
[ -n "$(find "$WT3/ok1/src/node_modules" -type l)" ] || broken "the planted node_modules link is not seen as a link"
[ -z "$(git -C "$WT3/ok1" status --porcelain)" ] || broken "the planted node_modules is not ignored"
DEV3=$(git -C "$R3" rev-parse develop)

out=$(retire)
report "1:missing-arguments" "$?:$(result_of "$out")" "retire.sh without a name"
out=$(retire ../ok1)
report "1:bad-name" "$?:$(result_of "$out")" "a name that climbs out of .claude/worktrees"
out=$(retire nothing1)
report "1:no-such-directory" "$?:$(result_of "$out")" "a name with no directory"
mkdir -p "$WT3/husk3/src" && echo source > "$WT3/husk3/src/f.txt"
out=$(retire husk3)
report "1:target-is-a-husk" "$?:$(result_of "$out")" "a husk is refused, not deleted"
report yes "$([ -f "$WT3/husk3/src/f.txt" ] && echo yes || echo no)" "... and its files are still there"
new_wt dirty1 || broken "cannot build the dirty worktree"
echo change > "$WT3/dirty1/src/client/pkg/x.txt"
out=$(retire dirty1)
report "1:uncommitted-changes" "$?:$(result_of "$out")" "a worktree with uncommitted work is refused"
# retire.sh's first real use refused a clean scratch tree over exactly this: generated files the
# client tests rewrote with LF over a CRLF checkout - modified to git status, unchanged in content.
new_wt ph1 || broken "cannot build the line-ending worktree"
[ "$(tr -cd '\r' < "$WT3/ph1/src/client/gen_pb.ts" | wc -c)" -gt 0 ] || broken "the generated file was not checked out CRLF"
sleep 1
printf 'export const a = 1;\n' > "$WT3/ph1/src/client/gen_pb.ts"
[ -n "$(git -C "$WT3/ph1" status --porcelain 2> /dev/null)" ] || broken "status does not report the LF rewrite, so the case would test nothing"
out=$(retire ph1)
report "0:retired" "$?:$(result_of "$out")" "a worktree whose generated files were only rewritten with other line endings is retired"
new_wt un1 || broken "cannot build the untracked-file worktree"
echo "a note nobody committed" > "$WT3/un1/notes.txt"
out=$(retire un1)
report "1:untracked-files" "$?:$(result_of "$out")" "a worktree with an untracked, unignored file is refused"
report yes "$([ -f "$WT3/un1/notes.txt" ] && echo yes || echo no)" "... and the file is still there"
new_wt ahead1 || broken "cannot build the unlanded worktree"
echo work > "$WT3/ahead1/w.txt" && git -C "$WT3/ahead1" add w.txt && git -C "$WT3/ahead1" commit -q -m "unlanded work"
out=$(retire ahead1)
report "1:unmatched-commits" "$?:$(result_of "$out")" "a worktree with commits not on develop is refused"
report yes "$(printf '%s\n' "$out" | grep -q "unlanded work" && echo yes || echo no)" "... naming each commit with its subject, not a bare sha"
new_wt out1 || broken "cannot build the outside-link worktree"
mkdir -p "$W/outside" && echo "must survive" > "$W/outside/sentinel.txt"
link_dir "$WT3/out1/src/node_modules/away" "$W/outside"
[ -f "$WT3/out1/src/node_modules/away/sentinel.txt" ] || broken "the outside link does not reach the sentinel, so it tests nothing"
out=$(retire out1)
report "1:link-outside" "$?:$(result_of "$out")" "a worktree with a link out of itself is refused"
report yes "$([ -f "$W/outside/sentinel.txt" ] && [ -d "$WT3/out1" ] && echo yes || echo no)" "... and nothing was deleted, inside or out"
out=$(cd "$WT3/ok1/src" && bash "$RS" ok1 2>&1)
report "1:cwd-inside-target" "$?:$(result_of "$out")" "retiring the worktree your shell is in is refused"
out=$(retire ok1)
report "0:retired" "$?:$(result_of "$out")" "a landed, clean worktree is retired"
report no "$([ -e "$WT3/ok1" ] && echo yes || echo no)" "... its directory is gone"
report no "$(git -C "$R3" worktree list --porcelain | grep -q '/ok1$' && echo yes || echo no)" "... it is no longer registered"
report yes "$(git -C "$R3" rev-parse -q --verify b-ok1 > /dev/null && echo yes || echo no)" "... and its branch was kept"
report "$DEV3" "$(git -C "$R3" rev-parse develop)" "... and develop did not move"

# A conflict-resolved cherry-pick: the CONTENT is on develop, and its patch id can never match, so
# `git cherry` reports it forever. The refusal must stay - from inside the script this is
# indistinguishable from work nobody landed - but it must not claim the commit is unlanded.
# Developer 1's csel case, 2026-09-20. Every precondition is asserted, because a pick that did not
# conflict, or content that did not reach develop, would leave this case testing nothing.
printf 'one\n' > "$R3/c.txt" && git -C "$R3" add c.txt && git -C "$R3" commit -q -m "a file both sides will touch" ||
  broken "cannot plant the shared file"
new_wt pick1 || broken "cannot build the cherry-picked worktree"
printf 'one\nfrom-the-branch\n' > "$WT3/pick1/c.txt" &&
  git -C "$WT3/pick1" commit -q -am "the work that was cherry-picked" || broken "cannot commit the branch's work"
PICKED=$(git -C "$WT3/pick1" rev-parse HEAD)
printf 'one\nfrom-develop\n' > "$R3/c.txt" && git -C "$R3" commit -q -am "a conflicting change on develop" ||
  broken "cannot commit the conflicting change"
git -C "$R3" cherry-pick "$PICKED" > /dev/null 2>&1 && broken "the planted cherry-pick did not conflict, so the case would test nothing"
printf 'one\nfrom-the-branch\n' > "$R3/c.txt" && git -C "$R3" add c.txt &&
  git -C "$R3" -c core.editor=true cherry-pick --continue > /dev/null 2>&1 || broken "cannot resolve the planted cherry-pick"
[ "$(cat "$R3/c.txt")" = "$(cat "$WT3/pick1/c.txt")" ] || broken "the resolved pick did not put the branch's content on develop"
git -C "$R3" cherry develop "$PICKED" | grep -q '^+' || broken "the resolved pick matched by patch id, so the case would test nothing"
out=$(retire pick1)
report "1:unmatched-commits" "$?:$(result_of "$out")" "work that landed through a conflict-resolved cherry-pick is refused, and never called unlanded"
report yes "$(printf '%s\n' "$out" | grep -q "the work that was cherry-picked" && echo yes || echo no)" "... with the commit's subject, so a reader can check the content"
report yes "$(printf '%s\n' "$out" | grep -q "check the content on develop" && echo yes || echo no)" "... and told to check content on develop rather than messages"
report yes "$([ -d "$WT3/pick1" ] && echo yes || echo no)" "... and nothing was deleted"
if [ "$MSYS" = 1 ]; then
  # The failure this script exists for: node_modules nested past Windows' 260-character limit.
  deep() { # <worktree> - bury a file past MAX_PATH inside its node_modules
    local d="$1/src/node_modules" i=0
    while [ "$(printf '%s' "$(cygpath -w "$d")" | wc -c)" -lt 300 ]; do d="$d/a-deeply-nested-package-$i"; i=$((i + 1)); done
    mkdir -p "$d" && echo deep > "$d/index.js"
  }
  new_wt long1 && deep "$WT3/long1" || broken "cannot build the long-path worktree"
  new_wt long2 && deep "$WT3/long2" || broken "cannot build the long-path control"
  git -C "$R3" worktree remove "$WT3/long2" > /dev/null 2>&1
  [ -e "$WT3/long2" ] || broken "plain git worktree remove cleared a >260-character tree, so the long-path case would test nothing"
  report no "$(git -C "$R3" worktree list --porcelain | grep -q '/long2$' && echo yes || echo no)" "control: plain git worktree remove deregistered the long-path tree ..."
  report yes "$([ -e "$WT3/long2" ] && echo yes || echo no)" "... and left its directory behind - a husk"
  rm -rf "$WT3/long2"
  out=$(retire long1)
  report "0:retired" "$?:$(result_of "$out")" "retire.sh retires the same long-path tree"
  report no "$([ -e "$WT3/long1" ] && echo yes || echo no)" "... and leaves no husk"
  # The failure that happened for real, twice, after the first version shipped: no node_modules
  # at all, the long path in dotnet's ignored obj/ under a real module - the file a build leaves.
  build_output() { # <worktree> - the obj/ file a dotnet build writes under that module
    local f="$1/$MOD/obj/Debug/net10.0/EtAlii.Adp.Diagram.DiagramsPythonCloudInfrastructure.GeneratedMSBuildEditorConfig.editorconfig"
    mkdir -p "$(dirname "$f")" && echo "is_global = true" > "$f" &&
      [ "$(printf '%s' "$(cygpath -w "$f")" | wc -c)" -gt 260 ]
  }
  git -C "$R3" worktree add -q -b b-obj1 "$WT3/obj1" develop && build_output "$WT3/obj1" || broken "cannot build the obj/ long-path worktree past 260 characters"
  git -C "$R3" worktree add -q -b b-obj2 "$WT3/obj2" develop && build_output "$WT3/obj2" || broken "cannot build the obj/ long-path control"
  [ -z "$(git -C "$WT3/obj1" status --porcelain)" ] || broken "the planted obj/ output is not ignored"
  git -C "$R3" worktree remove "$WT3/obj2" > /dev/null 2>&1
  [ -e "$WT3/obj2" ] || broken "plain git worktree remove cleared the obj/ tree, so the case would test nothing"
  report yes "$([ -e "$WT3/obj2" ] && [ ! -e "$WT3/obj2/.git" ] && echo yes || echo no)" "control: plain git worktree remove on a build's obj/ tree leaves a husk, as it did for real"
  rm -rf "$WT3/obj2"
  out=$(retire obj1)
  report "0:retired" "$?:$(result_of "$out")" "retire.sh retires the same obj/ tree"
  report no "$([ -e "$WT3/obj1" ] && echo yes || echo no)" "... and leaves no husk"
  report no "$(git -C "$R3" worktree list --porcelain | grep -q '/obj1$' && echo yes || echo no)" "... and nothing is left registered"
else
  # A delete that stops early must leave a worktree of its own, never a husk. On Linux a directory
  # without write permission is enough to stop rm; Windows has no equally cheap way to hold a file.
  new_wt stuck1 || broken "cannot build the stuck worktree"
  mkdir -p "$WT3/stuck1/src/held" && echo held > "$WT3/stuck1/src/held/f.txt" && git -C "$WT3/stuck1" add src/held/f.txt &&
    git -C "$WT3/stuck1" commit -q -m held && git -C "$R3" merge -q --ff-only b-stuck1 || broken "cannot land the stuck worktree's commit"
  chmod 555 "$WT3/stuck1/src/held"
  if touch "$WT3/stuck1/src/held/probe" 2> /dev/null; then
    chmod 755 "$WT3/stuck1/src/held"
    broken "a read-only directory is still writable here (running as root?), so the stopped-delete case would test nothing"
  fi
  out=$(retire stuck1)
  report "1:could-not-empty-worktree" "$?:$(result_of "$out")" "a delete that stops early is reported"
  report yes "$([ -f "$WT3/stuck1/.git" ] && [ "$(git -C "$WT3/stuck1" rev-parse --show-toplevel 2> /dev/null)" = "$WT3/stuck1" ] && echo yes || echo no)" "... and leaves a worktree of its own, not a husk"
  chmod 755 "$WT3/stuck1/src/held"
fi

echo "SELFTEST cases=$N wrong=$WRONG expected=$EXPECTED"
if [ "$N" = "$EXPECTED" ] && [ "$WRONG" = 0 ]; then
  echo "RESULT=selftest-green"
  exit 0
fi
echo "RESULT=selftest-red"
exit 1
