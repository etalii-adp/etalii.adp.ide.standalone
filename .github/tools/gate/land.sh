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
set -u
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY

if [ $# -ne 2 ] || [ -z "${1:-}" ] || [ -z "${2:-}" ]; then
  echo "usage: land.sh <base> <merged>"
  echo "RESULT=missing-arguments"
  exit 1
fi

HERE=$(cd "$(dirname "$0")" && pwd) || { echo "RESULT=aborted-cannot-locate-script"; exit 3; }
. "$HERE/gate-lib.sh" 2>/dev/null
if ! type gate_main_checkout gate_is_develops > /dev/null 2>&1; then
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

ON=$(git -C "$MAIN" symbolic-ref -q --short HEAD || true)
if [ "$ON" != develop ]; then
  echo "RESULT=main-checkout-not-on-develop (it is on '${ON:-a detached HEAD}' - a fast-forward would move that instead)"
  exit 1
fi
NOW=$(git -C "$MAIN" rev-parse --verify -q develop || true)
if [ -z "$NOW" ] || [ "$NOW" != "$BASE" ]; then
  echo "RESULT=develop-moved-regate (develop is ${NOW:-unreadable}; the gates merged onto $BASE and never saw what arrived since)"
  exit 1
fi
if ! git -C "$MAIN" merge-base --is-ancestor "$BASE" "$MERGED"; then
  echo "RESULT=not-a-descendant ($MERGED does not descend from $BASE)"
  exit 1
fi
if ! git -C "$MAIN" merge --ff-only "$MERGED"; then
  echo "RESULT=ff-refused"
  exit 1
fi
AFTER=$(git -C "$MAIN" rev-parse --verify -q develop || true)
if [ "$AFTER" != "$MERGED" ]; then
  echo "RESULT=develop-is-not-the-gated-commit (develop is ${AFTER:-unreadable}, gated $MERGED)"
  exit 1
fi
echo "RESULT=landed ($MERGED)"
