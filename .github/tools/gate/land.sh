#!/bin/bash
# Lands what gate.sh gated: fast-forwards develop, in the main checkout, to exactly the merged
# commit the gates judged - and refuses anything else. gate.sh prints this command as its last
# line; run it yourself, in the foreground, straight after.
#
#   bash C:/git/EtAlii.Adp/.github/tools/gate/land.sh <base> <merged>
#
#   base     the develop commit the merge was made onto (gate.sh's GATED_ON_BASE)
#   merged   the merged commit the gates ran on (gate.sh's MERGED)
#
# It is separate from gate.sh on purpose. A fail-closed --ff-only refuses a develop that MOVED,
# but lands on one whose window is merely open; chaining the fast-forward onto the gates caused
# a breach that way. So the gates stop, and landing is a second, deliberate step that reads
# develop fresh at the moment it runs.
#
# Refuses: a copy that is not develop's; a main checkout not on develop (a fast-forward moves
# whatever branch is checked out there); a develop that is no longer the base the gates merged
# onto; a merged commit that does not descend from that base; and a fast-forward git refuses.
# Ends in one RESULT= line; only RESULT=landed is a landing.
#
# IT WARNS ABOUT A DIRTY SHARED INDEX AND DOES NOT REFUSE ON ONE, deliberately, and the next author
# will be tempted to make it refuse because that looks obviously safer. It is not. Three things have
# to be held at once:
#
# ONE: A FAST-FORWARD COMMITS NOTHING. Staged entries in the main checkout do not enter develop's
# tip; they survive the landing as staged entries against the new base.
#
#   THE PROOF, and it is a measured fact rather than a deduction: with an unrelated path staged, the
#   landing succeeds, the staged entry survives with its content, and `git show develop:<that path>`
#   finds NOTHING. The file is in the index and nowhere in develop's tip. That case is in the suite
#   below, and `absent` is its PASS - see "which is why a landing cannot carry another session's work
#   into develop".
#
# So the cost of a dirty index here is entirely DOWNSTREAM - the next session's bare `git commit`
# sweeping those paths under its own message, which is how three commits were misattributed in one
# day. Three sessions reached that conclusion by reasoning before anybody measured it, and an earlier
# draft of this very file said the opposite ("carried along in your landing"), so it is written as an
# assertion with its evidence attached rather than as something obvious.
#
# TWO: GIT REFUSES ON A COLLISION, NOT ON A DIRTY INDEX. A local change to a path the fast-forward
# does not touch lets it through; one to a path it does touch stops it, loudly, changing nothing.
# The board's own hazard is verdict snapshots under `.spec-workflow/approvals/`, and a branch landing
# client or backend work never touches those paths - so git has no reason to object and does not.
# The half git structurally cannot see is exactly the half that has cost this board anything.
#
# THREE, AND THIS IS THE ONE THAT SETTLES IT - THERE IS NO BAND IN WHICH A REFUSAL ADDS VALUE.
# Split the cases by whether the staged paths collide with what the fast-forward touches, because
# those are the only two cases there are:
#
#   they collide      -> git ALREADY refuses, loudly, naming the files. A refusal here is REDUNDANT.
#   they do not       -> the fast-forward is safe and the entries survive intact. A refusal here
#                        blocks a SAFE operation, and the only way to clear it is a commit, which
#                        moves develop and stales the gate run already paid for. Pure deadlock.
#
# So it is redundant exactly where the hazard is real and deadlocking exactly where it is absent.
# That is not a cost argument and must not be written as one: a future author meeting a cost argument
# reaches for the refusal again the first time a cost changes, and there is no version of it worth
# tuning. THE DIAGNOSIS IS NOT THE CHEAPER OPTION, IT IS THE ONLY ONE. (Developer 3's argument,
# relayed and kept in its own shape because it is stronger than the two it replaced.)
#
# `CLAUDE.md` corroborates independently: its spec-workflow section rules that ownerless staged
# snapshots are committed BEFORE a gate and again AFTER a landing, on the stated ground that *a
# fast-forward writes no commit and cannot be invalidated by ownerless files in the index, so
# committing them first at the landing moves develop and invalidates the landing you were about to
# run*. A tool quietly contradicting the document every session reads is worse than either being
# wrong, because the two never meet in one reader's head.
#
# WHAT THE DIAGNOSIS IS FOR, and it is the half that has actually cost work. `RESULT=ff-refused` used
# to be a bare string: it did not say the index was the cause, did not say develop had NOT moved, and
# did not say the same line was re-runnable. The shared notes record the lost work surviving as a
# dangling commit PAIR - which is a stash. So the eater was never the merge; it was an operator
# reaching past an unreadable refusal for `git stash`, which detaches whatever another session had in
# flight. A LOUD REFUSAL THAT DOES NOT NAME ITS CAUSE IS AS DANGEROUS AS A SILENT ONE, because it
# sends a competent reader looking for a remedy and the remedy they reach for is the one that loses
# the data.
set -u
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY

