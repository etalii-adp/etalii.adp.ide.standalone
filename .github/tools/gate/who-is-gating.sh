#!/bin/bash
# Is a gate running, and on which base? Run it before you move develop.
#
#   bash C:/git/EtAlii.Adp/.github/tools/gate/who-is-gating.sh
#
# A develop commit competes with a running gate exactly as another gate does: the gate takes
# develop's tip as its base and refuses to land onto a moved develop, so a commit straight to
# develop stales every run already in flight - and the verdict arrives only after the full backend
# suite has been paid for. Three sessions hit that in one day on 2026-09-23, because a running gate
# is invisible from develop: the lock is per scratch worktree and says nothing about whose base it
# protects.
#
# FOUR OUTPUTS, EACH A DIFFERENT STRING. Silence is never one of them:
#
#   GATING=none                                nobody holds a lock
#   mrga1 gating <branch> on base <sha> ...    a holder, with what it would be staled by
#
# The BASE is the field to defend if anyone proposes shortening this line. It is what lets a reader
# go and CHECK rather than take the tell's word: `git merge-base --is-ancestor <base> develop` says
# whether that run is already stale, and a line saying only `somebody is gating` gives nothing to
# test. Two independent uses on 2026-09-23 - one session read the base and found a live run three
# commits behind develop; another used it to know which state to go and inspect when verifying that
# develop's format gate was red. Neither the tell nor the check would have answered alone, which is
# why this is one line rather than two commands. (Observed by Developer 3, from using it.)
#   mrga1 gating (owner not written yet)       a holder whose line is not on disk yet
#   mrga1 green, awaiting land on base X ...   a GREEN gate whose landing has not happened yet
#   TELL_UNREADABLE=<dir>  (exit 2)            the path this reader expects does not exist
#
# THE AWAITING-LAND LINE HOLDS THE BOARD JUST AS A RUNNING GATE DOES, from the green verdict until
# land.sh lands that commit or refuses it for good. Without it this reader dropped to GATING=none the
# moment a green gate's process ended, while develop was still promised to it: at 11:58:50Z on 2026-09-25
# a spec commit landed ten seconds after a green gate on exactly that reading, and the gate lost its
# landing. A retryable refusal (ff-refused, main checkout not on develop) keeps the line; a claim nobody
# lands within the hour is dropped - its base will have moved, and land.sh refuses a moved base itself.
#
# EXIT CODES, AND WHY THE DEFAULT IS THE PERMISSIVE ONE. By default this exits 0 for a free board
# AND for a holder, because its job is to print the board and a caller that prints must not fail.
# That makes it fail-open in a chain: `who-is-gating.sh && git commit` runs the commit whatever the
# board says, so every such chain ever written has been a print statement wearing a check's clothes.
#
#   bash who-is-gating.sh --require-free
#
# FOUR EXIT CODES, EACH MEANING ONE THING:
#
#   0  the board was read, and with --require-free it is FREE
#   1  the board was read and is NOT free            (--require-free only)
#   2  the board could not be read
#   3  the argument was refused
#
# 3 IS THE PROBE'S REQUIRED VALUE, and the reason is the point rather than a detail: it is the only
# code no FAILURE can produce, so it proves the copy answering parsed the argument. Do not fold it
# back into 2. Refusal and unreadability shared 2 until 2026-09-24, and while they did, the probe
# below passed against precisely the copy it exists to detect, on any board it could not read.
# AN OVERLOADED EXIT CODE CANNOT CARRY A PROOF: two conditions sharing a number is fine for a human
# reading a message and useless for a check that must decide.
#
# ROLLOUT HAZARD, AND IT BIT THE AUTHOR WITHIN THE HOUR. Every copy of this script that predates
# this flag IGNORES ALL ARGUMENTS: it prints the board and exits 0, so `--require-free` against an
# older copy returns the same 0 for a held board as for a free one. That is the exact fail-open the
# flag exists to remove, arriving THROUGH the flag - a caller that adopts it early is unprotected
# while believing it is protected, and nothing in the output says which copy answered.
#
# THE AUTHOR'S OWN INSTANCE, kept here because a rule with its author's mistake attached survives a
# reader who thinks it does not apply to them: an hour after building this flag I ran it from the
# main checkout, read `exit 0` as THE BOARD IS FREE, and only then noticed the flag was not on
# develop at all. The board was free - the printed GATING=none said so - but the exit code could not
# have told me otherwise, and I had reached for the exit code precisely because I had just made it
# mean something.
#
# So a caller that cares proves the instrument understands the question before believing its answer:
#
#   bash who-is-gating.sh --probe-unsupported > /dev/null 2>&1
#   [ $? -eq 3 ] || { echo 'this copy predates --require-free; its exit code means nothing'; exit 9; }
#
# A copy that refuses a nonsense argument is a copy that would also refuse a mistyped one, and a copy
# that accepts it is one whose zero is uninformative. This is the liveness rule applied to a CLI flag:
#
# HOW THE HOLE IN THIS PROBE WAS FOUND, since it survived its author writing the script, the prose and
# the clause: its first outside adopter did NOT rest on the exit code. It read the message - which
# names the caller's own argument back, and which a copy that ignores arguments cannot produce - and
# it reported what it had read rather than that the probe had passed. WHEN A CHECK'S CAREFUL USERS
# COMPENSATE FOR ITS WEAKNESS, THE RECORD SHOWS A PASS AND THE WEAKNESS NEVER SURFACES. The author of
# a rule is the worst-placed person to notice their own instance of it.# an instrument reporting absence must first prove it is present.
#
# It still does not authorise anything. A zero from --require-free says the board was free at the
# instant it was read; the naming is a person, because locks are per scratch worktree and so do not
# exclude each other - two sessions can read `free` in the same second and both be correct.
#
# The first draft of this was `cat <glob> 2>/dev/null`, which prints the identical nothing for
# "nobody is gating", "the directory never existed", "the writer moved", "a mistyped path" and "the
# glob did not expand". Four of those mean you have NO INFORMATION while all five read as clear to
# proceed - the same shape as a missing results file making a chain print ALL GREEN. Under
# PowerShell it is worse than useless: `2>/dev/null` is a redirect there, not a discard, so the
# command would quietly create a file called C:\dev\null on the reader's machine.
#
# WHAT IT CANNOT DO, so that a clear read is never taken for more than it is:
#   - It cannot see a run that died before taking its lock, so ABSENCE IS WEAK EVIDENCE.
#   - It cannot say whether the board is yours. It warns; it never authorises. The board decides.
#   - It says nothing about a develop commit in flight, only about gates.
#   - An `ignore-after` in the past means the line may be disregarded - not that the lock may be
#     removed. A stale lock is a question to ask, never a licence.
set -u
MODE=${1:-}
if [ $# -gt 1 ]; then
  # A surplus argument is refused rather than ignored, for the same reason a mistyped one is: the
  # caller believed it was asking for something, and quietly answering a different question is how
  # the permissive default gets selected by accident.
  echo "TELL_REFUSED=<expected one argument at most; got $#>"
  exit 3
fi
case "$MODE" in
  '' | --require-free) ;;
  *)
    echo "TELL_REFUSED=<unrecognised argument '$MODE'; expected --require-free or nothing>"
    exit 3
    ;;
esac
HERE=$(cd "$(dirname "$0")" && pwd) || { echo "TELL_UNREADABLE=<cannot locate this script>"; exit 2; }
. "$HERE/gate-lib.sh" 2> /dev/null
if ! type gate_main_checkout gate_who_is_gating > /dev/null 2>&1; then
  echo "TELL_UNREADABLE=$HERE/gate-lib.sh (the library did not load)"
  exit 2
fi
MAIN=$(gate_main_checkout "$HERE") || { echo "TELL_UNREADABLE=<no main checkout above $HERE>"; exit 2; }
GITDIR=$(git -C "$MAIN" rev-parse --git-common-dir 2> /dev/null) || {
  echo "TELL_UNREADABLE=<no git directory for $MAIN>"
  exit 2
}
case "$GITDIR" in
  /* | [A-Za-z]:*) ;;
  *) GITDIR="$MAIN/$GITDIR" ;;
esac
gate_who_is_gating "$GITDIR/worktrees" "$MODE"
