#!/bin/bash
# The shared gate. Merges a branch onto develop's tip in YOUR scratch worktree, runs the four gates
# on that merged tree, and STOPS: it never moves develop. On success its last line is the land.sh
# command that fast-forwards develop to exactly the commit that was gated - run it yourself, in
# the foreground.
#
#   bash C:/git/EtAlii.Adp/.github/tools/gate/gate.sh <scratch> <branch> <identity> "<message>"
#
#   scratch   your own scratch worktree's name, mrg<N> - created detached if absent, never shared:
#             a second chain in the same tree would reset the first one's merge under it
#   branch    the branch to gate (develop itself is a valid no-op, and a useful positive control)
#   identity  the per-task identity the merge commit is signed with
#   message   the merge commit's message
#
# All four are required and none has a default: the scripts this replaces inherited branch and
# message by being sed-edited from the previous run, and landed a wrong message that way.
#
# Every run ends in one RESULT= line. Only RESULT=gates-green-ff-withheld is a pass.
#
# What it refuses, and why (processes.md, "Committing and merging in the shared main checkout",
# carries the reasoning; this list is only the map):
#   - a copy that is not develop's committed gate: everybody runs the same one or it is not shared
#   - a scratch path the procedure's own guard does not verify - the guard is read from
#     develop:processes.md at run time, not copied, and a missing, duplicated or incomplete block
#     aborts rather than running unguarded
#   - a scratch tree another gate is using
#   - any gate that exits nonzero, and a dotnet log without positive evidence that tests ran
#   - gates that changed tracked files: they would have judged a tree that is not the commit
#   - a scratch tree that stopped being itself during the gates, and a develop that moved
set -u
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY

if [ $# -ne 4 ] || [ -z "${1:-}" ] || [ -z "${2:-}" ] || [ -z "${3:-}" ] || [ -z "${4:-}" ]; then
  echo "usage: gate.sh <scratch> <branch> <identity> \"<message>\""
  echo "RESULT=missing-arguments"
  exit 1
fi
SCRATCH_NAME=$1
BRANCH=$2
IDENT=$3
MSG=$4
case "$SCRATCH_NAME" in
  mrg[a-z0-9] | mrg[a-z0-9][a-z0-9] | mrg[a-z0-9][a-z0-9][a-z0-9]) ;;
  *)
    echo "RESULT=bad-scratch-name ('$SCRATCH_NAME': expected mrg<N>, e.g. mrga1 - your own, short)"
    exit 1
    ;;
esac

STAGE=setup
LOCK=""
TMPD=""
finish() {
  local rc=$?
  if [ -n "$LOCK" ]; then rm -rf "$LOCK"; fi
  if [ -n "$TMPD" ]; then rm -rf "$TMPD"; fi
  if [ "$STAGE" = guard ] && [ "$rc" -ne 0 ]; then echo "RESULT=aborted-by-guard"; fi
}
trap finish EXIT

HERE=$(cd "$(dirname "$0")" && pwd) || { echo "RESULT=aborted-cannot-locate-script"; exit 3; }
. "$HERE/gate-lib.sh" 2>/dev/null
if ! type extract_guard gate_main_checkout gate_is_develops gate_verdict > /dev/null 2>&1; then
  echo "RESULT=aborted-library-missing ($HERE/gate-lib.sh)"
  exit 3
fi
MAIN=$(gate_main_checkout "$HERE") || { echo "RESULT=aborted-no-main-checkout (from $HERE)"; exit 3; }
if ! gate_is_develops "$MAIN" "$HERE" gate.sh gate-lib.sh; then
  echo "RESULT=aborted-not-develops-gate ($GATE_DIFFERS differs from develop's copy, or develop has none - run the one in the main checkout: $MAIN/.github/tools/gate/gate.sh)"
  exit 3
fi
if ! git -C "$MAIN" rev-parse --verify -q "refs/heads/$BRANCH" > /dev/null; then
  echo "RESULT=no-such-branch ($BRANCH)"
  exit 1
fi
echo "BRANCH=$BRANCH"
echo "IDENTITY_REQUESTED=$IDENT"
TMPD=$(mktemp -d) || { echo "RESULT=aborted-no-temp-dir"; exit 3; }

# --- The scratch-worktree guard, as the procedure writes it. Taken from develop, not from the
# --- branch being gated, so a branch that edits the guard cannot weaken the guard that gates it.
git -C "$MAIN" show develop:.spec-workflow/steering/processes.md 2>/dev/null | extract_guard > "$TMPD/guard.sh"
GRC=("${PIPESTATUS[@]}")
if [ "${GRC[0]}" != 0 ]; then echo "RESULT=aborted-cannot-read-procedure (git show exit ${GRC[0]})"; exit 3; fi
if [ "${GRC[1]}" != 0 ]; then echo "RESULT=aborted-guard-not-intact (extract_guard exit ${GRC[1]}: 2 none, 3 several, 4 part missing)"; exit 3; fi
MRG="$MAIN/.claude/worktrees/$SCRATCH_NAME"
MRG_TOP=""
STAGE=guard
. "$TMPD/guard.sh"
STAGE=gates
if [ -z "$MRG_TOP" ]; then echo "RESULT=aborted-guard-established-nothing"; exit 3; fi
echo "SCRATCH_VERIFIED=$MRG_TOP"

