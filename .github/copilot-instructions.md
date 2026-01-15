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

## UI/UX conventions (controls, styling)
These are the “house rules” for building new controls so they match the existing look/feel. Most of the styling lives in `ScryScreen.App/Styles/ScryStyles.axaml`.

### Icon buttons + toggles
- Prefer `Classes="circleSmall"` for compact icon buttons and icon toggles.
- **Active state for Buttons (not ToggleButtons):** add `active` to the classes (e.g., `Classes="circleSmall active"`). Ensure the icon binds to the parent foreground, e.g. `Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"`, so the active style drives the icon.
- **Toggle “selected” state (gold outline + gold icon):**
  - Don’t use `noAccent` on toggles that represent a selection/active mode.
  - Bind icon foreground to the toggle’s foreground: `Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ToggleButton}}"`.
  - Use the existing toggle flavor classes:
    - `effectToggle`: checked icon becomes gold.
    - `soundToggle`: checked icon becomes gold.
- **Neutral-border toggles:** if “checked” is *not* a selection (pure visibility/options), keep the border neutral by adding `noAccent`.

### Sizes + layout density
- Default `circleSmall` sizing comes from styles; only hard-code `Width/Height/CornerRadius` when you need an ultra-compact variant (e.g., portal tile mini-controls).
- When you *do* hard-code a compact variant, keep it consistent (typically `28x28` with `CornerRadius=14`).

### TextBoxes (focus/selection/readability)
- Don’t hand-roll focus/selection colors per-control; the global theme already sets gold focus border + selection behavior.
- Use `IsEnabled="False"` (disabled) for truly non-interactive derived fields. This avoids “read-only but selectable” behavior.
- For derived-but-readable fields, use the read-only background cue already established:
  - `IsEnabled="False"` and `Background="{DynamicResource ScryReadOnly}"`.

### Input behaviors
- Numeric input should use the shared behaviors:
  - Integers: `beh:NumericTextBoxBehavior.IsNumericOnly="True"`
  - Decimals: `beh:NumericTextBoxBehavior.IsNumericDecimalOnly="True"`
- For fast editing on compact inputs: `beh:SelectAllOnFocusBehavior.IsEnabled="True"`.

### Tooltips + info affordances
- Prefer a consistent “info” icon button for field help (lightbulb glyph) with `ToolTip.Tip` on the button.

### State semantics
- Keep a strict distinction between:
  - **Disabled**: not clickable/selectable (use `IsEnabled="False"`).
  - **Active/selected**: gold outline + gold icon (toggle checked w/o `noAccent`, or button with `active`).

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
