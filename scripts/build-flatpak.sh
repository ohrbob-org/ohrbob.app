#!/bin/bash
set -e

# Run from the repository root so the manifest and build-dir resolve.
cd "$(dirname "$0")/.."

# The Flatpak manifest excludes .git from the build sources, so the in-app
# version generator cannot read the tag inside the sandbox. Resolve it here
# and pass it through to the build.
VERSION="$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)"
VERSION="${VERSION:-0.0.0}"

echo "=> Building Flatpak ${VERSION} using native Flatpak Builder..."
# We run the host flatpak-builder binary directly, natively!
# No docker, no root, no permission issues!
# (The sandboxed org.flatpak.Builder flatpak is only needed if it is installed;
#  it is not required here since /usr/bin/flatpak-builder is present.)
flatpak-builder --user --install --force-clean \
    --env=OHRBOB_VERSION="$VERSION" \
    build-dir org.ohrbob.ohrbob.yml

echo "========================================================"
echo "=> Build & Install successful!"
echo "=> You can now launch the Flatpak app natively by running:"
echo "   flatpak run org.ohrbob.ohrbob"
