#!/bin/bash
# Builds dist/android/Ohrbob-<version>.apk — the Android APK for the mobile head.
#
# This is the same APK the release workflow builds on a tag:
# .github/workflows/release.yml calls this script, so a local build and a CI
# build produce the same thing.
#
# Usage:
#   ./scripts/build-android-apk.sh [version]
#
# The version defaults to the newest git tag without its leading "v"
# (v0.0.6 -> 0.0.6), falling back to 0.0.0 when there are no tags. It is
# stamped into the APK as the display version, with a numeric versionCode
# derived from it so a sideloaded update is accepted over an older install.
#
# The APK is signed with the .NET for Android debug keystore, which is enough
# to install it on a device or emulator. It is NOT signed for the Play Store:
# for that, build with -p:AndroidKeyStore=true plus AndroidSigningKeyStore,
# AndroidSigningStorePass, AndroidSigningKeyAlias and AndroidSigningKeyPass.
#
# Needs the .NET 10 SDK with the `android` workload, a full JDK (javac + jar,
# not just a JRE), and the Android SDK.
set -euo pipefail

# Run from the repository root so the project and dist/ paths resolve.
cd "$(dirname "$0")/.."

APP_ID="com.CompanyName.Ohrbob.App"
PROJECT="Ohrbob.App.Android/Ohrbob.App.Android.csproj"

if [ "$#" -ge 1 ]; then
    VERSION="$1"
else
    VERSION="$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)"
    VERSION="${VERSION:-0.0.0}"
fi

# Android wants a monotonically increasing integer versionCode; derive one from
# the version (1.2.3 -> 10203) so a newer APK always installs over an older one.
VERSION_CODE="$(printf '%s' "$VERSION" | awk -F. '{ printf "%d%02d%02d", $1, $2, $3 }' 2>/dev/null || true)"
case "$VERSION_CODE" in
    ''|*[!0-9]*) VERSION_CODE=1 ;;
esac
if ! [ "$VERSION_CODE" -ge 1 ] 2>/dev/null; then
    VERSION_CODE=1
fi

# The Android build needs a full JDK (javac + jar); a plain JRE fails with
# "Could not find required file `jar`". Honour JAVA_HOME, then the locations
# this machine keeps a JDK in, then any JDK under /usr/lib/jvm.
JDK=""
if [ -n "${JAVA_HOME:-}" ] && [ -x "$JAVA_HOME/bin/javac" ] && [ -x "$JAVA_HOME/bin/jar" ]; then
    JDK="$JAVA_HOME"
else
    for candidate in "$HOME/Projects/android-emu/jdk" "$PWD/jdk" /tmp/opencode/jdk /usr/lib/jvm/*/; do
        if [ -x "${candidate%/}/bin/javac" ] && [ -x "${candidate%/}/bin/jar" ]; then
            JDK="${candidate%/}"
            break
        fi
    done
fi
if [ -z "$JDK" ]; then
    echo "error: no full JDK found (need javac + jar)." >&2
    echo "       Set JAVA_HOME to a JDK and retry." >&2
    exit 1
fi
export JAVA_HOME="$JDK"
export PATH="$JAVA_HOME/bin:$PATH"

# The Android SDK, with the same fallback as scripts/android-emu.sh.
SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Projects/android-emu/android-sdk}}"
if [ ! -d "$SDK" ]; then
    echo "error: Android SDK not found at $SDK (set ANDROID_HOME)." >&2
    exit 1
fi
export ANDROID_HOME="$SDK"
export ANDROID_SDK_ROOT="$SDK"

OUTPUT="dist/android/Ohrbob-${VERSION}.apk"
BUILT="Ohrbob.App.Android/bin/Release/net10.0-android/${APP_ID}-Signed.apk"

echo "=> Building Ohrbob ${VERSION} as an Android APK"
echo "=> Using JDK $JAVA_HOME"
echo "=> Using Android SDK $ANDROID_HOME"

# EmbedAssembliesIntoApk makes the APK self-contained, so it runs without the
# fast-deployment machinery (see scripts/android-emu.sh).
dotnet build "$PROJECT" \
    --configuration Release \
    -p:EmbedAssembliesIntoApk=true \
    -p:ApplicationDisplayVersion="$VERSION" \
    -p:ApplicationVersion="$VERSION_CODE" \
    -p:AndroidSdkDirectory="$ANDROID_HOME" \
    -p:JavaSdkDirectory="$JAVA_HOME"

if [ ! -f "$BUILT" ]; then
    echo "error: expected APK not found at $BUILT" >&2
    exit 1
fi

echo "=> Copying the APK to $OUTPUT..."
mkdir -p dist/android
cp "$BUILT" "$OUTPUT"

echo "========================================================"
echo "=> Built $OUTPUT"
echo "=> Install it with: adb install -r '$OUTPUT'"
