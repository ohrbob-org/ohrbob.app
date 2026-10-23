#!/bin/bash
# Builds dist/win/OhrbobSetup-<version>.exe — the Windows installer for the
# desktop head, from a Linux dev box.
#
# Inno Setup is a Windows program, so this cross-publishes the win-x64 build
# and then compiles installer/ohrbob.iss with Inno Setup 6.7.1 under Wine in
# Docker — the same Inno Setup version the release workflow uses on
# windows-latest, so the result matches what CI produces.
#
# Usage:
#   ./scripts/build-windows-installer.sh [version]
#
# The version defaults to the newest git tag without its leading "v"
# (v0.0.5 -> 0.0.5), falling back to 0.0.0 when there are no tags.
#
# Needs the .NET 10 SDK and Docker. The Docker image is pulled once and then
# reused, so repeat builds need no network.
#
# To build on Windows itself instead, run the "Compile the installer" step of
# .github/workflows/release.yml, which calls ISCC.exe directly.
set -euo pipefail

# Run from the repository root so the project, installer/ and dist/ paths resolve.
cd "$(dirname "$0")/.."

RID="win-x64"
ISCC_IMAGE="amake/innosetup:innosetup6-bookworm"

WORK="build-dir/windows"
PUBLISH_DIR="$WORK/publish"
OUTPUT_DIR="dist/win"

if [ "$#" -ge 1 ]; then
    VERSION="$1"
else
    VERSION="$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)"
    VERSION="${VERSION:-0.0.0}"
fi

if ! command -v docker >/dev/null 2>&1; then
    echo "error: docker is required." >&2
    echo "       Inno Setup is a Windows program; on Linux this script runs it" >&2
    echo "       under Wine inside a container." >&2
    exit 1
fi

# The container mounts the repo at /work, which Wine sees as Z:\work, so the
# paths handed to ISCC have to be Wine paths.
to_wine_path() {
    printf 'Z:\\work\\%s' "${1//\//\\}"
}

echo "=> Building Ohrbob ${VERSION} as a Windows installer"

echo "=> Publishing the desktop head for ${RID}..."
rm -rf "$PUBLISH_DIR"
dotnet publish Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:Version="$VERSION" \
    --output "$PUBLISH_DIR"

mkdir -p "$OUTPUT_DIR"

echo "=> Compiling the installer with Inno Setup under Wine..."
# :Z relabels the bind mount for SELinux, which is what makes it writable on
# Fedora and friends. The image runs as uid 1000, which is the first user on
# virtually every desktop Linux install — the same user running this script.
docker run --rm \
    --volume "$PWD:/work:Z" \
    "$ISCC_IMAGE" \
    "/DAppVersion=$VERSION" \
    "/DAppDir=$(to_wine_path "$PUBLISH_DIR")" \
    'Z:\work\installer\ohrbob.iss'

echo "========================================================"
echo "=> Built $OUTPUT_DIR/OhrbobSetup-${VERSION}.exe"
