#!/usr/bin/env bash
#
# Start the Ohrbob Android emulator, install the built APK and launch it.
#
#   ./scripts/run-android-apk.sh              # newest dist/android/Ohrbob-*.apk
#   ./scripts/run-android-apk.sh 0.0.9        # a specific version
#   ./scripts/run-android-apk.sh -no-window   # anything starting with "-" goes to `emulator`
#
# Build the APK first with ./scripts/build-android-apk.sh. This installs that
# artifact, so you are testing exactly what a release ships. (For a Debug build
# straight from source, use ./scripts/android-emu.sh --deploy instead.)
#
# The AVD is created on demand, so this works on a box that has the Android SDK
# but no emulator set up yet: if the AVD named by OHRBOB_AVD (default
# Pixel_API_34) is missing, the system image is installed with sdkmanager and
# the AVD is created with avdmanager. Set ANDROID_HOME (or ANDROID_SDK_ROOT) to
# an SDK that contains the command-line tools.
#
# Notes for this machine (see scripts/android-emu.sh for the full story):
#   * Use `-gpu auto`. With KVM the emulator can segfault on other GPU modes.
#   * The emulator is cold-booted (`-no-snapshot`), so a snapshot written by a
#     different graphics renderer cannot crash it on startup.
#   * localhost -> 10.0.2.2 is how the emulator reaches the host backend.
set -euo pipefail

# $DIR is the repository root: the APK paths below are relative to it, not to
# this script.
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Projects/android-emu/android-sdk}}"
if [ ! -d "$SDK" ]; then
  echo "error: Android SDK not found at '$SDK'." >&2
  echo "       Set ANDROID_HOME to an Android SDK that has the command-line tools." >&2
  exit 1
fi
export ANDROID_HOME="$SDK"
export ANDROID_SDK_ROOT="$SDK"
export PATH="$ANDROID_HOME/emulator:$ANDROID_HOME/platform-tools:$PATH"
ADB="$ANDROID_HOME/platform-tools/adb"

AVD="${OHRBOB_AVD:-Pixel_API_34}"
SYSTEM_IMAGE="system-images;android-34;google_apis;x86_64"
PKG="com.CompanyName.Ohrbob.App"

# A bare argument (no leading "-") is a version; everything else is passed
# straight to `emulator`.
VERSION=""
PASSTHRU=()
for arg in "$@"; do
  case "$arg" in
    -*) PASSTHRU+=("$arg") ;;
    *) VERSION="$arg" ;;
  esac
done

if [ -n "$VERSION" ]; then
  APK="$DIR/dist/android/Ohrbob-$VERSION.apk"
else
  APK="$(ls -t "$DIR"/dist/android/Ohrbob-*.apk 2>/dev/null | head -n1 || true)"
fi
if [ -z "${APK:-}" ] || [ ! -f "$APK" ]; then
  echo "No APK found${VERSION:+ for version $VERSION}." >&2
  echo "Build one first: ./scripts/build-android-apk.sh ${VERSION:-}" >&2
  exit 1
fi

