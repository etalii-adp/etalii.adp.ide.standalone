# gate-lib.sh - the shared gate's testable parts. Sourced by gate.sh, land.sh and gate.test.sh;
# nothing here runs on its own. Why each part is shaped the way it is: processes.md,
# "Committing and merging in the shared main checkout" and "Running the backend tests".

# extract_guard - reads processes.md on stdin, prints the scratch-worktree guard on stdout.
#   0  exactly one fenced sh block containing `canon() {`, carrying every required part
#   2  no such block      3  more than one      4  a required part is missing
#
# A chain that extracts its guard by pattern runs UNGUARDED the moment an edit splits, renames or
# truncates the block - silently, unless the extractor refuses. So it refuses anything but one
# intact block, and "intact" includes the newest check (symbolic-ref, the detached refusal): a
# chain handed an older block aborts rather than running with less.
extract_guard() {
  local blk rc need
  blk=$(tr -d '\r' | awk -v want='canon() {' '
    /^```sh/            { inb = 1; buf = ""; next }
    /^```/ && inb       { inb = 0; if (index(buf, want)) { n++; out = buf }; next }
    inb                 { buf = buf $0 "\n" }
    END                 { if (n == 1) { printf "%s", out; exit 0 }; exit (n == 0 ? 2 : 3) }')
  rc=$?
  [ "$rc" -eq 0 ] || return "$rc"
  for need in 'show-toplevel' '$MAIN_TOP' 'symbolic-ref'; do
    case "$blk" in *"$need"*) ;; *) return 4 ;; esac
  done
  printf '%s\n' "$blk"
}

# gate_canon <path> - one spelling per path, for comparisons: Windows form where cygpath exists,
# lower case, no trailing slash. The same definition as the guard's canon() in processes.md. When
# cygpath is missing the raw path is kept, so on Windows every comparison fails - closed - rather
# than every path becoming the same empty string.
gate_canon() {
  local p
  p=$(cygpath -m "$1" 2> /dev/null) || p=$1
  printf '%s' "$p" | tr '[:upper:]' '[:lower:]' | sed 's:/*$::'
}

# gate_main_checkout <dir> - prints the main checkout of the repository <dir> belongs to: the
# parent of the shared git directory, which is the same answer from the main checkout and from
# every worktree. Fails when there is no such thing.
gate_main_checkout() {
  local common
  common=$(git -C "$1" rev-parse --path-format=absolute --git-common-dir 2>/dev/null) || return 1
  case "$common" in */.git) printf '%s\n' "${common%/.git}" ;; *) return 1 ;; esac
}

# gate_is_develops <main> <dir> <file>... - succeeds only when every named file in <dir> is
# byte-identical to develop's committed copy of .github/tools/gate/<file>. The point of a shared
# gate is that everybody runs the same one: a local copy that has drifted, or a branch's edited
# copy, is refused rather than trusted. A file develop does not have compares as different.
gate_is_develops() {
  local main=$1 dir=$2 f
  shift 2
  for f in "$@"; do
    git -C "$main" show "develop:.github/tools/gate/$f" 2>/dev/null | cmp -s - "$dir/$f" || {
      GATE_DIFFERS=$f
      return 1
    }
  done
}

# gate_tree_matches_head <dir> - 0 when the worktree's tracked CONTENT is its HEAD's, 1 when it
# differs, 2 when it cannot be read. Content, not `git status`: the client's pretest regenerates
# src/client/src/generated/ with LF over a CRLF checkout, and status reports every such file as
# modified with nothing in it changed - the shared gate's first real run, all four gates green,
# refused on exactly that. `git diff` compares after the same normalisation git applies on commit.
# retire.sh's first real use then refused a clean scratch tree the same way. The shape was already
# in the board's shared notes from 2026-09-04 (credited to Developer 1): after `buf generate` those
# files show as modified, "line-ending churn only, not content", and `git diff --numstat` returns
# nothing. It reached neither script because nobody asked them the question that record answers:
# "modified" to git status and "changed" are different claims. Ask this function the second one.
gate_tree_matches_head() {
  git -C "$1" diff --quiet HEAD -- 2> /dev/null
  case $? in
    0) return 0 ;;
    1) return 1 ;;
    *) return 2 ;;
  esac
}

# --- A run's logs. Two requirements that look opposed and are not.
#
# One: no run may read another run's log. A reused scratch tree once served a twelve-minute-old green
# dotnet log as the current run's, which is why the logs used to be deleted at the start of a run.
# Two: a REFUSED run's logs must survive the next run. On 2026-09-12 Developer 3 asked for the log of
# a flake that only appears under the full suite, and the re-gate had already deleted it - the author
# of the deletion losing the evidence it was asked for, hours after writing it.
#
# Both hold once each run writes to its OWN directory, named from the clock and the pid, and a
# refused run's directory is kept under a name a live run cannot produce. Staleness stops being
# something to delete against and becomes impossible: gate_run_logs_dir refuses a directory that
# already holds anything.

# gate_run_logs_dir <parent> <run-id> - creates and prints this run's log directory.
#   1 cannot create      2 not empty (something else is using it - never write there)
gate_run_logs_dir() {
  local dir="$1/$2"
  mkdir -p "$dir" 2> /dev/null || return 1
  [ -z "$(ls -A "$dir" 2> /dev/null)" ] || return 2
  printf '%s\n' "$dir"
}

