# Agent Memory

## Avalonia UI
- **Documentation:** The complete Avalonia documentation is located locally in `../avalonia-docs`. Always search this directory first when looking for API references, syntax, or styling rules.
- **Responsive Layouts:** Use pure XAML `ContainerQuery` instead of code-behind size events. 
  - Tag the parent element with `Container.Name="SomeName"` and `Container.Sizing="Width"`.
  - Place `<ContainerQuery Name="SomeName" Query="max-width: 600">` in the `Styles` collection to conditionally apply styles.

## General Workflow
- **Validation:** Always verify changes by running the build and the test suite (`dotnet test`). Even simple UI/ViewModel changes can break bindings or existing tests.
- **Testing:** Always add tests for everything being implemented. New features and fixes must be covered by the test suite.

## Publishing & Remotes
- **Development mirror:** Gogs, `ssh://git@gogs.qwirl.de:8622/ohrbob/ohrbob.app.git` (git remote `origin`).
- **Public repository:** `https://github.com/ohrbob-org/ohrbob.app.git`. `main` is published there automatically on every commit (Gogs push mirror).
- **Consequence — treat every commit as public:** whatever lands on `main` is world-readable immediately. Never commit secrets, tokens, credentials, `.env`/`.envrc` contents, or personal data. This is also why the git history was rewritten to remove a personal email address.
- **The Flatpak manifest builds from the public repo:** its `git` source points at `github.com/ohrbob-org/ohrbob.app` (pinned to a tag/commit). Work is only buildable from the manifest once it is committed to `main` and reachable there.
- **Tags publish too:** release tags (`vX.Y.Z`) are what the manifest and AppStream screenshot URLs pin to, so a tag must be pushed for those URLs to resolve.
