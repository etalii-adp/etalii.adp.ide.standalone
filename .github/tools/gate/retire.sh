#!/bin/bash
# Retires a worktree without making a husk.
#
#   bash C:/git/EtAlii.Adp/.github/tools/gate/retire.sh <name>
#
#   name   the worktree's directory name under .claude/worktrees/
#
# `git worktree remove` on this machine deregisters a worktree and then, on the long paths a client
# gate leaves in node_modules, fails to delete it - leaving a directory without its .git that
# answers every git command for the MAIN CHECKOUT until somebody deletes it by hand. Every worktree
# that ran the client gate is one of those waiting to happen. So this deletes node_modules first,
# by absolute path, and only then asks git to remove the worktree: the husk is never made, rather
# than handled safely afterwards.
#
# Refuses, before deleting anything: a copy that is not develop's; a caller whose shell is inside
# the worktree; a directory that is not a registered worktree of its own - a husk, the main
# checkout, a dangling .git; uncommitted changes; commits that have not landed on develop; and any
# link whose target lies outside the worktree, since a delete that followed it could reach real
# source. It cannot see whether another session is working in the worktree - check that first
# (processes.md, "Retiring a worktree"). It does not delete the branch.
#
# Ends in one RESULT= line; only RESULT=retired is a retirement. If git's removal still leaves the
# directory, it says so and runs no git command in it: that directory is a husk.
set -u
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY

if [ $# -ne 1 ] || [ -z "${1:-}" ]; then
  echo "usage: retire.sh <name>"
  echo "RESULT=missing-arguments"
  exit 1
fi
NAME=$1
case "$NAME" in
  *[!A-Za-z0-9._-]* | .*)
    echo "RESULT=bad-name ('$NAME': a directory name under .claude/worktrees/, nothing else)"
    exit 1
    ;;
esac

HERE=$(cd "$(dirname "$0")" && pwd) || { echo "RESULT=aborted-cannot-locate-script"; exit 3; }
. "$HERE/gate-lib.sh" 2>/dev/null
if ! type gate_canon gate_main_checkout gate_is_develops > /dev/null 2>&1; then
  echo "RESULT=aborted-library-missing ($HERE/gate-lib.sh)"
  exit 3
fi
MAIN=$(gate_main_checkout "$HERE") || { echo "RESULT=aborted-no-main-checkout (from $HERE)"; exit 3; }
if ! gate_is_develops "$MAIN" "$HERE" retire.sh gate-lib.sh; then
  echo "RESULT=aborted-not-develops-gate ($GATE_DIFFERS differs from develop's copy, or develop has none)"
  exit 3
fi

T="$MAIN/.claude/worktrees/$NAME"
if [ ! -e "$T" ]; then echo "RESULT=no-such-directory ($T)"; exit 1; fi
T_C=$(gate_canon "$T")
case "$(gate_canon "$(pwd -P)")" in
  "$T_C" | "$T_C"/*)
    echo "RESULT=cwd-inside-target (your shell is in $T - leave it first; a removal from inside fails)"
    exit 1
    ;;
esac

# --- Is it what it claims to be? Only --show-toplevel compared against the path tells a husk from
# --- a worktree; everything else a husk answers for the main checkout.
MAIN_C=$(gate_canon "$(git -C "$MAIN" rev-parse --show-toplevel)")
TOP=$(git -C "$T" rev-parse --show-toplevel 2> /dev/null || true)
if [ -z "$TOP" ]; then
  echo "RESULT=not-a-worktree (git will not answer for $T - a dangling .git file? Look with file tools)"
  exit 1
fi
if [ "$(gate_canon "$TOP")" = "$MAIN_C" ]; then
  echo "RESULT=target-is-a-husk ($T resolves to the main checkout: run no git command in it, look with file tools, delete it by hand)"
  exit 1
fi
if [ "$(gate_canon "$TOP")" != "$T_C" ]; then
  echo "RESULT=not-its-own-worktree ($T resolves to $TOP)"
  exit 1
fi
REGISTERED=no
while IFS= read -r line; do
  case "$line" in
    "worktree "*) if [ "$(gate_canon "${line#worktree }")" = "$T_C" ]; then REGISTERED=yes; fi ;;
  esac
done < <(git -C "$MAIN" worktree list --porcelain)
if [ "$REGISTERED" != yes ]; then echo "RESULT=not-registered ($T is not in git's worktree list)"; exit 1; fi

# --- Would anything be lost?
CHANGES=$(git -C "$T" status --porcelain) || { echo "RESULT=cannot-read-status"; exit 1; }
if [ -n "$CHANGES" ]; then
  printf '%s\n' "$CHANGES"
  echo "RESULT=uncommitted-changes"
  exit 1
fi
HEAD_SHA=$(git -C "$T" rev-parse --verify -q HEAD) || { echo "RESULT=cannot-read-head"; exit 1; }
if ! git -C "$MAIN" merge-base --is-ancestor "$HEAD_SHA" develop; then
  # Patch-id, not ancestry: a commit that landed under a different sha is not lost work.
  UNLANDED=$(git -C "$MAIN" cherry develop "$HEAD_SHA") || { echo "RESULT=cannot-compare-with-develop"; exit 1; }
  if printf '%s\n' "$UNLANDED" | grep -q '^+'; then
    printf '%s\n' "$UNLANDED" | grep '^+'
    echo "RESULT=unlanded-commits"
    exit 1
  fi
fi

# --- Could a delete reach outside? npm's workspace links are junctions with absolute targets
# --- inside their own tree, which is harmless; a link out of the tree is a reason to stop.
OUTSIDE=""
while IFS= read -r link; do
  target=$(readlink -m "$link" 2> /dev/null || true)
  case "$(gate_canon "${target:-/nowhere}")" in
    "$T_C"/*) ;;
    *) OUTSIDE="$OUTSIDE$link -> ${target:-unresolvable}"$'\n' ;;
  esac
done < <(find "$T" -path "$T/.git" -prune -o -type l -print 2> /dev/null)
if [ -n "$OUTSIDE" ]; then
  printf '%s' "$OUTSIDE"
  echo "RESULT=link-outside (a delete that followed one of these could reach files outside $T)"
  exit 1
fi

# --- Delete what makes git's own removal fail, then let git remove the worktree.
while IFS= read -r modules; do
  rm -rf "$modules"
  if [ -e "$modules" ]; then echo "RESULT=node-modules-not-deleted ($modules)"; exit 1; fi
done < <(find "$T" -path "$T/.git" -prune -o -type d -name node_modules -prune -print 2> /dev/null)
REMOVE_OUT=$(git -C "$MAIN" worktree remove "$T" 2>&1)
REMOVE_EXIT=$?

# --- From here on, no git command runs in $T: if it survived, it is a husk.
if [ -e "$T" ]; then
  printf '%s\n' "$REMOVE_OUT"
  echo "Left behind (listed with file tools):"
  ls -A "$T" | head -20
  echo "RESULT=removed-but-directory-survived (git remove exit $REMOVE_EXIT; if it was deregistered this is now a husk - look, then delete it by hand)"
  exit 1
fi
while IFS= read -r line; do
  case "$line" in
    "worktree "*) if [ "$(gate_canon "${line#worktree }")" = "$T_C" ]; then echo "RESULT=still-registered"; exit 1; fi ;;
  esac
done < <(git -C "$MAIN" worktree list --porcelain)
echo "RESULT=retired ($T)"
