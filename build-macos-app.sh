#!/bin/bash
# Builds dist/Ohrbob.app — the macOS bundle for the desktop head. The bundle's
# Info.plist (Ohrbob.App.Desktop/Info.plist) declares CFBundleURLTypes so macOS
# routes ohrbob:// links to this app.
#
# Run this on macOS: producing the Mach-O executable needs the Apple toolchain,
# so it cannot be cross-built from Linux/Windows.
set -euo pipefail

RID="${1:-osx-arm64}" # pass osx-x64 for Intel Macs

APP_NAME="Ohrbob"
EXECUTABLE="Ohrbob.App.Desktop"
BUNDLE="dist/${APP_NAME}.app"

echo "=> Publishing the desktop head for ${RID}..."
dotnet publish Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj \
    -c Release -r "${RID}" --self-contained true \
    -o "${BUNDLE}/Contents/MacOS"

echo "=> Assembling the bundle..."
install -Dm644 Ohrbob.App.Desktop/Info.plist "${BUNDLE}/Contents/Info.plist"
install -Dm644 art/ohrbob-logo.svg "${BUNDLE}/Contents/Resources/ohrbob-logo.svg"
chmod +x "${BUNDLE}/Contents/MacOS/${EXECUTABLE}"

echo "========================================================"
echo "=> Built ${BUNDLE}"
echo "=> Verify the scheme registration with:"
echo "   /usr/libexec/PlistBuddy -c 'Print :CFBundleURLTypes' '${BUNDLE}/Contents/Info.plist'"
echo "=> Then cold- and warm-start a link with:"
echo "   open 'ohrbob://connect?server=x&token=y'"
