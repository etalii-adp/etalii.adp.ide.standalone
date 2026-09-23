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
#   TELL_UNREADABLE=<dir>  (exit 2)            the path this reader expects does not exist
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
gate_who_is_gating "$GITDIR/worktrees"
