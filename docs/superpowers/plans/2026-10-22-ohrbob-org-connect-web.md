# Ohrbob.org Connect — Web Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a login-gated `GET/POST https://ohrbob.org/connect/app` page in `Ohrbob.Web` that creates an API key (name + scope) and hands the app a `ohrbob://connect?server=…&token=…` deep link.

**Architecture:** A new `ConnectController` (`[Authorize]`) with `App` `[HttpGet]` (renders a small centered card) and `App` `[HttpPost]` (creates the key via the existing `ApiKeysApiService`, then renders a handoff view that navigates to the deep link). Reuses the existing controller/service/test patterns; no routing changes needed (default route maps `/connect/app`).

**Tech Stack:** ASP.NET Core MVC + Razor, Moq + NUnit tests, `Ohrbob.Server.Shared.Data.ApiKey`/`Profile`.

**Spec:** `docs/superpowers/specs/2026-10-22-ohrbob-org-connect-flow-design.md` (in the `ohrbob2.app` repo).

## Global Constraints

- Route is exactly `/connect/app`; `server` in the link is `OhrbobApiOptions.FrontendPlayerUrl` (e.g. `https://player.ohrbob.org`).
- Deep-link shape is fixed: `ohrbob://connect?server=<escaped server>&token=<escaped token>`.
- Token scope: `ApiKey.ProfileId == ""` means account-level ("All Profiles"); otherwise a specific profile id.
- Auth: cookie-based; the controller is `[Authorize]` and reads the account id from claim `Consts.OhrbobAccountIdClaim` (see `AccountController.GetAccountId`).
- No QR code on this page. The existing Account → API Keys page is untouched.
- New/unauth people go through the existing onboarding, then return to `/connect/app`.

## Review Focus

Most likely to bite a real person, most likely first. Each line's test lives in the named task.

1. Visiting `/connect/app` while logged out must bounce through onboarding and land back on `/connect/app` (Task 1).
2. Submitting a blank name must not create a nameless key and must show a recoverable error (Task 2).
3. A failure from the API (e.g. unique-token/validation error) must not 500 — it must show an error and keep the person on the card (Task 2).
4. The handoff page must actually leave the browser for the `ohrbob://` link (JS navigate + a manual fallback link), since a bare 302 to an unknown scheme is unreliable (Task 3).
5. Account-level vs profile scope must round-trip into `ApiKey.ProfileId` unchanged (Task 2).

---

### Task 1: `GET /connect/app` — the card

**Files:**
- Create: `Ohrbob.Web/Models/ConnectAppViewModel.cs`
- Create: `Ohrbob.Web/Controllers/ConnectController.cs`
- Create: `Ohrbob.Web/Views/Connect/App.cshtml`
- Test: `Ohrbob.Web.Tests/ConnectControllerTests.cs`

**Interfaces:**
- Consumes: `ProfilesApiService.GetProfilesByAccountIdAsync(string accountId) -> Task<List<Profile>?>`; `Consts.OhrbobAccountIdClaim`.
- Produces: `ConnectController` with `Task<IActionResult> App()` (GET) and later `Task<IActionResult> App(string name, string profileId)` (POST); `ConnectAppViewModel { List<Profile> Profiles { get; set; } }`.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async Task App_Get_WhenLoggedIn_ReturnsViewWithProfiles()
{
    var profiles = new List<Profile> { new() { Id = "p1", AccountId = AccountId, Name = "Main" } };
    var controller = CreateController(profiles); // helper mirrors OnboardingControllerTests

    var result = await controller.App();

    var view = result as ViewResult;
    Assert.That(view, Is.Not.Null);
    var model = view!.Model as ConnectAppViewModel;
    Assert.That(model, Is.Not.Null);
    Assert.That(model!.Profiles.Select(p => p.Id), Is.EqualTo(new[] { "p1" }));
}

