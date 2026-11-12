# Publishing to the Amazon Appstore

The `scripts/publish-amazon.sh` script builds, signs, and uploads the Ohrbob
Android APK to the Amazon Appstore.

Amazon's [App Submission API](https://developer.amazon.com/docs/app-submission-api/overview.html)
is similar to the Google Play API, with two important differences:

- It accepts **APKs only**, not AABs.
- It can only update an app that already exists in the Appstore. The first
  version has to be submitted once manually in the Developer Console.

## Prerequisites

The script needs `curl`, `jq`, the .NET 10 SDK with the `android` workload, a
full JDK, and the Android SDK (the same requirements as the other Android
scripts).

## 1. Signing Credentials (Keystore)

Use the same release keystore as Google Play. Amazon does not re-sign APKs, so
this key is what users receive.

1. Place the keystore in the repository root as `release.keystore`.
2. Export its password before running the script:
   ```bash
   export ANDROID_KEYSTORE_PASS="your_password"
   ```
   If the alias or key password differs from the defaults (`ohrbob` and the
   keystore password), also set `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASS`.

## 2. Upload Credentials (Amazon Security Profile)

Amazon uses Login With Amazon OAuth instead of a service-account JSON file.

1. Sign in to the [Amazon Developer Console](https://developer.amazon.com/login.html)
   with an account that has **Admin** permissions.
2. Go to **My Settings → API Access**.
3. Select **App Submission API** and click **Create a new security profile**.
4. Open the profile's **Web Settings** tab and copy the **Client ID** and
   **Client Secret**. The Client ID starts with
   `amzn1.application-oa2-client.` — the shorter `amzn1.application.…`
   value is the security-profile/application key, not the client ID.
5. Back on the **API Access** page, attach the security profile to the
   App Submission API.
6. Export the credentials in your terminal (or `.envrc`):
   ```bash
   export AMAZON_CLIENT_ID="amzn1.application-oa2-client...."
   export AMAZON_CLIENT_SECRET="...."
   ```

## 3. App ID

The API identifies the app by Amazon's **App ID**, not by the Android package
name.

1. In the Developer Console, go to **My Apps** and open your app.
2. On the **Upload Your App File** screen, scroll to **Additional information**.
3. Copy the **App ID** (starts with `amzn1.devportal.mobileapp.`) and export it:
   ```bash
   export AMAZON_APP_ID="amzn1.devportal.mobileapp...."
   ```

## 4. First Submission

The API cannot create a new app listing. Before the script can update it, the
app must exist in the Amazon Appstore. Submit the first version manually in the
Developer Console.

That first APK must be signed with `release.keystore` (the same key the script
uses), otherwise later updates signed by the script will be rejected. Build it
with the same `AndroidKeyStore` signing properties the script passes;
`scripts/build-android-apk.sh` is debug-signed and only meant for sideloading.

## Usage

Run the script from anywhere in the repository. The version defaults to the
newest git tag without its leading `v`.

```bash
# Upload the version from the latest git tag
./scripts/publish-amazon.sh

# Upload a specific version
./scripts/publish-amazon.sh 1.2.3
```

The script:

1. Builds a release-signed APK with a version code derived from the version
   (`1.2.3` → `10203`).
2. Creates a new Amazon *edit* (or reuses one left open by a failed run).
3. Replaces the existing APK in that edit, or uploads one if there is none.
4. Commits the edit. Amazon then puts the update into review.

There is no track parameter: Amazon has no internal/beta/production tracks.
Staged rollouts are configured separately in the Developer Console.

Amazon rejects a version code that is not higher than the previous APK's, so
tags must increase (`v1.2.3` → `v1.2.4`).

Store screenshots are in `art/amazon-screenshots/` (720×1280 PNG). Upload them
in the Developer Console under the app's **Images** section.

## Troubleshooting

- **HTTP 412 Precondition Failed** — the app is currently in review or
  otherwise pending. Wait until it is live, then retry.
- **`Cannot create a new 'edit' for the app in its current state`** — the app
  does not exist in the Appstore yet; submit the first version manually.
- **`No app found with the entered Inputs`** — `AMAZON_APP_ID` is wrong. It is
  the `amzn1.devportal.mobileapp....` value from the console, not
  `org.ohrbob.App`.
- **403 "Request is not authorized"** — the access token expired (they last
  one hour); just rerun the script.
