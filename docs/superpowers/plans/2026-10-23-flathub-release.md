# Flathub Release Preparation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prepare the Ohrbob desktop client so a human can submit it to Flathub, and provide a local-only manifest that proves the offline build works.

**Architecture:** Rename the app ID repo-wide to `org.ohrbob.ohrbob`, add AppStream metainfo, generate offline NuGet sources, and rewrite the Flatpak manifest to build offline from pinned sources on the Freedesktop `26.08` runtime for both `x86_64` and `aarch64`. The manifest produced here is local-only scaffolding; kai.wegner authors the submitted copy.

**Tech Stack:** Flatpak / flatpak-builder, Freedesktop 26.08 runtime + SDK, `org.freedesktop.Sdk.Extension.dotnet10`, .NET 10 (Avalonia desktop, self-contained), `flatpak-builder-tools/dotnet`, AppStream (`appstreamcli`), `flatpak-builder-lint`.

**Spec:** `docs/superpowers/specs/2026-10-23-flathub-release-design.md`

## Global Constraints

- App ID is `org.ohrbob.ohrbob` in every place an ID appears (manifest `app-id`, `command`, desktop file name, icon name, AppStream `<id>`, MPRIS `DesktopEntry`).
- `MimeType=x-scheme-handler/ohrbob;` and the `ohrbob` URI scheme are unchanged.
- License is `GPL-3.0-or-later`; metainfo `metadata_license` is `CC0-1.0`.
- Runtime `org.freedesktop.Platform//26.08`, SDK `org.freedesktop.Sdk//26.08`, extension `org.freedesktop.Sdk.Extension.dotnet10//26.08`.
- Architectures: `x86_64` **and** `aarch64` (no `flathub.json`).
- `.NET` RID map: `x86_64` → `linux-x64`, `aarch64` → `linux-arm64`.
- No `--share=network` in `build-options`.
- Keep `--own-name=org.mpris.MediaPlayer2.ohrbob`.
- Cursor access: keep `--filesystem=xdg-data/icons:ro`, drop `--filesystem=~/.icons:ro`.
- Publish stays `--self-contained true`.
- SemVer; first release tag is `v0.0.1`.
- The manifest written here is **local-only** and is not the submitted artifact.

## Review Focus

- **Offline restore completeness** — a package missing from `nuget-sources.json` (especially the RID runtime packs) makes `dotnet restore` fail only when the network is gone. Task 5's offline build pins it.
- **Stale ID references after the rename** — any leftover `app.ohrbob.Ohrbob` breaks the MPRIS/desktop handshake or the window identity. Task 1's rename test pins it.
- **aarch64 path** — the RID/arch mapping is exercised only on aarch64, which this machine cannot build. Task 4 maps it explicitly; Task 5 can only verify x86_64.
- **Cursor theme resolution** — with `~/.icons` removed, a host cursor theme living only there will not resolve. Task 5's manual launch checks it.
- **AppStream/submission validity** — missing screenshots or a release entry with no matching tag makes the metainfo or `flatpak-builder-lint` fail at submission. Tasks 2, 7 and 8 pin it.

---

### Task 1: Rename the app ID to `org.ohrbob.ohrbob`

**Files:**
- Rename: `app.ohrbob.Ohrbob.desktop` → `org.ohrbob.ohrbob.desktop`
- Modify: `Ohrbob.App/App.axaml:4`
- Modify: `Ohrbob.App.Desktop/MprisMediaSession.cs:222-224`
- Modify: `Ohrbob.App.Desktop/Info.plist:16,33`
- Modify: `Ohrbob.App.Tests/Desktop/MprisMediaSessionTests.cs:220,229`

**Interfaces:**
- Consumes: nothing.
- Produces: the string `org.ohrbob.ohrbob` as the desktop file basename and MPRIS `DesktopEntry`. Later tasks install the desktop file under this name and reference it from the manifest and metainfo.

- [ ] **Step 1: Rename the desktop file and update its entries**

