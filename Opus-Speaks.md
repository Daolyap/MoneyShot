# Opus Speaks

A roadmap of prompts for the next agent to action. Each section is self-contained and can be picked up independently. Pick whatever the user prioritizes or work top-down.

---

## 1. Add a test project

There is currently no test project in the solution and no `dotnet test` target. Several services have non-trivial logic that is regression-prone and easy to test without a Windows display:

> Add a new xUnit test project at `MoneyShot.Tests/` targeting `net8.0` (no `-windows`, no WPF reference). Reference `MoneyShot.csproj` and add it to `MoneyShot.sln`. Write tests for:
>
> - **`HotKeyService.ParseHotKey`** — every modifier combination, every key it claims to support, and the failure cases (empty, garbage, unknown keys). This is the most fragile parsing logic in the codebase and it has zero coverage.
> - **`SettingsService.ValidateAndSanitizeSettings`** — feed it tampered settings: relative `DefaultSavePath`, paths with `..`, paths with invalid chars, out-of-range `DefaultLineThickness`, unknown `DefaultFileFormat`. Assert it normalizes to safe defaults rather than throwing.
> - **`AutoUpdateService.ParseVersion`** — feed tag-name strings (`v1.2.3`, `v1.2.3-build.45.abc1234`, `1.2`, garbage) and assert the parsed `Version` is what we expect.
> - **`AutoUpdateService.SelectPreferredAsset`** — verify .exe is preferred over .zip and that an empty asset list returns null.
>
> Wire the test project into `.github/workflows/build.yml` so PRs run `dotnet test` and fail on regressions. Note: WPF UI testing is out of scope for this pass — this is purely service-layer coverage.

## 2. Refactor `EditorWindow.xaml.cs`

The editor's code-behind is ~1900 lines and growing. It mixes annotation rendering, selection, drag/resize, undo, crop, zoom, save, and UI plumbing in a single class. Recent resize bugs were all rooted in tangled state.

> Split `MoneyShot/Views/EditorWindow.xaml.cs` into focused collaborators while keeping the public surface (the `EditorWindow` window) unchanged. Suggested decomposition:
>
> - `Editor/AnnotationToolRegistry.cs` — owns the per-tool factory (`CreateRectangle`, `CreateEllipse`, `CreateArrow`, `CreatePolyline`, `CreateNumberLabel`, `CreateBlurRectangle`) and the per-tool `Update*` logic.
> - `Editor/SelectionController.cs` — owns `_selectedElement`, `_selectionBorder`, `_resizeHandles`, the `SelectElement`/`ClearSelection` flow, and the `BeginResize`/`MoveElement`/`ResizeElement` methods.
> - `Editor/UndoController.cs` — owns the undo stack and the nested `IUndoAction` types (`AddElementUndoAction`, `RemoveElementUndoAction`, `CropUndoAction`, `ResizeUndoAction`).
> - `Editor/CanvasRenderer.cs` — owns `CaptureCanvasAsImage` and the `CreatePixelatedBrush` rendering.
>
> Keep `EditorWindow.xaml.cs` as the WPF wiring (event handlers, keyboard shortcuts, toolbar buttons) that delegates to these controllers. Do not introduce a DI container for this — pass collaborators in via the constructor. Do not rename XAML element IDs or change `Canvas_MouseDown/Move/Up` signatures since they're bound from XAML.
>
> Use this refactor as the opportunity to delete the duplicated `IsNaN` guards scattered through every position read — centralize them in a single `SafeCanvasPosition` helper on the controller.

## 3. Make resize handles easier to grab and add resize support for arrows

The resize handles are 8×8 pixels — fine on 1080p, frustrating on 4K. Arrows cannot be resized at all today (they're excluded from `CreateResizeHandles` because `Path` doesn't have explicit `Width`/`Height`).

> In `MoneyShot/Views/EditorWindow.xaml.cs`:
>
> - Bump the visible handle size to 12×12 and wrap each handle in a transparent `Rectangle` of 24×24 that catches mouse clicks. The visible square stays small but the hit zone is finger-sized.
> - Add resize support for `Path` (arrow) elements. When an arrow is selected, generate a 2-handle layout (start point, end point) instead of the 8-handle box. Dragging an endpoint should re-run `UpdateArrow` with the new endpoint while keeping the other end fixed. Store the arrow's original `_startPoint` and `currentPoint` in `BeginResize` and apply the same arrow-geometry math used in `UpdateArrow`.
> - Same treatment for `Line` (currently `Line` selection has no handles either — it should have 2).

## 4. MSI installer & CI hardening

Several items from the deleted `plan.md` are still worth doing. Audit `Installer/Product.wxs` and `.github/workflows/release.yml` for:

> Audit `Installer/Product.wxs` and report which of the following are present, missing, or wrong:
>
> - `Scope="perMachine"` and `ALLUSERS=1`
> - `MajorUpgrade Schedule="afterInstallValidate"`
> - Launch condition checking Windows 10+ and .NET 8 runtime presence
> - `RemoveFolder` elements for clean uninstall
> - Stable component GUIDs (not auto-generated each build)
> - WiX Util extension for installed-folder ACL lockdown
>
> Then audit `.github/workflows/*.yml` for:
>
> - All `uses:` lines pinned to commit SHAs (not mutable `@v4` tags)
> - Explicit `permissions:` blocks per workflow (least privilege)
> - No use of deprecated `actions/upload-release-asset@v1` (replace with `softprops/action-gh-release@v2`)
>
> Apply fixes inline. Report any that are intentionally non-applicable (e.g. if the project has decided against per-machine install).