if [ $# -ne 2 ] || [ -z "${1:-}" ] || [ -z "${2:-}" ]; then
  echo "usage: land.sh <base> <merged>"
  echo "RESULT=missing-arguments"
  exit 1
fi

HERE=$(cd "$(dirname "$0")" && pwd) || { echo "RESULT=aborted-cannot-locate-script"; exit 3; }
. "$HERE/gate-lib.sh" 2>/dev/null
if ! type gate_main_checkout gate_is_develops gate_blocking_paths gate_awaiting_land_clear > /dev/null 2>&1; then
  echo "RESULT=aborted-library-missing ($HERE/gate-lib.sh)"
  exit 3
fi
MAIN=$(gate_main_checkout "$HERE") || { echo "RESULT=aborted-no-main-checkout (from $HERE)"; exit 3; }
if ! gate_is_develops "$MAIN" "$HERE" land.sh gate-lib.sh; then
  echo "RESULT=aborted-not-develops-gate ($GATE_DIFFERS differs from develop's copy, or develop has none)"
  exit 3
fi

BASE=$(git -C "$MAIN" rev-parse --verify -q "$1^{commit}") || { echo "RESULT=unknown-base ($1)"; exit 1; }
MERGED=$(git -C "$MAIN" rev-parse --verify -q "$2^{commit}") || { echo "RESULT=unknown-merged-commit ($2)"; exit 1; }

# THE GREEN GATE'S CLAIM ON DEVELOP (gate_awaiting_land_write) IS RELEASED ONLY WHEN THIS LANDING IS OVER
# FOR GOOD - landed, or refused in a way no retry of this same line can fix: develop moved, the commit
# does not descend from the base, or develop ended up somewhere other than the gated commit.
#
# IT IS KEPT ON A RETRYABLE REFUSAL, deliberately, and that is the half a plain "release on any refusal"
# gets wrong. ff-refused tells its operator that develop has NOT moved and to re-run this same line;
# main-checkout-not-on-develop is cured by switching back and re-running. Releasing the claim on either
# would open the board under a landing that is about to be retried - somebody commits in the gap, and
# the retry fails develop-moved-regate: the exact loss this claim exists to prevent. The unknown-* and
# aborted-* refusals keep it too, since they cannot even name the commit whose claim they would release.
COMMON=$(git -C "$MAIN" rev-parse --path-format=absolute --git-common-dir 2> /dev/null || true)
release() {
  echo "AWAITING_LAND_RELEASED=$(gate_awaiting_land_clear "$COMMON/worktrees" "$MERGED")"
}
keep() {
  echo "AWAITING_LAND=still-held (this refusal is retryable - the board stays yours until this same line lands, or is refused for good)"
}

ON=$(git -C "$MAIN" symbolic-ref -q --short HEAD || true)
if [ "$ON" != develop ]; then
  keep
  echo "RESULT=main-checkout-not-on-develop (it is on '${ON:-a detached HEAD}' - a fast-forward would move that instead)"
  exit 1
fi
NOW=$(git -C "$MAIN" rev-parse --verify -q develop || true)
if [ -z "$NOW" ] || [ "$NOW" != "$BASE" ]; then
  release
  echo "RESULT=develop-moved-regate (develop is ${NOW:-unreadable}; the gates merged onto $BASE and never saw what arrived since)"
  exit 1
fi
if ! git -C "$MAIN" merge-base --is-ancestor "$BASE" "$MERGED"; then
  release
  echo "RESULT=not-a-descendant ($MERGED does not descend from $BASE)"
  exit 1
fi
# Read the shared index BEFORE the merge. Afterwards "what blocked it" is no longer answerable, and
# after a SUCCESSFUL landing the staged paths are the ones somebody now owes a commit for.
STAGED=$(git -C "$MAIN" diff --cached --name-only 2> /dev/null || true)
LOCALLY_CHANGED=$(git -C "$MAIN" diff --name-only HEAD 2> /dev/null || true)
FF_TOUCHES=$(git -C "$MAIN" diff --name-only "$BASE" "$MERGED" 2> /dev/null || true)
if [ -n "$STAGED" ]; then
  echo "STAGED_IN_MAIN_CHECKOUT=$(printf '%s' "$STAGED" | tr '\n' ' ')"
  echo "STAGED_NOTE=<these do NOT enter develop's tip - a fast-forward commits nothing - and they survive this landing as staged entries against the new base. Commit precisely these paths, in their own commit, straight after this lands, whether or not they are yours. The hazard is the next bare git commit sweeping them under somebody else's message.>"
fi
if ! git -C "$MAIN" merge --ff-only "$MERGED"; then
  BLOCKING=$(gate_blocking_paths "$FF_TOUCHES" "$LOCALLY_CHANGED")
  if [ -n "$BLOCKING" ]; then
    echo "BLOCKED_BY=$(printf '%s' "$BLOCKING" | tr '\n' ' ')"
    echo "REMEDY=<develop has NOT moved and $MERGED is still landable. Commit those paths, then re-run this same line. Do NOT stash: a stash here detaches whatever another session had in flight, which is how this board lost work before. Do NOT re-gate: nothing is stale, so there is nothing to refold.>"
  else
    echo "BLOCKED_BY=<nothing: no locally changed path is one this fast-forward would update, so the index is not the cause. Read git's own message above - this is not the case the diagnosis covers.>"
  fi
  keep
  echo "RESULT=ff-refused"
  exit 1
fi
AFTER=$(git -C "$MAIN" rev-parse --verify -q develop || true)
if [ "$AFTER" != "$MERGED" ]; then
  release
  echo "RESULT=develop-is-not-the-gated-commit (develop is ${AFTER:-unreadable}, gated $MERGED)"
  exit 1
fi
release
echo "RESULT=landed ($MERGED)"
