# Agent Memory

## Avalonia UI
- **Documentation:** The complete Avalonia documentation is located locally in `../avalonia-docs`. Always search this directory first when looking for API references, syntax, or styling rules.
- **Responsive Layouts:** Use pure XAML `ContainerQuery` instead of code-behind size events. 
  - Tag the parent element with `Container.Name="SomeName"` and `Container.Sizing="Width"`.
  - Place `<ContainerQuery Name="SomeName" Query="max-width: 600">` in the `Styles` collection to conditionally apply styles.

## General Workflow
- **Validation:** Always verify changes by running the build and the test suite (`dotnet test`). Even simple UI/ViewModel changes can break bindings or existing tests.
- **Testing:** Always add tests for everything being implemented. New features and fixes must be covered by the test suite.
