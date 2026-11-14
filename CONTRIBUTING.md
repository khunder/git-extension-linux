# Contributing

Thanks for helping with Git Extensions for Linux.

## Getting started

1. Install the .NET 11 SDK (see `global.json`) and `git`.
2. Build and test:

   ```sh
   dotnet build GitExtensions.slnx
   dotnet test tests/app/UnitTests/GitCommands.Portable.Tests
   dotnet test tests/app/UnitTests/GitExtensions.Avalonia.Tests
   ```

3. Run the app on a repository:

   ```sh
   dotnet run --project src/app/GitExtensions.Avalonia -- /path/to/repository
   ```

## Guidelines

- The build treats warnings as errors (StyleCop and threading analyzers); keep it clean.
- Git calls block: run them off the UI thread and update UI state on the UI thread.
- Use theme resources for colours so both the light and the dark theme work.
- Add tests for behaviour changes. The Avalonia tests are headless and use real temporary git repositories.
- Keep the portable core (`GitCommands`, `GitExtUtils`, `GitExtensions.Extensibility.Core`, `GitUIPluginInterfaces`) free of UI dependencies.

## Pull requests

Open a pull request against `main` with a short description of the change and how you tested it. The CI workflow builds the solution and runs the tests.

The original project: <https://github.com/gitextensions/gitextensions>.
