#!/bin/bash
# Builds dist/Ohrbob-<version>-x86_64.AppImage — the Linux AppImage for the
# desktop head.
#
# This is the same packaging the release workflow runs on a tag:
# .github/workflows/release.yml calls this script, so a local build and a CI
# build produce the same thing.
#
# Usage:
#   ./scripts/build-appimage.sh [version]
#
# The version defaults to the newest git tag without its leading "v"
# (v0.0.5 -> 0.0.5), falling back to 0.0.0 when there are no tags.
#
# Needs the .NET 10 SDK, curl and install(1). appimagetool is cached in
# build-dir/appimage/ after the first run, but it still fetches its AppImage
# runtime from GitHub while packaging, so this step needs network access.
set -euo pipefail

# Run from the repository root so the project, art/ and dist/ paths resolve.
cd "$(dirname "$0")/.."

RID="linux-x64"
EXECUTABLE="Ohrbob.App.Desktop"
APP_ID="org.ohrbob.ohrbob"
APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"

WORK="build-dir/appimage"
APPDIR="$WORK/Ohrbob.AppDir"
APPIMAGETOOL="$WORK/appimagetool"

if [ "$#" -ge 1 ]; then
    VERSION="$1"
else
    VERSION="$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)"
    VERSION="${VERSION:-0.0.0}"
fi

OUTPUT="dist/Ohrbob-${VERSION}-x86_64.AppImage"

echo "=> Building Ohrbob ${VERSION} as an AppImage"

echo "=> Publishing the desktop head for ${RID}..."
rm -rf "$WORK/publish"
dotnet publish Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:Version="$VERSION" \
    --output "$WORK/publish"

echo "=> Assembling the AppDir..."
rm -rf "$APPDIR"
mkdir -p \
    "$APPDIR/usr/bin" \
    "$APPDIR/usr/share/applications" \
    "$APPDIR/usr/share/icons/hicolor/scalable/apps" \
    "$APPDIR/usr/share/icons/hicolor/512x512/apps"

cp -a "$WORK/publish/." "$APPDIR/usr/bin/"

# The .desktop file is shared with the Flatpak build and launches
# org.ohrbob.ohrbob, so expose that name next to the real binary.
ln -s "$EXECUTABLE" "$APPDIR/usr/bin/$APP_ID"

install -Dm644 "$APP_ID.desktop" \
    "$APPDIR/usr/share/applications/$APP_ID.desktop"
install -Dm644 art/ohrbob-logo.svg \
    "$APPDIR/usr/share/icons/hicolor/scalable/apps/$APP_ID.svg"
install -Dm644 art/ohrbob-logo.png \
    "$APPDIR/usr/share/icons/hicolor/512x512/apps/$APP_ID.png"
install -Dm755 packaging/linux/AppRun "$APPDIR/AppRun"

# appimagetool reads the desktop file, the icon and .DirIcon from the top
# level of the AppDir.
ln -s "usr/share/applications/$APP_ID.desktop" "$APPDIR/$APP_ID.desktop"
ln -s "usr/share/icons/hicolor/512x512/apps/$APP_ID.png" "$APPDIR/$APP_ID.png"
ln -s "$APP_ID.png" "$APPDIR/.DirIcon"

if [ ! -x "$APPIMAGETOOL" ]; then
    echo "=> Fetching appimagetool (one-time, cached in $WORK)..."
    mkdir -p "$WORK"
    curl -fsSL -o "$APPIMAGETOOL" "$APPIMAGETOOL_URL"
    chmod +x "$APPIMAGETOOL"
fi

echo "=> Packaging the AppImage..."
mkdir -p dist
# --appimage-extract-and-run avoids needing libfuse2 on the build machine.
ARCH=x86_64 VERSION="$VERSION" \
    "$APPIMAGETOOL" --appimage-extract-and-run "$APPDIR" "$OUTPUT"

echo "========================================================"
echo "=> Built $OUTPUT"
echo "=> Run it with: ./$OUTPUT"
echo "   If your distro has no libfuse2, run it with"
echo "   ./$OUTPUT --appimage-extract-and-run instead."
