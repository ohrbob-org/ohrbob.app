#!/bin/bash
#
# Builds and installs the Ohrbob desktop app inside the Flatpak build sandbox.
#
# This runs *inside* the sandbox as the module's build step. It is kept
# upstream (next to the app sources) so that the Flatpak manifest and local
# builds share one source of truth for how the app is packaged, instead of
# carrying the build recipe as discrete commands in the manifest.
#
# Invoked from the manifest's build-commands; see org.ohrbob.ohrbob.yml.
# (For the host-side helper that drives flatpak-builder, see build-flatpak.sh.)
#
set -euo pipefail

# Map the Flatpak architecture to a .NET runtime identifier (RID).
rid="linux-x64"
if [ "${FLATPAK_ARCH:-}" = "aarch64" ]; then
    rid="linux-arm64"
fi

project="Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj"
dest="${FLATPAK_DEST:-/app}"

# NuGet packages are vendored by nuget-sources.json (see howto.flatpak.md).
# The build sandbox has no network access, so restore only from that source.
dotnet restore "$project" -r "$rid" --source nuget-sources
dotnet publish "$project" -c Release -r "$rid" --self-contained true --no-restore -o "$dest/bin/ohrbob"

# The command in the manifest must match the app id, so expose the published
# binary under that name.
ln -sf "$dest/bin/ohrbob/Ohrbob.App.Desktop" "$dest/bin/org.ohrbob.ohrbob"

# Desktop entry, AppStream metadata and icon. The license is not installed
# here: flatpak-builder records LICENSE (a common name in the source root)
# automatically to "$dest/share/licenses/org.ohrbob.ohrbob/ohrbob/".
install -Dm644 org.ohrbob.ohrbob.desktop -t "$dest/share/applications/"
install -Dm644 org.ohrbob.ohrbob.metainfo.xml -t "$dest/share/metainfo/"
install -Dm644 art/ohrbob-logo.svg "$dest/share/icons/hicolor/scalable/apps/org.ohrbob.ohrbob.svg"