```bash
git mv app.ohrbob.Ohrbob.desktop org.ohrbob.ohrbob.desktop
```
In `org.ohrbob.ohrbob.desktop` set `Exec=org.ohrbob.ohrbob %u` and `Icon=org.ohrbob.ohrbob`; leave `MimeType=x-scheme-handler/ohrbob;` untouched.

- [ ] **Step 2: Point the test at the new filename**

In `Ohrbob.App.Tests/Desktop/MprisMediaSessionTests.cs`, `FindShippedDesktopFile` looks for `app.ohrbob.Ohrbob.desktop` (line 220) and reports `could not locate app.ohrbob.Ohrbob.desktop ...` (line 229). Change both to `org.ohrbob.ohrbob.desktop`.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter FullyQualifiedName~DesktopEntryMatchesTheShippedDesktopFileId`
Expected: FAIL — the test now finds `org.ohrbob.ohrbob.desktop` but the app still reports `app.ohrbob.Ohrbob`.

- [ ] **Step 4: Update the app-side ID**

- `Ohrbob.App.Desktop/MprisMediaSession.cs:224`: return `"org.ohrbob.ohrbob"` and update the comment on line 222.
- `Ohrbob.App/App.axaml:4`: `Name="org.ohrbob.ohrbob"`.
- `Ohrbob.App.Desktop/Info.plist`: `CFBundleIdentifier` (line 16) and `CFBundleURLName` (line 33) → `org.ohrbob.ohrbob`.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj`
Expected: PASS (all tests), including `DesktopEntryMatchesTheShippedDesktopFileId`.

- [ ] **Step 6: Confirm no stale references remain**

Run: `grep -rn "app\.ohrbob\.Ohrbob" --include=* . | grep -v -E "bin/|obj/|\.git/|build-dir|\.flatpak-builder|\.cache/"`
Expected: no output (the manifest, `build-flatpak.sh` and `howto.flatpak.md` are handled in Tasks 4 and 6).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: rename app id to org.ohrbob.ohrbob

Refs #5"
```

---

### Task 2: Add the AppStream metainfo

**Files:**
- Create: `org.ohrbob.ohrbob.metainfo.xml`

**Interfaces:**
- Consumes: the desktop file basename from Task 1.
- Produces: `org.ohrbob.ohrbob.metainfo.xml`, installed by the manifest (Task 4) to `/app/share/metainfo/`; Task 7 adds screenshots and Task 8 adds the release entry.

- [ ] **Step 1: Write the metainfo skeleton**

Create `org.ohrbob.ohrbob.metainfo.xml` with exactly these values:

- `<id>` = `org.ohrbob.ohrbob`
- `<name>` = `Ohrbob`
- `<summary>` = `Player client for Ohrbob`
- `<metadata_license>` = `CC0-1.0`
- `<project_license>` = `GPL-3.0-or-later`
- `<description>` — two `<p>` paragraphs from the README's feature summary
- `<launchable type="desktop-id">org.ohrbob.ohrbob.desktop</launchable>`
- `<provides><binary>org.ohrbob.ohrbob</binary></provides>` (the `ohrbob://` scheme is expressed by the desktop file, not here)
- `<url type="homepage">https://ohrbob.org</url>`
- `<developer id="org.ohrbob"><name>Ohrbob</name></developer>`
- `<content_rating type="oars-1.1"/>`

Start with `<component type="desktop-application">`, UTF-8, 2-space indent, trailing newline.

- [ ] **Step 2: Validate**

Run: `appstreamcli validate org.ohrbob.ohrbob.metainfo.xml`
Expected: exits 0, no errors. Warnings about a missing screenshot are acceptable until Task 7.

- [ ] **Step 3: Commit**

```bash
git add org.ohrbob.ohrbob.metainfo.xml
git commit -m "docs: add AppStream metainfo

Refs #5"
```

---

### Task 3: Generate offline NuGet sources for both RIDs

**Files:**
- Create: `nuget-sources.json`

