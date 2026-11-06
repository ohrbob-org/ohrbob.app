#!/bin/bash
# Builds, signs, and uploads the Android App Bundle (AAB) to Google Play.
#
# Usage: ./scripts/publish-google-play.sh [track] [version]
# Example: ./scripts/publish-google-play.sh internal 1.2.3
#
# --- CREDENTIALS SETUP ---
#
# 1. Signing (Keystore):
#    Create one if you don't have it:
#      keytool -genkey -v -keystore release.keystore -alias ohrbob -keyalg RSA -keysize 2048 -validity 10000
#    Save it as `release.keystore` in the root folder, and set these env vars:
#      export ANDROID_KEYSTORE_PASS="your_password"
#      export ANDROID_KEY_ALIAS="ohrbob"
#      export ANDROID_KEY_PASS="your_password"
#
# 2. Uploading (Google Play Service Account):
#    - Go to Google Cloud Console (https://console.cloud.google.com).
#    - Enable the "Google Play Android Developer API".
#    - Under IAM & Admin > Service Accounts, create a Service Account and a JSON key.
#    - Save the downloaded JSON file as `play-store-key.json` in the project root.
#    - Copy the service account's email address.
#    - Go to Google Play Console -> Users and permissions.
#    - Click "Invite new users", paste the email, and grant the
#      "Release to production, exclude devices, and use Play App Signing" permission.
#
# 3. Dependencies:
#    You need fastlane to run the upload: 
#      Ubuntu/Debian: sudo apt install ruby-dev
#      Fedora:        sudo dnf install ruby-devel
#      Then:          sudo gem install fastlane
set -euo pipefail

cd "$(dirname "$0")/.."

TRACK="${1:-internal}"
VERSION="${2:-$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || echo "0.0.0")}"

APP_ID="org.ohrbob.App"
PROJECT="Ohrbob.App.Android/Ohrbob.App.Android.csproj"
KEYSTORE="${ANDROID_KEYSTORE_FILE:-release.keystore}"
KEYSTORE_PASS="${ANDROID_KEYSTORE_PASS:-}"
KEY_ALIAS="${ANDROID_KEY_ALIAS:-ohrbob}"
KEY_PASS="${ANDROID_KEY_PASS:-$KEYSTORE_PASS}"
JSON_KEY="${GOOGLE_PLAY_JSON_KEY:-googleplaydeploymentkey.json}"

if [ ! -f "$KEYSTORE" ]; then
    echo "error: Keystore not found at $KEYSTORE. See setup instructions in script." >&2
    exit 1
fi
if [ -z "$KEYSTORE_PASS" ]; then
    echo "error: ANDROID_KEYSTORE_PASS is not set." >&2
    exit 1
fi
if [ ! -f "$JSON_KEY" ]; then
    echo "error: Google Play JSON key not found at $JSON_KEY. See setup instructions in script." >&2
    exit 1
fi
if ! command -v fastlane >/dev/null 2>&1; then
    echo "error: fastlane is not installed (sudo gem install fastlane)." >&2
    exit 1
fi

VERSION_CODE="$(printf '%s' "$VERSION" | awk -F. '{ printf "%d%02d%02d", $1, $2, $3 }' 2>/dev/null || true)"
case "$VERSION_CODE" in ''|*[!0-9]*) VERSION_CODE=1 ;; esac

echo "=> Building and signing Ohrbob $VERSION (code $VERSION_CODE) for Play Store..."

# Determine JDK/SDK (same logic as build-android-apk.sh)
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
    echo "error: no full JDK found. Set JAVA_HOME." >&2
    exit 1
fi
export JAVA_HOME="$JDK"

SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Projects/android-emu/android-sdk}}"
if [ ! -d "$SDK" ]; then
    echo "error: Android SDK not found at $SDK." >&2
    exit 1
fi
export ANDROID_HOME="$SDK"

# Build and Sign AAB
dotnet build "$PROJECT" \
    --configuration Release \
    -p:AndroidPackageFormat=aab \
    -p:ApplicationDisplayVersion="$VERSION" \
    -p:ApplicationVersion="$VERSION_CODE" \
    -p:AndroidSdkDirectory="$ANDROID_HOME" \
    -p:JavaSdkDirectory="$JAVA_HOME" \
    -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$(realpath "$KEYSTORE")" \
    -p:AndroidSigningStorePass="$KEYSTORE_PASS" \
    -p:AndroidSigningKeyAlias="$KEY_ALIAS" \
    -p:AndroidSigningKeyPass="$KEY_PASS"

BUILT="Ohrbob.App.Android/bin/Release/net10.0-android/${APP_ID}-Signed.aab"
OUTPUT="dist/android/Ohrbob-${VERSION}.aab"

if [ ! -f "$BUILT" ]; then
    echo "error: expected AAB not found at $BUILT" >&2
    exit 1
fi

mkdir -p dist/android
cp "$BUILT" "$OUTPUT"
echo "=> Built and signed AAB: $OUTPUT"

echo "=> Uploading to Google Play (track: $TRACK)..."
fastlane supply --package_name "$APP_ID" \
                --aab "$OUTPUT" \
                --json_key "$JSON_KEY" \
                --track "$TRACK"

echo "========================================================"
echo "=> Successfully uploaded $OUTPUT to Play Store track '$TRACK'"
