# Ohrbob Flatpak Packaging

This repository includes a Flatpak manifest (`org.ohrbob.ohrbob.yml`) to build and run the Linux desktop app as a Flatpak.

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

## Build Recipe

The steps that run *inside* the Flatpak sandbox live in `scripts/build-flatpak-app.sh`, and the manifest only calls it:

```yaml
modules:
  - name: ohrbob
    buildsystem: simple
    build-commands:
      - bash scripts/build-flatpak-app.sh
```

Keeping the recipe in the repository instead of inline in the manifest gives the packaging a single source of truth: the same script builds the app for local `flatpak-builder` runs and for the Flathub submission. It maps the Flatpak architecture to a .NET RID, restores NuGet packages from the vendored `nuget-sources.json`, publishes a self-contained build, and installs the desktop file, AppStream metadata and icon. The license is not installed by the script — flatpak-builder records `LICENSE` from the source root automatically.

Note that `scripts/build-flatpak.sh` is a different, host-side helper: it only drives `flatpak-builder` against the manifest (see below).

## How to Build and Launch Locally

### Native Build (Recommended)

This repository includes a script that builds the flatpak completely natively as your standard user, using the host `flatpak-builder`. **No Docker or sudo required.**

```bash
# 1. Run the build script
./scripts/build-flatpak.sh

# 2. Launch the installed app!
flatpak run org.ohrbob.ohrbob
```

### Directly on Host

You need `flatpak` and `flatpak-builder` installed on your system.
You also need the Flathub remote and the required SDKs:

```bash
flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install flathub org.freedesktop.Platform//26.08
flatpak install flathub org.freedesktop.Sdk//26.08
flatpak install flathub org.freedesktop.Sdk.Extension.dotnet10//26.08
```

## How to Publish to Flathub

Flathub builds in a fully-enforced sandbox with **no network access during the build**, so every dependency must be declared as a source and fetched beforehand. This manifest is already set up for that: `nuget-sources.json` carries every NuGet package (for both `linux-x64` and `linux-arm64`), and the build restores with `--source nuget-sources` and publishes with `--no-restore`.

> **Note:** the manifest in this repository is local development scaffolding, built from the working tree (`type: dir`). The copy submitted to Flathub is authored by hand and replaces the `dir` source with a pinned `git` source (tag + commit) pointing at the public repository. It calls the same `scripts/build-flatpak-app.sh`, so the build recipe stays in one place.

### 1. Offline Dependencies

Regenerate `nuget-sources.json` whenever dependencies change:

```bash
python3 flatpak-dotnet-generator.py \
  nuget-sources.json Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj \
  --freedesktop 26.08 --dotnet 10 --runtime linux-x64 linux-arm64
```

Get the generator from [flatpak-builder-tools](https://github.com/flatpak/flatpak-builder-tools/tree/master/dotnet).

### 2. AppStream Metadata

Flathub requires an AppStream file — `org.ohrbob.ohrbob.metainfo.xml` — installed to `/app/share/metainfo/`. It must pass `appstreamcli validate`.

### 3. Submit to Flathub

1. Fork the [flathub/flathub](https://github.com/flathub/flathub) repository on GitHub.
2. Create a new branch and add the manifest (and related files: desktop file, icon, AppStream metadata, `nuget-sources.json`) to a new folder named `org.ohrbob.ohrbob`.
3. Push the branch and open a Pull Request against `flathub/flathub`.
4. The Flathub bot will perform a test build. Once reviewers approve it, a dedicated repository (`github.com/flathub/org.ohrbob.ohrbob`) is created for you to manage future updates.
