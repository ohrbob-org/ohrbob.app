# Ohrbob Flatpak Packaging

This repository includes a Flatpak manifest (`app.ohrbob.Ohrbob.yml`) to build and run the Linux desktop app as a Flatpak.

## Excluding Local Files From the Build

The manifest uses a whole-repo `dir` source (`path: .`), which would otherwise copy everything in the working tree — including local state that must not end up in the build. The source therefore declares a `skip` list:

```yaml
sources:
  - type: dir
    path: .
    skip:
      - .git
      - .cache
      - .local
      - .idea
      - .superpowers
      - .env
      - .envrc
```

If you add another dot-folder or dot-file that shouldn't be bundled, add it here too. The same paths are also listed in `.gitignore` so they stay out of version control. Note that `skip` on `dir` sources requires flatpak-builder >= 1.4.0.

## How to Build and Launch Locally

### Native Build (Recommended)

This repository includes a script that builds the flatpak completely natively as your standard user, using the sandboxed `flatpak-builder`. **No Docker or sudo required.**

```bash
# 1. Run the build script
./build-flatpak.sh

# 2. Launch the installed app!
flatpak run app.ohrbob.Ohrbob
```

### Directly on Host

You need `flatpak` and `flatpak-builder` installed on your system.
You also need the Flathub remote and the required SDKs:

```bash
flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install flathub org.freedesktop.Platform//24.08
flatpak install flathub org.freedesktop.Sdk//24.08
flatpak install flathub org.freedesktop.Sdk.Extension.dotnet10//24.08
```

## How to Publish to Flathub

Flathub has strict requirements for publishing applications. The most important one for .NET apps is that **network access is blocked during the build phase**. This means our current local manifest (which uses `--share=network`) will not be accepted as-is on Flathub.

### 1. Make the Build Offline

To submit to Flathub, you must use the [flatpak-builder-tools](https://github.com/flatpak/flatpak-builder-tools/tree/master/dotnet) to generate an offline source list of all your NuGet dependencies.

1. Clone `flatpak-builder-tools`.
2. Run the `dotnet/flatpak-dotnet-generator.py` script on your project to create a `nuget-sources.json` file:
   ```bash
   ./flatpak-dotnet-generator.py nuget-sources.json path/to/Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj
   ```
3. Update `app.ohrbob.Ohrbob.yml`:
   - Remove `--share=network` from `build-options`.
   - Add the generated `nuget-sources.json` to your manifest.
   - Adjust the `dotnet publish` command to build completely offline (`--no-restore`).

### 2. Add AppStream Metadata

Flathub requires an AppStream XML file (e.g., `app.ohrbob.Ohrbob.metainfo.xml`) that includes a description, screenshots, and release notes. You will need to create this file and install it to `/app/share/metainfo/` in the build commands.

### 3. Submit to Flathub

1. Fork the [flathub/flathub](https://github.com/flathub/flathub) repository on GitHub.
2. Create a new branch and add your `app.ohrbob.Ohrbob.yml` (and related files like the desktop file, icons, and AppStream metadata) to a new folder named `app.ohrbob.Ohrbob`.
3. Push the branch and open a Pull Request against `flathub/flathub`.
4. The Flathub bot will perform a test build. Once reviewers approve it, a dedicated repository (`github.com/flathub/app.ohrbob.Ohrbob`) will be created for you to manage future updates.
