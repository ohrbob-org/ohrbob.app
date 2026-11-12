#!/bin/bash
# Builds, signs, and uploads the Android APK to the Amazon Appstore.
#
# Usage: ./scripts/publish-amazon.sh [version]
# Example: ./scripts/publish-amazon.sh 1.2.3
#
# The Amazon Appstore Submission API only accepts APKs, not AABs, and it can
# only update an app that already exists in the store. The first version must
# be submitted once through the Developer Console.
#
# --- CREDENTIALS SETUP ---
#
# 1. Signing (Keystore):
#    Same keystore as Google Play (release.keystore in the project root):
#      export ANDROID_KEYSTORE_PASS="your_password"
#      export ANDROID_KEY_ALIAS="ohrbob"
#      export ANDROID_KEY_PASS="your_password"
#
# 2. Uploading (Amazon Security Profile):
#    - Developer Console -> My Settings -> API Access.
#    - Create a security profile under "App Submission API".
#    - Attach it to the API, then copy its Client ID and Client Secret.
#      export AMAZON_CLIENT_ID="amzn1.application-oa2-client...."
#      export AMAZON_CLIENT_SECRET="...."
#    - Open your app in the Developer Console -> My Apps, and scroll to
#      "Additional information" to copy the App ID (amzn1.devportal.mobileapp....).
#      export AMAZON_APP_ID="amzn1.devportal.mobileapp...."
#
# 3. Dependencies:
#    curl and jq.
#
# See howto.amazon.md for the full walkthrough.
set -euo pipefail

cd "$(dirname "$0")/.."

VERSION="${1:-$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || echo "0.0.0")}"

APP_ID="${AMAZON_APP_ID:-}"
PROJECT="Ohrbob.App.Android/Ohrbob.App.Android.csproj"
KEYSTORE="${ANDROID_KEYSTORE_FILE:-release.keystore}"
KEYSTORE_PASS="${ANDROID_KEYSTORE_PASS:-}"
KEY_ALIAS="${ANDROID_KEY_ALIAS:-ohrbob}"
KEY_PASS="${ANDROID_KEY_PASS:-$KEYSTORE_PASS}"
CLIENT_ID="${AMAZON_CLIENT_ID:-}"
CLIENT_SECRET="${AMAZON_CLIENT_SECRET:-}"
API="https://developer.amazon.com/api/appstore/v1/applications/$APP_ID"

fail() {
    echo "error: $*" >&2
    exit 1
}

case "$CLIENT_ID" in
    amzn1.application-oa2-client.*) ;;
    '') fail "AMAZON_CLIENT_ID is not set." ;;
    *) fail "AMAZON_CLIENT_ID is not an LWA client ID (it should start with amzn1.application-oa2-client.)." ;;
esac
[ -n "$KEYSTORE_PASS" ] || fail "ANDROID_KEYSTORE_PASS is not set."
[ -f "$KEYSTORE" ] || fail "Keystore not found at $KEYSTORE. See setup instructions in script."
[ -n "$CLIENT_SECRET" ] || fail "AMAZON_CLIENT_SECRET is not set."
[ -n "$APP_ID" ] || fail "AMAZON_APP_ID is not set (Developer Console -> My Apps -> your app -> Additional information)."
command -v jq >/dev/null 2>&1 || fail "jq is not installed."

VERSION_CODE="$(printf '%s' "$VERSION" | awk -F. '{ printf "%d%02d%02d", $1, $2, $3 }' 2>/dev/null || true)"
case "$VERSION_CODE" in ''|*[!0-9]*) VERSION_CODE=1 ;; esac

echo "=> Building and signing Ohrbob $VERSION (code $VERSION_CODE) for the Amazon Appstore..."

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
[ -n "$JDK" ] || fail "no full JDK found. Set JAVA_HOME."
export JAVA_HOME="$JDK"

SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Projects/android-emu/android-sdk}}"
[ -d "$SDK" ] || fail "Android SDK not found at $SDK."
export ANDROID_HOME="$SDK"

