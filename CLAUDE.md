# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build / Run

The project is a WPF app targeting `net8.0-windows`. `EnableWindowsTargeting=true` is set in the csproj so it can be restored/built on non-Windows agents, but it can only be **run** on Windows.

```powershell
dotnet restore MoneyShot/MoneyShot.csproj
dotnet build   MoneyShot/MoneyShot.csproj --configuration Debug
dotnet run     --project MoneyShot/MoneyShot.csproj
```

There is **no test project** in the solution — no `dotnet test` target exists. Verification is manual; see the checklist in `DEVELOPER.md`.

The MSI installer is built from `Installer/Product.wxs` using the WiX v4 toolset. Local MSI builds are not part of `dotnet build`; they happen in the GitHub Actions workflows (`build-msi.yml`, `release.yml`).

### Versioning

The base version lives in `MoneyShot/MoneyShot.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`). CI appends `$GITHUB_RUN_NUMBER` and passes `/p:AssemblyVersion=...`, `/p:FileVersion=...`, and `-d ProductVersion=...` (to WiX) so the assembly, file, and MSI versions all stay in sync. When bumping the version, update the csproj — CI handles the rest.

## Architecture

### Two windows + service layer (no DI, no MVVM framework)

Services are instantiated directly in `MainWindow` (`MainWindow.xaml.cs:31-35`). There is no DI container and no MVVM library — XAML is wired with code-behind throughout. New services should be added the same way; don't introduce a container for one or two extra dependencies.

- **`MainWindow`** — system tray host + capture-mode entry points. Owns the `HotKeyService`, `NotifyIcon`, and the auto-update flow. The window is typically hidden (`StartInTray=true` by default) and only re-shown on error or via tray menu.
- **`EditorWindow`** (`Views/EditorWindow.xaml.cs`, ~1800 lines) — the annotation editor. Heavy code-behind: tool selection, drawing, selection/resize/move, crop, undo, zoom, and save are all in this single file. New annotation tools follow the recipe in `DEVELOPER.md` (enum entry → toolbar button → `Create*` factory → switch case in `Canvas_MouseDown`).

### Capture pipeline (and why the `Thread.Sleep` calls exist)

`ScreenshotService` uses GDI+ (`Graphics.CopyFromScreen`) and converts the `Bitmap` to a WPF `BitmapSource` via `Imaging.CreateBitmapSourceFromHBitmap`, then **must call `DeleteObject` on the HBITMAP** to avoid GDI handle leaks (`ScreenshotService.cs:64-87`). The result is `Freeze()`d so it can cross threads.

Before capture, `MainWindow` calls `Hide()` and then `Thread.Sleep(200-300)`. This is intentional — without the delay the window can still be visible in the captured frame. Don't remove these sleeps without an alternative (e.g. waiting for a render-tick confirmation).

For region capture with `HideUiFromScreenshots=true`, the flow captures one **frozen full-screen bitmap first**, then passes it into `RegionSelector`, which crops from the frozen bitmap rather than re-capturing. Any UI shown on top of the selector therefore won't pollute the final image. See `MainWindow.xaml.cs:298-337`.

### Hotkeys (Win32, requires window handle)

`HotKeyService` registers global hotkeys via `RegisterHotKey` and listens for `WM_HOTKEY` through an `HwndSource.AddHook`. It must be `Initialize(window)`d **after** the window's HWND exists — that's why `RegisterHotKeys()` is called from `MainWindow_Loaded`, not the constructor. `ParseHotKey` only understands the modifiers/keys it has explicit cases for (`Ctrl`/`Alt`/`Shift`/`Win` and `PrintScreen`/`0-9`/`F1-F12`); add new keys there if you need them. Settings store hotkeys as strings like `"Ctrl+PrintScreen"`.

When hotkeys are reconfigured in settings, call `MainWindow.ReloadHotKeys()` — it unregisters all and re-registers from the new settings.

### Settings persistence

`SettingsService` writes `%AppData%\MoneyShot\settings.json` atomically (write to `.tmp`, then `File.Move`). On load it runs `ValidateAndSanitizeSettings` which:
- Resolves `DefaultSavePath` via `Path.GetFullPath` and falls back to `MyPictures` if it's invalid/relative/too-long — **don't bypass this**, it prevents path traversal from a tampered settings file.
- Clamps `DefaultLineThickness` to `[1, 20]` and validates `DefaultFileFormat` against an allowlist.

Two registry-backed settings live here too:
- `SetStartupWithWindows` — `HKCU\...\Run`.
- `SetWindowsPrintScreenDisabled` — `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled`. This globally suppresses Windows' built-in Snipping Tool hotkey so MoneyShot can claim PrintScreen.

### Auto-update

`AutoUpdateService` polls `https://api.github.com/repos/Daolyap/MoneyShot/releases/latest`, downloads either the `.exe` or `.zip` asset, stages the new exe alongside the running one, then writes and launches a self-deleting batch script (`BuildWindowsSwapScript`) that waits for the current process to exit, swaps the binary, and relaunches. The owner/repo constants are hard-coded in `AutoUpdateService.cs:18-19`.

The optional GitHub token read at line 50 is intentionally `Environment.GetEnvironmentVariable("")` — i.e. disabled. If you wire one up, do it through a real env-var name and treat it as a secret.

### Editor undo model

`EditorWindow` uses a `Stack<IUndoAction>` with a small set of action records (`AddElementUndoAction`, `RemoveElementUndoAction`, `CropUndoAction`, `ResizeUndoAction`, etc., declared as nested types). Any new mutation that should be undoable needs to push a corresponding action — there is no automatic snapshotting. `ElementState` is the lightweight record used by resize-undo to capture position + size + optional font size.

### Resize / drag — design notes

Cursor-mode mouse handling has caused multiple regressions. Two rules that must be preserved:

1. **Each resize handle owns its own `MouseLeftButtonDown` handler** (`ResizeHandle_MouseLeftButtonDown` → `BeginResize`). The handler marks the event `Handled = true` so `Canvas_MouseDown` never sees handle clicks. Don't go back to manual hit-testing inside `Canvas_MouseDown` — that path was a source of snap-to-(0,0) bugs because of NaN width fall-through.
2. **Don't `ClampToCanvasBounds` cursor-mode positions** (drag or resize). Clamping is correct for *drawing* new shapes (keeps them inside the image) but wrong for *moving/resizing* existing ones — it freezes `_dragStartPoint` at the canvas edge and produces a huge delta on the next frame, snapping the element. Drawing tools use `clickPoint` (clamped); cursor-mode uses `rawPoint`.

Newly-created `Rectangle`/`Ellipse`/`Blur` shapes are initialized with `Width=0`, `Height=0` and explicit `Canvas.SetLeft/Top` to the click point — keep this. Without it, shapes briefly appear at canvas (0,0) before the first `MouseMove` fires.

## Conventions specific to this repo

- Nullable reference types are enabled project-wide (`<Nullable>enable</Nullable>`); honor it on new code.
- WPF + WinForms types collide on names like `Application`, `Button`, `MessageBox`, `HorizontalAlignment`, `Cursors`, `Screen`. Existing code uses fully-qualified names (`System.Windows.MessageBox`, `System.Windows.Forms.Screen`, etc.) rather than aliases — match that style.
- `System.Diagnostics.Debug.WriteLine` is the de-facto logger. There is no logging framework; don't add one for a single feature.
- Service errors that the user must see are surfaced via `System.Windows.MessageBox.Show` from the calling window; services themselves throw `InvalidOperationException` with a friendly message wrapping the underlying exception (see `SettingsService`, `SaveService`).
