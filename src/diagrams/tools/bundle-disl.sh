#!/usr/bin/env bash
# Bundle a diagram module's DISL definition from an etalii.adp checkout.
#
#   bash src/diagrams/tools/bundle-disl.sh <module> <etalii.adp checkout>
#
# Copies definitions/diagrams/<module>.dis and .md, as committed at the checkout's HEAD, into
# src/diagrams/<module>/definition/, and rewrites provenance.json beside them with the repository,
# the path, the full revision and the sha256 of the bundled .dis bytes.
#
# A developer action only: it never runs at build, and it reads nothing from a user's folders.
# The bytes come from the commit (`git show`), not from the working file, so the recorded revision
# is exactly what was copied; a checkout whose definition differs from its HEAD is refused rather
# than bundled under a revision that does not hold it.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <module> <etalii.adp checkout>" >&2
  exit 2
fi

module="$1"
checkout="$2"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$here/../$module/definition"
source_dis="definitions/diagrams/$module.dis"
source_md="definitions/diagrams/$module.md"

if [ ! -d "$here/../$module" ]; then
  echo "No diagram module folder src/diagrams/$module." >&2
  exit 1
fi

revision="$(git -C "$checkout" rev-parse HEAD)"
for path in "$source_dis" "$source_md"; do
  git -C "$checkout" cat-file -e "HEAD:$path" 2>/dev/null || { echo "$path is not committed at $revision." >&2; exit 1; }
  if [ -n "$(git -C "$checkout" status --porcelain -- "$path")" ]; then
    echo "$path has uncommitted changes in $checkout; commit or discard them first." >&2
    exit 1
  fi
done

mkdir -p "$target"
git -C "$checkout" show "HEAD:$source_dis" > "$target/$module.dis"
git -C "$checkout" show "HEAD:$source_md" > "$target/$module.md"

sha256="$(sha256sum "$target/$module.dis" | cut -d' ' -f1)"

# CRLF, as .gitattributes asks of a JSON file in the working tree.
printf '{\r\n  "repository": "etalii-adp/etalii.adp",\r\n  "path": "%s",\r\n  "revision": "%s",\r\n  "sha256": "%s"\r\n}\r\n' \
  "$source_dis" "$revision" "$sha256" > "$target/provenance.json"

echo "Bundled $module at $revision (sha256 $sha256)."
