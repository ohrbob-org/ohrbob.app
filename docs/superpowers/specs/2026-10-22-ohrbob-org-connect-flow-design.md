# Ohrbob.org Connect Flow — Design

Date: 2026-10-22

## Intent and why

Connecting the Ohrbob app today is developer-oriented: the connect screen
defaults to `http://10.0.2.2:5002` (an Android-emulator address) with a
hardcoded token, and asks people to scan a QR code or paste a connection
deep link. There is no obvious "just connect me to Ohrbob" path.

We want the default way in to be the hosted service, ohrbob.org. The person
onboards there (creating an account if needed), and the app is connected by
a link handed back from the browser. Self-hosting stays possible, but moves
behind an "Advanced" option.

## Goals

- The app's default connect screen becomes hosted onboarding: a short
  explanation, **one primary button**, and a secondary **Advanced** action.
- The button opens `https://ohrbob.org/connect/app` in the system browser.
  The app then simply stays on the connect screen — no waiting state, no
  polling.
- The web app gains a **login-gated** page at `/connect/app`: a small
  centered card with a token **name** field and a token **scope** selector.
  Submitting creates an API key and redirects to
  `ohrbob://connect?server=<player base url>&token=<token>`.
- The app receives `ohrbob://connect?server=…&token=…` on **desktop (Linux,
  Windows, macOS), Android and iOS** and connects automatically — reusing the
  existing `MainViewModel.ConnectAsync` deep-link path.
- **Advanced** reveals today's card (deep-link paste, server URL, token,
  Connect). Its default server URL becomes the hosted player URL.

## Non-goals

- No rework of the existing **Account → API Keys** page or its QR flow; it
  stays as it is.
- No in-app "waiting for ohrbob.org…" state, spinner, or callback polling.
- No change to the streaming protocol, the `X-Stream-Token` header, or the
  meaning of server/token.
- No QR code on the new `/connect/app` page.
- No new account/registration flow — new people use the existing onboarding.

## Anti-goals

- The normal path must never require the person to type or copy a server URL
  or token.
- Must not break self-hosted connections (Advanced must remain fully
  functional).
- Must not ship a normal-mode button that silently does nothing on a platform
  where the callback can't actually be delivered — the round-trip must land
  back in the app on every target platform.
- Must not spawn a second app window on any desktop OS when the callback
  arrives while the app is already running.

## Constraints

- **Two repositories:**
  - `ohrbob2.app` — the Avalonia app (projects: `Ohrbob.App` shared UI/VM,
    `Ohrbob.App.Desktop`, `Ohrbob.App.Android`, `Ohrbob.App.iOS`).
  - `ohrbob2` — the ASP.NET web frontend (`Ohrbob.Web`) plus `Ohrbob.Api`
    and `Ohrbob.Player`.
- The deep-link shape is **fixed** by the existing web QR code and must keep
  working: `ohrbob://connect?server=<server>&token=<token>`, where `server`
  is the Player's public base URL (`OhrbobApiOptions.FrontendPlayerUrl`,
  configured as `https://player.ohrbob.org`). The app's "server URL" is this
  player base URL, not the ohrbob.org website.
- The exact website path to open is `https://ohrbob.org/connect/app`.
- The web app uses cookie auth (`[Authorize]`), MVC + Razor views, routes
  wired with `MapControllerRoute`.
- Token scope mirrors `Ohrbob.Server.Shared.Data.ApiKey`: `ProfileId` empty
  means account-level ("All Profiles"); otherwise a specific profile.

## Decisions already made

- **Normal mode** = one primary button + an Advanced toggle. After opening
  the browser the app does nothing special.
- **Advanced** = today's connect card, including the deep-link paste field.
  Its default server URL becomes the hosted player URL
  (`https://player.ohrbob.org`). *(Assumption: this is the "ohrbob.org"
  value the person meant; it is what the website puts in the link.)*
- **Web card** fields: token **name** and a **scope selector**
  (All Profiles / account-level, plus each of the account's profiles).
- **Submit** creates an API key (account-level or profile-scoped per the
  selector) and then redirects to the deep link, which causes the app to
  connect.
- **New person**: goes through the existing onboarding first, then lands on
  `/connect/app`.
- **Desktop means Linux, Windows and macOS** — the full round-trip must work on
  all three desktop OSes (scheme registration + single-instance on each), plus
  Android and iOS.
- **Receiving a link while already connected**: if the app is *not* connected,
  switch silently. If it *is* connected (in particular if the incoming link
  targets a different server/account), **confirm before replacing** the active
  connection and interrupting playback.
- The existing Account → API Keys QR flow is left untouched.
- **The packaged Flatpak is the Linux delivery target**, so packaging is in
  scope: the exported `.desktop` must accept a URL argument (`%u`), and opening
  the browser must work inside the sandbox (OpenURI portal).
- The hardcoded dev token and the `10.0.2.2` emulator server default in the
  card are gated behind `DEBUG` rather than shipping as "Advanced" defaults.

## What's left to the builder

- Exact copy/labels and visual treatment. Reuse the existing connect card in
  the app; make `/connect/app` a small centered card consistent with the web
  app's styling.
- How the deep link reaches `MainViewModel` on each platform. Today only the
  Linux `.desktop` declares `x-scheme-handler/ohrbob`; desktop `Program.cs`
  ignores `argv`, Windows/macOS have no scheme registration, and Android/iOS
  have no scheme/intent registration. The builder chooses the mechanism per
  platform.
- Single-instance forwarding on Linux, Windows and macOS (a running app must
  receive the URL instead of a second window).
- Android intent-filter, iOS `CFBundleURLTypes`, and Windows/macOS scheme
  registration details.
- The confirmation UX when a link arrives over an active connection.
- Whether `/connect/app` redirects straight to the deep link or renders an
  intermediate confirmation after submit.
- Auth `returnUrl` handling so onboarding returns the person to
  `/connect/app`.
- Token-name validation (non-empty; whether duplicate names are allowed).
- Reuse of the existing `AccountController.AddApiKey` pattern vs a new
  action, and where the new route/controller lives.
- Tests on both sides (app deep-link parsing/connection; web API-key creation
  + redirect).