**Interfaces:**
- Consumes: nothing.
- Produces: `nuget-sources.json`, a flatpak-builder sources array consumed by Task 4's manifest.

- [ ] **Step 1: Install the build prerequisites**

```bash
flatpak install --user flathub org.freedesktop.Sdk//26.08 org.freedesktop.Sdk.Extension.dotnet10//26.08
```
Expected: both install into the user installation.

- [ ] **Step 2: Get the generator**

```bash
git clone --depth 1 https://github.com/flatpak/flatpak-builder-tools /tmp/opencode/flatpak-builder-tools
ls /tmp/opencode/flatpak-builder-tools/dotnet/flatpak-dotnet-generator.py
```

- [ ] **Step 3: Generate the sources file for both RIDs**

`--runtime` accepts multiple values, so one run covers both. The generator restores with
the RID, so the runtime packs are captured.

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
python3 /tmp/opencode/flatpak-builder-tools/dotnet/flatpak-dotnet-generator.py \
  --freedesktop 26.08 --dotnet 10 \
  --runtime linux-x64 linux-arm64 \
  nuget-sources.json Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj
jq 'length' nuget-sources.json
```
`--freedesktop`/`--dotnet` must be explicit: the tool defaults to `24.08` / `8`.

- [ ] **Step 4: Confirm it is a flatpak-builder sources array**

Run: `jq -e 'type == "array" and (.[0] | has("url") and has("sha512") and has("dest-filename") and has("dest"))' nuget-sources.json`
Expected: `true`

- [ ] **Step 5: Confirm the RID runtime packs are present**

Run: `jq -r '.[].dest' nuget-sources.json | grep -E "Microsoft\.(NETCore|AspNetCore)\.App\.Runtime\.linux-(x64|arm64)" | sort -u`
Expected: `Microsoft.NETCore.App.Runtime.linux-x64`, `Microsoft.NETCore.App.Runtime.linux-arm64`, `Microsoft.AspNetCore.App.Runtime.linux-x64`, `Microsoft.AspNetCore.App.Runtime.linux-arm64`.

- [ ] **Step 6: Commit**

```bash
git add nuget-sources.json
git commit -m "build(flatpak): add offline NuGet sources for x86_64 and aarch64

Refs #5"
```

---

### Task 4: Write the local-only manifest `org.ohrbob.ohrbob.yml`

**Files:**
- Create: `org.ohrbob.ohrbob.yml`
- Remove: `app.ohrbob.Ohrbob.yml`

**Interfaces:**
- Consumes: `org.ohrbob.ohrbob.desktop`, `org.ohrbob.ohrbob.metainfo.xml`, `nuget-sources.json`.
- Produces: the manifest Task 5 builds; `build-flatpak.sh` (Task 6) invokes it by name.

- [ ] **Step 1: Write the manifest**

Take the current `app.ohrbob.Ohrbob.yml` as the base and apply exactly these changes:

- `app-id` and `command`: `org.ohrbob.ohrbob`
- `runtime-version`: `'26.08'`; keep `sdk: org.freedesktop.Sdk` and `sdk-extensions: [org.freedesktop.Sdk.Extension.dotnet10]`
- `build-options`: delete the `build-args: [--share=network]` block; keep `append-path`, `append-ld-library-path`, `DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_ROOT`
- `XCURSOR_PATH`: `/run/host/user-share/icons:/run/host/share/icons`
- `finish-args`: remove `--filesystem=~/.icons:ro`, keep `--filesystem=xdg-data/icons:ro` and every other entry
- module `sources`: keep `type: dir, path: .` with the existing `skip` list, **plus** `- nuget-sources.json`
- module `build-commands` (order matters):

```yaml
      - |
        RID="linux-x64"; [ "$FLATPAK_ARCH" = "aarch64" ] && RID="linux-arm64"
        dotnet restore Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj -r "$RID" --source nuget-sources
        dotnet publish Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj -c Release -r "$RID" --self-contained true --no-restore -o /app/bin/ohrbob
      - ln -s /app/bin/ohrbob/Ohrbob.App.Desktop /app/bin/org.ohrbob.ohrbob
      - install -Dm644 org.ohrbob.ohrbob.desktop -t /app/share/applications/
      - install -Dm644 org.ohrbob.ohrbob.metainfo.xml -t /app/share/metainfo/
      - install -Dm644 art/ohrbob-logo.svg /app/share/icons/hicolor/scalable/apps/org.ohrbob.ohrbob.svg
      - install -Dm644 LICENSE -t /app/share/licenses/org.ohrbob.ohrbob/
