#!/bin/bash
set -e

echo "=> Building Flatpak using native Flatpak Builder..."
# We run the host flatpak-builder binary directly, natively!
# No docker, no root, no permission issues!
# (The sandboxed org.flatpak.Builder flatpak is only needed if it is installed;
#  it is not required here since /usr/bin/flatpak-builder is present.)
flatpak-builder --user --install --force-clean build-dir app.ohrbob.Ohrbob.yml

echo "========================================================"
echo "=> Build & Install successful!"
echo "=> You can now launch the Flatpak app natively by running:"
echo "   flatpak run app.ohrbob.Ohrbob"
