#!/usr/bin/env bash
#
# Launch the Ohrbob Android emulator, optionally building/installing the app on it.
#
#   ./scripts/android-emu.sh                # just start the emulator
#   ./scripts/android-emu.sh --deploy       # start it, then build + install + launch the app
#   ./scripts/android-emu.sh -no-window     # anything else is passed straight to `emulator`
#
# Notes for this machine (each of these cost real debugging time):
#   * Use `-gpu auto`. With KVM the emulator segfaults on -gpu off /
#     swiftshader_indirect / guest; `auto` selects ANGLE + Mesa and boots in ~25s.
#   * Build the app with -p:EmbedAssembliesIntoApk=true. Otherwise the installed APK
#     relies on fast deployment and dies at startup with
#     "No assemblies found in .../.__override__/x86_64 ... Exiting".
#   * Building needs a full JDK (javac + jar); the distro JRE
#     (/usr/lib/jvm/java-25-openjdk) has no `jar`. Set JAVA_HOME if it isn't found.
set -euo pipefail

# $DIR is the repository root: the Android project and APK paths below are
# relative to it, not to this script.
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK="${ANDROID_HOME:-$HOME/Projects/android-emu/android-sdk}"
export ANDROID_HOME="$SDK"
export ANDROID_SDK_ROOT="$SDK"
export PATH="$ANDROID_HOME/emulator:$ANDROID_HOME/platform-tools:$PATH"
ADB="$ANDROID_HOME/platform-tools/adb"

if [ -z "${JAVA_HOME:-}" ] || [ ! -x "${JAVA_HOME}/bin/javac" ]; then
  for candidate in "$HOME/Projects/android-emu/jdk" "$DIR/jdk" /tmp/opencode/jdk; do
    if [ -x "$candidate/bin/javac" ]; then JAVA_HOME="$candidate"; break; fi
  done
fi
if [ -n "${JAVA_HOME:-}" ]; then
  export JAVA_HOME
  export PATH="$JAVA_HOME/bin:$PATH"
fi

AVD="${OHRBOB_AVD:-Pixel_API_34}"
PKG="com.CompanyName.Ohrbob.App"
APK="$DIR/Ohrbob.App.Android/bin/Debug/net10.0-android/$PKG-Signed.apk"

DEPLOY=0
PASSTHRU=()
for arg in "$@"; do
  case "$arg" in
    --deploy) DEPLOY=1 ;;
    *) PASSTHRU+=("$arg") ;;
  esac
done

if [ ! -x "$ANDROID_HOME/emulator/emulator" ]; then
  echo "Android SDK not found at $ANDROID_HOME (set ANDROID_HOME)." >&2
  exit 1
fi

EMU_PID=""
if "$ADB" devices | grep -qE '^emulator-[0-9]+[[:space:]]+device'; then
  echo "An emulator is already running; not starting another."
else
  echo "Starting emulator '$AVD' (-gpu auto) ${PASSTHRU[*]:-}"
  "$ANDROID_HOME/emulator/emulator" -avd "$AVD" -gpu auto "${PASSTHRU[@]}" &
  EMU_PID=$!
fi

if [ "$DEPLOY" = "1" ]; then
  echo "Waiting for the device to finish booting..."
  "$ADB" wait-for-device
  until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do
    sleep 5
  done

  if [ -z "${JAVA_HOME:-}" ]; then
    echo "No JDK found. Set JAVA_HOME to a JDK (javac + jar) and retry." >&2
    exit 1
  fi

  echo "Building + installing $PKG ..."
  dotnet build "$DIR/Ohrbob.App.Android/Ohrbob.App.Android.csproj" \
    -c Debug -p:EmbedAssembliesIntoApk=true
  if ! "$ADB" install -r -g "$APK" >/dev/null 2>&1; then
    # The Android debug key changes between builds on this machine, which makes `-r`
    # fail with INSTALL_FAILED_UPDATE_INCOMPATIBLE. Replace the old install.
    echo "Replacing existing install (debug signature changed)..."
    "$ADB" uninstall "$PKG" >/dev/null 2>&1 || true
    "$ADB" install -g "$APK"
  fi

  # Point the app at the host backend, reusing the desktop server/token
  # (localhost -> 10.0.2.2 is how the emulator reaches the host).
  DESKTOP_CFG="$HOME/.config/Ohrbob/config.json"
  if [ -f "$DESKTOP_CFG" ]; then
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
  fi

  "$ADB" shell am force-stop "$PKG"
  "$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null
  echo "Launched $PKG."
fi

if [ -n "$EMU_PID" ]; then
  wait "$EMU_PID"
fi
