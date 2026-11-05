#!/bin/bash
# gate.test.sh - the tests of the scripts beside it that still apply: retire.sh and
# who-is-gating.sh, and the parts of gate-lib.sh they use. The Build workflow runs it in a job of
# its own, `gate-scripts`; it is not part of the product test suite (etalii.adp spec 025, FR-096):
#
#   bash .github/tools/gate/gate.test.sh
#
# gate.sh and land.sh are not tested here. They are the local-merge process CLAUDE.md says no
# longer applies - delivery is a pull request - and their cases, with the dotnet logs that were
# their fixtures, were deleted with GateScriptTests, which ran this script inside `dotnet test`.
#
# Hermetic. Everything it changes is built in a throwaway repository under a temp directory, with
# its own git configuration; the real repository is only READ - the scripts under test.
#
# Every defect a script exists to refuse is a case here, beside a case that MUST PASS through the
# same capture - a check seen only to refuse proves nothing, since a harness that never ran refuses
# too. Preconditions are asserted, not assumed: a planted husk that does not resolve to its main
# checkout stops the run as broken rather than letting a case report on a question it never asked.
#
# Ends in RESULT=selftest-green only when every case ran - the count must equal EXPECTED, so a
# case that silently stops running is a failure too - and none was wrong.
#
# Run it as a user other than root: one retire.sh case needs a read-only directory to stop a
# delete, and root writes into one anyway, so as root the run stops as broken rather than passing.
set -u
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
. "$HERE/gate-lib.sh"

case "$(uname -s)" in MINGW* | MSYS* | CYGWIN*) MSYS=1 ;; *) MSYS=0 ;; esac
# --quick runs everything except the throwaway-repository sections at the end, which are what
# makes the full suite expensive: they build real repositories, worktrees and, on Windows, MAX_PATH
# probes. Quick keeps the tree check, the tell, and the argument handling.
QUICK=0
case "${1:-}" in
  "") ;;
  --quick) QUICK=1 ;;
  *)
    echo "RESULT=selftest-broken (unrecognised argument '$1'; expected --quick or nothing)"
    exit 2
    ;;
esac
[ $# -le 1 ] || { echo "RESULT=selftest-broken (expected one argument at most; got $#)"; exit 2; }
# The counts are PINNED PER MODE. On Linux both were MEASURED on 2026-10-31, as a user other than
# root: 57 in full and 32 in quick, after who-owns.sh left with spec-workflow (27 cases, 10 of them
# in quick). The msys numbers are DERIVED, not measured: the 205 and 130 measured on Git Bash before
# the gate.sh and land.sh cases left, less those cases (115 on msys, four of them msys-only in the
# guard section, so 88 in quick), less the who-owns cases. A wrong pin fails LOUDLY with the true
# count in the same line, so the first run on Git Bash corrects it rather than passing quietly.
if [ "$MSYS" = 1 ]; then EXPECTED=63; EXPECTED_QUICK=32; else EXPECTED=57; EXPECTED_QUICK=32; fi
[ "$QUICK" = 1 ] && EXPECTED=$EXPECTED_QUICK

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
result_of() { printf '%s\n' "$1" | sed -n 's/^RESULT=\([a-z-]*\).*/\1/p' | tail -1; }

echo "== the tree check retire.sh makes compares content, not what status reports"
# The shared gate's first real run: four gates green, then refused, because the client's pretest
# rewrote generated files with LF over a CRLF checkout - `git status` said modified, `git diff`
# found nothing. retire.sh asks the same question before it deletes a worktree. Planted here
# exactly: same content, other line endings.
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

echo "== --require-free: an exit code that means FREE, rather than one that means READ"
# The default is fail-open BY DESIGN - it exits 0 for a holder as well as for a free board, because
# printing the board is this tool every caller wants. Half the cases below pin that zero, so a later
# change to either half cannot quietly turn a chain over this tool from a check back into a print.
RF=$(mktemp -d)/worktrees
gate_who_is_gating "$RF" --require-free > /dev/null; report 2 "$?" "--require-free on an unreadable board exits 2 - a board it cannot parse fails rather than passes"
mkdir -p "$RF"
gate_who_is_gating "$RF" --require-free > /dev/null; report 0 "$?" "--require-free exits 0 on a free board, which is the only state that may produce a zero"
mkdir -p "$RF/mrgf1/adp-gate.lock"
gate_who_is_gating "$RF" --require-free > /dev/null; report 1 "$?" "a lock whose owner is not written yet is NOT free"
gate_who_is_gating "$RF" > /dev/null; report 0 "$?" "... while the default still exits 0 for it, which is the fail-open the flag exists for"
gate_tell_write "$RF/mrgf1/adp-gate.lock" mrgf1 claude/rf abc1234
gate_who_is_gating "$RF" --require-free > /dev/null; report 1 "$?" "a live holder is not free"
gate_who_is_gating "$RF" > /dev/null; report 0 "$?" "... and the default zero for a live holder is unchanged, which is what makes every && chain over it fail-open"
printf "mrgf1 gating claude/rf on base abc1234 started 2020-01-01T00:00:00Z ignore-after 2020-01-01T02:00:00Z\n" > "$RF/mrgf1/adp-gate.lock/owner"
gate_who_is_gating "$RF" --require-free > /dev/null; report 1 "$?" "an EXPIRED line is not free: it prints ASK, and ASK is not a licence"
printf "mrgf1 gating claude/rf on base abc1234 started 2020-01-01T00:00:00Z ignore-after unknown\n" > "$RF/mrgf1/adp-gate.lock/owner"
gate_who_is_gating "$RF" --require-free > /dev/null; report 1 "$?" "an unknown expiry is not free either, for the same reason"
# The script's own argument handling, which nothing else covers. Both refuse before the board is read,
# so neither depends on what the real board is doing while this suite runs.
bash "$HERE/who-is-gating.sh" --requirefree > /dev/null 2>&1; report 3 "$?" "a MISTYPED flag is refused with the refusal code, not with the unreadable one"
bash "$HERE/who-is-gating.sh" --require-free extra > /dev/null 2>&1; report 3 "$?" "a surplus argument is refused too - the caller asked for something else than what would have run"
# The probe requires the one code no FAILURE can produce, so refusal and unreadable must differ. They
# shared 2 until 2026-09-24, and while they did the probe passed against exactly the copy it detects.
REFUSED=$(bash "$HERE/who-is-gating.sh" --typo-for-the-probe > /dev/null 2>&1; echo $?)
report distinct "$([ "$REFUSED" != 2 ] && echo distinct || echo SHARED-WITH-UNREADABLE)" "refusal does not share the unreadable code, which is what lets a probe require it"

# Everything from here to the summary builds throwaway repositories, and it is the whole of the
# difference between --quick and a full run. The body below is deliberately NOT re-indented: bash
# does not care, and re-indenting it would hide a two-line change inside a 240-line diff.
if [ "$QUICK" != 1 ]; then
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

fi  # end of the throwaway-repository sections, skipped by --quick

echo "SELFTEST cases=$N wrong=$WRONG expected=$EXPECTED mode=$([ "$QUICK" = 1 ] && echo quick || echo full)"
if [ "$N" = "$EXPECTED" ] && [ "$WRONG" = 0 ]; then
  echo "RESULT=selftest-$([ "$QUICK" = 1 ] && echo quick-green || echo green)"
  exit 0
fi
echo "RESULT=selftest-$([ "$QUICK" = 1 ] && echo quick-red || echo red)"
exit 1