```

- add a `cleanup` list: `- /lib/debug`, `- '*.a'` (keep it minimal; correct after the first build shows leftovers)

- [ ] **Step 2: Delete the old manifest**

```bash
git rm app.ohrbob.Ohrbob.yml
```

- [ ] **Step 3: Lint (best effort)**

Run: `pip install --user flatpak-builder-lint && flatpak-builder-lint manifest org.ohrbob.ohrbob.yml`
Expected: reports no critical errors. `type: dir` sources and missing appstream in the repo are expected warnings until the submitted copy (which uses a `git` source) is authored.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "build(flatpak): add offline, arch-aware manifest for org.ohrbob.ohrbob

Refs #5"
```

---

### Task 5: Verify the offline build and run

**Files:**
- No source changes; produces evidence only.

**Interfaces:**
- Consumes: the manifest and sources from Tasks 3 and 4.
- Produces: a locally installed `org.ohrbob.ohrbob` flatpak.

- [ ] **Step 1: Build offline**

Run: `flatpak-builder --user --install --force-clean build-dir org.ohrbob.ohrbob.yml`
Expected: completes with `Build & Install successful`; `dotnet restore` reports no network use and finds packages in `nuget-sources`.

- [ ] **Step 2: Confirm the installed ref**

Run: `flatpak info org.ohrbob.ohrbob`
Expected: `Ref: app/org.ohrbob.ohrbob/x86_64/...`, origin `ohrbob-origin`, user installation.

- [ ] **Step 3: Launch and check the risky paths**

Run: `flatpak run org.ohrbob.ohrbob`
Expected: window opens; after connecting to a server, the media widget/DBus appears. Check the cursor renders at the correct size (the `~/.icons` removal) and the `ohrbob://` scheme opens the app.

- [ ] **Step 4: Record evidence**

Write the build/verify output (build log tail, `flatpak info` output, and any screenshot of the running app) into the PR/issue #5 comment at the end of the plan. No commit for this task.

---

### Task 6: Update `build-flatpak.sh` and `howto.flatpak.md`

**Files:**
- Modify: `build-flatpak.sh:9,14`
- Modify: `howto.flatpak.md:3,36,64,71,76,78`

**Interfaces:**
- Consumes: the manifest name from Task 4.
- Produces: a working local entrypoint.

- [ ] **Step 1: Point the script at the new manifest**

In `build-flatpak.sh`, change the `flatpak-builder` invocation (line 9) to `org.ohrbob.ohrbob.yml` and the echo on line 14 to `flatpak run org.ohrbob.ohrbob`.

- [ ] **Step 2: Run it**

Run: `./build-flatpak.sh`
Expected: `Build & Install successful!`

- [ ] **Step 3: Update the docs**

In `howto.flatpak.md`, replace every `app.ohrbob.Ohrbob` reference with `org.ohrbob.ohrbob`, update the runtime install lines to `26.08`, and note that the local manifest is scaffolding while the submitted copy is human-authored.

- [ ] **Step 4: Commit**

```bash
git add build-flatpak.sh howto.flatpak.md
git commit -m "docs: point the local Flatpak workflow at org.ohrbob.ohrbob

Refs #5"
```

---

### Task 7: Capture desktop screenshots and wire them into the metainfo

**Files:**
- Create: `screenshots/*.png` (e.g. `listen.png`, `discover.png`)
- Modify: `org.ohrbob.ohrbob.metainfo.xml` (add `<screenshots>`)