# --- One gate per scratch tree. The lock lives in the tree's own git directory, not in a temp
# --- folder, so a temp clean-up cannot remove it while a gate is running.
GITDIR=$(git -C "$MRG" rev-parse --absolute-git-dir 2>/dev/null) || { echo "RESULT=aborted-no-scratch-git-dir"; exit 3; }
if ! mkdir "$GITDIR/adp-gate.lock" 2> /dev/null; then
  echo "RESULT=scratch-busy (another gate holds $GITDIR/adp-gate.lock - if none is running, remove that directory)"
  exit 1
fi
LOCK="$GITDIR/adp-gate.lock"
echo "pid $$ since $(date -u +%Y-%m-%dT%H:%M:%SZ) gating $BRANCH" > "$LOCK/owner"

if ! git -C "$MRG" config --worktree user.name "$IDENT"; then echo "RESULT=identity-not-set"; exit 1; fi
if [ "$(git -C "$MRG" config user.name)" != "$IDENT" ]; then echo "RESULT=identity-not-effective"; exit 1; fi
echo "IDENTITY_EFFECTIVE=$IDENT"

LOGS="$GITDIR/adp-gate-logs"
rm -rf "$LOGS"
mkdir -p "$LOGS" || { echo "RESULT=aborted-no-log-dir"; exit 3; }
BASE=$(git -C "$MAIN" rev-parse --verify -q develop) || { echo "RESULT=aborted-no-develop"; exit 3; }
echo "GATED_ON_BASE=$BASE"
git -C "$MRG" reset -q --hard "$BASE" || { echo "RESULT=reset-failed"; exit 1; }
if ! git -C "$MRG" merge --no-ff "$BRANCH" -m "$MSG" > "$LOGS/merge.log" 2>&1; then
  cat "$LOGS/merge.log"
  git -C "$MRG" merge --abort > /dev/null 2>&1
  echo "RESULT=merge-conflict"
  exit 1
fi
MERGED=$(git -C "$MRG" rev-parse --verify -q HEAD) || { echo "RESULT=aborted-no-merged-commit"; exit 3; }
echo "MERGED=$MERGED"

# --- The four gates (and the install they need), each status captured on its own line.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
(cd "$MRG/src" && npm install) > "$LOGS/npm-install.log" 2>&1
NPM_INSTALL_EXIT=$?
echo "NPM_INSTALL_EXIT=$NPM_INSTALL_EXIT"
(cd "$MRG/src/client" && npm test) > "$LOGS/npm-test.log" 2>&1
NPM_TEST_EXIT=$?
echo "NPM_TEST_EXIT=$NPM_TEST_EXIT"
(cd "$MRG/src/client" && npm run typecheck) > "$LOGS/typecheck.log" 2>&1
TC_EXIT=$?
echo "TYPECHECK_EXIT=$TC_EXIT"
(cd "$MRG/src/backend" && dotnet format style --verify-no-changes --severity info EtAlii.Adp.slnx) > "$LOGS/format.log" 2>&1
FMT_EXIT=$?
echo "FORMAT_EXIT=$FMT_EXIT"
(cd "$MRG/src/backend" && dotnet test --solution EtAlii.Adp.slnx) > "$LOGS/dotnet-test.log" 2>&1
DT_EXIT=$?
echo "DOTNET_TEST_EXIT=$DT_EXIT"
echo "LOGS=$LOGS"

gate_verdict "$LOGS/dotnet-test.log"
echo "DOTNET_TOTAL=${DT_TOTAL:-none} DOTNET_FAILED=${DT_FAILED:-none}"
if [ "$VERDICT" != green ]; then echo "RESULT=gates-red ($WHY )"; exit 1; fi

# --- The gates ran for minutes. Before vouching for what they judged, prove it is still the
# --- commit that will land, in the tree that was verified, on the develop it was merged onto.
CHANGED=$(git -C "$MRG" status --porcelain --untracked-files=no) || { echo "RESULT=cannot-read-scratch-status"; exit 1; }
if [ -n "$CHANGED" ]; then echo "$CHANGED"; echo "RESULT=gates-changed-tracked-files"; exit 1; fi
if [ "$(git -C "$MRG" rev-parse HEAD 2>/dev/null)" != "$MERGED" ]; then echo "RESULT=scratch-head-moved-during-gates"; exit 1; fi
NOW_TOP=$(git -C "$MRG" rev-parse --show-toplevel 2>/dev/null || true)
if [ -z "$NOW_TOP" ] || [ "$NOW_TOP" != "$MRG_TOP" ]; then
  echo "RESULT=scratch-changed-during-gates (was $MRG_TOP, now '${NOW_TOP:-nothing}')"
  exit 1
fi
NOW=$(git -C "$MAIN" rev-parse --verify -q develop || true)
if [ -z "$NOW" ] || [ "$NOW" != "$BASE" ]; then
  echo "DEVELOP_MOVED_DURING_GATES=${NOW:-unreadable} (gated on $BASE)"
  echo "RESULT=gates-green-but-base-stale-refold-needed"
  exit 1
fi
echo "RESULT=gates-green-ff-withheld"
echo "TO_LAND=bash \"$MAIN/.github/tools/gate/land.sh\" $BASE $MERGED"
