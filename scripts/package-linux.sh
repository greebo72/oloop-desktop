#!/usr/bin/env bash
#
# Builds the Linux package.
#
#   scripts/package-linux.sh [version] [rid]
#
# The result is dist/oloop-<version>-linux-<arch>.tar.gz: the
# self-contained publish output plus a launcher. It runs on every Linux
# distribution and needs nothing installed.
#
# An AppImage would be the nicer icon, but it needs appimagetool and a
# FUSE that many systems do not have. The tarball works everywhere.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

version="${1:-$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' OloopApp.csproj)}"
rid="${2:-linux-$(uname -m)}"
arch="${rid#linux-}"

out="$root/dist"
stage="$out/oloop-$version-linux-$arch"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "==> Publishing oloop $version for $rid (self-contained)"
rm -rf "$work/publish"
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
chmod +x "$stage/oloop"

# The embedded font (Inter, SIL OFL 1.1) has to travel with its license.
cp THIRD-PARTY-NOTICES.md "$stage/THIRD-PARTY-NOTICES.md"

cat > "$stage/README.txt" <<'README'
oloop - a local cross-project tracker

Unpack the archive and run:

    ./oloop

The app needs no installation and no runtime: everything it needs is in
this folder. The data folder is chosen in the settings; it can also come
from the OLOOP_ROOT environment variable.

To remove the app, delete this folder. Nothing is written outside of it
except the settings file in ~/.config/oloop/.
README

echo "==> Tarball"
mkdir -p "$out"
rm -f "$out/oloop-$version-linux-$arch.tar.gz"
(cd "$out" && tar -czf "oloop-$version-linux-$arch.tar.gz" "oloop-$version-linux-$arch")

echo "==> Done"
echo "    $out/oloop-$version-linux-$arch.tar.gz"
du -sh "$out/oloop-$version-linux-$arch.tar.gz"
