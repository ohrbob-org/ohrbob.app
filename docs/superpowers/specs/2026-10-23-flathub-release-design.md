# Flathub release — Ohrbob desktop client

- **Date:** 2026-10-23
- **Tracker:** issue #5 (`ohrbob/ohrbob.app`)
- **Status:** draft for approval

## Intent and why

Publish the Ohrbob desktop client (`Ohrbob.App.Desktop`) to Flathub so Linux users can
install and update it from their desktop store, instead of building `build-flatpak.sh`
themselves. Today the repo can only produce a *local* Flatpak, built with network access.
Flathub builds in a fully-enforced sandbox and only from declared, public, offline
sources, so several things have to change.

## Decisions already made (kai.wegner, issue #5 comment)

| Decision | Value |
| --- | --- |
| License | `GPL-3.0-or-later` |
| Public source | `https://github.com/ohrbob-org/ohrbob.app` (private now; made public before submitting) |
| App ID | `org.ohrbob.ohrbob` |
| Domain for verification | `ohrbob.org` (owned; resolves and serves HTTPS) |
| Manifest authorship | The manifest is **local-only** (mine, for testing). kai.wegner writes the submitted copy. |
| Versioning | Git tags from now on; SemVer, starting at `0.0.1` |
| Architectures | `x86_64` **and** `aarch64` |
| Source of truth | Gogs stays the development mirror; GitHub becomes the public source |
| Screenshots | Need new Linux desktop captures (to be made) |

`org.ohrbob.ohrbob` is a legal Flathub ID: it has 3 components, the domain portion
(`org.ohrbob`) is lowercase, and it does **not** end in a generic term (the earlier
`org.ohrbob.app` would have, and is why it was rejected). The reverse-DNS reversal still
yields `ohrbob.org`.

## Goals

- A Flathub submission that passes review: offline build, public pinned sources, valid
  AppStream, minimal permissions, correct license metadata.
- `build-flatpak.sh` keeps working for local development.

## Non-goals

- Other stores/archives; Windows, macOS, iOS, Android packaging.
- Fixing the runtime UX bugs found while testing the Flatpak build (wrong launcher
  icon, tiny cursor, MPRIS registration) beyond what packaging forces. The cursor item
  **is** touched because it drives the `--filesystem` question.

## Anti-goals

- Shipping a manifest that contains AI-generated or AI-assisted content — Flathub's
  Generative AI policy forbids it ("disclosure does not exempt"). See the authorship
  split in section H.

## Constraints (external, non-negotiable)

1. **No network during the Flathub build.** Every dependency is a declared source.
2. **Built entirely from source.** No precompiled binaries in the submission.
3. **Runtime must be the latest** available at submission time.
4. **Manifest must be human-authored.** Flathub's Generative AI policy hard-forbids
   AI-generated or AI-assisted content in the *manifest*. Other packaging material
   (metainfo, desktop file, generated source lists) is not forbidden but must be
   *disclosed*. The strict prohibition is the manifest specifically.
5. **The app ID must match** the AppStream `<id>`, the installed desktop file name, the
   icon name, and the manifest `app-id`/`command`.
6. **Sources need public URLs** with integrity data (tag + commit / sha256).

## The work

### A. App ID rename `app.ohrbob.Ohrbob` → `org.ohrbob.ohrbob`

Every occurrence moves together (verified by grep):

- `app.ohrbob.Ohrbob.yml` → rename file to `org.ohrbob.ohrbob.yml`; change `app-id`,
  `command`, the desktop install path, and the icon install path.
- `app.ohrbob.Ohrbob.desktop` → rename to `org.ohrbob.ohrbob.desktop`; `Exec=` and
  `Icon=` become `org.ohrbob.ohrbob`. `MimeType=x-scheme-handler/ohrbob;` is unchanged
  (the URI scheme stays `ohrbob`).
- `Ohrbob.App/App.axaml:4` — the window/resource `Name`.
- `Ohrbob.App.Desktop/MprisMediaSession.cs:222-224` — the desktop id handshake for MPRIS
  (DBus `DesktopEntry`).
- `Ohrbob.App.Desktop/Info.plist` (lines ~16, ~33) — macOS bundle id; rename for
  consistency (not required by Flathub, but the ID should be one value repo-wide).
- `Ohrbob.App.Tests/Desktop/MprisMediaSessionTests.cs:220,229` — the test locates
  `app.ohrbob.Ohrbob.desktop`; update.
- `build-flatpak.sh`, `howto.flatpak.md` — command/manifest name.

The manifest's `ln -s /app/bin/ohrbob/Ohrbob.App.Desktop /app/bin/<command>` symlink must
use the new command name.

### B. License

- `LICENSE` (GPL-3.0 verbatim) added. **Done.**
- Install it in the manifest to `$FLATPAK_DEST/share/licenses/org.ohrbob.ohrbob`.
- Declare the SPDX expression `GPL-3.0-or-later` in the AppStream metainfo.
- The nested dependency (NuGet packages) licenses are separate; the manifest style guide
  expects per-module license files where applicable.

### C. Offline build

- Remove `build-options.build-args: [--share=network]`.
- Generate `nuget-sources.json` with
  `flatpak-dotnet-generator.py` (from flatpak-builder-tools/dotnet) against
  `Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj`, add it as a manifest source.
- Change the build command to `dotnet publish … --no-restore` so the restore reads the
  prefetched sources.

