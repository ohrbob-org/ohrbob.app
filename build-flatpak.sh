#!/bin/bash
set -e

echo "=> Building Flatpak using native Flatpak Builder..."
# We run the flatpak-builder installed as a user flatpak, natively!
# No docker, no root, no permission issues!
flatpak run org.flatpak.Builder --user --install --force-clean build-dir app.ohrbob.Ohrbob.yml

echo "========================================================"
echo "=> Build & Install successful!"
echo "=> You can now launch the Flatpak app natively by running:"
echo "   flatpak run app.ohrbob.Ohrbob"
