# Copilot instructions (ScryScreen)

## Big picture
- This is a .NET 8 Avalonia desktop app. Main UI is `ScryScreen.App`; domain-ish logic is in `ScryScreen.Core`.
- The app manages multiple “portals” (separate windows) that can show text, images, video, and overlay effects.
  - Portal lifecycle + screen placement: `ScryScreen.App/Services/PortalHostService.cs`
  - Main orchestration VM: `ScryScreen.App/ViewModels/MainWindowViewModel.cs`

## UI composition + MVVM conventions
- Composition root is manual (no DI container): `ScryScreen.App/App.axaml.cs` constructs `MainWindow`, initializes `ErrorReporter`, then creates `PortalHostService` + `MainWindowViewModel`.
- View lookup is name-based reflection: `ScryScreen.App/ViewLocator.cs` maps `FooViewModel` → `FooView`.
- ViewModels use CommunityToolkit.Mvvm source generators:
  - Use `[ObservableProperty]` fields and `[RelayCommand]` methods (see `MainWindowViewModel`, `PortalRowViewModel`).
  - Use `partial void OnXChanged(...)` for change hooks (e.g., initiative/effects view models).
- Threading: anything that touches UI-bound collections should be marshalled to the UI thread.
  - Example: `PortalHostService` raises events via `Dispatcher.UIThread.Post(...)`.

## Error handling + diagnostics
- UI/background exception reporting is centralized in `ScryScreen.App/Services/ErrorReporter.cs` (shows an Avalonia `ErrorDialog`). Prefer `ErrorReporter.Report(ex, "context")` over throwing in event handlers.
- Startup logging + Windows-native fatal dialog is in `ScryScreen.App/Program.cs`.
  - Crash-test hook: set env var `SCRYSCREEN_TEST_STARTUP_CRASH=1`.

## Video + LibVLC integration (important gotchas)
- Video uses LibVLCSharp + a native HWND host (`ScryScreen.App/Controls/VlcVideoHost.cs`). Native surfaces can’t be overdrawn by Avalonia.
  - Overlays above video are rendered using a separate transparent owned window: `PortalHostService` creates `PortalOverlayWindow` and keeps it aligned.
- Avoid calling `MediaPlayer.Play()` before the native target is ready; LibVLC may spawn its own fallback window.
  - This is called out directly in `ScryScreen.App/ViewModels/PortalWindowViewModel.cs`.
- Looping/paused-frame reliability is handled by dedicated helpers:
  - Loop tick + restart gating: `ScryScreen.App/Services/VideoLoopController.cs`
  - Paused frame priming (and testable abstractions `IVideoPlayback`/`IVideoDelay`): `ScryScreen.App/Services/VideoPausedFramePrimer.cs`

## Persistence + assets
- Session persistence writes JSON under LocalAppData (initiative/effects/last session): `ScryScreen.App/Services/LastSessionPersistence.cs`.
- Effect sounds are loaded from app resources at `avares://ScryScreen.App/Assets/Sounds/`.
  - Drop-in sound filename conventions: `ScryScreen.App/Assets/Sounds/README.md`.

## Dev workflows
- Build: `dotnet build ScryScreen.sln -c Debug`
- Run app: `dotnet run --project .\\ScryScreen.App\\ScryScreen.App.csproj -c Debug`
- Tests (xUnit): `dotnet test ScryScreen.Tests\\ScryScreen.Tests.csproj -c Debug`
- Publish (single-file, self-contained, ReadyToRun by default): `publish.ps1`
  - Example: `powershell -NoProfile -ExecutionPolicy Bypass -File .\\publish.ps1 -Configuration Release -Runtime win-x64 -Zip`
  - VS Code task mirrors this: `.vscode/tasks.json`