dotnet build "$PROJECT" \
    --configuration Release \
    -p:AndroidPackageFormat=apk \
    -p:EmbedAssembliesIntoApk=true \
    -p:ApplicationDisplayVersion="$VERSION" \
    -p:ApplicationVersion="$VERSION_CODE" \
    -p:AndroidSdkDirectory="$ANDROID_HOME" \
    -p:JavaSdkDirectory="$JAVA_HOME" \
    -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$(realpath "$KEYSTORE")" \
    -p:AndroidSigningStorePass="$KEYSTORE_PASS" \
    -p:AndroidSigningKeyAlias="$KEY_ALIAS" \
    -p:AndroidSigningKeyPass="$KEY_PASS"

BUILT="Ohrbob.App.Android/bin/Release/net10.0-android/org.ohrbob.App-Signed.apk"
OUTPUT="dist/android/Ohrbob-${VERSION}.apk"

[ -f "$BUILT" ] || fail "expected APK not found at $BUILT"

mkdir -p dist/android
cp "$BUILT" "$OUTPUT"
echo "=> Built and signed APK: $OUTPUT"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

AUTH="$(curl -sS -X POST https://api.amazon.com/auth/o2/token \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode grant_type=client_credentials \
    --data-urlencode client_id="$CLIENT_ID" \
    --data-urlencode client_secret="$CLIENT_SECRET" \
    --data-urlencode scope='appstore::apps:readwrite' 2>/dev/null || true)"
TOKEN="$(printf '%s' "$AUTH" | jq -r '.access_token // empty' 2>/dev/null || true)"
[ -n "$TOKEN" ] || fail "could not get an Amazon access token: ${AUTH:-no response}"

# GET/POST/PUT with auth; fails on non-2xx and leaves the headers in $TMP/headers.
request() {
    local method="$1" url="$2"
    shift 2
    local raw code body
    raw="$(curl -sS -X "$method" "$url" \
        -H "Authorization: Bearer $TOKEN" \
        -D "$TMP/headers" \
        -w $'\n%{http_code}' "$@")" || fail "network error: $method $url"
    code="${raw##*$'\n'}"
    body="${raw%$'\n'*}"
    [ "$code" -ge 200 ] && [ "$code" -lt 300 ] || fail "$method $url returned HTTP $code: $body"
    printf '%s' "$body"
}

etag() {
    awk 'tolower($1) == "etag:" { print $2 }' "$TMP/headers" | tr -d '\r'
}

# Reuse an edit left open by a failed run, otherwise start a new one.
EDIT_ID="$(curl -sS -H "Authorization: Bearer $TOKEN" "$API/edits" | jq -r '.id // empty' 2>/dev/null || true)"
if [ -z "$EDIT_ID" ]; then
    EDIT_ID="$(request POST "$API/edits" -H 'Content-Type: application/x-www-form-urlencoded' | jq -r '.id')"
fi
echo "=> Using Amazon edit $EDIT_ID"

APK_ID="$(request GET "$API/edits/$EDIT_ID/apks" | jq -r '.[0].id // empty')"
if [ -n "$APK_ID" ]; then
    request GET "$API/edits/$EDIT_ID/apks/$APK_ID" >/dev/null
    echo "=> Replacing existing APK $APK_ID..."
    request PUT "$API/edits/$EDIT_ID/apks/$APK_ID/replace" \
        -H "If-Match: $(etag)" \
        -H 'Content-Type: application/vnd.android.package-archive' \
        --data-binary "@$OUTPUT" >/dev/null
else
    echo "=> Uploading APK..."
    request POST "$API/edits/$EDIT_ID/apks/upload" \
        -H 'Content-Type: application/vnd.android.package-archive' \
        -H "fileName: $(basename "$OUTPUT")" \
        --data-binary "@$OUTPUT" >/dev/null
fi

request GET "$API/edits/$EDIT_ID" >/dev/null
request POST "$API/edits/$EDIT_ID/commit" -H "If-Match: $(etag)" >/dev/null

echo "========================================================"
echo "=> Successfully uploaded $OUTPUT to the Amazon Appstore"
echo "=> Amazon will review the update; watch the Developer Console for its status."
