#!/bin/bash
# Is anybody on this specification? Run it before placing work on one.
#
#   bash C:/git/EtAlii.Adp/.github/tools/gate/who-owns.sh <spec-name>
#   bash C:/git/EtAlii.Adp/.github/tools/gate/who-owns.sh --all
#   bash C:/git/EtAlii.Adp/.github/tools/gate/who-owns.sh <spec-name> --require-unowned
#
# THE FAILURE IT EXISTS TO PREVENT. A Developer was placed on client-centralization while its owner
# was offline. The owner came back with complete, gated work and the carry branch had to be deleted -
# not abandoned, deleted. Nothing readable at the time said that specification had an owner: the
# session list showed an idle roster, and the tasks document showed unmarked boxes. AN IDLE SESSION
# IS NOT AN UNOWNED SPECIFICATION, AND AN UNMARKED TASK IS NOT AN UNSTARTED ONE. Both of those read
# as absence and neither is.
#
# THREE STATES, NEVER TWO. Silence is not one of them, and `unreadable` never collapses into `free`:
#
#   OWNER=<identity> EVIDENCE=<worktree|unlanded-branch|recent-authorship>   (exit 0)
#   OWNERSHIP=unowned REASON=every-task-marked                               (exit 0)
#   OWNERSHIP_UNREADABLE=<what could not be read>                            (exit 2)
#
# The evidence lines come first and the verdict last, so a reader can disagree with the verdict on
# the same output that produced it. `EVIDENCE=` says WHICH signal answered, and `LINK=` says whether
# a branch was tied to the specification by the paths it touches (a proof) or by its name (a hint).
#
# EXIT CODES, EACH MEANING ONE THING:
#
#   0  the question was answered - either an owner or a positively established `unowned`
#   1  --require-unowned only: it was answered and there IS an owner
#   2  it could not be answered
#   3  the argument was refused
#
# 3 IS THE PROBE'S REQUIRED VALUE, borrowed from who-is-gating.sh beside this file and for its
# reason: it is the only code no failure can produce, so it proves the copy answering parsed the
# argument at all. Every copy of a script that predates a flag ignores that flag, prints its usual
# output and exits its usual code - which is the exact fail-open the flag was added to remove,
# arriving through the flag. A caller that leans on the exit code proves the instrument understands
# the question before believing its answer:
#
#   bash who-owns.sh --probe-unsupported > /dev/null 2>&1
#   [ $? -eq 3 ] || { echo 'this copy predates --require-unowned; its exit code means nothing'; exit 9; }
#
# WHY --require-unowned IS THE FLAG RATHER THAN --require-owner. The dangerous direction is placing
# work on an owned specification, so the flag guards the placement: a zero means the search ran and
# found nobody. An unreadable board returns 2 and not 0, which is the whole point - the safe state is
# the one that must be POSITIVELY REACHED, rather than the unsafe one that must be avoided. A gate
# verdict here once compared a missing number, errored, and printed ALL GREEN; that shape is not
# repeated.
#
# WHAT IT CANNOT DO, so a clear read is never taken for more than it is:
#   - OWNED MEANS CLAIMED, NEVER ACTIVE, and releasing a specification is a human decision. A
#     specification with an open task and a claimant reads as owned however long its owner has been
#     gone - two-tab-connection-wedge reported an owner idle for hours, correctly by this reader's
#     rules and uselessly for anyone wanting to know whether work is moving. That is the mirror of
#     the failure this exists for: an offline owner was read as an absent one, and refusing that
#     inference also refuses the true case where an owner really has left. Ask the board, not this.
#   - It cannot see a session that is thinking about a specification and has touched nothing. The
#     earliest ownership becomes visible is the first worktree or the first commit.
#   - It reads no session titles, because a session renames itself Idle and keeps a worktree; it
#     reads worktrees, branches, and per-task identities on commits, which are facts.
#   - It reads the tasks document in ONE direction only: every box marked can END ownership, because
#     an owner holds a specification until every task is done. No box count is ever read as evidence
#     that nobody has started.
#   - It warns; it never authorises. Placement is the board's decision, and two readers can both see
#     `unowned` in the same second and both be correct.
set -u
SPEC=${1:-}
MODE=${2:-}
if [ $# -gt 2 ]; then
  echo "OWNERSHIP_REFUSED=<expected at most a specification name and a flag; got $#>"
  exit 3
fi
case "$SPEC" in
  '')
    echo "OWNERSHIP_REFUSED=<name a specification, or --all>"
    exit 3
    ;;
  --all)
    [ -z "$MODE" ] || {
      echo "OWNERSHIP_REFUSED=<--all takes no second argument; got '$MODE'>"
      exit 3
    }
    ;;
  -*)
    echo "OWNERSHIP_REFUSED=<unrecognised argument '$SPEC'; expected a specification name or --all>"
    exit 3
    ;;
esac
case "$MODE" in
  '' | --require-unowned) ;;
  *)
    echo "OWNERSHIP_REFUSED=<unrecognised argument '$MODE'; expected --require-unowned or nothing>"
    exit 3
    ;;
esac
HERE=$(cd "$(dirname "$0")" && pwd) || {
  echo "OWNERSHIP_UNREADABLE=<cannot locate this script>"
  exit 2
}
. "$HERE/gate-lib.sh" 2> /dev/null
if ! type gate_main_checkout gate_who_owns gate_who_owns_all > /dev/null 2>&1; then
  echo "OWNERSHIP_UNREADABLE=$HERE/gate-lib.sh (the library did not load)"
  exit 2
fi
MAIN=$(gate_main_checkout "$HERE") || {
  echo "OWNERSHIP_UNREADABLE=<no main checkout above $HERE>"
  exit 2
}
if [ "$SPEC" = --all ]; then
  gate_who_owns_all "$MAIN"
  exit $?
fi
# The report is captured, printed, and then read for its verdict - one run, not two. An owner and a
# positively established `unowned` both exit 0 from the library, so --require-unowned has to look at
# the verdict line itself; running the search a second time would let the board move between the two
# answers and report one while returning the other.
REPORT=$(gate_who_owns "$MAIN" "$SPEC")
RC=$?
printf '%s\n' "$REPORT"
[ "$MODE" = --require-unowned ] || exit "$RC"
[ "$RC" -eq 0 ] || exit 2
case "$REPORT" in
  *'
OWNER='* | 'OWNER='*) exit 1 ;;
esac
exit 0
