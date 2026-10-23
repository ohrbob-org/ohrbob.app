#!/bin/bash
# Installs a built AppImage for the current user, so the desktop can launch it
# and route ohrbob:// links to it.
#
# Usage:
#   ./scripts/install-appimage.sh [path/to/Ohrbob-x.y.z-x86_64.AppImage]
#   ./scripts/install-appimage.sh --uninstall
#
# Without an argument it installs the newest dist/Ohrbob-*-x86_64.AppImage.
#
# An AppImage is just a file: it only handles ohrbob:// links once its desktop
# entry is installed into the desktop database. This does that under a desktop
# entry id of its own (org.ohrbob.ohrbob.appimage.desktop) so it does not
# collide with the Flatpak, which registers the same scheme under
# org.ohrbob.ohrbob.desktop.
#
# Nothing here needs root: everything lands in the user's XDG data directories.
set -euo pipefail

cd "$(dirname "$0")/.."

DESKTOP_ID="org.ohrbob.ohrbob.appimage"
APP_ID="org.ohrbob.ohrbob"
DESKTOP_SOURCE="packaging/linux/$DESKTOP_ID.desktop"

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
APPS_DIR="$DATA_HOME/applications"
ICONS_DIR="$DATA_HOME/icons/hicolor"
INSTALL_DIR="${APPIMAGE_INSTALL_DIR:-$HOME/.local/bin}"
INSTALLED_APPIMAGE="$INSTALL_DIR/Ohrbob.AppImage"
ENTRY="$APPS_DIR/$DESKTOP_ID.desktop"
MIMEAPPS="${XDG_CONFIG_HOME:-$HOME/.config}/mimeapps.list"

refresh() {
    # Rebuild the desktop database so the entry is picked up without a
    # re-login. desktop-file-utils may not be installed; that is not fatal.
    update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
}

if [ "${1:-}" = "--uninstall" ]; then
    rm -f "$ENTRY" "$INSTALLED_APPIMAGE"
    # The icon is deliberately left behind: Icon= is the shared
    # org.ohrbob.ohrbob, so another entry (the Flatpak's) may resolve to it.
    #
    # Our line has to come out of mimeapps.list too, or ohrbob:// keeps
    # pointing at a desktop entry that no longer exists.
    if [ -f "$MIMEAPPS" ]; then
        escaped_id="${DESKTOP_ID//./\\.}"
        sed -i "/${escaped_id}\.desktop/d" "$MIMEAPPS"
    fi
    refresh
    echo "=> Removed $ENTRY and $INSTALLED_APPIMAGE"
    echo -n "=> ohrbob:// is now handled by: "
    xdg-mime query default x-scheme-handler/ohrbob || echo "(nothing)"
    exit 0
fi

if [ "$#" -ge 1 ]; then
    SRC="$1"
else
    SRC=""
    for candidate in dist/Ohrbob-*-x86_64.AppImage; do
        [ -e "$candidate" ] || continue
        if [ -z "$SRC" ] || [ "$candidate" -nt "$SRC" ]; then
            SRC="$candidate"
        fi
    done
fi

if [ -z "$SRC" ] || [ ! -f "$SRC" ]; then
    echo "error: no AppImage to install." >&2
    echo "       Build one first: ./scripts/build-appimage.sh" >&2
    echo "       Or name one:     ./scripts/install-appimage.sh dist/Ohrbob-0.0.5-x86_64.AppImage" >&2
    exit 1
fi

echo "=> Installing $SRC"
install -Dm755 "$SRC" "$INSTALLED_APPIMAGE"

# Exec has to be the AppImage's absolute path: the entry embedded in the
# AppImage cannot know where the user will keep the file.
mkdir -p "$APPS_DIR"
escaped="${INSTALLED_APPIMAGE//\\/\\\\}"
escaped="${escaped//&/\\&}"
sed "s|^Exec=.*|Exec=$escaped %u|" "$DESKTOP_SOURCE" > "$ENTRY"
chmod 644 "$ENTRY"

install -Dm644 art/ohrbob-logo.png "$ICONS_DIR/512x512/apps/$APP_ID.png"
install -Dm644 art/ohrbob-logo.svg "$ICONS_DIR/scalable/apps/$APP_ID.svg"

refresh

# Without this the Flatpak's entry keeps the scheme, and xdg-open never
# reaches the AppImage. xdg-mime writes mimeapps.list here and fails silently
# if the directory does not exist yet.
mkdir -p "${XDG_CONFIG_HOME:-$HOME/.config}"
xdg-mime default "$DESKTOP_ID.desktop" x-scheme-handler/ohrbob

echo "========================================================"
echo "=> Installed $INSTALLED_APPIMAGE"
echo "=> Desktop entry $ENTRY"
echo -n "=> ohrbob:// now resolves to: "
xdg-mime query default x-scheme-handler/ohrbob
echo "=> Try: xdg-open 'ohrbob://connect?server=x&token=y'"
