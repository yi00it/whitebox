# Contributing to Whitebox

Thanks for helping out! Whitebox deletes files on people's computers, so the bar for safety is high. Please read this before opening a PR.

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Fork and clone the repo, then run `dotnet run`.
3. Make sure `dotnet build -c Release -warnaserror` passes. CI runs the same check.

## Ground rules for anything that removes data

- **Never test cleaning on real user data.** Use a throwaway folder. `JunkCategory` and `JunkRule` are public, so you can point a rule at a temp directory.
- **Caches only.** Don't add locations that hold history, cookies, passwords, sessions, saves or settings, even if they're big.
- **Skip what's in use.** If an app must be closed for its cache to be cleaned safely, list its process names in the category so it's skipped while running.
- **Prefer the Recycle Bin** (`Shell.Recycle`) for anything the user picked by hand, like files or folders.
- **Default off** for anything a user might want to keep (developer caches, privacy traces).

## Adding a cleanup location

Locations live in `Services/JunkEngine.cs` → `CreateCategories()`. A category is a list of rules:

```csharp
new("myapp", "Apps", "My App", "Web and media cache", "", JunkKind.Files,
    defaultChecked: true, requiresAdmin: false,
    [R(@"%LOCALAPPDATA%\MyApp\Cache")],
    ["MyApp"]),   // skipped while MyApp.exe is running
```

Paths support environment variables and `*` segments (for example `User Data\*\Cache` matches every browser profile). In your PR, explain why the location is safe to clear and what the app does when the files are gone.

## Code style

- Match the surrounding code: MVVM with one view model per page, services with no UI dependencies.
- Keep heavy work off the UI thread (`Task.Run`) and make long scans cancellable.
- UI colors and styles belong in `Themes/Theme.xaml`, not inline.
- No new NuGet dependencies without discussion. The app intentionally has none.

## Reporting bugs

Use the issue templates and include your Whitebox version (shown at the bottom of the sidebar) and Windows version.

## Releasing (maintainers)

Bump nothing by hand. Tag and push:

```powershell
git tag v1.1.0
git push origin v1.1.0
```

The Release workflow builds both executables, writes `SHA256SUMS.txt` and publishes a GitHub Release with generated notes. The version shown in the app comes from the tag.
