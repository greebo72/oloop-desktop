#!/usr/bin/env bash
#
# Builds the macOS app bundle.
#
#   scripts/package-macos.sh [version] [rid]
#
# The result is dist/oloop.app and, as a zip, dist/oloop-<version>-macos-<arch>.zip.
# The zip is what a user downloads - a .app that is not zipped loses its
# executable bit and its code signature on the way through a browser.
#
# The bundle needs:
#   - the self-contained publish output (no .NET runtime needed on the machine)
#   - an Info.plist with the version, the icon and the bundle identifier
#   - the icon as .icns

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

version="${1:-$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' OloopApp.csproj)}"
rid="${2:-osx-$(uname -m)}"
arch="${rid#osx-}"

out="$root/dist"
bundle="$out/oloop.app"
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

echo "==> Building the bundle"
rm -rf "$bundle"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"

cp -R "$work/publish/." "$bundle/Contents/MacOS/"

# The icon has to sit in Resources and be named AppIcon.icns.
cp Assets/oloop.icns "$bundle/Contents/Resources/AppIcon.icns"

cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>oloop</string>
    <key>CFBundleDisplayName</key>
    <string>oloop</string>
    <key>CFBundleIdentifier</key>
    <string>app.oloop.desktop</string>
    <key>CFBundleVersion</key>
    <string>$version</string>
    <key>CFBundleShortVersionString</key>
    <string>$version</string>
    <key>CFBundleExecutable</key>
    <string>oloop</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSRequiresAquaSystemAppearance</key>
    <false/>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.productivity</string>
</dict>
</plist>
PLIST

# The binary has to be executable inside the bundle.
chmod +x "$bundle/Contents/MacOS/oloop"

# An ad-hoc signature: without it macOS refuses to start an app that was
# downloaded (Gatekeeper). A real signature needs an Apple developer account.
if command -v codesign >/dev/null 2>&1; then
    echo "==> Signing ad-hoc"
    codesign --force --deep --sign - "$bundle" 2>&1 | sed 's/^/    /'
fi

echo "==> Zipping"
mkdir -p "$out"
rm -f "$out/oloop-$version-macos-$arch.zip"
(cd "$out" && zip -q -r "oloop-$version-macos-$arch.zip" oloop.app)

echo "==> Done"
echo "    $bundle"
echo "    $out/oloop-$version-macos-$arch.zip"
du -sh "$bundle" "$out/oloop-$version-macos-$arch.zip"
