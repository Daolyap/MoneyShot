# Opus Speaks

A roadmap of prompts for the next agent. The original 8 sections of this document have all been worked on; the remaining items below are what's left, plus follow-ups created during the implementation pass.

---

## Status snapshot

- [x] **#1 Test project** — `MoneyShot.Tests/` exists with 95 passing xUnit tests covering `HotKeyService.ParseHotKey`, `SettingsService.ValidateAndSanitizeSettings`, `AutoUpdateService.ParseVersion` / `CompareSemVer` / `SelectPreferredAsset` / `ParseSha256Sums`. Wired into `.github/workflows/build.yml`.
- [~] **#2 EditorWindow refactor** — partial. `MoneyShot/Editor/` now holds `UndoController`, `CanvasRenderer`, `CanvasPosition`, `ElementResizeMode`, and `ElementState`. `EditorWindow.xaml.cs` calls into them and the duplicated `IsNaN` guards are gone. The `SelectionController` / `AnnotationToolRegistry` split was **deferred** — see Section A below.
- [x] **#3 Resize handles + arrow/line resize** — visible squares are 12×12 inside 24×24 transparent hit zones; `Line` and `Path` (arrow) elements get 2-handle endpoint resize via `BeginEndpointResize` / `ApplyEndpointResize`.
- [x] **#4 MSI / CI hardening** — Product.wxs already had per-machine scope, MajorUpgrade after install validate, stable component GUIDs, and RemoveFolder. Added `<Launch Condition>` for 64-bit Windows 10+. Workflows already pin to commit SHAs and use `softprops/action-gh-release@v2`. Added baseline `permissions: contents: read` at workflow level on `release.yml` and `build-msi.yml`.
- [~] **#5 UI/UX upgrades** — 4 of 5 landed. Custom colour picker, stroke-thickness slider, active-tool indicator, and `?` keyboard-shortcut overlay are wired in `EditorWindow.xaml(.cs)`. The **WindowChrome migration was deferred** — see Section B below.
- [~] **#6 Logger** — pragmatic deviation: instead of pulling in `Microsoft.Extensions.Logging` and rewriting every service constructor for DI, added `MoneyShot/Services/Logger.cs`, a static facade that writes to both Debug.WriteLine *and* a daily rolling file at `%AppData%\MoneyShot\logs\moneyshot-YYYYMMDD.log` (7-day retention). Every existing `System.Diagnostics.Debug.WriteLine` call in the services + MainWindow + RegionSelector now goes through it. The user-visible "View Logs" Settings button is **still TODO** — see Section C.
- [x] **#7 SHA-256 verification** — `AutoUpdateService` now fetches `SHA256SUMS.txt` from the release, parses it (`ParseSha256Sums` is unit-tested), and verifies the downloaded asset's SHA-256 before writing the swap script. Releases without a sums file log a warning but still install (back-compat with old releases). `release.yml` generates `SHA256SUMS.txt` from the .zip + .msi and uploads it alongside.
- [x] **#8 Screenshot history** — captures persist to `%AppData%\MoneyShot\history` with thumbnails + sidecar JSON. `MainWindow` saves on every capture path (FullScreen / Region / Monitor N), tray menu has a "History" item, `HistoryWindow.xaml` shows a thumbnail grid with right-click → Open in Editor / Copy to Clipboard / Delete. Retention defaults to 50 (configurable via `AppSettings.HistoryRetentionCount`). Settings UI for the toggle / count is **still TODO** — see Section D.

---

## A. Finish the EditorWindow refactor

The compositional pieces (`UndoController`, `CanvasRenderer`) are extracted, but selection/drag/resize state still lives in `EditorWindow.xaml.cs`. That code is the source of every past resize regression, so splitting it requires careful work and ideally manual UI test coverage.

> Continue the refactor of `MoneyShot/Views/EditorWindow.xaml.cs` by extracting:
>
> - `MoneyShot/Editor/SelectionController.cs` — owns `_selectedElement`, `_selectionBorder`, `_resizeHandles`, the `SelectElement` / `ClearSelection` / `MoveElement` / `BeginResize` / `ResizeElement` flow, the new endpoint-resize fields (`_isEndpointResizing`, `_originalEndpointStart`, `_originalEndpointEnd`), and the `CreateResizeHandle` / `CreateEndpointHandle` factories.
> - `MoneyShot/Editor/AnnotationToolRegistry.cs` — owns the per-tool factories (`CreateRectangle`, `CreateEllipse`, `CreateArrow`, `CreatePolyline`, `CreateNumberLabel`, `CreateBlurRectangle`, `CreateLine`) and the per-tool `Update*` methods.
>
> Pass collaborators in via the `EditorWindow` constructor — do **not** introduce a DI container. Keep `EditorWindow.xaml.cs` as the WPF wiring layer that delegates to these controllers from event handlers. Don't rename XAML element IDs or change `Canvas_MouseDown/Move/Up` signatures (they're bound from XAML).
>
> Run the editor on Windows after the split and exercise: rectangle/ellipse/arrow/line/text resize, drag, undo of each, crop, zoom. The fields `_isResizing`, `_isEndpointResizing`, `_isDragging`, `_isDrawing` are mutually exclusive — preserve that invariant.