[Test]
public async Task App_Get_WhenNoAccountClaim_ReturnsForbid()
{
    var controller = CreateController(new List<Profile>());
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

    var result = await controller.App();

    Assert.That(result, Is.InstanceOf<ForbidResult>());
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2/Ohrbob.Web.Tests --filter FullyQualifiedName~ConnectControllerTests`
Expected: FAIL — `ConnectController` / `ConnectAppViewModel` do not exist.

- [ ] **Step 3: Create `ConnectAppViewModel`**

`Ohrbob.Web/Models/ConnectAppViewModel.cs`: a class with `public List<Profile> Profiles { get; set; } = new();`.

- [ ] **Step 4: Create `ConnectController` (GET)**

Mirror `AccountController`: `[Authorize]`, constructor injecting `ProfilesApiService` and `IOptions<OhrbobApiOptions>`; private `GetAccountId()`; `[HttpGet] public async Task<IActionResult> App()` → `Forbid()` when no account id, else load profiles and `return View(model)`.

- [ ] **Step 5: Create the view `Views/Connect/App.cshtml`**

Layout default; a small centered card (`mx-auto`, max width ~26rem) matching the web app's card styling: title, one text input `name="name"`, one `<select name="profileId">` whose first option is `value=""` labelled "All Profiles (account level)" followed by one option per `Model.Profiles` (`value="@p.Id"`), and a submit button. Post back to `App` via `asp-action="App" method="post"` with `@Html.AntiForgeryToken()`. Show `TempData["Error"]` if present.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2/Ohrbob.Web.Tests --filter FullyQualifiedName~ConnectControllerTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2
git add Ohrbob.Web/Models/ConnectAppViewModel.cs Ohrbob.Web/Controllers/ConnectController.cs Ohrbob.Web/Views/Connect/App.cshtml Ohrbob.Web.Tests/ConnectControllerTests.cs
git commit -m "feat(web): add /connect/app card for hosted app connection"
```

---

### Task 2: `POST /connect/app` — create the key

**Files:**
- Modify: `Ohrbob.Web/Controllers/ConnectController.cs`
- Test: `Ohrbob.Web.Tests/ConnectControllerTests.cs`

**Interfaces:**
- Consumes: `ApiKeysApiService.CreateApiKeyAsync(string accountId, ApiKey apiKey) -> Task<ApiKey?>`; `OhrbobApiOptions.FrontendPlayerUrl`.
- Produces: `Task<IActionResult> App(string name, string profileId)` returning the handoff view with a `string` model (the deep link).

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task App_Post_CreatesKeyWithScopeAndReturnsDeepLink()
{
    ApiKey? created = null;
    var controller = CreateController(new List<Profile>(), onCreate: k => created = k);

    var result = await controller.App("Living room", "p1");

    Assert.That(created!.ProfileId, Is.EqualTo("p1"));
    Assert.That(created.Name, Is.EqualTo("Living room"));
    var view = result as ViewResult;
    Assert.That(view!.ViewName, Is.EqualTo("Handoff"));
    Assert.That((string)view.Model!, Does.StartWith("ohrbob://connect?server="));
    Assert.That((string)view.Model!, Does.Contain("token="));
}

[Test]
public async Task App_Post_AccountLevelScope_LeavesProfileIdEmpty()
{
    ApiKey? created = null;
    var controller = CreateController(new List<Profile>(), onCreate: k => created = k);

    await controller.App("Kitchen", "");

    Assert.That(created!.ProfileId, Is.EqualTo(string.Empty));
}

[Test]
public async Task App_Post_BlankName_DoesNotCreateKeyAndReturnsCard()
{
    var createdKeys = 0;
    var controller = CreateController(new List<Profile>(), onCreate: _ => createdKeys++);

    var result = await controller.App("   ", "");

    Assert.That(createdKeys, Is.EqualTo(0));
    Assert.That(result, Is.InstanceOf<ViewResult>());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2/Ohrbob.Web.Tests --filter FullyQualifiedName~ConnectControllerTests`
Expected: FAIL — no `App(string, string)` overload.

- [ ] **Step 3: Implement the POST action**

`[HttpPost] [ValidateAntiForgeryToken] public async Task<IActionResult> App(string name, string profileId)`:
- `Forbid()` when no account id.
- If `string.IsNullOrWhiteSpace(name)`: set `TempData["Error"] = "Please enter a name."`, reload profiles, `return View(connectModel)` (the card).
- Else build `new ApiKey { AccountId = accountId, Name = name.Trim(), ProfileId = profileId ?? string.Empty }`, call `CreateApiKeyAsync`. On `null` or exception: `TempData["Error"]`, reload card, `return View(connectModel)`.
- On success return `View("Handoff", deepLink)` where `deepLink = $"ohrbob://connect?server={Uri.EscapeDataString(_options.Value.FrontendPlayerUrl)}&token={Uri.EscapeDataString(createdKey.Token)}"`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2/Ohrbob.Web.Tests --filter FullyQualifiedName~ConnectControllerTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2
git add Ohrbob.Web/Controllers/ConnectController.cs Ohrbob.Web.Tests/ConnectControllerTests.cs
git commit -m "feat(web): create API key and hand back the deep link on /connect/app"
```

---

### Task 3: Handoff view

**Files:**
- Create: `Ohrbob.Web/Views/Connect/Handoff.cshtml`

**Interfaces:**
- Consumes: the `string` model (deep link) returned by Task 2.

- [ ] **Step 1: Write the view**

`@model string`; a small centered card: "Opening the Ohrbob app…", a manual fallback link `<a href="@Model">` labelled "Open in the Ohrbob app", and a script that navigates: `window.location.href = @Html.Raw(Json.Serialize(Model));`. Keep it consistent with the web app's card styling.

- [ ] **Step 2: Verify manually against a running web instance**

Run the web app (`cd /home/kaiwegner/Projects/ohrbob2 && docker compose up` or the repo's dev command), log in, open `/connect/app`, submit a name, and confirm the browser is handed the `ohrbob://connect?server=…&token=…` link (address bar / OS prompt). Expected: navigation to the deep link is attempted.

- [ ] **Step 3: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2
git add Ohrbob.Web/Views/Connect/Handoff.cshtml
git commit -m "feat(web): handoff view that opens the ohrbob:// deep link"
```
