# Publishing to Google Play

The `scripts/publish-google-play.sh` script automates building, signing, and uploading the Ohrbob Android App Bundle (AAB) to the Google Play Store.

## Prerequisites

You need `fastlane` installed to handle the Play Store API communication securely.

**On Ubuntu/Debian:**
```bash
sudo apt install ruby-dev
sudo gem install fastlane
```

**On Fedora:**
```bash
sudo dnf install ruby-devel
sudo gem install fastlane
```

## 1. Signing Credentials (Keystore)

Google Play requires Android App Bundles to be cryptographically signed.

1. Place your release keystore in the repository root as `release.keystore`. 
   *(If you need to generate a new one, run: `keytool -genkey -v -keystore release.keystore -alias ohrbob -keyalg RSA -keysize 2048 -validity 10000`)*
2. Set the keystore password as an environment variable in your terminal before running the script:
   ```bash
   export ANDROID_KEYSTORE_PASS="your_password"
   ```

*(Advanced: If your key alias or key password differs from the defaults, you can also set `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASS`).*

## 2. Upload Credentials (Google Play API)

Google recently removed the old "API Access" page from the Play Console. The new process involves creating a Service Account directly in Google Cloud and inviting it as a user to your Play Console.

1. Go to the [Google Cloud Console](https://console.cloud.google.com) and create or select a project.
2. Enable the **Google Play Android Developer API** for your project. (You can search for it in the top search bar, or go to APIs & Services -> Enable APIs and Services).
3. Go to **IAM & Admin -> Service Accounts** and click **Create Service Account**.
4. Once created, click on the new service account, go to the **Keys** tab, click **Add Key -> Create new key**, choose **JSON**, and download it.
5. Rename the downloaded file to `play-store-key.json` and place it in the repository root.
6. Copy the **email address** of the service account you just created.
7. Go to the [Google Play Console](https://play.google.com/console).
8. In the left menu, go to **Users and permissions**.
9. Click **Invite new users** and paste the service account email address.
10. In the Permissions section, grant it the **"Release to production, exclude devices, and use Play App Signing"** permission, and invite the user.

## Usage

Run the script from anywhere in the repository. It accepts the release track (default: `internal`) and the version (default: parsed from git tags).

```bash
# Upload to the internal testing track (default)
./scripts/publish-google-play.sh internal

# Upload a specific version to the beta track
./scripts/publish-google-play.sh beta 1.2.3

# Upload to production
./scripts/publish-google-play.sh production
```