## B. Migrate the editor + main window off `AllowsTransparency`

Both `EditorWindow.xaml` and `MainWindow.xaml` (probably — check) use `WindowStyle="None"` + `AllowsTransparency="True"`, which forces software rendering for the entire window. The custom title bar in `EditorWindow` already shows symptoms of this (the `try/catch` around `DragMove` at `EditorWindow.xaml.cs:TitleBar_MouseLeftButtonDown` exists because `DragMove` throws under certain transparency edge cases).

> Replace `WindowStyle="None"` + `AllowsTransparency="True"` with `WindowChrome` in `MoneyShot/Views/EditorWindow.xaml` (and `MoneyShot/MainWindow.xaml` if applicable). Use `WindowChrome.WindowChrome` with custom caption buttons inside the chrome's resize border so the dark glassmorphism look survives. Remove the `try/catch (InvalidOperationException)` around `DragMove` in the title bar handler — `WindowChrome` makes that unnecessary.
>
> This is invasive. Be ready to iterate on resize-grip thickness, caption-button positioning, and the rounded-corner Border. Compare side-by-side with the current build before merging.

## C. Surface the log directory in Settings

Logs already write to `%AppData%\MoneyShot\logs\moneyshot-YYYYMMDD.log` with 7-day retention via `MoneyShot.Services.Logger.LogDirectoryPath`. Users have no easy way to find them.

> Add a "View Logs" button to `MoneyShot/Views/SettingsWindow.xaml` that opens `MoneyShot.Services.Logger.LogDirectoryPath` in Explorer via `Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })`. Place it near the bottom of the settings dialog.

## D. Settings UI for screenshot history

The history feature ships with `AppSettings.SaveCapturesToHistory` (default true) and `AppSettings.HistoryRetentionCount` (default 50), but neither is exposed in the Settings window — users can only change them by editing `%AppData%\MoneyShot\settings.json` by hand.

> In `MoneyShot/Views/SettingsWindow.xaml`, add:
>
> - A checkbox "Save captures to local history" bound to `AppSettings.SaveCapturesToHistory`.
> - A NumericUpDown / spinner "Keep last N captures" bound to `AppSettings.HistoryRetentionCount` (range 0–500). 0 disables retention enforcement.
> - A "Clear history" button that calls `HistoryService.List()` then `Delete()` for each entry, with a confirmation dialog.
>
> Surface the history folder path next to the controls so users know where the files live (`%AppData%\MoneyShot\history`).

## E. Authenticode signing for the published .exe (out-of-scope follow-up to #7)

SHA-256 verification protects against tampering between GitHub Releases and the user, but the published binary itself is unsigned — Windows SmartScreen will warn on first run, and there's no chain-of-trust on the publisher.

> Add Authenticode signing to `release.yml`. Options:
>
> - Buy a code-signing cert and store the PFX as a `secrets.SIGNING_CERT_PFX_BASE64` + `secrets.SIGNING_CERT_PASSWORD` GitHub Secret. Sign with `signtool sign /f cert.pfx /p $password /tr http://timestamp.digicert.com /td sha256 /fd sha256 MoneyShot.exe`.
> - Once signed, optionally call `WinVerifyTrust` from `AutoUpdateService.PrepareExecutableAsset` before the swap to refuse unsigned binaries.
>
> Until this lands, the SHA-256 check is the only integrity guarantee — that's still meaningfully better than nothing.

## F. Migrate to `Microsoft.Extensions.Logging` if a real DI need ever appears

The current `Logger` static facade closes the "no Release-build output" gap, but it doesn't enable structured logging, log levels controlled by config, or per-namespace filtering. If the project ever takes on a complexity that justifies DI (multiple loggers, multiple sinks, `ILogger<T>` for testability), the right move is the originally-proposed migration from this document — replace `Logger` with `Microsoft.Extensions.Logging` + a logger factory in `App.xaml.cs`, then take constructor-injected `ILogger<T>` in each service.

Until that pressure exists, the static facade is the right call.

---

## Notes for the next agent

- **Do not run `git` commands without explicit user approval.** The user enforces this.
- **Verify what the memory and CLAUDE.md tell you against the current code before acting.** Memories drift. The `CLAUDE.md` resize section was updated when the handle-owns-its-own-MouseDown refactor landed; if you see different code, the docs are stale, not the code.
- **The only test of "feature correctness" is running the WPF app on Windows.** `dotnet build` and `dotnet test` prove the service-layer code compiles and that pure functions behave correctly, not that the WPF UI works. Anything touching `EditorWindow`, `HistoryWindow`, or the tray menu needs human-eyes verification.
- **Don't reintroduce `RESIZE-BUG-JUSTIFY.md`, `plan.md`, or `PROJECT_SUMMARY.md`.** They were deleted because they were agent scratch-pads, not user docs. If you need to write a note for a future agent, append to this file instead.
- **Capture history is local-only by design.** Do not add cloud sync, telemetry, or any remote upload of history files without an explicit settings toggle and an in-product disclosure.
- The MSI license (`Installer/License.rtf`) is now AGPL-3.0 and must stay in sync with the repo's `LICENSE` file. If the repo ever relicenses, update both.
