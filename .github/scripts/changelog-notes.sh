#!/usr/bin/env bash
# Prints the notes of one release from CHANGELOG.md: the text under the
# heading "## [X.Y.Z] - date", up to the next "## [" heading, without the
# link references at the end of the file.
#
# Usage: changelog-notes.sh <tag or version> [CHANGELOG.md]
#   changelog-notes.sh v0.5.1
#
# Exits with 1 and says why when the version has no section or the section
# is empty, so a release is never created without its notes.
set -euo pipefail

version="${1:?usage: changelog-notes.sh <tag or version> [file]}"
version="${version#v}"
file="${2:-CHANGELOG.md}"

if [ ! -f "$file" ]; then
  echo "changelog-notes: $file does not exist" >&2
  exit 1
fi

notes="$(
  awk -v version="$version" '
    /^## \[/ {
      if (inside) exit
      if (index($0, "## [" version "]") == 1) { inside = 1; next }
    }
    inside && /^\[[^]]+\]: / { next }
    inside { print }
  ' "$file"
)"

# Drop the empty lines at the start and at the end.
notes="$(printf '%s\n' "$notes" | sed -e '/./,$!d' | sed -e ':a' -e '/^\n*$/{$d;N;ba' -e '}')"

if [ -z "${notes//[$'\n\t ']/}" ]; then
  echo "changelog-notes: $file has no notes for version $version" >&2
  echo "Add a section \"## [$version] - YYYY-MM-DD\" before the tag is pushed." >&2
  exit 1
fi

printf '%s\n' "$notes"
