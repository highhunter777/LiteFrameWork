# LiteTesting

`LiteTesting` is the reusable test foundation shared by the .NET and Unity test runners.

- `Core/`: runner-neutral categories, deterministic data, timeouts, artifacts and cleanup.
- `Editor/`: Unity object and temporary AssetDatabase ownership for EditMode tests.
- `LiteTesting.Core.csproj`: the .NET build of the same core source used by Unity.

The core assembly has no Unity or business dependency. Product-specific fixtures belong in the
owning product test project, not in this directory.