# sdkmanager/avdmanager live in the SDK's command-line tools; there is no way
# to provision an AVD without them.
find_sdk_tool() {
  local name="$1" candidate
  for candidate in "$ANDROID_HOME"/cmdline-tools/*/bin/"$name" "$ANDROID_HOME"/tools/bin/"$name"; do
    if [ -x "$candidate" ]; then
      printf '%s' "$candidate"
      return 0
    fi
  done
  return 1
}

SDKMANAGER="$(find_sdk_tool sdkmanager || true)"
AVDMANAGER="$(find_sdk_tool avdmanager || true)"
if [ -z "$SDKMANAGER" ] || [ -z "$AVDMANAGER" ]; then
  echo "error: sdkmanager/avdmanager not found under $ANDROID_HOME." >&2
  echo "       Install the Android command-line tools (cmdline-tools) into the SDK." >&2
  exit 1
fi

# Install whichever of the emulator, platform-tools, platform and system image
# the SDK is missing. Idempotent: a fully-provisioned SDK installs nothing.
ensure_packages() {
  local installed missing=() pkg
  installed="$("$SDKMANAGER" --list_installed 2>/dev/null || true)"
  for pkg in platform-tools emulator "platforms;android-34" "$SYSTEM_IMAGE"; do
    if ! printf '%s\n' "$installed" | grep -qF "$pkg"; then
      missing+=("$pkg")
    fi
  done
  if [ "${#missing[@]}" -gt 0 ]; then
    echo "=> Installing Android packages: ${missing[*]}"
    yes | "$SDKMANAGER" --install "${missing[@]}"
  fi
}

ensure_packages

if "$AVDMANAGER" list avd 2>/dev/null | grep -q "Name: $AVD"; then
  echo "=> AVD '$AVD' already exists."
else
  echo "=> Creating AVD '$AVD' from $SYSTEM_IMAGE ..."
  # `no` answers avdmanager's "create a custom hardware profile?" prompt.
  echo no | "$AVDMANAGER" create avd -n "$AVD" -k "$SYSTEM_IMAGE" -d pixel
fi

if [ ! -x "$ANDROID_HOME/emulator/emulator" ]; then
  echo "error: emulator not found at $ANDROID_HOME/emulator/emulator." >&2
  exit 1
fi

EMU_PID=""
if "$ADB" devices | grep -qE '^emulator-[0-9]+[[:space:]]+device'; then
  echo "An emulator is already running; not starting another."
else
  echo "Starting emulator '$AVD' (-gpu auto -no-snapshot) ${PASSTHRU[*]:-}"
  "$ANDROID_HOME/emulator/emulator" -avd "$AVD" -gpu auto -no-snapshot -no-metrics "${PASSTHRU[@]}" &
  EMU_PID=$!
fi

echo "Waiting for the device to finish booting..."
"$ADB" wait-for-device
until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do
  sleep 5
done

echo "Installing $(basename "$APK") ..."
if ! "$ADB" install -r -g "$APK" >/dev/null 2>&1; then
  # A different signing key (e.g. a Debug build, or another machine's debug
  # key) makes `-r` fail with INSTALL_FAILED_UPDATE_INCOMPATIBLE. Replace it.
  echo "Replacing existing install (signature changed)..."
  "$ADB" uninstall "$PKG" >/dev/null 2>&1 || true
  "$ADB" install -g "$APK"
fi

# Point the app at the host backend, reusing the desktop server/token
# (localhost -> 10.0.2.2 is how the emulator reaches the host). Writing the
# app's private files needs `run-as`, which only works on a debuggable build;
# a Release APK is not debuggable, so this is best-effort.
DESKTOP_CFG="$HOME/.config/Ohrbob/config.json"
if [ -f "$DESKTOP_CFG" ]; then
  if "$ADB" shell run-as "$PKG" true >/dev/null 2>&1; then
    CFG="$(mktemp)"
    python3 - "$DESKTOP_CFG" > "$CFG" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
d["ServerUrl"] = d["ServerUrl"].replace("localhost", "10.0.2.2").replace("127.0.0.1", "10.0.2.2")
json.dump(d, sys.stdout, indent=2)
PY
    "$ADB" push "$CFG" /data/local/tmp/ohrbob-config.json >/dev/null
    "$ADB" shell run-as "$PKG" mkdir -p files/.config/Ohrbob
    "$ADB" shell run-as "$PKG" cp /data/local/tmp/ohrbob-config.json files/.config/Ohrbob/config.json
    rm -f "$CFG"
    echo "Pushed app config (server -> 10.0.2.2, desktop token)."
  else
    echo "Release APK is not debuggable; skipping config push (configure in the app)."
  fi
fi

"$ADB" shell am force-stop "$PKG"
"$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null
echo "Launched $PKG."

# If we started the emulator, stay attached so it keeps running (and so Ctrl-C
# shuts it down) instead of leaving an orphan behind.
if [ -n "$EMU_PID" ]; then
  wait "$EMU_PID"
fi
