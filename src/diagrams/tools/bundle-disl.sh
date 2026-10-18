#!/usr/bin/env bash
# Bundle a diagram module's DISL definitions from an etalii.adp checkout.
#
#   bash src/diagrams/tools/bundle-disl.sh <module> <etalii.adp checkout> [<definition>...]
#
# With no definition named, copies definitions/diagrams/<module>.dis and .md, as committed at the
# checkout's HEAD, into src/diagrams/<module>/definition/, and rewrites provenance.json beside them
# with the repository, the path, the full revision and the sha256 of the bundled .dis bytes.
#
# A module that holds several definitions (one body read by several tool types, as the six C4 types
# read one .dsl) names them: each definitions/diagrams/<definition>.dis and .md is copied into the
# module's definition/ folder, with its own <definition>.provenance.json beside it, so that each
# definition's provenance is embedded as the resource the loader looks for.
#
# A developer action only: it never runs at build, and it reads nothing from a user's folders.
# The bytes come from the commit (`git show`), not from the working file, so the recorded revision
# is exactly what was copied; a checkout whose definition differs from its HEAD is refused rather
# than bundled under a revision that does not hold it.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 <module> <etalii.adp checkout> [<definition>...]" >&2
  exit 2
fi

module="$1"
checkout="$2"
shift 2
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$here/../$module/definition"

if [ ! -d "$here/../$module" ]; then
  echo "No diagram module folder src/diagrams/$module." >&2
  exit 1
fi

revision="$(git -C "$checkout" rev-parse HEAD)"

# Refuses a source file that is not committed at HEAD, or that differs from HEAD in the checkout.
committed() {
  git -C "$checkout" cat-file -e "HEAD:$1" 2>/dev/null || { echo "$1 is not committed at $revision." >&2; exit 1; }
  if [ -n "$(git -C "$checkout" status --porcelain -- "$1")" ]; then
    echo "$1 has uncommitted changes in $checkout; commit or discard them first." >&2
    exit 1
  fi
}

# Bundles definitions/diagrams/<name>.dis, its .md and its .fbl when it has one, and writes the
# provenance to <provenance> in the module's definition/ folder.
bundle() {
  local name="$1" provenance="$2"
  local source_dis="definitions/diagrams/$name.dis"
  local source_md="definitions/diagrams/$name.md"
  local source_fbl="definitions/diagrams/$name.fbl"

  committed "$source_dis"
  committed "$source_md"

  mkdir -p "$target"
  git -C "$checkout" show "HEAD:$source_dis" > "$target/$name.dis"
  # The .md is ordinary text, CRLF in the working tree as .gitattributes asks; the .dis is -text and
  # kept byte for byte, because its sha256 is recorded below.
  git -C "$checkout" show "HEAD:$source_md" | sed -e 's/\r$//' -e 's/$/\r/' > "$target/$name.md"

  local sha256
  sha256="$(sha256sum "$target/$name.dis" | cut -d' ' -f1)"

  # CRLF, as .gitattributes asks of a JSON file in the working tree.
  # A tool type whose file is ADP's own keeps its FBL binding beside its specification, and the
  # binding is bundled with it, byte for byte, with its own hash in the provenance. A type whose
  # binding lives elsewhere (specifications/fbl/, or in the module) bundles none.
  if git -C "$checkout" cat-file -e "HEAD:$source_fbl" 2>/dev/null; then
    committed "$source_fbl"
    git -C "$checkout" show "HEAD:$source_fbl" > "$target/$name.fbl"
    local binding_sha256
    binding_sha256="$(sha256sum "$target/$name.fbl" | cut -d' ' -f1)"
    printf '{\r\n  "repository": "etalii-adp/etalii.adp",\r\n  "path": "%s",\r\n  "revision": "%s",\r\n  "sha256": "%s",\r\n  "binding": "%s",\r\n  "bindingSha256": "%s"\r\n}\r\n' \
      "$source_dis" "$revision" "$sha256" "$source_fbl" "$binding_sha256" > "$target/$provenance"
  else
    printf '{\r\n  "repository": "etalii-adp/etalii.adp",\r\n  "path": "%s",\r\n  "revision": "%s",\r\n  "sha256": "%s"\r\n}\r\n' \
      "$source_dis" "$revision" "$sha256" > "$target/$provenance"
  fi

  echo "Bundled $name at $revision (sha256 $sha256)."
}

if [ "$#" -eq 0 ]; then
  bundle "$module" provenance.json
else
  # Every name is checked before anything is written, so a mistyped name bundles nothing.
  for name in "$@"; do
    committed "definitions/diagrams/$name.dis"
    committed "definitions/diagrams/$name.md"
  done
  for name in "$@"; do
    bundle "$name" "$name.provenance.json"
  done
fi