### D. Manifest `org.ohrbob.ohrbob.yml`

- Runtime → the latest Freedesktop runtime (see builder calls), keeping
  `sdk-extension: org.freedesktop.Sdk.Extension.dotnet10` (exists for 24.08/25.08/26.08)
  and the `DOTNET_ROOT`/path settings.
- Source becomes a pinned git source (tag + commit) pointing at the public GitHub repo,
  not `type: dir, path: .`. Keep the `skip` intent only where it still applies.
- Add `cleanup` rules for build leftovers / debug symbols.
- Keep `finish-args`, minus the permission changes below; add the OpenURI portal
  `--talk-name` if launching links in the sandboxed browser requires it.
- Keep `--own-name=org.mpris.MediaPlayer2.ohrbob` (bus name, unrelated to app ID).

### E. AppStream metainfo

New `org.ohrbob.ohrbob.metainfo.xml`, installed to `/app/share/metainfo/`. Needs:
component `<id>`, name, summary, description, `project_license` = `GPL-3.0-or-later`,
developer, `launchable`, `provides` (the `ohrbob://` scheme), screenshots (new Linux
desktop window captures, hosted at stable public URLs), `releases` (needs a tagged
release), and an OARS `content_rating`. Must pass `appstreamcli validate`.

### F. Architecture

Both `x86_64` and `aarch64` are built (no `flathub.json` arch limit). The publish command
is hardcoded `-r linux-x64`, so it must become architecture-aware — flatpak-builder exposes
`$FLATPAK_ARCH` (`x86_64` / `aarch64`), which maps to the .NET RID (`linux-x64` /
`linux-arm64`). Consequences:

- The single `nuget-sources.json` must carry the **runtime packs for both RIDs**.
- Per-arch differences, if any, use `only-arches` rather than duplicating modules.
- The app is self-contained, so each arch's output must include its own runtime.

Verified: `org.freedesktop.Sdk.Extension.dotnet10` exists for `aarch64` on `26.08`.

### G. Releases and updates

- Git tags from now on, SemVer, starting at `0.0.1`. Tag the commit the manifest pins, and
  keep the AppStream `<release>` in step.
- The manifest git source pins tag + commit; add `x-checker-data` so Flathub can propose
  updates.
- Periodic bumps: new tag → update the manifest ref → update the AppStream release entry.

### H. Submission mechanics

Fork `flathub/flathub`, add a folder `org.ohrbob.ohrbob/` containing the manifest,
metainfo, desktop file, icon, and `nuget-sources.json`, open a PR, let the test build run.

**Authorship split:** the manifest I produce is local-only scaffolding for building and
testing. kai.wegner writes the copy that goes into the submission PR, so the submitted
manifest is human-authored per Flathub's policy. Everything else here (metainfo, desktop
file, rename, source lists, screenshots) is mine to prepare.

## Builder calls (my judgement — changeable, flagged for review)

1. **Runtime:** use the latest Freedesktop runtime available at submission (currently
   `26.08`); the `dotnet10` extension exists for it.
2. **Arch:** build both `x86_64` and `aarch64` (no `flathub.json` limit). Publish becomes
   `$FLATPAK_ARCH`-aware; the NuGet source list carries both RIDs' runtime packs.
3. **Cursor-theme permissions:** drop `--filesystem=~/.icons:ro`; keep the XDG location
   (`--filesystem=xdg-data/icons:ro`) and adjust `XCURSOR_PATH` accordingly. This is the
   narrowest option that still addresses the cursor bug; local build + a reviewer opinion
   will confirm.
4. **Screen updates:** add `x-checker-data` for automatic update PRs.
5. **Self-contained publish (not framework-dependent).** Keep
   `dotnet publish … --self-contained true`, but the RID is chosen from `$FLATPAK_ARCH`
   (`linux-x64` / `linux-arm64`). Consequence: `nuget-sources.json` must capture the
   **runtime packs** for *both* RIDs (`Microsoft.NETCore.App.Runtime.linux-x64` and
   `…linux-arm64`, plus the AspNetCore equivalents), so generate it against restores for
   both. If offline restore still misses packs, the fallback is framework-dependent
   publish against the runtime the `dotnet10` extension provides — that changes the build
   command, the source list, `cleanup`, and app size, so it is a deliberate fallback, not
   the default.

Grounding (verified): `org.freedesktop.Sdk.Extension.dotnet10` exists on `26.08` for
x86_64, and every declared NuGet dependency (including `SoundFlow 1.4.1`) is on
nuget.org — so runtime choice and public package availability are settled.

## Action items

All design questions are answered (see the decisions table). The remaining actions, by
owner:

| Action | Owner |
| --- | --- |
| Scrub secrets from git history | kai.wegner |
| Make the GitHub repo public | kai.wegner |
| Capture Linux desktop screenshots | to schedule |
| Author the submitted manifest copy | kai.wegner |
| Everything under "The work" | agent (local-only) |

## Verification plan

- Build locally with the host `flatpak-builder`, with the network source removed, to
  prove the offline path works.
- `appstreamcli validate org.ohrbob.ohrbob.metainfo.xml`.
- `flatpak-builder-lint` on the manifest, as Flathub CI would.
- Confirm the app launches from the installed Flatpak and the `ohrbob://` scheme and
  MPRIS still work after the rename.