**Interfaces:**
- Consumes: the running app (Task 5) and the metainfo (Task 2).
- Produces: `<screenshot>` entries whose `<image>` URLs Task 8 relies on for submission.

- [ ] **Step 1: Capture window screenshots on Linux**

With the app running (real session, or `xvfb-run`), capture three window-only shots at roughly `1000x700`, dark theme, connected to a server so real content shows. Save as `screenshots/listen.png` (default), `screenshots/discover.png`, `screenshots/profiles.png`.

- [ ] **Step 2: Add the screenshots block**

In `org.ohrbob.ohrbob.metainfo.xml`, before `<releases>`:

```xml
  <screenshots>
    <screenshot type="default">
      <image>https://raw.githubusercontent.com/ohrbob-org/ohrbob.app/v0.0.1/screenshots/listen.png</image>
      <caption>Playing a station</caption>
    </screenshot>
    <screenshot>
      <image>https://raw.githubusercontent.com/ohrbob-org/ohrbob.app/v0.0.1/screenshots/discover.png</image>
      <caption>Discovering stations</caption>
    </screenshot>
    <screenshot>
      <image>https://raw.githubusercontent.com/ohrbob-org/ohrbob.app/v0.0.1/screenshots/profiles.png</image>
      <caption>Profiles</caption>
    </screenshot>
  </screenshots>
```
(Call: host them on the public GitHub repo at the release tag. If the tag scheme changes, update these URLs.)

- [ ] **Step 3: Re-validate**

Run: `appstreamcli validate org.ohrbob.ohrbob.metainfo.xml`
Expected: exits 0; the missing-screenshot warning is gone.

- [ ] **Step 4: Commit**

```bash
git add screenshots org.ohrbob.ohrbob.metainfo.xml
git commit -m "docs: add desktop screenshots to the AppStream metainfo

Refs #5"
```

---

### Task 8: Cut the `v0.0.1` release

**Files:**
- Modify: `org.ohrbob.ohrbob.metainfo.xml` (add `<releases>`)
- Modify: `Ohrbob.App.Desktop/Info.plist` (version strings)

**Interfaces:**
- Consumes: the metainfo from Tasks 2 and 7.
- Produces: the git tag `v0.0.1` that the submitted manifest's git source and the screenshot URLs pin to.

- [ ] **Step 1: Add the release entry**

In `org.ohrbob.ohrbob.metainfo.xml`, add `<releases><release version="0.0.1" date="YYYY-MM-DD"><description><p>First release.</p></description></release></releases>` using the tag date.

- [ ] **Step 2: Make the macOS bundle version consistent**

In `Ohrbob.App.Desktop/Info.plist`, set `CFBundleShortVersionString` and `CFBundleVersion` to `0.0.1`.

- [ ] **Step 3: Commit and tag**

```bash
git add org.ohrbob.ohrbob.metainfo.xml Ohrbob.App.Desktop/Info.plist
git commit -m "chore: release v0.0.1

Refs #5"
git tag -a v0.0.1 -m "Ohrbob 0.0.1"
git push origin main
git push origin v0.0.1
git describe --tags
```

- [ ] **Step 4: Comment on issue #5**

Post the build evidence from Task 5, the tag, and the note that the submitted manifest is the human-authored copy replacing the `type: dir` source with a pinned `git` source at `v0.0.1`.

---

## Self-Review Notes

- **Spec coverage:** rename (A→Task 1), license metadata + install (B→Tasks 2, 4), offline build (C→Tasks 3, 4, 5), manifest rewrite (D→Task 4), metainfo (E→Tasks 2, 7, 8), dual arch (F→Tasks 3, 4), tags (G→Task 8), submission mechanics/authorship split (H→Task 8 note + the "local-only" global constraint). Screenshots (spec section H/action items) → Task 7. Secrets scrub and GitHub publication are kai.wegner's actions, tracked in #5, not this plan.
- **Not in this plan, by design:** the submitted manifest itself. It is human-authored; this plan only hands over the local scaffolding and the design.