## 5. UI/UX upgrades

The current editor UI is functional but dated. Below are concrete improvements ordered by impact:

> Modernize `MoneyShot/Views/EditorWindow.xaml`:
>
> 1. **Replace `WindowStyle="None" + AllowsTransparency="True"` with `WindowChrome`.** The current setup forces software rendering for the entire window (transparency disables hardware acceleration on the chrome) and the custom title bar is buggy (`DragMove` `try/catch` at `MainWindow.xaml.cs:485` is a symptom). Use `WindowChrome.GlyphsPlatform` with custom caption buttons in the chrome area to keep the dark look without losing GPU rendering.
> 2. **Add a keyboard-shortcut overlay.** Press `?` to show a translucent panel listing all shortcuts (`R/C/A/L/F/T/P/1`, `Ctrl+Z/C/S`, `Ctrl++/-/0`, `Esc`, `Delete`). Auto-dismiss on next keypress.
> 3. **Add a custom color picker.** Currently 8 fixed colors. Add a 9th "..." button that opens a `ColorDialog` (or a simple HSL picker) and remembers the last-used custom color.
> 4. **Show the active tool visually.** Today every toolbar button looks identical and there's no indication of which tool is selected. Add a `BasedOn`-style toggle for the current tool's button (border highlight + background tint).
> 5. **Stroke thickness slider.** `_lineThickness` is hard-coded to 3 in the editor with no way to change it without going through Settings. Add a small slider (1–10) next to the color row.

## 6. Replace `Debug.WriteLine` with a real logger

`Debug.WriteLine` is used as the de-facto logger throughout the codebase but produces zero output in Release builds. Diagnosing user-reported bugs is essentially impossible.

> Add `Microsoft.Extensions.Logging` and `Microsoft.Extensions.Logging.Debug` as `PackageReference`s in `MoneyShot.csproj`. Configure a logger factory in `App.xaml.cs` that writes to:
>
> - The Debug output (existing behavior in dev)
> - A rolling text file at `%AppData%\MoneyShot\logs\moneyshot-{date}.log` (new — keep last 7 days)
>
> Inject `ILogger<T>` into each service via constructor and replace every `System.Diagnostics.Debug.WriteLine($"...")` call with the appropriate `_logger.LogDebug/LogInformation/LogWarning/LogError`. Don't introduce a DI container for this — `App.xaml.cs` instantiates services already, so pass loggers manually.
>
> Surface the log directory in the Settings window with a "View Logs" button that opens the folder in Explorer.

## 7. Auto-update integrity verification

`AutoUpdateService` downloads the latest .exe/.zip from GitHub Releases and runs it. There is no signature check, no checksum check, and no protection against a compromised release.

> In `MoneyShot/Services/AutoUpdateService.cs`, add SHA-256 verification of the downloaded asset:
>
> 1. Update the release workflow (`.github/workflows/release.yml`) to publish a `SHA256SUMS.txt` file alongside the .exe and .zip in every GitHub release. Generate it during the build step.
> 2. In `GetAvailableUpdateAsync`, fetch `SHA256SUMS.txt` from the release assets and parse the expected hash for the chosen asset.
> 3. After download, compute the actual SHA-256 of the asset and compare. Throw if mismatch — never run an unverified binary.
>
> Out of scope (but worth a future pass): Authenticode signing of the published .exe and `WinVerifyTrust` validation before swap.

## 8. Screenshot history

Listed as TODO in `README.md` since v1. Most users want to revisit recent captures without re-doing them.

> Add a screenshot history feature:
>
> 1. After every capture (full-screen, region, or per-monitor), persist a thumbnail (PNG, max 400px wide) plus the full bitmap to `%AppData%\MoneyShot\history\{timestamp}.png` and a sidecar `.json` with `{capturedAt, width, height, source}`. Keep the last 50; evict oldest.
> 2. Add a "History" tray menu item and a corresponding window (`Views/HistoryWindow.xaml`) showing a grid of thumbnails with hover-to-zoom and right-click → Copy to Clipboard / Open in Editor / Delete.
> 3. Make history retention count and disk-quota configurable in `AppSettings` and exposed in the Settings window.
>
> Privacy note: history is local-only by design. Do not add cloud sync without an explicit settings toggle and an in-product disclosure.

---

## Notes for the next agent

- **Do not run `git` commands without explicit user approval.** The user enforces this.
- **Verify what the memory and CLAUDE.md tell you against the current code before acting.** Memories drift. The `CLAUDE.md` resize section was updated when the handle-owns-its-own-MouseDown refactor landed; if you see different code, the docs are stale, not the code.
- **The only test of "feature correctness" is running the WPF app on Windows.** `dotnet build` proves it compiles, not that it works. If you can't actually run the binary, say so explicitly rather than claiming success.
- **Don't reintroduce `RESIZE-BUG-JUSTIFY.md`, `plan.md`, or `PROJECT_SUMMARY.md`.** They were deleted because they were agent scratch-pads, not user docs. If you need to write a note for a future agent, append to this file instead.
