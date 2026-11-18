#!/usr/bin/env bash
#
# Builds the Windows package from macOS or Linux.
#
#   scripts/package-windows.sh [version] [rid]
#
# The result is dist/oloop-<version>-<rid>.zip: a self-contained publish
# output that can be unpacked and run without installing the .NET runtime.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

version="${1:-$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' OloopApp.csproj)}"
rid="${2:-win-x64}"

case "$rid" in
    win-*) ;;
    *) echo "Runtime identifier must target Windows (for example: win-x64)" >&2; exit 1 ;;
esac

if ! command -v zip >/dev/null 2>&1; then
    echo "The zip command is required to create the Windows package" >&2
    exit 1
fi

out="$root/dist"
stage="$out/oloop-$version-$rid"
archive="$out/oloop-$version-$rid.zip"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "==> Publishing oloop $version for $rid (self-contained)"
dotnet publish OloopApp.csproj \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    --output "$work/publish" \
    -p:Version="$version"

echo "==> Staging"
rm -rf "$stage"
mkdir -p "$stage"
cp -R "$work/publish/." "$stage/"
cp THIRD-PARTY-NOTICES.md "$stage/THIRD-PARTY-NOTICES.md"

echo "==> Zipping"
mkdir -p "$out"
rm -f "$archive"
(cd "$stage" && zip -q -r "$archive" .)

echo "==> Done"
echo "    $archive"
du -sh "$archive"