# gate_keep_logs_red <dir> <why> - keeps a refused run's logs: renames <dir> to <dir>-red, writes a
# note inside saying what it is, points <parent>/last-red-run.txt at it, and prints the new path.
# The note and the pointer exist so somebody who does not know this convention still finds it.
# Idempotent: a directory already kept is left where it is.
gate_keep_logs_red() {
  local dir=$1 why=${2:-} kept
  case "$dir" in *-red) printf '%s\n' "$dir"; return 0 ;; esac
  [ -d "$dir" ] || return 1
  kept="$dir-red"
  mv "$dir" "$kept" 2> /dev/null || return 1
  {
    echo "These are the logs of a gate run that was REFUSED, kept on purpose."
    echo "Run:     $(basename "$dir")"
    echo "Verdict: ${why:-refused}"
    echo "Kept at: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo
    echo "A later gate run writes to its own directory beside this one and never touches this one."
    echo "dotnet-test.log holds the per-assembly stdout, including any server-side exception."
    echo "Delete it once whoever is investigating has what they need."
  } > "$kept/WHAT-THIS-IS.txt" 2> /dev/null
  printf '%s\n' "$kept" > "$(dirname "$dir")/last-red-run.txt" 2> /dev/null
  printf '%s\n' "$kept"
}

# gate_prune_logs <parent> <keep> - keeps the newest <keep> plain run directories and the newest
# <keep> kept-red ones. Anything that is not a run directory is left alone, including the pointer
# file and a directory somebody renamed while investigating.
gate_prune_logs() {
  local parent=$1 keep=$2 pattern
  [ -d "$parent" ] || return 0
  for pattern in 'plain' 'red'; do
    local dir
    ls -1dt "$parent"/[0-9]*T[0-9]*Z-[0-9]* 2> /dev/null | while IFS= read -r dir; do
      dir=${dir%/}
      case "$dir" in
        *-red) [ "$pattern" = red ] || continue ;;
        *) [ "$pattern" = plain ] || continue ;;
      esac
      printf '%s\n' "$dir"
    done | tail -n "+$((keep + 1))" | while IFS= read -r dir; do rm -rf "$dir"; done
  done
}

# gate_verdict <dotnet-log> - judges the four gates. Reads NPM_INSTALL_EXIT, NPM_TEST_EXIT,
# TC_EXIT, FMT_EXIT and DT_EXIT; sets VERDICT (green|refused), WHY, DT_TOTAL and DT_FAILED.
#
# It STARTS REFUSED and green is assigned in exactly one place, behind every positive condition,
# because a verdict's dangerous input is missing, not wrong: a wrong value is compared and fails,
# a missing one is often never compared at all. So every status is compared as a string (`[ "" -ne
# 0 ]` is an error, and an error is false inside `if`), and the log must show POSITIVE evidence
# that tests ran - a nonzero `total:` and `failed: 0` - rather than merely lack "Zero tests ran".
# That one form refuses a deleted log, an empty one, one truncated before its summary, `total: 0`
# and `failed: 3` behind an exit code of 0.
#
# dotnet writes CRLF. Git Bash's sed happens to strip CR itself (measured), but `sed -b`, bash's
# `read` and a Linux sed do not - so CR is stripped here and no reader's habits are relied on.
gate_verdict() {
  local log=${1:-}
  VERDICT=refused
  WHY=""
  DT_TOTAL=""
  DT_FAILED=""
  [ "${NPM_INSTALL_EXIT:-}" = 0 ] || WHY="$WHY npm-install=${NPM_INSTALL_EXIT:-missing}"
  [ "${NPM_TEST_EXIT:-}" = 0 ]    || WHY="$WHY npm-test=${NPM_TEST_EXIT:-missing}"
  [ "${TC_EXIT:-}" = 0 ]          || WHY="$WHY typecheck=${TC_EXIT:-missing}"
  [ "${FMT_EXIT:-}" = 0 ]         || WHY="$WHY format=${FMT_EXIT:-missing}"
  [ "${DT_EXIT:-}" = 0 ]          || WHY="$WHY dotnet-test=${DT_EXIT:-missing}"
  if [ -n "$log" ] && [ -s "$log" ]; then
    DT_TOTAL=$(tr -d '\r' < "$log" | sed -n 's/^ *total: *\([0-9][0-9]*\) *$/\1/p' | tail -1)
    DT_FAILED=$(tr -d '\r' < "$log" | sed -n 's/^ *failed: *\([0-9][0-9]*\) *$/\1/p' | tail -1)
    case "$DT_TOTAL" in *[1-9]*) ;; *) WHY="$WHY dotnet-ran-no-tests(total='$DT_TOTAL')" ;; esac
    case "$DT_FAILED" in '' | *[!0]*) WHY="$WHY dotnet-failed='$DT_FAILED'" ;; esac
    if tr -d '\r' < "$log" | grep -q "Zero tests ran"; then WHY="$WHY dotnet-zero-tests-ran"; fi
  else
    WHY="$WHY dotnet-log-missing-or-empty"
  fi
  if [ -z "$WHY" ]; then VERDICT=green; fi
}
