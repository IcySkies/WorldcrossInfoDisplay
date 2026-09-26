# Repository Instructions

## Build Requirement

- After every change to the Worldcross Info Display source, project, test, or solution files, run:

  `dotnet build WorldcrossInfoDisplay.csproj --configuration Release`

- Resolve all build errors and warnings before considering the change complete.
- Report the Release build result in the final response.

## Tests

- When behavior changes, run:

  `dotnet test Tests/WorldcrossInfoDisplay.Tests.csproj --configuration Release`

- Report any test failures or remaining coverage gaps.
